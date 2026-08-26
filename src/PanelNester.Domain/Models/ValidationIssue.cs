namespace PanelNester.Domain.Models;

public abstract record ValidationIssue(
    string Code,
    string Message,
    string? RowId = null,
    WorksheetRowLocation? Location = null);

public sealed record ValidationError(
    string Code,
    string Message,
    string? RowId = null,
    WorksheetRowLocation? Location = null)
    : ValidationIssue(Code, Message, RowId, Location);

public sealed record ValidationWarning(
    string Code,
    string Message,
    string? RowId = null,
    WorksheetRowLocation? Location = null)
    : ValidationIssue(Code, Message, RowId, Location);
