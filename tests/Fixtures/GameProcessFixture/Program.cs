using System.Diagnostics;

// Windowless, bounded, test-owned fixture. Never reads real game data.
var directory = Environment.CurrentDirectory;
var readyTemporary = Path.Combine(directory, "ready.tmp");
await File.WriteAllTextAsync(readyTemporary, Environment.ProcessId.ToString());
// Publish only after the writer has closed, so observing readiness guarantees a readable, complete PID.
File.Move(readyTemporary, Path.Combine(directory, "ready.txt"), overwrite: true);
var childFile = Path.Combine(directory, "child.txt");
if (File.Exists(childFile))
{
    var child = (await File.ReadAllTextAsync(childFile)).Trim();
    using var process = Process.Start(new ProcessStartInfo(child) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(child)! });
    return;
}
var handoffFile = Path.Combine(directory, "handoff.txt");
if (File.Exists(handoffFile))
{
    var child = (await File.ReadAllTextAsync(handoffFile)).Trim();
    var childWorking = (await File.ReadAllTextAsync(Path.Combine(directory, "handoff-working.txt"))).Trim();
    using var process = Process.Start(new ProcessStartInfo(child) { UseShellExecute = false, WorkingDirectory = childWorking });
    await Task.Delay(300);
    return;
}
var deadline = Stopwatch.StartNew();
while (!File.Exists(Path.Combine(directory, "exit.txt")) && deadline.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
