using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace GameVault.App;

public partial class MainWindow
{
    private readonly DispatcherTimer filterTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(75)};
    private readonly Dictionary<FrameworkElement,LibraryItemView> visibleCoverRows=[];
    private Dictionary<Guid,LibraryItemView> libraryRows=[];
    private GameVault.Core.LibraryGame? summarizedGame;
    private void InitializePerformanceFeatures()
    {
        filterTimer.Tick+=(_,_)=>{filterTimer.Stop();if(!stoppingObservation)ApplyFilters((GameList.SelectedItem as LibraryItemView)?.Game.Id);};
    }
    private async Task BindCoverRowAsync(FrameworkElement row)
    {
        if(visibleCoverRows.TryGetValue(row,out var previous) && !ReferenceEquals(previous,row.DataContext))
        {
            visibleCoverRows.Remove(row);
            if(!visibleCoverRows.Values.Contains(previous))previous.ReleaseCover();
        }
        if(!row.IsLoaded || row.DataContext is not LibraryItemView item)return;
        visibleCoverRows[row]=item;await item.LoadCoverAsync();
    }
    private void CoverRowUnloaded(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement row || !visibleCoverRows.Remove(row,out var item))return;
        if(!visibleCoverRows.Values.Contains(item))item.ReleaseCover();
    }
    private void ReloadVisibleCovers()
    {
        foreach(var item in visibleCoverRows.Values.Distinct().ToArray())_=item.LoadCoverAsync();
    }
    private void DisposePerformanceFeatures()
    {
        filterTimer.Stop();foreach(var item in visibleCoverRows.Values.Distinct())item.ReleaseCover();visibleCoverRows.Clear();
    }
}
