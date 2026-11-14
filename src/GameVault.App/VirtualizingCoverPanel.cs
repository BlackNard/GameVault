using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace GameVault.App;

internal sealed record CoverGridLayout(int Columns,int First,int End,double ExtentHeight,double Offset)
{
 public const double TileWidth=200,TileHeight=340;
 public static CoverGridLayout Calculate(int count,double width,double height,double offset)
 {
  var columns=Math.Max(1,(int)(Math.Max(1,width)/TileWidth));var extent=Math.Ceiling(count/(double)columns)*TileHeight;
  offset=Math.Clamp(offset,0,Math.Max(0,extent-height));
  var first=Math.Min(count,Math.Max(0,(int)(offset/TileHeight)-1)*columns);
  var end=Math.Min(count,((int)Math.Ceiling((offset+height)/TileHeight)+1)*columns);
  return new(columns,first,end,extent,offset);
 }
}
public sealed class VirtualizingCoverPanel:VirtualizingPanel,IScrollInfo
{
 private Size viewport,extent;private double offset;private int columns=1;
 protected override Size MeasureOverride(Size available)
 {
  viewport=new(double.IsFinite(available.Width)?available.Width:900,double.IsFinite(available.Height)?available.Height:600);
  var owner=ItemsControl.GetItemsOwner(this);var count=owner?.Items.Count??0;
  var layout=CoverGridLayout.Calculate(count,viewport.Width,viewport.Height,offset);columns=layout.Columns;offset=layout.Offset;extent=new(viewport.Width,layout.ExtentHeight);ScrollOwner?.InvalidateScrollInfo();
  // Accessing InternalChildren initializes the items host generator on first measure.
  var children=InternalChildren;
  var generator=ItemContainerGenerator;
  for(var i=children.Count-1;i>=0;i--)
  {
   var position=new GeneratorPosition(i,0);var index=generator.IndexFromGeneratorPosition(position);
   if(index<layout.First||index>=layout.End){generator.Remove(position,1);RemoveInternalChildRange(i,1);}
  }
  if(layout.End>layout.First)
  {
   var position=generator.GeneratorPositionFromIndex(layout.First);var childIndex=position.Offset==0?position.Index:position.Index+1;
   using(generator.StartAt(position,GeneratorDirection.Forward,true))
   for(var index=layout.First;index<layout.End;index++,childIndex++)
   {
    var child=(UIElement)generator.GenerateNext(out var added);
    if(added){if(childIndex>=InternalChildren.Count)AddInternalChild(child);else InsertInternalChild(childIndex,child);generator.PrepareItemContainer(child);}
    child.Measure(new(CoverGridLayout.TileWidth,CoverGridLayout.TileHeight));
   }
  }
  return viewport;
 }
 protected override Size ArrangeOverride(Size finalSize)
 {
  for(var i=0;i<InternalChildren.Count;i++){var index=ItemContainerGenerator.IndexFromGeneratorPosition(new(i,0));InternalChildren[i].Arrange(new Rect(index%columns*CoverGridLayout.TileWidth,index/columns*CoverGridLayout.TileHeight-offset,CoverGridLayout.TileWidth,CoverGridLayout.TileHeight));}
  return finalSize;
 }
 protected override void OnItemsChanged(object sender,ItemsChangedEventArgs args)
 {
  base.OnItemsChanged(sender,args);
  if(args.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Remove or System.Collections.Specialized.NotifyCollectionChangedAction.Replace or System.Collections.Specialized.NotifyCollectionChangedAction.Move)
  {if(args.ItemUICount>0&&args.Position.Index>=0&&args.Position.Index+args.ItemUICount<=InternalChildren.Count)RemoveInternalChildRange(args.Position.Index,args.ItemUICount);}
  InvalidateMeasure();
 }
 protected override void BringIndexIntoView(int index){var y=index/columns*CoverGridLayout.TileHeight;if(y<offset)SetVerticalOffset(y);else if(y+CoverGridLayout.TileHeight>offset+viewport.Height)SetVerticalOffset(y+CoverGridLayout.TileHeight-viewport.Height);}
 public Rect MakeVisible(Visual visual,Rect rectangle){var owner=ItemsControl.GetItemsOwner(this);var container=owner is null?null:ItemsControl.ContainerFromElement(owner,visual);if(container is not null)BringIndexIntoView(owner!.ItemContainerGenerator.IndexFromContainer(container));return rectangle;}
 public bool CanVerticallyScroll{get;set;}public bool CanHorizontallyScroll{get;set;}public double ExtentWidth=>extent.Width;public double ExtentHeight=>extent.Height;public double ViewportWidth=>viewport.Width;public double ViewportHeight=>viewport.Height;public double HorizontalOffset=>0;public double VerticalOffset=>offset;public ScrollViewer? ScrollOwner{get;set;}
 public void SetVerticalOffset(double value){if(!double.IsFinite(value))return;var next=Math.Clamp(value,0,Math.Max(0,extent.Height-viewport.Height));if(next==offset)return;offset=next;InvalidateMeasure();ScrollOwner?.InvalidateScrollInfo();}
 public void SetHorizontalOffset(double value){}public void LineUp()=>SetVerticalOffset(offset-48);public void LineDown()=>SetVerticalOffset(offset+48);public void PageUp()=>SetVerticalOffset(offset-viewport.Height);public void PageDown()=>SetVerticalOffset(offset+viewport.Height);public void MouseWheelUp()=>SetVerticalOffset(offset-144);public void MouseWheelDown()=>SetVerticalOffset(offset+144);public void LineLeft(){}public void LineRight(){}public void PageLeft(){}public void PageRight(){}public void MouseWheelLeft(){}public void MouseWheelRight(){}
}
