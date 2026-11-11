using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameVault.Infrastructure.Playtime;

public interface IPlaytimeClock
{
    long AwakeTicks { get; }
    DateTimeOffset UtcNow { get; }
}

public sealed class PlaytimeClock : IPlaytimeClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long AwakeTicks
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return (long)(Stopwatch.GetTimestamp() * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));
            if (!QueryUnbiasedInterruptTime(out var ticks))
                throw new IOException("Windows could not read the awake elapsed-time clock.");
            return checked((long)ticks);
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryUnbiasedInterruptTime(out ulong ticks);
}
