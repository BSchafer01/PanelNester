namespace PanelNester.Domain.Models;

public sealed record NestSheet
{
    public string SheetId { get; init; } = string.Empty;

    public int SheetNumber { get; init; }

    public string MaterialName { get; init; } = string.Empty;

    public decimal SheetLength { get; init; }

    public decimal SheetWidth { get; init; }

    public decimal UtilizationPercent { get; init; }
}
