namespace GameVault.Core;

/// <summary>What is appearing; each kind has one short transition (issue #55).</summary>
public enum MotionKind { None, Page, Section, Fade, Drawer, Dialog, Toast, Theme }

/// <summary>How long a transition takes and where the element comes from, in screen units.</summary>
public sealed record MotionSpec(int Milliseconds, double OffsetX = 0, double OffsetY = 0);

public static class MotionRules
{
    /// <summary>Nothing moves with Reduce motion on, or when Windows itself has animations turned off.</summary>
    public static bool Enabled(bool reduceMotion, bool windowsAnimations) => !reduceMotion && windowsAnimations;

    public static MotionSpec For(MotionKind kind) => kind switch
    {
        MotionKind.Page => new(180, 0, 10),
        MotionKind.Section => new(150, 0, 8),
        MotionKind.Fade => new(150),
        MotionKind.Drawer => new(220, 40, 0),
        MotionKind.Dialog => new(150, 0, -8),
        MotionKind.Toast => new(180, 24, 0),
        MotionKind.Theme => new(240),
        _ => new(0)
    };
}

public static class ListSync
{
    /// <summary>Makes <paramref name="shown"/> equal <paramref name="wanted"/> without recreating what stays, so only new rows animate in.</summary>
    public static void Apply<T>(IList<T> shown, IReadOnlyList<T> wanted)
    {
        for (var i = shown.Count - 1; i >= 0; i--)
            if (!wanted.Contains(shown[i])) shown.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < shown.Count && EqualityComparer<T>.Default.Equals(shown[i], wanted[i])) continue;
            var at = shown.IndexOf(wanted[i]);
            if (at >= 0) shown.RemoveAt(at);
            shown.Insert(i, wanted[i]);
        }
    }
}
