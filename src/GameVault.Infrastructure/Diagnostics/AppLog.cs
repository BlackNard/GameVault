using System.Text;

namespace GameVault.Infrastructure.Diagnostics;

/// <summary>A tiny rolling text log (Logs/gamevault.log, previous copy .old) so bug reports have something to read. Never throws.</summary>
public static class AppLog
{
    public const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static string? file;

    public static void Initialize(string root)
    {
        lock (Gate) file = Path.Combine(root, "Logs", "gamevault.log");
        Info("GameVault started");
    }

    public static string? FilePath { get { lock (Gate) return file; } }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? error = null) => Write("ERROR", error is null ? message : $"{message}: {error.GetType().Name}: {error.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                if (file is null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytes) File.Move(file, file + ".old", overwrite: true);
                File.AppendAllText(file, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {level} {message.ReplaceLineEndings(" ")}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The newest lines, newest last, from the current and previous log files.</summary>
    public static string[] Tail(string logFile, int lines)
    {
        try
        {
            var all = new List<string>();
            foreach (var candidate in new[] { logFile + ".old", logFile })
                if (File.Exists(candidate)) all.AddRange(File.ReadAllLines(candidate));
            return [.. all.TakeLast(lines)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }
}
