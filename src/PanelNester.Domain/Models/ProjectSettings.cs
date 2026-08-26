namespace PanelNester.Domain.Models;

public sealed record ProjectSettings
{
    public decimal KerfWidth { get; init; }

    public InchDisplayFormat InchDisplayFormat { get; init; } = InchDisplayFormat.Decimal;

    public ReportSettings ReportSettings { get; init; } = new();

    public StiffenerTakeoffSettings StiffenerTakeoff { get; init; } = new();
}
