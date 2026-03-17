namespace PanelNester.Domain;

public sealed class MaterialValidationException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}
