using GameVault.Core;
namespace GameVault.Tests;

public sealed class ShellModelTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    private static readonly DateTimeOffset T = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void History_goes_back_and_forward_and_ignores_the_screen_you_are_already_on()
    {
        var h = new NavigationHistory();
        Assert.True(h.Record(NavRoute.Library));
        Assert.False(h.Record(NavRoute.Library));
        h.Record(NavRoute.Game(A)); h.Record(NavRoute.Game(A, "Sessions")); h.Record(NavRoute.Settings);
        Assert.Equal(NavRoute.Settings, h.Current);
        Assert.Equal(NavRoute.Game(A, "Sessions"), h.Back());
        Assert.Equal(NavRoute.Game(A), h.Back());
        Assert.True(h.CanGoForward);
        Assert.Equal(NavRoute.Game(A, "Sessions"), h.Forward());
        Assert.Equal(NavRoute.Game(A), h.Back());
        Assert.Equal(NavRoute.Library, h.Back());
        Assert.False(h.CanGoBack);
        Assert.Null(h.Back());
    }

    [Fact]
    public void Recording_after_going_back_drops_the_forward_entries()
    {
        var h = new NavigationHistory();
        h.Record(NavRoute.Library); h.Record(NavRoute.Game(A)); h.Record(NavRoute.Settings);
        h.Back(); h.Back();
        h.Record(NavRoute.Game(B));
        Assert.False(h.CanGoForward);
        Assert.Equal(NavRoute.Library, h.Back());
    }

    [Fact]
    public void Back_skips_games_that_no_longer_exist_and_stays_put_when_nothing_valid_is_left()
    {
        var h = new NavigationHistory();
        h.Record(NavRoute.Library); h.Record(NavRoute.Game(A)); h.Record(NavRoute.Game(B)); h.Record(NavRoute.Settings);
        Assert.Equal(NavRoute.Library, h.Back(r => r.GameId != A && r.GameId != B));
        Assert.Null(h.Back(r => false));
        Assert.Equal(NavRoute.Library, h.Current);
        Assert.Equal(NavRoute.Settings, h.Forward(r => r.Kind != NavKind.Game));
    }

    [Fact]
    public void A_refused_move_can_be_reverted_without_losing_the_forward_entries()
    {
        var h = new NavigationHistory();
        h.Record(NavRoute.Library); h.Record(NavRoute.Game(A)); h.Record(NavRoute.Settings);
        Assert.Equal(NavRoute.Game(A), h.Back());
        h.Revert();
        Assert.Equal(NavRoute.Settings, h.Current);
        Assert.False(h.CanGoForward);
        h.Revert();                                   // nothing pending: no effect
        Assert.Equal(NavRoute.Settings, h.Current);
    }

    [Fact]
    public void History_is_capped()
    {
        var h = new NavigationHistory(3);
        for (var i = 0; i < 10; i++) h.Record(NavRoute.Game(Guid.NewGuid()));
        var steps = 0; while (h.Back() is not null) steps++;
        Assert.Equal(2, steps);
    }

    [Theory]
    [InlineData(NavKind.Library, null, "Library")]
    [InlineData(NavKind.Settings, null, "Settings")]
    [InlineData(NavKind.Backups, null, "Backups")]
    [InlineData(NavKind.Captures, null, "Captures")]
    [InlineData(NavKind.Game, "Overview", "Library › TEKKEN 8")]
    [InlineData(NavKind.Game, "Sessions", "Library › TEKKEN 8 › Sessions & notes")]
    [InlineData(NavKind.Game, "Saves", "Library › TEKKEN 8 › Saves")]
    [InlineData(NavKind.Game, "Media", "Library › TEKKEN 8 › Media")]
    public void Breadcrumb_names_where_you_are(NavKind kind, string? section, string expected) =>
        Assert.Equal(expected, NavBreadcrumb.Text(new NavRoute(kind, kind == NavKind.Game ? A : null, section), "TEKKEN 8"));

    [Fact]
    public void Palette_ranks_prefix_then_word_start_then_substring_then_loose_matches()
    {
        var entries = new[]
        {
            new PaletteEntry("Games", "Resident Evil 2"), new PaletteEntry("Games", "Dead Space"), new PaletteEntry("Games", "TEKKEN 8"),
            new PaletteEntry("Actions", "Open Settings"), new PaletteEntry("Actions", "Add game"), new PaletteEntry("Actions", "Play TEKKEN 8"),
        };
        Assert.Equal(["TEKKEN 8", "Play TEKKEN 8"], PaletteSearch.Filter(entries, "tek").Select(e => e.Title));
        var space = PaletteSearch.Filter(entries, "sp").Select(e => e.Title).ToArray();
        Assert.Equal(["Dead Space"], space);                // "Space" starts a word; "Open Settings" has no s followed by p
        Assert.Equal(["Dead Space"], PaletteSearch.Filter(entries, "dsp").Select(e => e.Title));  // loose in-order match
        Assert.Empty(PaletteSearch.Filter(entries, "zzz"));
        Assert.Equal(entries.Length, PaletteSearch.Filter(entries, "  ").Length);
    }

    [Fact]
    public void Palette_limits_results()
    {
        var many = Enumerable.Range(0, 100).Select(i => new PaletteEntry("Games", "Game " + i));
        Assert.Equal(10, PaletteSearch.Filter(many, "game", 10).Length);
    }

    [Fact]
    public void Toasts_expire_but_problems_stay_until_dismissed()
    {
        var q = new ToastQueue();
        var ok = q.Add("Backup complete", now: T);
        var bad = q.Add("Could not save", "disk full", isError: true, now: T);
        q.Prune(T.AddSeconds(4));
        Assert.Equal(2, q.Items.Count);
        q.Prune(T.AddSeconds(6));
        Assert.Equal([bad.Id], q.Items.Select(t => t.Id));
        q.Dismiss(bad.Id);
        Assert.Empty(q.Items);
        Assert.NotEqual(ok.Id, bad.Id);
    }

    [Fact]
    public void A_toast_with_an_action_lives_a_little_longer()
    {
        var q = new ToastQueue();
        q.Add("Removed", actionLabel: "Undo", action: () => { }, now: T);
        q.Prune(T.AddSeconds(6)); Assert.Single(q.Items);
        q.Prune(T.AddSeconds(9)); Assert.Empty(q.Items);
    }

    [Fact]
    public void The_same_message_twice_refreshes_instead_of_stacking()
    {
        var q = new ToastQueue();
        var first = q.Add("Saved", "Notes", now: T);
        var again = q.Add("Saved", "Notes", now: T.AddSeconds(1));
        Assert.Single(q.Items); Assert.Equal(first.Id, again.Id);
        q.Add("Saved", "Notes", now: T.AddSeconds(10));
        Assert.Equal(2, q.Items.Count);
    }

    [Fact]
    public void Only_a_few_toasts_show_and_errors_are_the_last_to_be_pushed_out()
    {
        var q = new ToastQueue(3);
        var err = q.Add("Problem", isError: true, now: T);
        for (var i = 0; i < 6; i++) q.Add("Info " + i, now: T.AddSeconds(i * 3));
        Assert.Equal(3, q.Items.Count);
        Assert.Contains(q.Items, t => t.Id == err.Id);
    }

    [Fact]
    public void Changed_fires_on_add_dismiss_and_expiry()
    {
        var q = new ToastQueue(); var count = 0; q.Changed += (_, _) => count++;
        var t = q.Add("One", now: T); q.Dismiss(t.Id); q.Add("Two", now: T); q.Prune(T.AddMinutes(1));
        Assert.Equal(4, count);
    }

    [Theory]
    [InlineData("Could not load the library. Disk error.", true)]
    [InlineData("Backup destination saved. Existing backups remain.", false)]
    [InlineData("Capture needs attention. Retry.", true)]
    [InlineData("Screenshot saved for TEKKEN 8 at 11:04 PM.", false)]
    public void Messages_that_describe_a_problem_are_recognised(string message, bool problem) =>
        Assert.Equal(problem, ToastText.LooksLikeProblem(message));

    [Theory]
    [InlineData("Backup complete. 2 files verified.", "Backup complete", "2 files verified.")]
    [InlineData("Saved", "Saved", null)]
    public void First_sentence_becomes_the_title(string message, string title, string? detail) =>
        Assert.Equal((title, detail), ToastText.Split(message));

    [Fact]
    public void A_very_long_message_without_a_sentence_break_is_shortened_but_kept_in_the_detail()
    {
        var (title, detail) = ToastText.Split(new string('x', 200));
        Assert.True(title.Length <= 90); Assert.Equal(200, detail!.Length);
    }
}

