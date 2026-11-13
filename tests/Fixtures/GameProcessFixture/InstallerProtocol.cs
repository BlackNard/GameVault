using System.Runtime.InteropServices;
using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure;

namespace GameVault.InstallerFixture;

internal static class InstallerProtocol
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new IOException("Installed fixture protocol is restricted to disposable GitHub runners.");
        if (args.Length != 4 || !Guid.TryParseExact(args[2], "N", out var token) || !Guid.TryParseExact(args[3], "N", out var request))
            throw new IOException("Invalid installer fixture request.");
        var root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var command = args[1];
        if (command is not ("seed" or "inspect" or "hold")) throw new IOException("Unknown fixture command.");
        var output = Path.Combine(root, "fixture-result-" + request.ToString("N") + ".json");
        // Inspect validates the ownership marker even before the hold operation.
        InstallerSummary summary = command == "seed" ? await InstallerLibrary.SeedAsync(root, token) : await InstallerLibrary.InspectAsync(root, token);
        using var lease = command == "hold" ? ApplicationWorkspace.Open(ApplicationPaths.FromRoot(root)) : null;
        var report = new { ok = true, summary, fingerprint = InstallerLibrary.Fingerprint(root),
            runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(), infrastructure = typeof(ApplicationWorkspace).Assembly.Location,
            processId = Environment.ProcessId, command, request };
        var temporary = output + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        File.Move(temporary, output, false);
        if (command == "hold")
        {
            var stop = Path.Combine(root, "fixture-stop-" + request.ToString("N"));
            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (!File.Exists(stop) && DateTime.UtcNow < deadline) await Task.Delay(50);
        }
        return 0;
    }
}
