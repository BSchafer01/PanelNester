namespace PanelNester.Domain.Models;

public sealed record ImportResponse
{
    public bool Success { get; init; }

    public IReadOnlyList<PartRow> Parts { get; init; } = Array.Empty<PartRow>();

    public IReadOnlyList<RequiredPiece> RequiredPieces { get; init; } = Array.Empty<RequiredPiece>();

    public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();

    public IReadOnlyList<ValidationWarning> Warnings { get; init; } = Array.Empty<ValidationWarning>();

    public IReadOnlyList<string> AvailableColumns { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ImportSourceColumn> SourceColumns { get; init; } = Array.Empty<ImportSourceColumn>();

    public IReadOnlyList<ImportFieldMappingStatus> ColumnMappings { get; init; } = Array.Empty<ImportFieldMappingStatus>();

    public IReadOnlyList<ImportMaterialResolution> MaterialResolutions { get; init; } = Array.Empty<ImportMaterialResolution>();

    public ImportWorksheetDescriptor? Worksheet { get; init; }
}

public sealed record ImportSourceColumn
{
    public string Address { get; init; } = string.Empty;

    public string Heading { get; init; } = string.Empty;
}
