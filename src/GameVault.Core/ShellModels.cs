namespace GameVault.Core;

public enum NavKind { Library, Settings, Game, Backups, Captures }

/// <summary>A place in the app that Back and Forward can return to. Section is a game page section (Overview, Sessions, Saves, Media).</summary>
public sealed record NavRoute(NavKind Kind, Guid? GameId = null, string? Section = null)
{
 public static readonly NavRoute Library = new(NavKind.Library);
 public static readonly NavRoute Settings = new(NavKind.Settings);
 /// <summary>One page of Settings; <see cref="Settings"/> alone means the page last shown.</summary>
 public static NavRoute SettingsPage(string key) => new(NavKind.Settings, null, key);
 public static readonly NavRoute Backups = new(NavKind.Backups);
 public static readonly NavRoute Captures = new(NavKind.Captures);
 /// <summary>One game's folder on the Captures page.</summary>
 public static NavRoute CapturesOf(Guid id) => new(NavKind.Captures, id);
 public static NavRoute Game(Guid id, string? section = null) => new(NavKind.Game, id, string.IsNullOrWhiteSpace(section) ? "Overview" : section);
}

/// <summary>Back/Forward history across the main screens. Recording the screen you are already on does nothing; recording a new one drops everything ahead.</summary>
public sealed class NavigationHistory(int capacity = 50)
{
 readonly List<NavRoute> entries = [];
 int index = -1;
 public NavRoute? Current => index >= 0 ? entries[index] : null;
 public bool CanGoBack => index > 0;
 public bool CanGoForward => index >= 0 && index < entries.Count - 1;

 public bool Record(NavRoute route)
 {
  ArgumentNullException.ThrowIfNull(route);
  if (Current == route) return false;
  if (index < entries.Count - 1) entries.RemoveRange(index + 1, entries.Count - index - 1);
  entries.Add(route);
  if (entries.Count > capacity) entries.RemoveAt(0);
  index = entries.Count - 1;
  return true;
 }

 /// <summary>Moves back to the nearest earlier entry that is still valid (for example a game that still exists). Returns null and stays put when there is none.</summary>
 public NavRoute? Back(Func<NavRoute, bool>? isValid = null) => Move(-1, isValid);
 public NavRoute? Forward(Func<NavRoute, bool>? isValid = null) => Move(1, isValid);

 int moveFrom = -1;
 /// <summary>Undoes the last Back or Forward when the screen could not actually be shown (for example the user cancelled an unsaved-changes prompt).</summary>
 public void Revert() { if (moveFrom >= 0 && moveFrom < entries.Count) index = moveFrom; moveFrom = -1; }

 NavRoute? Move(int step, Func<NavRoute, bool>? isValid)
 {
  for (var i = index + step; i >= 0 && i < entries.Count; i += step)
   if (isValid is null || isValid(entries[i])) { moveFrom = index; index = i; return entries[i]; }
  return null;
 }
}

/// <summary>Text for the title bar breadcrumb.</summary>
public static class NavBreadcrumb
{
 public static string Text(NavRoute route, string? gameName)
 {
  switch (route.Kind)
  {
   case NavKind.Settings: return SettingsCatalog.IsKnown(route.Section) ? "Settings › " + SettingsCatalog.Title(route.Section) : "Settings";
   case NavKind.Backups: return "Backups";
   case NavKind.Captures: return route.GameId is null ? "Captures" : "Captures › " + (string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName);
   case NavKind.Game:
    var name = string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName;
    var section = route.Section switch { "Sessions" => "Sessions & notes", "Saves" => "Saves", "Media" => "Media", _ => null };
    return section is null ? "Library › " + name : "Library › " + name + " › " + section;
   default: return "Library";
  }
 }
}

public sealed record PaletteEntry(string Group, string Title, string Hint = "", object? Tag = null);

/// <summary>Ranks command palette entries for a typed query: prefix, word start, substring, then loose in-order matches.</summary>
public static class PaletteSearch
{
 public static PaletteEntry[] Filter(IEnumerable<PaletteEntry> entries, string? query, int max = 40)
 {
  var all = entries.ToArray();
  var q = (query ?? "").Trim().ToLowerInvariant();
  if (q.Length == 0) return all.Take(max).ToArray();
  return all.Select((entry, order) => (entry, order, score: Score(entry.Title.ToLowerInvariant(), q)))
   .Where(x => x.score > 0)
   .OrderByDescending(x => x.score).ThenBy(x => x.order)
   .Select(x => x.entry).Take(max).ToArray();
 }

