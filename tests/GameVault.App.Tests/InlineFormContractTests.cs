using System.Windows.Controls;
using GameVault.App;
namespace GameVault.App.Tests;
public sealed class InlineFormContractTests
{
    [Theory]
    [InlineData(typeof(GameEditor))]
    [InlineData(typeof(LaunchSettingsWindow))]
    [InlineData(typeof(NoteExportWindow))]
    public void FormsMountInTheExistingWindow(Type type)
    {
        Assert.True(typeof(UserControl).IsAssignableFrom(type));
        Assert.Equal(typeof(bool), type.GetProperty("IsBusy")?.PropertyType);
        Assert.Equal(typeof(EventHandler<bool>), type.GetEvent("Completed")?.EventHandlerType);
        Assert.Equal(typeof(Task<bool>), type.GetMethod("CanLeaveAsync")?.ReturnType);
    }
}
