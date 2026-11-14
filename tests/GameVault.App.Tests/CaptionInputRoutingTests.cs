using GameVault.App;

namespace GameVault.App.Tests;

public sealed class CaptionInputRoutingTests
{
    [Theory]
    [InlineData(0x00A1u, 9u, 0x0201u)] // NC press -> client press; Button owns capture.
    [InlineData(0x00A3u, 9u, 0x0201u)] // Double-click is another press, never caption double-toggle.
    [InlineData(0x00A2u, 9u, 0x0202u)]
    [InlineData(0x00A0u, 9u, 0u)] // Hover stays native for Snap Layouts.
    [InlineData(0x00A1u, 2u, 0u)] // Titlebar dragging stays native.
    [InlineData(0x00A1u, 8u, 0u)] // Minimize remains an ordinary WPF button.
    [InlineData(0x00A1u, 20u, 0u)] // Close retains its existing closing/tray workflow.
    [InlineData(0x00A1u, 10u, 0u)] // Resize stays native.
    [InlineData(0x0202u, 9u, 0u)] // Captured client release is never forwarded twice.
    public void Only_maximize_nonclient_button_messages_enter_the_Wpf_button_route(uint message, uint hitTest, uint expected)
    {
        Assert.Equal(expected, WindowChromeBehavior.ClientButtonMessage(message, hitTest));
    }
}
