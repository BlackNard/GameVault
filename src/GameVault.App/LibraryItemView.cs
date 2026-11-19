using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using GameVault.Core;

namespace GameVault.App;

internal sealed class LibraryItemView(LibraryGame game, Func<string, CancellationToken, Task<BitmapImage>>? coverLoader = null) : INotifyPropertyChanged
{
    private CancellationTokenSource? coverRequest;
    private int coverGeneration;
    private LaunchState? launch;
    private SessionProgress? session;
    public LibraryGame Game { get; private set; } = game;
    public string Name => Game.Profile?.Name ?? "Profile needs attention";
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string Status => Game.Profile is null ? "Read-only profile" : StatusText(Game.Profile.Status);
    public string Availability => launch?.IsActive == true ? launch.Phase == LaunchPhase.Starting ? "Starting game…" : "Running" :
        launch?.Phase == LaunchPhase.Failed ? "Launch needs attention · see game details" : Game.Profile is null ? "See the storage notice for details." :
        Game.IsReadOnly ? "Read-only · profile needs attention" :
        Game.ExecutableMissing ? "Game not found · edit its location" : Game.Profile.IsArchived ? "Removed · profile retained" : "Ready in your library";
    public string Played => (session?.TotalSeconds ?? Game.PlayedSeconds) is not long seconds ? "Playtime unavailable" :
        seconds <= 0 ? "No recorded playtime" : FormatSeconds(seconds);
    public string SessionTime => session is null ? "No active session" :
        $"{(session.IsActive ? "This session" : "Last session")}: {FormatSeconds(session.SessionSeconds)}";
    public string? SessionWarning => session?.Warning;
    public BitmapImage? Cover { get; private set; }
    private int iconGeneration;
    private bool iconLoading;
    /// <summary>Small cover for the left rail. Loaded on request and not released when a grid card scrolls away.</summary>
    public BitmapImage? RailIcon { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetLaunchState(LaunchState state)
    { launch = state; PropertyChanged?.Invoke(this, new(nameof(Availability))); }

    public void SetStoredProgress(SessionProgress? progress) => SetSessionProgress(progress is { IsActive: false } && Game.PlayedSeconds is long seconds ? progress with { TotalSeconds = seconds } : progress);

    public void SetSessionProgress(SessionProgress? progress)
    {
        session = progress;
        foreach (var name in new[] { nameof(Played), nameof(SessionTime), nameof(SessionWarning) }) PropertyChanged?.Invoke(this, new(name));
    }

    public static string FormatSeconds(long seconds) => $"{seconds / 3600}h {seconds % 3600 / 60:00}m {seconds % 60:00}s";

    public void RefreshProfile(GameProfile profile)
    {
        if (profile.Id != Game.Id) return;
        Game = Game with { Profile = profile };
        foreach (var name in new[] { nameof(Name), nameof(Initial), nameof(Status), nameof(Availability) })
            PropertyChanged?.Invoke(this, new(name));
    }

    public void RefreshGame(LibraryGame game)
    {
        if (game.Id != Game.Id) throw new ArgumentException("A library row must retain its game identity.", nameof(game));
        CoverImages.InvalidateThumbnail(Game.CoverFile);
        iconGeneration++; RailIcon = null; PropertyChanged?.Invoke(this, new(nameof(RailIcon)));
        ReleaseCover();
        Game = game;
        if (session is { IsActive: false } && game.PlayedSeconds is long seconds) session = session with { TotalSeconds = seconds };
        foreach (var name in new[] { nameof(Game), nameof(Name), nameof(Initial), nameof(Status), nameof(Availability), nameof(Played), nameof(SessionTime), nameof(SessionWarning) })
            PropertyChanged?.Invoke(this, new(name));
    }

    public async Task LoadRailIconAsync()
    {
        if (RailIcon is not null || iconLoading || Game.CoverFile is not string file) return;
        iconLoading = true;
        var generation = iconGeneration;
        try
        {
            var image = await CoverImages.LoadIconAsync(file);
            if (generation != iconGeneration) return;
            RailIcon = image;
            PropertyChanged?.Invoke(this, new(nameof(RailIcon)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or OperationCanceledException or System.Runtime.InteropServices.COMException) { }
        finally { iconLoading = false; }
    }

    public void ReleaseCover()
    {
        coverGeneration++;
        coverRequest?.Cancel(); coverRequest?.Dispose(); coverRequest = null;
        Cover = null;
        PropertyChanged?.Invoke(this, new(nameof(Cover)));
    }

    public async Task LoadCoverAsync()
    {
        if (coverRequest is not null || Cover is not null || Game.CoverFile is null) return;
        var request = new CancellationTokenSource(); coverRequest = request;
        var generation = coverGeneration;
        try {
            var image = await (coverLoader ?? CoverImages.LoadThumbnailAsync)(Game.CoverFile, request.Token);
            if (generation != coverGeneration || request.IsCancellationRequested) return;
            Cover = image;
            PropertyChanged?.Invoke(this, new(nameof(Cover)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or OperationCanceledException or System.Runtime.InteropServices.COMException) { }
        finally { if (ReferenceEquals(coverRequest, request)) { coverRequest = null; request.Dispose(); } }
    }
    public static string StatusText(GameStatus status) => status switch
    { GameStatus.NotStarted => "Not started", GameStatus.OnHold => "On hold", _ => status.ToString() };
}
