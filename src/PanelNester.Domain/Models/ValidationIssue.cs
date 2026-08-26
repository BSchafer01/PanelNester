namespace PanelNester.Domain.Models;

public sealed record ValidationLocation
{
    public string WorksheetName { get; init; } = string.Empty;

    public int WorksheetPosition { get; init; }

    public int PhysicalRow { get; init; }
}

public abstract record ValidationIssue(
    string Code,
    string Message,
    string? RowId = null,
    ValidationLocation? Location = null);

public sealed record ValidationError(
    string Code,
    string Message,
    string? RowId = null,
    ValidationLocation? Location = null)
    : ValidationIssue(Code, Message, RowId, Location);

public sealed record ValidationWarning(
    string Code,
    string Message,
    string? RowId = null,
    ValidationLocation? Location = null)
    : ValidationIssue(Code, Message, RowId, Location);
