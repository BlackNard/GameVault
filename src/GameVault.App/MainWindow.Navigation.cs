using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameVault.Core;
using GameVault.Infrastructure.Saves;
namespace GameVault.App;

public partial class MainWindow
{
 private InlineEditorSession? activeInlineEditor;
 private string gameSection="Overview";
 private BackupDestinationService Destinations=>new(paths);
 private bool libraryVisible=true;
 private void RestoreLibraryFocus()
 {
  _=Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
  {
   if(!libraryVisible||editorOpen||InlinePrompts.IsOpen(this))return;
   if(GameList.SelectedItem is not LibraryItemView item){Search.Focus();return;}
   GameList.ScrollIntoView(item);
   _=Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
   {
    if(!libraryVisible||editorOpen||InlinePrompts.IsOpen(this))return;
    if(GameList.ItemContainerGenerator.ContainerFromItem(item) is DependencyObject container && FindCardButton(container) is Button card)card.Focus();else GameList.Focus();
   }));
  }));
 }
 private static Button? FindCardButton(DependencyObject node)
 {
  if(node is Button button)return button;
  for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)
   if(FindCardButton(System.Windows.Media.VisualTreeHelper.GetChild(node,i)) is Button child)return child;
  return null;
 }
 private CancellationTokenSource? heroCoverRequest;
 private LibraryGame? heroCoverGame;
 private async Task RefreshHeroCoverAsync(LibraryItemView item)
 {
  if(ReferenceEquals(heroCoverGame,item.Game))return;
  heroCoverGame=item.Game;heroCoverRequest?.Cancel();heroCoverRequest?.Dispose();heroCoverRequest=new();var token=heroCoverRequest.Token;GameHeroCover.Source=null;
  if(item.Game.CoverFile is not string file)return;
  try{var image=await CoverImages.LoadThumbnailAsync(file,token);if(!token.IsCancellationRequested&&GameList.SelectedItem is LibraryItemView selected&&selected.Game.Id==item.Game.Id)GameHeroCover.Source=image;}
  catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or OperationCanceledException or System.Runtime.InteropServices.COMException){ }
 }
 private void InitializeRedesign()
 {
  WindowChromeBehavior.Apply(this,MaximizeCaptionButton);
  Loaded+=async(_,_)=>await RefreshBackupDestinationsAsync();
 }
 private void ToggleGameOptions(object sender,RoutedEventArgs e)=>GameOptionsPopup.IsOpen=!GameOptionsPopup.IsOpen;
 private void MinimizeWindow(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
 private void MaximizeWindow(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
 private void CloseWindow(object sender,RoutedEventArgs e)=>Close();
 private async void BackToLibrary(object sender,RoutedEventArgs e){if(!editorOpen&&await LeaveNotes())SetNavigation(true);}
 private async void OpenGameCard(object sender,RoutedEventArgs e)
 {
  if(busy||editorOpen||quitPreparing||stoppingObservation||InlinePrompts.IsOpen(this)||sender is not FrameworkElement{DataContext:LibraryItemView item})return;
  if(!await LeaveNotes())return;
  GameList.SelectedItem=item;ShowSelection();ShowGamePage();ShowGameSection("Overview");
 }
 private void LibraryKeyDown(object sender,KeyEventArgs e)
 {
  if(e.Key!=Key.Enter||InlinePrompts.IsOpen(this)||GameList.SelectedItem is not LibraryItemView)return;
  e.Handled=true;var item=GameList.SelectedItem as LibraryItemView;
  var button=new Button{DataContext=item};OpenGameCard(button,new RoutedEventArgs());
 }
 private void ShowGamePage()
 {
  libraryVisible=false;LibraryPanel.Visibility=SettingsPanel.Visibility=OverviewPanel.Visibility=Visibility.Collapsed;GamePage.Visibility=Visibility.Visible;
  if(GameList.SelectedItem is LibraryItemView item)_=RefreshHeroCoverAsync(item);
 }
 private async void GameTabClicked(object sender,RoutedEventArgs e)
 {
  if(busy||editorOpen||quitPreparing||stoppingObservation||InlinePrompts.IsOpen(this)||sender is not Button{Tag:string section})return;
  if(!await LeaveNotes())return;
  ShowGamePage();ShowGameSection(section);
  if(section=="Sessions")OpenNotes(sender,e);
 }
 private void ShowGameSection(string section)
 {
  gameSection=section;DetailFields.Visibility=Visibility.Visible;
  OverviewSection.Visibility=section=="Overview"?Visibility.Visible:Visibility.Collapsed;
  SavesSection.Visibility=section=="Saves"?Visibility.Visible:Visibility.Collapsed;
  MediaSection.Visibility=section=="Media"?Visibility.Visible:Visibility.Collapsed;
  foreach(var button in new[]{OverviewTab,NotesButton,SavesTab,MediaTab}){var on=(string?)button.Tag==section;if(on){button.SetResourceReference(Control.BorderBrushProperty,"AppAccent");button.SetResourceReference(Control.ForegroundProperty,"AppText");}else{button.ClearValue(Control.BorderBrushProperty);button.ClearValue(Control.ForegroundProperty);}}
  if(section=="Media")_=RefreshCaptureMediaAsync();
  if(section=="Saves")_=RefreshBackupDestinationsAsync();
  RecordRoute();
 }
 private async Task<bool> ShowInlineEditorAsync(UserControl view,Action<EventHandler<bool>> subscribe,Action<EventHandler<bool>> unsubscribe)
 {
  var returnToLibrary=LibraryPanel.Visibility==Visibility.Visible;
  var returnToSettings=SettingsPanel.Visibility==Visibility.Visible;
  var session=new InlineEditorSession(view switch
  {
   GameEditor editor=>editor.CanLeaveAsync,
   LaunchSettingsWindow editor=>editor.CanLeaveAsync,
   NoteExportWindow editor=>editor.CanLeaveAsync,
   StoreImportView editor=>editor.CanLeaveAsync,
   MatchPickerView editor=>editor.CanLeaveAsync,
   _=>throw new ArgumentException("The inline editor must provide a navigation guard.",nameof(view))
  });
  activeInlineEditor=session;
  void Completed(object? sender,bool saved)=>session.Complete(saved);
  subscribe(Completed);
  SetDrawerFullWidth(view is GameEditor);DrawerLayer.Visibility=Visibility.Visible;ContentRoot.IsEnabled=false;
  EditorHost.Content=view;EditorHost.Visibility=Visibility.Visible;
  try{return await session.Completion;}
  finally
  {
   unsubscribe(Completed);EditorHost.Content=null;EditorHost.Visibility=Visibility.Collapsed;
   DrawerLayer.Visibility=Visibility.Collapsed;SetDrawerFullWidth(false);ContentRoot.IsEnabled=true;
   if(returnToLibrary&&IsLoaded)RestoreLibraryFocus();
  }
 }
 private void CompleteInlineEditor()
 {
  var session=activeInlineEditor;activeInlineEditor=null;session?.EditorFinished();
 }
 private async Task RefreshBackupDestinationsAsync()
 {
  try
  {
   var service=Destinations;var global=await service.ReadAsync();
   DefaultBackupLocation.Text=global.DefaultRoot??"Portable: each game's profile folder";
   if(GameList.SelectedItem is LibraryItemView item)
   {
    var id=item.Game.Id;var game=await service.ReadAsync(id);
    if(GameList.SelectedItem is LibraryItemView selected&&selected.Game.Id==id)GameBackupLocation.Text=game.EffectiveDirectory+(game.GameRoot is null?"\nUses the default backup location.":"\nCustom location for this game.");
   }
  }
  catch(Exception error)when(IsExpected(error)){OperationNotice.Text="Backup location needs attention. "+error.Message;}
 }
 private async Task ChangeBackupDirectoryAsync(bool global,bool reset)
 {
  if(busy||editorOpen||quitPreparing||stoppingObservation)return;
  if(launches.GetActiveStates().Length>0||items.Any(i=>backups.IsWorking(i.Game.Id))){OperationNotice.Text="Finish running sessions and backups before changing backup locations.";return;}
  var id=(GameList.SelectedItem as LibraryItemView)?.Game.Id;if(!global&&id is null)return;
  string? folder=null;
  try
  {
   if(!reset){folder=NativePickers.Folder(this,"Choose backup destination");if(folder is null)return;}
   busy=true;UpdateButtons();
   if(global)await Destinations.SetDefaultAsync(folder);else await Destinations.SetGameAsync(id!.Value,folder);
   await RefreshBackupDestinationsAsync();
   if(id is Guid gameId)await RefreshBackupSummary(gameId);
   OperationNotice.Text="Backup destination saved. Existing backups remain in their original locations.";
  }
  catch(Exception error)when(IsExpected(error)||error is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException){OperationNotice.Text="Backup location was not changed. "+error.Message;}
  finally{EndOperation();}
 }
 private async void ChooseDefaultBackupDirectory(object sender,RoutedEventArgs e)=>await ChangeBackupDirectoryAsync(true,false);
 private async void ResetDefaultBackupDirectory(object sender,RoutedEventArgs e)=>await ChangeBackupDirectoryAsync(true,true);
 private async void ChooseGameBackupDirectory(object sender,RoutedEventArgs e)=>await ChangeBackupDirectoryAsync(false,false);
 private async void ResetGameBackupDirectory(object sender,RoutedEventArgs e)=>await ChangeBackupDirectoryAsync(false,true);
 private async void OpenBackupDirectory(object sender,RoutedEventArgs e)
 {
  if(GameList.SelectedItem is not LibraryItemView item)return;
  try
  {
   var folder=await Destinations.ResolveCurrentAsync(item.Game.Id);
   if(!Directory.Exists(folder)){OperationNotice.Text="No backup folder exists here yet. Create a backup first, or open an existing copy from Backup history.";return;}
   Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});
  }
  catch(Exception error)when(IsExpected(error)||error is System.ComponentModel.Win32Exception){OperationNotice.Text="Could not open backup folder. "+error.Message;}
 }
}
