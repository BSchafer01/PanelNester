namespace PanelNester.Desktop.Bridge;

internal sealed class BridgeHostReadinessGate
{
    private readonly TaskCompletionSource _readySource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        _readySource.Task.WaitAsync(cancellationToken);

    public bool TrySignalReady(BridgeMessageEnvelope message)
    {
        if (!string.Equals(message.Type, BridgeMessageTypes.BridgeUiReady, StringComparison.Ordinal))
        {
            return false;
        }

        _readySource.TrySetResult();
        return true;
    }
}
