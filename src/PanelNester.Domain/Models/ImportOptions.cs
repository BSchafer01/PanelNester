namespace PanelNester.Domain.Models;

public static class ImportFieldNames
{
    public const string Id = "Id";
    public const string Length = "Length";
    public const string Width = "Width";
    public const string Quantity = "Quantity";
    public const string Material = "Material";
    public const string Group = "Group";
    public const string SheetNumber = "Sheet Number";
    public const string RowNumber = "Row Number";
    public const string ColumnNumber = "Column Number";

    public static readonly IReadOnlyList<string> Required =
    [
        Id,
        Length,
        Width,
        Quantity,
        Material
    ];

    public static readonly IReadOnlyList<string> Optional =
    [
        Group,
        SheetNumber,
        RowNumber,
        ColumnNumber
    ];

    public static readonly IReadOnlyList<string> All =
    [
        .. Required,
        .. Optional
    ];
}

public static class ImportMaterialResolutionStatuses
{
    public const string Resolved = "resolved";
    public const string Unresolved = "unresolved";
    public const string Created = "created";
}

public sealed record ImportOptions
{
    public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; init; } = Array.Empty<ImportColumnMapping>();

    public IReadOnlyList<ImportMaterialMapping> MaterialMappings { get; init; } = Array.Empty<ImportMaterialMapping>();
}

public sealed record ImportColumnMapping
{
    public string SourceColumn { get; init; } = string.Empty;

    public string TargetField { get; init; } = string.Empty;
}

public sealed record ImportMaterialMapping
{
    public string SourceMaterialName { get; init; } = string.Empty;

    public string? TargetMaterialId { get; init; }
}

public sealed record ImportFieldMappingStatus
{
    public string TargetField { get; init; } = string.Empty;

    public string? SourceColumn { get; init; }

    public string? SuggestedSourceColumn { get; init; }
}

public sealed record ImportMaterialResolution
{
    public string SourceMaterialName { get; init; } = string.Empty;

    public string Status { get; init; } = ImportMaterialResolutionStatuses.Unresolved;

    public string? ResolvedMaterialId { get; init; }

    public string? ResolvedMaterialName { get; init; }
}
