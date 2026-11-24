using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Metadata;

namespace GameVault.App;

// Game information: description, release date, developers, genres and install size (ticket #83).
public partial class MainWindow
{
    private GameMetadataService? metadataService;
    private Guid aboutFor;
    private CancellationTokenSource? bulkInfo;

    // Order matters: it is the default order in which sources are asked. The store a game came from answers first, then the open databases, then the game file itself.
    private readonly WikidataProvider wikidata = new();
    private GameMetadataService Metadata => metadataService ??= CreateMetadataService();
    private GameMetadataService CreateMetadataService()
    {
        var service = new GameMetadataService(store, [new LocalInstallProvider(), new SteamStoreProvider(), new EpicStoreProvider(), new GogStoreProvider(), wikidata, new HowLongToBeatProvider(wikidata), new LocalFileProvider()]);
        // Facts are saved in stages while a lookup runs; show each stage on the open game page as soon as it arrives.
        service.FactsUpdated += (_, id) =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            _ = Dispatcher.InvokeAsync(() => { if (GameList.SelectedItem is LibraryItemView open && open.Game.Id == id) _ = RefreshAboutAsync(open); });
        };
        return service;
    }

    private static string SizeText(long bytes) => bytes switch
    {
        < 1024L * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
    };

    private async Task RefreshAboutAsync(LibraryItemView? item)
    {
        if (item?.Game.Profile is null) { AboutBlock.Visibility = Visibility.Collapsed; return; }
        var id = item.Game.Id;
        aboutFor = id;
        GameMetadataDocument? document = null;
        string? problem = null;
        try
        {
            var stored = await Metadata.LoadAsync(id);
            document = stored.Value;
            problem = stored.IsReadOnly ? stored.Message : null;
        }
        catch (Exception error) when (IsExpected(error)) { problem = error.Message; }
        if (aboutFor != id) return;
        AboutBlock.Visibility = Visibility.Visible;
        var description = document?.Get(MetadataField.Description);
        var lookingUp = lookupsInProgress.Contains(id);
        AboutLookupBar.Visibility = lookingUp ? Visibility.Visible : Visibility.Collapsed;
        AboutText.Text = description?.Text ?? (lookingUp ? "Looking up information about this game… the facts appear here as soon as they arrive." : document?.Identity is { NoMatch: true } ? "You said no listed game matches, so nothing is looked up for this one. Write your own description, or choose Not the right game? to pick one." : "No description yet. Information is looked up automatically; if this game could not be matched (several games share its title), choose Not the right game? to pick the correct one, or write your own description.");
        AboutFacts.Children.Clear();
        void Fact(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var panel = new StackPanel { Margin = new Thickness(0, 0, 32, 8) };
            var caption = new TextBlock { Text = label };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
            panel.Children.Add(caption);
            panel.Children.Add(new TextBlock { Text = value, Margin = new Thickness(0, 2, 0, 0) });
            AboutFacts.Children.Add(panel);
        }
        Fact("Released", document?.Get(MetadataField.ReleaseDate)?.Date is { } date ? PartialDate.Parse(date)?.Display() : null);
        Fact("Developer", Join(document?.Get(MetadataField.Developers)?.Items));
        Fact("Publisher", Join(document?.Get(MetadataField.Publishers)?.Items));
        Fact("Genres", Join(document?.Get(MetadataField.Genres)?.Items));
        Fact("Install size", document?.Get(MetadataField.InstallSize)?.Bytes is long bytes ? SizeText(bytes) : null);
        Fact("Rated", document?.Get(MetadataField.AgeRating)?.Text);
        Fact("Platforms", Join(document?.Get(MetadataField.Platforms)?.Items));
        Fact("Metacritic", document?.Get(MetadataField.CriticScore)?.Number is int score ? score.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        Fact("Player reviews", document?.Get(MetadataField.CommunitySummary)?.Text);
        Fact("Achievements", document?.Get(MetadataField.AchievementCount)?.Number is int achievements ? achievements.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        Fact("DLC", document?.Get(MetadataField.DlcCount)?.Number is int dlc ? dlc.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        Fact("Installed", Join(document?.Get(MetadataField.Installed)?.Items?.Select(i => i).ToArray()));
        RenderAboutDetails(document);
        var sources = document?.Fields.Values.Select(v => v.Provider).Distinct().Select(p => p switch { "steam" => "Steam store", "local" => "this PC", "you" => "you", var other => other }).ToArray();
        AboutSource.Text = sources is { Length: > 0 } ? "Information from " + string.Join(", ", sources) + "." : "";
        AboutNotice.Text = problem ?? "";
        RefreshInfoButton.IsEnabled = EditDescriptionButton.IsEnabled = WrongGameButton.IsEnabled = problem is null && !item.Game.IsReadOnly;
    }

    private static string? Join(string[]? items) => items is { Length: > 0 } ? string.Join(", ", items) : null;

    private async void RefreshInfoClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not LibraryItemView { Game.Profile: { IsArchived: false } } item || bulkInfo is not null) return;
        var id = item.Game.Id;
        RefreshInfoButton.IsEnabled = false;
        AboutNotice.Text = "Looking up information…";
        try
        {
            var result = await Task.Run(() => Metadata.RefreshAsync(id, true, CancellationToken.None));
            AboutNotice.Text = "";
            // Refresh info is an explicit request: a game without a cover (also one the player removed) gets its store cover again.
            await library.AllowCoverAgainAsync(id);
            var covers = await AutoCoversAsync([(id, item.Name)], CancellationToken.None);
            if (await EnsureHeroesAsync([(id, item.Name)], CancellationToken.None) > 0 || covers > 0) await RefreshLibrary(id);
            ShowToast(result.FieldsFilled > 0 ? "Game information updated" : "Nothing new found", result.Problems.Length > 0 ? string.Join(" ", result.Problems) : result.UsedNetwork ? "Steam store" : "Your PC", result.Problems.Length > 0);
        }
        catch (Exception error) when (IsExpected(error)) { ShowToast("Could not update the information", error.Message, true); }
        finally { RefreshInfoButton.IsEnabled = true; await RefreshAboutAsync(GameList.SelectedItem as LibraryItemView); }
    }

    private async void EditDescriptionClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not LibraryItemView { Game.Profile: { IsArchived: false } } item) return;
        var stored = await Metadata.LoadAsync(item.Game.Id);
        DescriptionInput.Text = stored.Value?.Get(MetadataField.Description)?.Text ?? "";
        DescriptionEditor.Visibility = Visibility.Visible;
        DescriptionInput.Focus();
    }

    private async void WrongGameClicked(object sender, RoutedEventArgs e)
    {
        if (busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this) || bulkInfo is not null || autoInfoRunning) { ShowToast("GameVault is busy", "Wait for the current lookup to finish, then try again.", true); return; }
        if (GameList.SelectedItem is not LibraryItemView { Game.Profile: { IsArchived: false } } item) return;
        var id = item.Game.Id;
        editorOpen = true;
        UpdateButtons();
        try
        {
            var view = new MatchPickerView(settings.Theme, item.Name, (title, token) => Metadata.SearchMatchesAsync(title, token), async candidate =>
            {
                await library.ClearHeroAsync(id);   // the old picture belongs to the game that was just ruled out
                var result = await Task.Run(() => Metadata.ApplyMatchAsync(id, candidate, CancellationToken.None));
                return candidate is null ? "Saved. Nothing will be looked up automatically for this game." : result.FieldsFilled > 0 ? $"Found {result.FieldsFilled} fact(s) for {candidate.Title}." : $"Saved {candidate.Title}. No facts were available for it yet.";
            });
            await ShowInlineEditorAsync(view, h => view.Completed += h, h => view.Completed -= h);
        }
        catch (Exception error) when (IsExpected(error)) { ShowToast("Could not change the match", error.Message, true); }
        finally
        {
            editorOpen = false;
            try
            {
                UpdateButtons(); ContinueDeferredQuit();
                if (await EnsureHeroesAsync([(id, item.Name)], CancellationToken.None) > 0 || libraryRows.TryGetValue(id, out var changed) && changed.Game.HeroFile is not null) await RefreshLibrary(id);
                await RefreshAboutAsync(GameList.SelectedItem as LibraryItemView);
            }
            finally { CompleteInlineEditor(); }
        }
    }

    private void CancelDescriptionClicked(object sender, RoutedEventArgs e) => DescriptionEditor.Visibility = Visibility.Collapsed;

    private async void SaveDescriptionClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not LibraryItemView { Game.Profile: { IsArchived: false } } item) return;
        try
        {
            await Metadata.SaveDescriptionAsync(item.Game.Id, DescriptionInput.Text);
            DescriptionEditor.Visibility = Visibility.Collapsed;
            ShowToast("Description saved", "Your text is never replaced by a lookup.");
        }
        catch (Exception error) when (IsExpected(error)) { ShowToast("Could not save the description", error.Message, true); }
        await RefreshAboutAsync(item);
    }

    // ---------- settings: Game information ----------
    private static readonly (MetadataField Field, string Label)[] FieldLabels =
    [
        (MetadataField.Description, "Description"), (MetadataField.ReleaseDate, "Release date"), (MetadataField.Developers, "Developers"),
        (MetadataField.Publishers, "Publishers"), (MetadataField.Genres, "Genres"), (MetadataField.InstallSize, "Install size"), (MetadataField.Hero, "Background image"),
        (MetadataField.About, "Long description"), (MetadataField.Tags, "Popular tags"), (MetadataField.Features, "Features"), (MetadataField.Platforms, "Platforms"), (MetadataField.Languages, "Languages"),
        (MetadataField.AgeRating, "Age rating"), (MetadataField.CriticScore, "Metacritic score"), (MetadataField.CommunitySummary, "Player reviews"), (MetadataField.Requirements, "System requirements"),
        (MetadataField.Links, "Links"), (MetadataField.AchievementCount, "Achievement count"), (MetadataField.DlcCount, "DLC count"), (MetadataField.Screenshots, "Screenshots"), (MetadataField.Installed, "Installed version info"), (MetadataField.TimeToBeat, "How long to beat"),
    ];

    private async void RefreshMetadataSettingsUi()
    {
        MetadataSettings current;
        bool readOnly;
        try { current = await Metadata.LoadSettingsAsync(); readOnly = await Metadata.SettingsReadOnlyAsync(); }
        catch (Exception error) when (IsExpected(error)) { current = MetadataSettings.Default; readOnly = true; }
        MetadataFields.Children.Clear();
        var capable = Metadata.CapableProviders();
        var names = Metadata.Providers.ToDictionary(p => p.Id, p => p.DisplayName);

        async void Save(MetadataSettings next)
        {
            try { await Metadata.SaveSettingsAsync(next); current = next; }
            catch (Exception error) when (IsExpected(error)) { ShowToast("Could not save the settings", error.Message, true); }
        }
        var onlyEmpty = new CheckBox { Content = "Only fill fields that are empty (never replace a value that is already there)", IsChecked = current.OnlyFillEmpty, IsEnabled = !readOnly, Margin = new Thickness(0, 0, 0, 8) };
        onlyEmpty.Click += (_, _) => Save(current with { OnlyFillEmpty = onlyEmpty.IsChecked == true });
        var onDisk = new CheckBox { Content = "Measure install size as size on disk (whole 4 KB clusters)", IsChecked = current.SizeOnDisk, IsEnabled = !readOnly, Margin = new Thickness(0, 0, 0, 12) };
        onDisk.Click += (_, _) => Save(current with { SizeOnDisk = onDisk.IsChecked == true });
        var automatic = new CheckBox { Content = "Look up information and a missing Steam cover automatically for new games and games that have none yet", IsChecked = current.AutoFetch, IsEnabled = !readOnly, Margin = new Thickness(0, 0, 0, 8) };
        automatic.Click += (_, _) => Save(current with { AutoFetch = automatic.IsChecked == true });
        MetadataFields.Children.Add(automatic);
        MetadataFields.Children.Add(onlyEmpty);
        MetadataFields.Children.Add(onDisk);
        foreach (var (field, label) in FieldLabels)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var enabled = new CheckBox { Content = label, IsChecked = current.IsEnabled(field), IsEnabled = !readOnly, MinWidth = 170, VerticalAlignment = VerticalAlignment.Center };
            var captured = field;
            enabled.Click += (_, _) =>
            {
                var disabled = current.DisabledFields.Where(f => f != captured.ToString()).ToList();
                if (enabled.IsChecked != true) disabled.Add(captured.ToString());
                Save(current with { DisabledFields = [.. disabled] });
            };
            row.Children.Add(enabled);
            var providers = capable[field];
            if (providers.Length > 1)
            {
                var order = new ComboBox { Width = 260, IsEnabled = !readOnly };
                var ordered = current.OrderFor(field, providers);
                foreach (var id in providers) order.Items.Add(new ComboBoxItem { Content = "First ask: " + names[id], Tag = id });
                order.SelectedItem = order.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == ordered[0]);
                order.SelectionChanged += (_, _) =>
                {
                    if (order.SelectedItem is not ComboBoxItem { Tag: string first }) return;
                    var rest = providers.Where(p => p != first);
                    var map = new Dictionary<string, string[]>(current.ProviderOrder) { [captured.ToString()] = [first, .. rest] };
                    Save(current with { ProviderOrder = map });
                };
                row.Children.Add(order);
            }
            else
            {
                var only = new TextBlock { Text = providers.Length == 0 ? "no source" : "from " + names[providers[0]], VerticalAlignment = VerticalAlignment.Center };
                only.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
                row.Children.Add(only);
            }
            MetadataFields.Children.Add(row);
        }
    }

    private async void FetchAllInfoClicked(object sender, RoutedEventArgs e)
    {
        if (bulkInfo is not null || busy) return;
        var games = items.Where(i => i.Game.Profile is { IsArchived: false } && !i.Game.IsReadOnly).Select(i => (i.Game.Id, i.Name)).ToArray();
        if (games.Length == 0) { FetchAllProgress.Text = "There are no games to look up."; return; }
        bulkInfo = new CancellationTokenSource();
        FetchAllInfoButton.IsEnabled = false;
        CancelFetchAllButton.Visibility = Visibility.Visible;
        var progress = new Progress<(int Done, int Total, string Name)>(p => FetchAllProgress.Text = p.Name.Length == 0 ? $"Done: {p.Done} of {p.Total}." : $"Looking up {p.Name} ({p.Done + 1} of {p.Total})…");
        try
        {
            var result = await Task.Run(() => Metadata.FetchMissingAsync(games, progress, bulkInfo.Token));
            FetchAllProgress.Text = (result.Cancelled ? "Stopped. " : "") + $"{result.GamesUpdated} of {result.Games} game(s) updated, {result.FieldsFilled} fact(s) added." + (result.Problems.Length > 0 ? " Some lookups had problems: " + string.Join(" ", result.Problems.Take(3)) : "");
        }
        catch (Exception error) when (IsExpected(error)) { FetchAllProgress.Text = "Could not finish: " + error.Message; }
        finally
        {
            bulkInfo.Dispose(); bulkInfo = null;
            FetchAllInfoButton.IsEnabled = true;
            CancelFetchAllButton.Visibility = Visibility.Collapsed;
            if (GameList.SelectedItem is LibraryItemView selected) await RefreshAboutAsync(selected);
        }
    }

    private void CancelFetchAllClicked(object sender, RoutedEventArgs e) => bulkInfo?.Cancel();
}