 public static int Score(string title, string q)
 {
  var at = title.IndexOf(q, StringComparison.Ordinal);
  if (at == 0) return 1000;
  if (at > 0)
  {
   var wordStart = !char.IsLetterOrDigit(title[at - 1]);
   return (wordStart ? 800 : 600) - Math.Min(at, 99);
  }
  // loose match: every query letter appears in order; closer together scores higher
  var position = -1; var first = -1; var last = -1;
  foreach (var c in q)
  {
   if (c == ' ') continue;
   position = title.IndexOf(c, position + 1);
   if (position < 0) return 0;
   if (first < 0) first = position;
   last = position;
  }
  return Math.Max(1, 300 - (last - first));
 }
}

public sealed record Toast(long Id, string Title, string? Detail, bool IsError, string? ActionLabel, Action? Action, DateTimeOffset Created);

/// <summary>Short-lived, non-blocking messages. Problems stay until dismissed; everything else expires. The same message arriving again just refreshes the existing toast.</summary>
public sealed class ToastQueue(int maxVisible = 4, TimeSpan? lifetime = null)
{
 readonly List<Toast> items = [];
 readonly TimeSpan life = lifetime ?? TimeSpan.FromSeconds(5);
 long next = 1;
 public IReadOnlyList<Toast> Items => items;
 public event EventHandler? Changed;

 public Toast Add(string title, string? detail = null, bool isError = false, string? actionLabel = null, Action? action = null, DateTimeOffset? now = null)
 {
  var time = now ?? DateTimeOffset.UtcNow;
  var existing = items.FindIndex(t => t.Title == title && t.Detail == detail && time - t.Created < TimeSpan.FromSeconds(2));
  if (existing >= 0) { var refreshed = items[existing] with { Created = time }; items[existing] = refreshed; Changed?.Invoke(this, EventArgs.Empty); return refreshed; }
  var toast = new Toast(next++, title, detail, isError, actionLabel, action, time);
  items.Add(toast);
  while (items.Count > maxVisible)
  {
   var drop = items.FindIndex(t => !t.IsError);
   items.RemoveAt(drop >= 0 ? drop : 0);
  }
  Changed?.Invoke(this, EventArgs.Empty);
  return toast;
 }

 public void Dismiss(long id)
 {
  if (items.RemoveAll(t => t.Id == id) > 0) Changed?.Invoke(this, EventArgs.Empty);
 }

 public void Prune(DateTimeOffset? now = null)
 {
  var time = now ?? DateTimeOffset.UtcNow;
  if (items.RemoveAll(t => !t.IsError && time - t.Created >= (t.ActionLabel is null ? life : life + TimeSpan.FromSeconds(3))) > 0)
   Changed?.Invoke(this, EventArgs.Empty);
 }
}

public static class ToastText
{
 static readonly string[] Problem = ["could not", "couldn't", "failed", "needs attention", "was not", "unavailable", "cannot", "can't", "refused", "error", "not found"];

 public static bool LooksLikeProblem(string message) =>
  Problem.Any(word => message.Contains(word, StringComparison.OrdinalIgnoreCase));

 /// <summary>First sentence becomes the title, the rest the detail.</summary>
 public static (string Title, string? Detail) Split(string message)
 {
  var text = message.Trim();
  var end = -1;
  for (var i = 0; i < text.Length - 1; i++)
   if (text[i] is '.' or '!' or '?' && (text[i + 1] == ' ' || text[i + 1] == '\n')) { end = i; break; }
  if (end < 0 || end > 90) return text.Length <= 90 ? (text, null) : (text[..87].TrimEnd() + "…", text);
  var title = text[..(end + 1)].TrimEnd('.');
  var detail = text[(end + 1)..].Trim();
  return (title, detail.Length == 0 ? null : detail);
 }
}

/// <summary>Which games appear in the "Continue playing" row on the library page.</summary>
public static class ContinuePlaying
{
 public sealed record Entry(Guid GameId, DateTimeOffset LastPlayedAt);

 /// <summary>The most recently played games first, at most <paramref name="count"/>. Games never played are left out.</summary>
 public static Entry[] Pick(IEnumerable<Entry?> games, int count = 4) =>
  games.Where(g => g is not null).Select(g => g!).OrderByDescending(g => g.LastPlayedAt).ThenBy(g => g.GameId).Take(Math.Max(0, count)).ToArray();

