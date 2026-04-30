namespace PanelNester.Domain.Models;

public sealed record StiffenerTakeoffSettings
{
    public bool Enabled { get; init; }

    public decimal MinimumLengthInches { get; init; } = 32m;

    public decimal MinimumWidthInches { get; init; } = 32m;

    public decimal WidthDeductionInches { get; init; } = 4m;

    public decimal StockLengthFeet { get; init; } = 20m;

    public string? ReportTitle { get; init; }

    public string? Extrusion { get; init; }

    public string? ReleaseId { get; init; }

    public string? PoNumber { get; init; }

    public string? Color { get; init; }

    public string? ColorNumber { get; init; }

    public string? Manufacturer { get; init; }

    public string? Status { get; init; }
}
