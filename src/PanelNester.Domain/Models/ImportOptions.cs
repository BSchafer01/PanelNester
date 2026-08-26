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
    public const string ProfileNumber = "Profile Number";
    public const string PartName = "Part Name";
    public const string Finish = "Finish";
    public const string PartNumber = "Part Number";

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

    public static readonly IReadOnlyList<string> StockLengthRequired =
    [
        Quantity,
        Length,
        ProfileNumber
    ];

    public static readonly IReadOnlyList<string> StockLengthOptional =
    [
        PartName,
        Finish,
        PartNumber
    ];

    public static readonly IReadOnlyList<string> StockLengthAll =
    [
        .. StockLengthRequired,
        .. StockLengthOptional
    ];

    public static IReadOnlyList<string> RequiredFor(ProjectKind projectKind) => FieldsFor(projectKind).Required;

    public static IReadOnlyList<string> OptionalFor(ProjectKind projectKind) => FieldsFor(projectKind).Optional;

    public static IReadOnlyList<string> AllFor(ProjectKind projectKind) => FieldsFor(projectKind).All;

    private static ImportFieldSet FieldsFor(ProjectKind projectKind) => projectKind switch
    {
        ProjectKind.StockLength => new(StockLengthRequired, StockLengthOptional, StockLengthAll),
        _ => new(Required, Optional, All)
    };

    private readonly record struct ImportFieldSet(
        IReadOnlyList<string> Required,
        IReadOnlyList<string> Optional,
        IReadOnlyList<string> All);
}

public static class ImportMaterialResolutionStatuses
{
    public const string Resolved = "resolved";
    public const string Unresolved = "unresolved";
    public const string Created = "created";
}

public sealed record ImportOptions
{
    public ProjectKind ProjectKind { get; init; } = ProjectKind.Sheet;

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
