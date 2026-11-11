using System.IO;
using System.IO.Pipes;
using GameVault.Infrastructure.Launching;

namespace GameVault.App.Tests;

public sealed class BrokerProtocolTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1024 * 1024)]
    public async Task InvalidMessageSizeIsRejectedBeforeReadingPayload(int size)
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(size));
        await Assert.ThrowsAsync<IOException>(() => BrokerWire.Read<BrokerRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task InvalidRequestDoesNotStartAGame()
    {
        var name = "GameVault.Elevation." + Guid.NewGuid().ToString("N");
        using var pipe = BrokerPipe.Create(name);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var helper = ElevationBroker.RunHelperAsync(name, Environment.ProcessId);
        await pipe.WaitForConnectionAsync(deadline.Token);
        await BrokerWire.Send(pipe, new BrokerRequest("not-an-absolute-executable", "not-a-working-folder"), deadline.Token);
        var response = await BrokerWire.Read<BrokerResponse>(pipe, deadline.Token);
        Assert.Null(response.Handles);
        Assert.Contains("invalid", response.Error);
        Assert.Equal(1, await helper);
    }
}
