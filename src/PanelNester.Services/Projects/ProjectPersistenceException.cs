namespace PanelNester.Services.Projects;

internal sealed class ProjectPersistenceException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