 /// <summary>Short, friendly "last played" text such as "Just now", "3 h ago", "Yesterday" or "12 Mar".</summary>
 public static string Ago(DateTimeOffset playedAt, DateTimeOffset now)
 {
  var span = now - playedAt;
  if (span < TimeSpan.Zero || span < TimeSpan.FromMinutes(1)) return "Just now";
  if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
  if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";
  if (span < TimeSpan.FromDays(2)) return "Yesterday";
  if (span < TimeSpan.FromDays(14)) return $"{(int)span.TotalDays} days ago";
  return playedAt.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
 }
}

/// <summary>A named library filter the user saved (status chip, removed toggle and search text).</summary>
public sealed record SavedLibraryView(string Name, string Status, bool Removed, string Search, string[]? Tags = null, string Sort = "Name");

/// <summary>The list of saved library views, kept in Data/library-views.json. Names are unique ignoring case; saving an existing name replaces it.</summary>
public sealed class SavedViewList
{
 public const int MaxViews = 12;
 public const int MaxNameLength = 40;
 readonly List<SavedLibraryView> views = [];
 public IReadOnlyList<SavedLibraryView> Views => views;

 public SavedViewList() { }
 public SavedViewList(IEnumerable<SavedLibraryView> existing) { foreach (var v in existing) { if (TryClean(v, out var clean)) Put(clean); } }

 /// <summary>Adds or replaces a view. Returns false when the name is empty or the list is full.</summary>
 public bool Save(SavedLibraryView view)
 {
  if (!TryClean(view, out var clean)) return false;
  if (views.Count >= MaxViews && !views.Any(v => Same(v.Name, clean.Name))) return false;
  Put(clean);
  return true;
 }

 public bool Remove(string name) => views.RemoveAll(v => Same(v.Name, name)) > 0;

 /// <summary>Renames a view. Fails when the new name is empty or already used by another view.</summary>
 public bool Rename(string name, string newName)
 {
  var i = views.FindIndex(v => Same(v.Name, name));
  if (i < 0 || string.IsNullOrWhiteSpace(newName)) return false;
  var trimmed = newName.Trim();
  if (trimmed.Length > MaxNameLength) trimmed = trimmed[..MaxNameLength].TrimEnd();
  if (views.Where((_, index) => index != i).Any(v => Same(v.Name, trimmed))) return false;
  views[i] = views[i] with { Name = trimmed };
  return true;
 }

 /// <summary>Moves a view up (negative) or down (positive) by that many places, stopping at the ends.</summary>
 public bool Move(string name, int offset)
 {
  var i = views.FindIndex(v => Same(v.Name, name));
  if (i < 0) return false;
  var target = Math.Clamp(i + offset, 0, views.Count - 1);
  if (target == i) return false;
  var view = views[i];
  views.RemoveAt(i);
  views.Insert(target, view);
  return true;
 }

 static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

 void Put(SavedLibraryView clean)
 {
  var i = views.FindIndex(v => Same(v.Name, clean.Name));
  if (i >= 0) views[i] = clean; else views.Add(clean);
 }

 static bool TryClean(SavedLibraryView? view, out SavedLibraryView clean)
 {
  clean = view!;
  if (view is null || string.IsNullOrWhiteSpace(view.Name)) return false;
  var name = view.Name.Trim();
  if (name.Length > MaxNameLength) name = name[..MaxNameLength].TrimEnd();
  var status = string.IsNullOrWhiteSpace(view.Status) ? "All" : view.Status.Trim();
  clean = new SavedLibraryView(name, status, view.Removed, (view.Search ?? "").Trim(), TagRules.Normalize(view.Tags), LibrarySortOrder.Normalize(view.Sort));
  return true;
 }
}

/// <summary>Matching for the settings search box: every word typed must appear somewhere in the section text.</summary>
public static class SettingsSearch
{
 public static bool Matches(string query, string sectionText)
 {
  if (string.IsNullOrWhiteSpace(query)) return true;
  return query.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
   .All(word => sectionText.Contains(word, StringComparison.OrdinalIgnoreCase));
 }
}

/// <summary>On-disk form of the saved library views (Data/library-views.json).</summary>
public sealed record LibraryViewsDocument(int SchemaVersion, SavedLibraryView[] Views)
{
 public static LibraryViewsDocument Empty => new(1, []);
 public static bool IsValid(LibraryViewsDocument? value) => value is { SchemaVersion: 1, Views: not null } && value.Views.Length <= SavedViewList.MaxViews && value.Views.All(v => v is not null && !string.IsNullOrWhiteSpace(v.Name) && v.Status is not null && v.Search is not null && (v.Tags is null || v.Tags.Length <= TagRules.MaxTagsPerGame) && v.Sort is not null);
}
