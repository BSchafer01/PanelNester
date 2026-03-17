namespace PanelNester.Desktop.Bridge;

internal sealed class BridgeDispatchException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
