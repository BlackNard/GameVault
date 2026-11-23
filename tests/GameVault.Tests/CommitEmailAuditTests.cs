using System.Diagnostics;

namespace GameVault.Tests;

/// <summary>scripts/audit-commit-emails.ps1 keeps personal e-mail addresses out of the public history.</summary>
public sealed class CommitEmailAuditTests : IDisposable
{
    private const string Personal = "someone.private@example.org";
    private readonly string repo = Path.Combine(Path.GetTempPath(), "GameVault commit audit", Guid.NewGuid().ToString("N"));

    public CommitEmailAuditTests()
    {
        Directory.CreateDirectory(repo);
        Git("init", "-q");
    }

    public void Dispose() { try { foreach (var f in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(repo, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static string ScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "scripts", "audit-commit-emails.ps1");
    }

    private (int Code, string Output) Run(string file, IEnumerable<string> arguments, IDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = repo };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null) foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.Result + error.Result);
    }

    private string Git(params string[] arguments)
    {
        var (code, output) = Run("git", arguments);
        Assert.True(code == 0, "git " + string.Join(' ', arguments) + " failed: " + output);
        return output.Trim();
    }

    private string Commit(string author, string? committer = null, string message = "change")
    {
        var environment = new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = "A", ["GIT_AUTHOR_EMAIL"] = author,
            ["GIT_COMMITTER_NAME"] = "C", ["GIT_COMMITTER_EMAIL"] = committer ?? author
        };
        var (code, output) = Run("git", ["commit", "-q", "--allow-empty", "-m", message], environment);
        Assert.True(code == 0, output);
        return Git("rev-parse", "HEAD");
    }

    private (int Code, string Output) Audit(string? range = null)
    {
        var arguments = new List<string> { "-NoProfile", "-File", ScriptPath(), "-Repository", repo };
        if (range is not null) { arguments.Add("-Range"); arguments.Add(range); }
        return Run("pwsh", arguments);
    }

    [Fact]
    public void GitHub_no_reply_addresses_bots_and_the_assistant_trailer_pass()
    {
        Commit("142729322+BlackNard@users.noreply.github.com");
        Commit("friend@users.noreply.github.com");
        Commit("49699333+dependabot[bot]@users.noreply.github.com");
        Commit("142729322+BlackNard@users.noreply.github.com", "noreply@github.com");
        Commit("142729322+BlackNard@users.noreply.github.com", message: "Add a thing\n\nCo-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>");
        var (code, output) = Audit();
        Assert.True(code == 0, output);
        Assert.Contains("passed", output);
    }

    [Fact]
    public void Dependabots_signed_off_by_line_with_githubs_public_support_address_passes()
    {
        Commit("49699333+dependabot[bot]@users.noreply.github.com", "noreply@github.com", message: "Bump the actions-weekly group with 3 updates\n\nSigned-off-by: dependabot[bot] <support@github.com>");
        var (code, output) = Audit();
        Assert.True(code == 0, output);
    }

    [Fact]
    public void A_personal_author_address_fails_and_the_address_is_never_printed()
    {
        Commit("142729322+BlackNard@users.noreply.github.com");
        var bad = Commit(Personal);
        var (code, output) = Audit();
        Assert.Equal(1, code);
        Assert.Contains(bad[..10], output);
        Assert.Contains("author", output);
        Assert.DoesNotContain(Personal, output);
        Assert.DoesNotContain("example.org", output);
    }

    [Fact]
    public void A_personal_committer_address_alone_fails()
    {
        Commit("142729322+BlackNard@users.noreply.github.com", Personal);
        var (code, output) = Audit();
        Assert.Equal(1, code);
        Assert.Contains("committer", output);
        Assert.DoesNotContain(Personal, output);
    }

    [Fact]
    public void A_personal_address_in_a_co_author_trailer_fails()
    {
        Commit("142729322+BlackNard@users.noreply.github.com", message: "Pair work\n\nCo-authored-by: Pat <" + Personal + ">");
        var (code, output) = Audit();
        Assert.Equal(1, code);
        Assert.Contains("Co-authored-by", output);
        Assert.DoesNotContain(Personal, output);
    }

    [Fact]
    public void Only_the_commits_in_the_range_are_checked()
    {
        Commit(Personal);
        var good = Commit("142729322+BlackNard@users.noreply.github.com");
        var firstOnly = Audit("HEAD~1..HEAD");
        Assert.True(firstOnly.Code == 0, firstOnly.Output);
        Assert.Contains("1 commit", firstOnly.Output);
        Assert.Equal(1, Audit().Code);          // the default window includes the old commit
        Assert.NotEmpty(good);
    }

    [Fact]
    public void A_bad_range_is_an_error_not_a_pass()
    {
        Commit("142729322+BlackNard@users.noreply.github.com");
        Assert.NotEqual(0, Audit("does-not-exist..HEAD").Code);
    }
}
