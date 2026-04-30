using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Reporting;

public sealed class StiffenerTakeoffService : IStiffenerTakeoffService
{
    private const decimal StiffenerSpacingIncrementInches = 24m;

    public Task<StiffenerTakeoffReportData> BuildAsync(
        StiffenerTakeoffRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);

        cancellationToken.ThrowIfCancellationRequested();

        var project = request.Project;
        var settings = project.Settings?.StiffenerTakeoff ?? new StiffenerTakeoffSettings();
        if (!settings.Enabled)
        {
            return Task.FromResult(
                new StiffenerTakeoffReportData
                {
                    ProjectMetadata = project.Metadata ?? new ProjectMetadata(),
                    ReportSettings = project.Settings?.ReportSettings ?? new ReportSettings(),
                    Settings = settings
                });
        }

        var overallRows = new Dictionary<int, int>();

        var overallEligiblePanelCount = 0;

        foreach (var part in project.State.Parts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsReadyPart(part) || !IsEligible(part, settings))
            {
                continue;
            }

            var stiffenerCount = CalculateStiffenerCount(part, settings);
            if (stiffenerCount <= 0)
            {
                continue;
            }

            var stiffenerLength = CalculateStiffenerLength(part, settings);
            if (stiffenerLength <= 0)
            {
                continue;
            }

            AddCount(overallRows, stiffenerLength, stiffenerCount);

            overallEligiblePanelCount += part.Quantity;
        }

        var overallLengths = BuildLengthSummaries(overallRows);
        var overallSummary = BuildSectionSummary(
            overallEligiblePanelCount,
            overallLengths,
            settings.StockLengthFeet,
            project.Settings?.KerfWidth ?? 0m);

        return Task.FromResult(
            new StiffenerTakeoffReportData
            {
                ProjectMetadata = project.Metadata ?? new ProjectMetadata(),
                ReportSettings = project.Settings?.ReportSettings ?? new ReportSettings(),
                Settings = settings,
                OverallSummary = overallSummary,
                OverallLengths = overallLengths,
                Materials = Array.Empty<StiffenerTakeoffMaterialSection>(),
                HasTakeoff = overallLengths.Count > 0
            });
    }

    private static bool IsReadyPart(PartRow part) =>
        !string.Equals(part.ValidationStatus, ValidationStatuses.Error, StringComparison.Ordinal) &&
        part.Quantity > 0 &&
        part.Length > 0 &&
        part.Width > 0;

    private static bool IsEligible(PartRow part, StiffenerTakeoffSettings settings) =>
        part.Length >= settings.MinimumLengthInches &&
        part.Width >= settings.MinimumWidthInches;

    private static int CalculateStiffenerCount(PartRow part, StiffenerTakeoffSettings settings)
    {
        var spacingBandCount = decimal.ToInt32(
            decimal.Floor((part.Length - settings.MinimumLengthInches) / StiffenerSpacingIncrementInches));
        return part.Quantity * (1 + spacingBandCount);
    }

    private static int CalculateStiffenerLength(PartRow part, StiffenerTakeoffSettings settings) =>
        decimal.ToInt32(decimal.Round(part.Width - settings.WidthDeductionInches, 0, MidpointRounding.AwayFromZero));

    private static void AddCount(IDictionary<int, int> lengths, int length, int count)
    {
        if (count <= 0)
        {
            return;
        }

        lengths[length] = lengths.TryGetValue(length, out var existingCount)
            ? existingCount + count
            : count;
    }

    private static IReadOnlyList<StiffenerTakeoffLengthSummary> BuildLengthSummaries(
        IReadOnlyDictionary<int, int> countsByLength) =>
        countsByLength
            .Where(entry => entry.Key > 0 && entry.Value > 0)
            .OrderBy(entry => entry.Key)
            .Select(entry =>
                new StiffenerTakeoffLengthSummary
                {
                    Label = $"S{entry.Key}",
                    LengthInches = entry.Key,
                    PieceCount = entry.Value
                })
            .ToArray();

    private static StiffenerTakeoffSectionSummary BuildSectionSummary(
        int eligiblePanelCount,
        IReadOnlyList<StiffenerTakeoffLengthSummary> lengths,
        decimal stockLengthFeet,
        decimal kerfWidth)
    {
        var totalStiffenerCount = lengths.Sum(length => length.PieceCount);
        var totalLinearFeet = lengths.Sum(length => length.LengthInches * length.PieceCount) / 12m;

        return new StiffenerTakeoffSectionSummary
        {
            EligiblePanelCount = eligiblePanelCount,
            TotalStiffenerCount = totalStiffenerCount,
            TotalLinearFeet = totalLinearFeet,
            StockLengthFeet = stockLengthFeet,
            RequiredStockCount = OptimizeSticks(lengths, stockLengthFeet, kerfWidth)
        };
    }

    internal static int OptimizeSticks(
        IReadOnlyList<StiffenerTakeoffLengthSummary> lengths,
        decimal stockLengthFeet,
        decimal kerfWidth)
    {
        var stockInches = stockLengthFeet * 12m;
        if (stockInches <= 0)
        {
            return 0;
        }

        var normalizedKerfWidth = Math.Max(kerfWidth, 0m);

        var stickRemainders = new List<decimal>();
        foreach (var length in lengths
                     .Where(length => length.LengthInches > 0 && length.PieceCount > 0)
                     .OrderByDescending(length => length.LengthInches)
                     .ThenBy(length => length.Label, StringComparer.Ordinal))
        {
            for (var i = 0; i < length.PieceCount; i++)
            {
                var placed = false;
                for (var stickIndex = 0; stickIndex < stickRemainders.Count; stickIndex++)
                {
                    var requiredLength = length.LengthInches + normalizedKerfWidth;
                    if (requiredLength > stickRemainders[stickIndex])
                    {
                        continue;
                    }

                    stickRemainders[stickIndex] -= requiredLength;
                    placed = true;
                    break;
                }

                if (placed)
                {
                    continue;
                }

                stickRemainders.Add(stockInches - length.LengthInches);
            }
        }

        return stickRemainders.Count;
    }
}
