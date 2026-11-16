using System.IO;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GameVault.App;
namespace GameVault.App.Tests;
public sealed class ManualUiBatchTests
{
 private static string Source(string file){var p=new DirectoryInfo(AppContext.BaseDirectory);while(p is not null&&!Directory.Exists(System.IO.Path.Combine(p.FullName,"src","GameVault.App")))p=p.Parent;return File.ReadAllText(System.IO.Path.Combine(p!.FullName,"src","GameVault.App",file));}
 [Fact] public void Shared_expander_uses_a_vector_in_both_states()
 {
  OnSta(()=>{var styles=(ResourceDictionary)Application.LoadComponent(new Uri("/GameVault;component/Styles.xaml",UriKind.Relative));var e=new Expander{Header="Section",Content=new TextBlock{Text="Content"},Style=(Style)styles[typeof(Expander)]};ThemePalette.Apply(e,"Midnight");e.ApplyTemplate();e.Measure(new(500,300));var icon=e.Template.FindName("Chevron",e);var path=Assert.IsType<System.Windows.Shapes.Path>(icon);Assert.NotNull(path.Data);var closed=path.Data.ToString();e.IsExpanded=true;e.Measure(new(500,300));Assert.NotEqual(closed,path.Data.ToString());Assert.Equal(Visibility.Visible,((ContentPresenter)e.Template.FindName("ExpandedContent",e)).Visibility);});
 }
 [Fact] public void Noneditable_selector_keeps_a_compact_height()
 {
  OnSta(()=>{var styles=(ResourceDictionary)Application.LoadComponent(new Uri("/GameVault;component/Styles.xaml",UriKind.Relative));var combo=new ComboBox{Style=(Style)styles[typeof(ComboBox)],FontSize=14};ThemePalette.Apply(combo,"Midnight");combo.Resources.MergedDictionaries.Add(styles);combo.Items.Add("Completed");combo.SelectedIndex=0;combo.ApplyTemplate();combo.Measure(new(200,100));Assert.Equal(40,combo.DesiredSize.Height,3);});
 }
 [Fact] public void Shared_templates_contain_no_corrupted_glyph_text(){var source=Source("Styles.xaml");Assert.DoesNotContain("\u00e2",source);Assert.DoesNotContain("\ufffd",source);}
 [Fact] public void Main_shell_keeps_entire_artwork_and_compact_add_action()
 {
  var xml=XDocument.Parse(Source("MainWindow.xaml"));XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation",x="http://schemas.microsoft.com/winfx/2006/xaml";
  var images=xml.Descendants(ns+"Image").Where(e=>e.Attribute("Source")?.Value=="{Binding Cover}"||e.Attribute(x+"Name")?.Value=="GameHeroCover").ToArray();Assert.Equal(2,images.Length);Assert.All(images,e=>Assert.Equal("Uniform",e.Attribute("Stretch")?.Value));
  var library=images.Single(e=>e.Attribute("Source")?.Value=="{Binding Cover}").Ancestors(ns+"Border").First();Assert.Equal("276",library.Attribute("Height")?.Value);
  var hero=images.Single(e=>e.Attribute(x+"Name")?.Value=="GameHeroCover").Ancestors(ns+"Border").First();Assert.Equal("96",hero.Attribute("Width")?.Value);Assert.Equal("144",hero.Attribute("Height")?.Value);
  var add=xml.Descendants(ns+"Button").Single(e=>e.Attribute(x+"Name")?.Value=="AddButton");Assert.Equal("40",add.Attribute("Height")?.Value);Assert.Equal("Center",add.Attribute("VerticalAlignment")?.Value);
  Assert.DoesNotContain("Advanced privacy",Source("MainWindow.xaml"));Assert.DoesNotContain("ClearLegacyArtworkKey",Source("MainWindow.Navigation.cs"));Assert.Contains("Local profile storage",Source("MainWindow.xaml"));
 }
 [Theory][InlineData(200,300)][InlineData(300,200)][InlineData(200,200)] public void Full_cover_fits_without_changing_aspect_ratio(int w,int h)
 {
  OnSta(()=>{var image=new Image{Stretch=Stretch.Uniform,Source=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,new byte[w*h*4],w*4)};image.Measure(new(184,276));image.Arrange(new Rect(0,0,184,276));Assert.InRange(image.RenderSize.Width,0,184);Assert.InRange(image.RenderSize.Height,0,276);Assert.Equal(w/(double)h,image.RenderSize.Width/image.RenderSize.Height,6);});
 }
 private static void OnSta(Action work){Exception? failure=null;var t=new Thread(()=>{try{work();}catch(Exception e){failure=e;}});t.SetApartmentState(ApartmentState.STA);t.Start();Assert.True(t.Join(TimeSpan.FromSeconds(20)));if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();}
}
