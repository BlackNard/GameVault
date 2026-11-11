using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using GameVault.Core;

namespace GameVault.App;

internal sealed class LibraryItemView(LibraryGame game) : INotifyPropertyChanged
{
    private bool coverRequested;
    private LaunchState? launch;
    public LibraryGame Game { get; private set; } = game;
    public string Name => Game.Profile?.Name ?? "Profile needs attention";
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string Status => Game.Profile is null ? "Read-only profile" : StatusText(Game.Profile.Status);
    public string Availability => launch?.IsActive == true ? launch.Phase == LaunchPhase.Starting ? "Starting game…" : "Running" :
        launch?.Phase == LaunchPhase.Failed ? "Launch needs attention · see game details" : Game.Profile is null ? "See the storage notice for details." :
        Game.IsReadOnly ? "Read-only · profile needs attention" :
        Game.ExecutableMissing ? "Game not found · edit its location" : Game.Profile.IsArchived ? "Removed · profile retained" : "Ready in your library";
    public string Played => Game.PlayedSeconds is not long seconds ? "Playtime unavailable" :
        seconds <= 0 ? "No recorded playtime" : $"{seconds / 3600}h {seconds % 3600 / 60}m recorded";
    public BitmapImage? Cover { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetLaunchState(LaunchState state)
    { launch = state; PropertyChanged?.Invoke(this, new(nameof(Availability))); }

    public void RefreshProfile(GameProfile profile)
    {
        if (profile.Id != Game.Id) return;
        Game = Game with { Profile = profile };
        foreach (var name in new[] { nameof(Name), nameof(Initial), nameof(Status), nameof(Availability) })
            PropertyChanged?.Invoke(this, new(name));
    }

    public async Task LoadCoverAsync()
    {
        if (coverRequested || Game.CoverFile is null) return;
        coverRequested = true;
        try { Cover = await Task.Run(() => CoverImages.Load(Game.CoverFile, 240)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException) { return; }
        PropertyChanged?.Invoke(this, new(nameof(Cover)));
    }

    public static string StatusText(GameStatus status) => status switch
    { GameStatus.NotStarted => "Not started", GameStatus.OnHold => "On hold", _ => status.ToString() };
}
