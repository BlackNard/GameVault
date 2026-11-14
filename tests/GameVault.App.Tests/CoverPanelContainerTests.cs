using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameVault.App;
namespace GameVault.App.Tests;
public sealed class CoverPanelContainerTests
{
 [Fact] public void Scroll_and_collection_reset_keep_only_visible_containers_without_a_window()
 {
  Exception? failure=null;var thread=new Thread(()=>
  {
   try
   {
    var scroll=new FrameworkElementFactory(typeof(ScrollViewer));scroll.SetValue(ScrollViewer.CanContentScrollProperty,true);scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
    var list=new ListBox{Width=900,Height=600,Template=new ControlTemplate(typeof(ListBox)){VisualTree=scroll},ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingCoverPanel)))};
    list.ItemsSource=Enumerable.Range(0,1000).ToArray();list.ApplyTemplate();Layout();
    var panel=Find<VirtualizingCoverPanel>(list);Assert.NotNull(panel);Assert.InRange(panel.Children.Count,1,20);Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));
    panel.SetVerticalOffset(34000);Layout();Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(0));Assert.InRange(panel.Children.Count,1,20);
    list.ItemsSource=Enumerable.Range(0,500).ToArray();Layout();Assert.InRange(panel.Children.Count,1,20);
    panel.SetVerticalOffset(0);Layout();Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));Assert.InRange(panel.Children.Count,1,20);
    void Layout(){list.Measure(new(900,600));list.Arrange(new Rect(0,0,900,600));list.UpdateLayout();}
   }
   catch(Exception error){failure=error;}
  });thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(20)));if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
 }
 private static T? Find<T>(DependencyObject item)where T:DependencyObject
 {
  if(item is T found)return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)if(Find<T>(VisualTreeHelper.GetChild(item,i)) is T child)return child;return null;
 }
}