public sealed class OverhaulPartTwoTests
{
 static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

 [Fact]
 public void Continue_playing_orders_newest_first_skips_unplayed_and_limits()
 {
  var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid(); var e = Guid.NewGuid();
  var picked = ContinuePlaying.Pick([new(a, Now.AddDays(-5)), null, new(b, Now.AddHours(-1)), new(c, Now.AddDays(-1)), new(d, Now.AddDays(-9)), new(e, Now.AddDays(-30))], 4);
  Assert.Equal([b, c, a, d], picked.Select(x => x.GameId));
  Assert.Empty(ContinuePlaying.Pick([null, null]));
  Assert.Empty(ContinuePlaying.Pick([new(a, Now)], 0));
 }

 [Theory]
 [InlineData(0, "Just now")]
 [InlineData(30, "Just now")]
 [InlineData(300, "5 min ago")]
 [InlineData(3 * 3600, "3 h ago")]
 [InlineData(30 * 3600, "Yesterday")]
 [InlineData(5 * 86400, "5 days ago")]
 public void Ago_is_friendly(int seconds, string expected) => Assert.Equal(expected, ContinuePlaying.Ago(Now.AddSeconds(-seconds), Now));

 [Fact]
 public void Ago_uses_a_date_after_two_weeks_and_tolerates_clock_skew()
 {
  Assert.Equal("1 Nov 2026", ContinuePlaying.Ago(new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero), Now));
  Assert.Equal("Just now", ContinuePlaying.Ago(Now.AddMinutes(5), Now));
 }

 [Fact]
 public void Saved_views_replace_by_name_ignoring_case_trim_and_cap()
 {
  var list = new SavedViewList();
  Assert.True(list.Save(new(" Backlog ", "NotStarted", false, " rpg ")));
  Assert.True(list.Save(new("backlog", "Playing", true, "")));
  var only = Assert.Single(list.Views);
  Assert.Equal(("backlog", "Playing", true, ""), (only.Name, only.Status, only.Removed, only.Search));
  Assert.False(list.Save(new("  ", "All", false, "")));
  for (var i = 0; i < SavedViewList.MaxViews - 1; i++) Assert.True(list.Save(new("v" + i, "All", false, "")));
  Assert.False(list.Save(new("one too many", "All", false, "")));
  Assert.True(list.Save(new("V1", "Completed", false, "")));
  Assert.True(list.Remove("BACKLOG"));
  Assert.False(list.Remove("missing"));
  Assert.Equal(SavedViewList.MaxViews - 1, list.Views.Count);
 }

 [Fact]
 public void Saved_view_names_are_shortened_and_empty_status_means_all()
 {
  var list = new SavedViewList([new(new string('x', 100), "", false, ""), new("", "All", false, "")]);
  var view = Assert.Single(list.Views);
  Assert.Equal(SavedViewList.MaxNameLength, view.Name.Length);
  Assert.Equal("All", view.Status);
 }

 [Theory]
 [InlineData("", "anything", true)]
 [InlineData("backup folder", "Backup storage. Choose the default destination folder", true)]
 [InlineData("BACKUP", "Backup storage", true)]
 [InlineData("tray", "Backup storage", false)]
 [InlineData("backup tray", "Backup storage", false)]
 public void Settings_search_requires_every_word(string query, string text, bool expected) => Assert.Equal(expected, SettingsSearch.Matches(query, text));
}
