using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace GameVault.Infrastructure.Launching;

// Stop for a game that runs as administrator: a one-shot elevated copy of GameVault closes or ends exactly the processes named.
public static partial class ElevationBroker
{
    private const string StopHelper = "--stop-helper";
    private const int MaxStopTargets = 64;

    internal static string[] StopArguments(bool force, ProcessIdentity[] processes) =>
        [StopHelper, force ? "end" : "close", .. processes.Take(MaxStopTargets).Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Id}:{p.CreatedUtcTicks}"))];

    internal static async Task StopElevatedAsync(bool force, ProcessIdentity[] processes, CancellationToken token)
    {
        var app = Environment.ProcessPath ?? throw new IOException("GameVault's executable could not be located.");
        var start = new ProcessStartInfo(app) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
        foreach (var argument in StopArguments(force, processes)) start.ArgumentList.Add(argument);
        // Declining Windows' prompt throws Win32Exception 1223 here.
        using var helper = Process.Start(start) ?? throw new IOException("Windows did not start the administrator helper.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await helper.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("The administrator helper did not answer in time."); }
        if (helper.ExitCode != 0) throw new IOException("The administrator helper could not stop the game.");
    }

    /// <summary>The helper's whole work; it opens no GameVault data. Returns 0 on success.</summary>
    public static int RunStopHelper(string[] args)
    {
        if (args.Length < 3 || args.Length > MaxStopTargets + 2 || args[0] != StopHelper || args[1] is not ("close" or "end")) return 1;
        var targets = new List<ProcessIdentity>();
        foreach (var text in args.Skip(2))
        {
            var parts = text.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
                !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var created)) return 1;
            targets.Add(new(id, created));
        }
        var system = new WindowsGameProcessSystem();
        var result = 0;
        foreach (var target in targets)
        {
            try { if (args[1] == "end") system.End(target); else system.RequestClose(target); }
            catch (Win32Exception) { result = 1; }
        }
        return result;
    }
}
