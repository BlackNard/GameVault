using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace GameVault.Infrastructure;

/// <summary>
/// One running GameVault per application folder (issue #69): starting it again asks the running one to show its window instead of failing on the folder lock.
/// The running copy listens on two named events in this Windows session; a second start sets the first and waits briefly for the second as the answer. Nothing is
/// sent but that signal.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SingleInstance : IDisposable
{
    private readonly EventWaitHandle request;
    private readonly EventWaitHandle answer;
    private RegisteredWaitHandle? wait;

    private SingleInstance(EventWaitHandle request, EventWaitHandle answer)
    {
        this.request = request;
        this.answer = answer;
    }

    private static string Name(string root, string kind)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
        return @"Local\GameVault-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..32] + "-" + kind;
    }

    /// <summary>
    /// Called by the copy that owns the folder. <paramref name="activate"/> runs on a background thread for every later start and returns whether the window
    /// was (or will be) shown; only then is the other start told it may end.
    /// </summary>
    public static SingleInstance Listen(string root, Func<bool> activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        var instance = new SingleInstance(new EventWaitHandle(false, EventResetMode.AutoReset, Name(root, "show")), new EventWaitHandle(false, EventResetMode.AutoReset, Name(root, "shown")));
        instance.wait = ThreadPool.RegisterWaitForSingleObject(instance.request, (_, _) =>
        {
            try { if (activate()) instance.answer.Set(); }
            catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException or OperationCanceledException) { }      // closing down: no answer
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        return instance;
    }

    /// <summary>
    /// Asks the copy already running from this folder to show itself. False when none is listening or it does not answer in time (then the caller carries on
    /// and reports the folder as it would have).
    /// </summary>
    public static bool TryActivateExisting(string root, TimeSpan timeout, Action? beforeSignal = null)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(Name(root, "show"), out var request)) return false;
            using (request)
            {
                if (!EventWaitHandle.TryOpenExisting(Name(root, "shown"), out var answer)) return false;
                using (answer)
                {
                    answer.Reset();
                    beforeSignal?.Invoke();
                    request.Set();
                    return answer.WaitOne(timeout);
                }
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        wait?.Unregister(null);
        wait = null;
        request.Dispose();
        answer.Dispose();
    }
}
