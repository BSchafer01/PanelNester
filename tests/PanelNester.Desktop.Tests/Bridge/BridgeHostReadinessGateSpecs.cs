using System.Text.Json;
using PanelNester.Desktop.Bridge;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class BridgeHostReadinessGateSpecs
{
    [Fact]
    public async Task Host_ready_waits_for_ui_ready_message()
    {
        var gate = new BridgeHostReadinessGate();
        var waitTask = gate.WaitAsync();

        var handshake = new BridgeMessageEnvelope(
            BridgeMessageTypes.BridgeHandshake,
            Guid.NewGuid().ToString("N"),
            JsonSerializer.SerializeToElement(
                new BridgeHandshakeRequest(
                    "PanelNester.WebUI",
                    "0.1.0",
                    Array.Empty<string>()),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.False(gate.TrySignalReady(handshake));
        Assert.False(waitTask.IsCompleted);

        var ready = new BridgeMessageEnvelope(
            BridgeMessageTypes.BridgeUiReady,
            Guid.NewGuid().ToString("N"),
            JsonSerializer.SerializeToElement(
                new BridgeUiReadyRequest(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.True(gate.TrySignalReady(ready));
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
