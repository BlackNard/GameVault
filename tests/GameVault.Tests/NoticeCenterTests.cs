using GameVault.Core;
using GameVault.Infrastructure.Updates;

namespace GameVault.Tests;

public sealed class NoticeCenterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault notices", Guid.NewGuid().ToString("N"));

    public NoticeCenterTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static readonly DateTimeOffset T0 = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static Notice N(string id, NoticeKind kind = NoticeKind.Update, string title = "Title", string? detail = null, int minute = 0, bool problem = false) =>
        new(id, kind, title, detail, T0.AddMinutes(minute), problem, "Open");

    [Fact]
    public void Notices_are_listed_newest_first_and_a_new_one_counts_as_unseen()
    {
        var center = new NoticeCenter();
        var changes = 0;
        center.Changed += (_, _) => changes++;
        center.Post(N("a", minute: 1));
        center.Post(N("b", NoticeKind.BackupAttention, minute: 5));
        center.Post(N("c", NoticeKind.UnexpectedExit, minute: 3));
        Assert.Equal(["b", "c", "a"], center.Items.Select(n => n.Id));
        Assert.Equal(3, center.Count);
        Assert.Equal(3, center.UnseenCount);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Opening_the_list_marks_everything_seen_but_keeps_the_notices_until_they_are_dismissed()
    {
        var center = new NoticeCenter();
        center.Post(N("a"));
        center.Post(N("b"));
        center.MarkAllSeen();
        Assert.Equal(0, center.UnseenCount);
        Assert.Equal(2, center.Count);
        Assert.True(center.IsSeen("a"));
        center.Post(N("c"));
        Assert.Equal(1, center.UnseenCount);                       // only the new one is unseen
        Assert.True(center.Remove("a"));
        Assert.False(center.Remove("a"));
        Assert.False(center.Remove("missing"));
        Assert.Equal(["c", "b"], center.Items.Select(n => n.Id).OrderByDescending(id => id));
    }

    [Fact]
    public void Posting_the_same_notice_again_changes_nothing_but_new_text_makes_it_unseen_again_and_keeps_its_place()
    {
        var center = new NoticeCenter();
        var changes = 0;
        center.Post(N("a", title: "Backup failed", detail: "disk full", minute: 1));
        center.MarkAllSeen();
        center.Changed += (_, _) => changes++;
        center.Post(N("a", title: "Backup failed", detail: "disk full", minute: 99));       // identical content: no change, no new badge
        Assert.Equal(0, changes);
        Assert.Equal(0, center.UnseenCount);
        center.Post(N("a", title: "Backup failed", detail: "access denied", minute: 99));   // the situation changed
        Assert.Equal(1, changes);
        Assert.Equal(1, center.UnseenCount);
        Assert.Equal(1, center.Count);
        Assert.Equal(T0.AddMinutes(1), center.Items.Single().CreatedAt);                    // it keeps its original time
        center.Post(N("a", title: "Backup failed", detail: "access denied", minute: 99, problem: true));   // a styling change alone is not news
        Assert.Equal(1, center.UnseenCount);
    }

    [Fact]
    public void Resolved_notices_of_one_kind_leave_and_the_others_stay()
    {
        var center = new NoticeCenter();
        center.Post(N("backup:1", NoticeKind.BackupAttention));
        center.Post(N("backup:2", NoticeKind.BackupAttention));
        center.Post(N("update:v2", NoticeKind.Update));
        center.Post(N("unexpected-exit", NoticeKind.UnexpectedExit));
        center.RetainOnly(NoticeKind.BackupAttention, new HashSet<string> { "backup:2" });
        Assert.Equal(["backup:2", "update:v2", "unexpected-exit"], center.Items.Select(n => n.Id).OrderBy(id => id == "backup:2" ? 0 : id == "update:v2" ? 1 : 2));
        Assert.Equal(3, center.Count);
        center.RetainOnly(NoticeKind.Update, new HashSet<string>());
        Assert.Equal(2, center.Count);
        var changed = false;
        center.Changed += (_, _) => changed = true;
        center.RetainOnly(NoticeKind.Update, new HashSet<string>());                        // nothing to remove: no change event
        Assert.False(changed);
        center.Clear();
        Assert.Equal(0, center.Count);
        Assert.Equal(0, center.UnseenCount);
        center.Clear();
    }

    [Fact]
    public void The_list_never_grows_past_its_capacity_and_drops_the_oldest()
    {
        var center = new NoticeCenter(capacity: 3);
        for (var i = 1; i <= 5; i++) center.Post(N($"n{i}", minute: i));
        Assert.Equal(3, center.Count);
        Assert.Equal(["n5", "n4", "n3"], center.Items.Select(n => n.Id));
        Assert.False(center.IsSeen("n1"));
    }

    private static UpdateInfo Info(string version, string? tag = null) =>
        new(version, tag ?? "v" + version, $"https://github.com/BlackNard/GameVault/releases/tag/{tag ?? "v" + version}", true, T0);

    private static ReleaseVersion V(string text) { Assert.True(ReleaseVersion.TryParse(text, out var v)); return v; }

    [Fact]
    public void The_found_update_survives_a_restart_and_a_dismissal_is_remembered_per_version()
    {
        Assert.Equal(UpdateNoticeState.Empty, UpdateNoticeCache.Read(root));
        UpdateNoticeCache.WriteLatest(root, Info("0.1.0-preview.3"));
        var state = UpdateNoticeCache.Read(root);
        Assert.Equal("0.1.0-preview.3", state.Latest!.Version);
        Assert.Equal("v0.1.0-preview.3", state.Latest.Tag);
        Assert.Equal(T0, state.Latest.PublishedAt);
        Assert.True(UpdateNoticeCache.ShouldShow(state, V("0.1.0-preview.2")));
        Assert.False(UpdateNoticeCache.ShouldShow(state, V("0.1.0-preview.3")));            // the app was updated: the notice goes away
        Assert.False(UpdateNoticeCache.ShouldShow(state, V("0.1.0")));

        UpdateNoticeCache.Dismiss(root, "v0.1.0-preview.3");
        var dismissed = UpdateNoticeCache.Read(root);
        Assert.False(UpdateNoticeCache.ShouldShow(dismissed, V("0.1.0-preview.2")));
        Assert.Equal("v0.1.0-preview.3", dismissed.DismissedTag);
        UpdateNoticeCache.WriteLatest(root, Info("0.1.0-preview.3"));                        // another check finds the same release: still dismissed
        Assert.False(UpdateNoticeCache.ShouldShow(UpdateNoticeCache.Read(root), V("0.1.0-preview.2")));
        UpdateNoticeCache.WriteLatest(root, Info("0.1.0-preview.4"));                        // a newer release brings a new notice
        Assert.True(UpdateNoticeCache.ShouldShow(UpdateNoticeCache.Read(root), V("0.1.0-preview.2")));
        UpdateNoticeCache.WriteLatest(root, null);                                           // up to date: nothing cached, the dismissal is kept
        var empty = UpdateNoticeCache.Read(root);
        Assert.Null(empty.Latest);
        Assert.Equal("v0.1.0-preview.3", empty.DismissedTag);
        Assert.False(UpdateNoticeCache.ShouldShow(empty, V("0.1.0-preview.2")));
    }

    [Fact]
    public void Damaged_oversized_or_unwritable_cache_files_mean_nothing_known_and_never_throw()
    {
        Directory.CreateDirectory(Path.Combine(root, "Cache"));
        var file = Path.Combine(root, "Cache", "update-notice.json");
        foreach (var text in new[] { "", "{ broken", "[1,2]", "{\"tag\":5,\"version\":\"1.0.0\",\"url\":\"x\"}", "{\"tag\":\"v1\"}" })
        {
            File.WriteAllText(file, text);
            Assert.Null(UpdateNoticeCache.Read(root).Latest);
        }
        File.WriteAllText(file, "{\"tag\":\"v9.9.9\",\"version\":\"9.9.9\",\"url\":\"https://github.com/BlackNard/GameVault/releases/tag/v9.9.9\",\"pad\":\"" + new string('x', 20000) + "\"}");
        Assert.Equal(UpdateNoticeState.Empty, UpdateNoticeCache.Read(root));                 // refused as too large
        File.WriteAllText(file, "{\"tag\":\"v9.9.9\",\"version\":\"not a version\",\"url\":\"https://github.com/BlackNard/GameVault/releases/tag/v9.9.9\"}");
        Assert.False(UpdateNoticeCache.ShouldShow(UpdateNoticeCache.Read(root), V("0.1.0")));
        var blocked = Path.Combine(root, "plain-file");
        File.WriteAllText(blocked, "x");
        UpdateNoticeCache.WriteLatest(blocked, Info("0.2.0"));                                // "Cache" cannot be created under a file
        UpdateNoticeCache.Dismiss(blocked, "v0.2.0");
        Assert.Equal(UpdateNoticeState.Empty, UpdateNoticeCache.Read(blocked));
    }

    [Fact]
    public void A_release_link_is_checked_without_a_checker_instance()
    {
        Assert.True(UpdateChecker.IsReleaseLink("https://github.com/BlackNard/GameVault/releases/tag/v1.0.0"));
        Assert.False(UpdateChecker.IsReleaseLink("https://github.com/Other/GameVault/releases/tag/v1.0.0"));
        Assert.False(UpdateChecker.IsReleaseLink("https://evil.example/BlackNard/GameVault/releases"));
        Assert.True(UpdateChecker.IsReleaseLink("https://github.com/Other/Repo/releases/tag/v1", "Other/Repo"));
    }
}
