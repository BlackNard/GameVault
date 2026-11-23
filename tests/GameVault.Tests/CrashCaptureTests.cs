using GameVault.Infrastructure.Diagnostics;

namespace GameVault.Tests;

public sealed class CrashCaptureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault crash capture", Guid.NewGuid().ToString("N"));

    public CrashCaptureTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static readonly DateTimeOffset Start = new(2026, 11, 23, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_first_start_reports_nothing_and_a_clean_quit_leaves_no_marker()
    {
        var first = CrashMarker.Begin(root, "0.1.0.0", Start);
        Assert.False(first.PreviousRunEndedUnexpectedly);
        Assert.True(File.Exists(Path.Combine(root, "Logs", CrashMarker.FileName)));
        CrashMarker.EndCleanly(root);
        Assert.False(File.Exists(Path.Combine(root, "Logs", CrashMarker.FileName)));
        Assert.False(CrashMarker.Begin(root, "0.1.0.0", Start.AddHours(1)).PreviousRunEndedUnexpectedly);
    }

    [Fact]
    public void A_start_after_a_run_that_never_quit_cleanly_reports_when_it_started_and_which_version()
    {
        CrashMarker.Begin(root, "0.1.0.0", Start);                  // crashed: EndCleanly never ran
        var next = CrashMarker.Begin(root, "0.2.0.0", Start.AddHours(2));
        Assert.True(next.PreviousRunEndedUnexpectedly);
        Assert.Equal(Start, next.PreviousStartedAt);
        Assert.Equal("0.1.0.0", next.PreviousVersion);
        CrashMarker.EndCleanly(root);
        Assert.False(CrashMarker.Begin(root, "0.2.0.0", Start.AddHours(3)).PreviousRunEndedUnexpectedly);   // the second run quit cleanly
    }

    [Fact]
    public void A_damaged_marker_still_counts_as_an_unclean_end_and_never_throws()
    {
        Directory.CreateDirectory(Path.Combine(root, "Logs"));
        File.WriteAllText(Path.Combine(root, "Logs", CrashMarker.FileName), "garbage\u0000 not a date|" + new string('x', 500));
        var result = CrashMarker.Begin(root, "0.1.0.0", Start);
        Assert.True(result.PreviousRunEndedUnexpectedly);
        Assert.Null(result.PreviousStartedAt);
        Assert.Null(result.PreviousVersion);
    }

    [Fact]
    public void An_unwritable_folder_means_no_notice_instead_of_a_crash()
    {
        var blocked = Path.Combine(root, "file-not-folder");
        File.WriteAllText(blocked, "x");                            // "Logs" cannot be created under a file
        Assert.False(CrashMarker.Begin(blocked, "0.1.0.0", Start).PreviousRunEndedUnexpectedly);
        CrashMarker.EndCleanly(blocked);
    }

    [Fact]
    public void A_logged_error_carries_the_exception_type_message_stack_frames_and_inner_errors_on_one_line()
    {
        Exception thrown;
        try { Fail(); thrown = null!; }
        catch (InvalidOperationException error) { thrown = error; }
        var text = AppLog.Describe(thrown);
        Assert.Contains("InvalidOperationException: outer problem", text);
        Assert.Contains(nameof(Fail), text);
        Assert.Contains("<- inner IOException: disk\nfull".Replace('\n', ' '), text);
        Assert.DoesNotContain('\n', text);
        Assert.True(text.Split('|').Length <= 1 + 4 * 12);
    }

    private static void Fail()
    {
        try { throw new IOException("disk\r\nfull"); }
        catch (IOException inner) { throw new InvalidOperationException("outer problem", inner); }
    }
}
