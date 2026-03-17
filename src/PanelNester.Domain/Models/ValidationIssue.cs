namespace PanelNester.Domain.Models;

public abstract record ValidationIssue(string Code, string Message, string? RowId = null);

public sealed record ValidationError(string Code, string Message, string? RowId = null)
    : ValidationIssue(Code, Message, RowId);

public sealed record ValidationWarning(string Code, string Message, string? RowId = null)
    : ValidationIssue(Code, Message, RowId);
