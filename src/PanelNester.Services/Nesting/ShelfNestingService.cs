using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Nesting;

public sealed class ShelfNestingService : INestingService
{
    private const decimal FitTolerance = 0.0001m;

    public Task<NestResponse> NestAsync(NestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var material = request.Material;
        if (!IsValidMaterial(material))
        {
            return Task.FromResult(
                CreateFailureResponse(
                    [
                        CreateRunFailure(
                            NestingFailureCodes.InvalidInput,
                            "Material sheet dimensions and edge margins must leave a usable sheet area.")
                    ]));
        }

        if (request.Parts is null || request.Parts.Count == 0)
        {
            return Task.FromResult(
                CreateFailureResponse(
                    [CreateRunFailure(NestingFailureCodes.EmptyRun, "No part rows were supplied for nesting.")]));
        }

        var sheets = new List<SheetState>();
        var placements = new List<NestPlacement>();
        var unplacedItems = new List<UnplacedItem>();
        var parts = ExpandParts(request.Parts ?? Array.Empty<PartRow>(), material.Name, unplacedItems);
        var spacingClearance = material.DefaultSpacing + Math.Max(request.KerfWidth, 0m);
        var usableLength = material.SheetLength - (material.DefaultEdgeMargin * 2);
        var usableWidth = material.SheetWidth - (material.DefaultEdgeMargin * 2);

        var groupedBatches = BuildGroupedPartBatches(request.Parts ?? Array.Empty<PartRow>(), parts, material.Name);

        if (groupedBatches.Count == 0)
        {
            foreach (var part in SortParts(parts))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!CanFitBlankSheet(part, material.AllowRotation, usableLength, usableWidth))
                {
                    unplacedItems.Add(new UnplacedItem
                    {
                        PartId = part.PartId,
                        ReasonCode = NestingFailureCodes.OutsideUsableSheet,
                        ReasonDescription = "Part exceeds the usable sheet area after edge margins are applied."
                    });
                    continue;
                }

                if (TryPlaceOnExistingSheets(part, material, spacingClearance, usableLength, usableWidth, sheets, placements))
                {
                    continue;
                }

                var nextSheet = new SheetState(sheets.Count + 1);
                if (TryCreateShelfPlacement(part, material, spacingClearance, usableLength, usableWidth, nextSheet, out var candidate))
                {
                    CommitPlacement(part, material, nextSheet, candidate!, placements);
                    sheets.Add(nextSheet);
                    continue;
                }

                unplacedItems.Add(new UnplacedItem
                {
                    PartId = part.PartId,
                    ReasonCode = NestingFailureCodes.NoLayoutSpace,
                    ReasonDescription = "Part could not be placed with the current shelf heuristic."
                });
            }
        }
        else
        {
            SheetState? carryoverSheet = null;

            foreach (var batch in groupedBatches)
            {
                var openSheets = new List<SheetState>();
                if (carryoverSheet is not null)
                {
                    openSheets.Add(carryoverSheet);
                }

                var placedAnyInGroup = false;

                foreach (var part in batch.Parts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!CanFitBlankSheet(part, material.AllowRotation, usableLength, usableWidth))
                    {
                        unplacedItems.Add(new UnplacedItem
                        {
                            PartId = part.PartId,
                            ReasonCode = NestingFailureCodes.OutsideUsableSheet,
                            ReasonDescription = "Part exceeds the usable sheet area after edge margins are applied."
                        });
                        continue;
                    }

                    if (TryPlaceOnExistingSheets(part, material, spacingClearance, usableLength, usableWidth, openSheets, placements))
                    {
                        placedAnyInGroup = true;
                        continue;
                    }

                    var nextSheet = new SheetState(sheets.Count + 1);
                    if (TryCreateShelfPlacement(part, material, spacingClearance, usableLength, usableWidth, nextSheet, out var candidate))
                    {
                        CommitPlacement(part, material, nextSheet, candidate!, placements);
                        sheets.Add(nextSheet);
                        openSheets.Add(nextSheet);
                        placedAnyInGroup = true;
                        continue;
                    }

                    unplacedItems.Add(new UnplacedItem
                    {
                        PartId = part.PartId,
                        ReasonCode = NestingFailureCodes.NoLayoutSpace,
                        ReasonDescription = "Part could not be placed with the current shelf heuristic."
                    });
                }

                carryoverSheet = placedAnyInGroup ? openSheets[^1] : null;
            }
        }

        var sheetArea = material.SheetLength * material.SheetWidth;
        var totalUsedArea = sheets.Sum(sheet => sheet.UsedArea);
        var sheetContracts = sheets
            .Select(sheet => new NestSheet
            {
                SheetId = sheet.SheetId,
                SheetNumber = sheet.SheetNumber,
                MaterialName = material.Name,
                SheetLength = material.SheetLength,
                SheetWidth = material.SheetWidth,
                UtilizationPercent = ToPercent(sheet.UsedArea, sheetArea)
            })
            .ToArray();

        return Task.FromResult(
            BuildResponse(placements.Count > 0, sheetContracts, placements, unplacedItems, totalUsedArea, sheetArea));
    }

    private static IReadOnlyList<ExpandedPart> ExpandParts(
        IEnumerable<PartRow> rows,
        string materialName,
        ICollection<UnplacedItem> unplacedItems)
    {
        var expandedParts = new List<ExpandedPart>();

        foreach (var row in rows)
        {
            if (IsRowError(row))
            {
                AddRowUnplacedItems(row, NestingFailureCodes.InvalidInput, DescribeRow(row), unplacedItems);
                continue;
            }

            if (!string.Equals(row.MaterialName, materialName, StringComparison.Ordinal))
            {
                AddRowUnplacedItems(
                    row,
                    NestingFailureCodes.InvalidInput,
                    $"Row material '{row.MaterialName}' does not match '{materialName}'.",
                    unplacedItems);
                continue;
            }

            if (row.Quantity <= 0 || row.Length <= 0 || row.Width <= 0)
            {
                AddRowUnplacedItems(
                    row,
                    NestingFailureCodes.InvalidInput,
                    "Row dimensions and quantity must be greater than zero before nesting.",
                    unplacedItems);
                continue;
            }

            for (var instanceNumber = 1; instanceNumber <= row.Quantity; instanceNumber++)
            {
                expandedParts.Add(new ExpandedPart
                {
                    InstanceId = $"{row.RowId}:{instanceNumber}",
                    SourceRowId = row.RowId,
                    PartId = row.Quantity == 1 ? GetBasePartId(row) : $"{GetBasePartId(row)}#{instanceNumber}",
                    Length = row.Length,
                    Width = row.Width,
                    MaterialName = row.MaterialName,
                    Group = NormalizeGroup(row.Group)
                });
            }
        }

        return expandedParts;
    }

    private static bool TryPlaceOnExistingSheets(
        ExpandedPart part,
        Material material,
        decimal spacingClearance,
        decimal usableLength,
        decimal usableWidth,
        IEnumerable<SheetState> sheets,
        ICollection<NestPlacement> placements)
    {
        foreach (var sheet in sheets)
        {
            if (TryFindExistingShelfPlacement(part, material.AllowRotation, spacingClearance, usableLength, sheet, out var existingCandidate))
            {
                CommitPlacement(part, material, sheet, existingCandidate!, placements);
                return true;
            }

            if (TryCreateShelfPlacement(part, material, spacingClearance, usableLength, usableWidth, sheet, out var newShelfCandidate))
            {
                CommitPlacement(part, material, sheet, newShelfCandidate!, placements);
                return true;
            }
        }

        return false;
    }

    private static bool TryFindExistingShelfPlacement(
        ExpandedPart part,
        bool allowRotation,
        decimal spacingClearance,
        decimal usableLength,
        SheetState sheet,
        out PlacementCandidate? candidate)
    {
        foreach (var shelf in sheet.Shelves)
        {
            foreach (var orientation in GetExistingShelfOrientations(part, allowRotation))
            {
                var xOffset = shelf.UsedLength + (shelf.ItemCount > 0 ? spacingClearance : 0m);
                if (Exceeds(orientation.Height, shelf.Height) || Exceeds(xOffset + orientation.Length, usableLength))
                {
                    continue;
                }

                candidate = new PlacementCandidate(shelf, orientation, xOffset, shelf.YOffset, false);
                return true;
            }
        }

        candidate = null;
        return false;
    }

    private static bool TryCreateShelfPlacement(
        ExpandedPart part,
        Material material,
        decimal spacingClearance,
        decimal usableLength,
        decimal usableWidth,
        SheetState sheet,
        out PlacementCandidate? candidate)
    {
        var nextShelfYOffset = 0m;
        if (sheet.Shelves.Count > 0)
        {
            var priorShelf = sheet.Shelves[^1];
            nextShelfYOffset = priorShelf.YOffset + priorShelf.Height + spacingClearance;
        }

        foreach (var orientation in GetNewShelfOrientations(part, material.AllowRotation, usableWidth))
        {
            if (Exceeds(orientation.Length, usableLength) || Exceeds(nextShelfYOffset + orientation.Height, usableWidth))
            {
                continue;
            }

            var newShelf = new ShelfState(nextShelfYOffset, orientation.Height);
            candidate = new PlacementCandidate(newShelf, orientation, 0m, nextShelfYOffset, true);
            return true;
        }

        candidate = null;
        return false;
    }

    private static void CommitPlacement(
        ExpandedPart part,
        Material material,
        SheetState sheet,
        PlacementCandidate candidate,
        ICollection<NestPlacement> placements)
    {
        if (candidate.CreatesNewShelf)
        {
            sheet.Shelves.Add(candidate.Shelf);
        }

        candidate.Shelf.UsedLength = candidate.XOffset + candidate.Orientation.Length;
        candidate.Shelf.ItemCount++;
        sheet.UsedArea += part.Length * part.Width;
        sheet.PlacementCount++;

        placements.Add(new NestPlacement
        {
            PlacementId = $"{sheet.SheetId}-placement-{sheet.PlacementCount}",
            SheetId = sheet.SheetId,
            PartId = part.PartId,
            Group = part.Group,
            X = material.DefaultEdgeMargin + candidate.XOffset,
            Y = material.DefaultEdgeMargin + candidate.YOffset,
            Width = candidate.Orientation.Length,
            Height = candidate.Orientation.Height,
            Rotated90 = candidate.Orientation.Rotated90
        });
    }

    private static IEnumerable<OrientedPart> GetExistingShelfOrientations(ExpandedPart part, bool allowRotation)
    {
        yield return new OrientedPart(part.Length, part.Width, false);

        if (allowRotation && part.Length != part.Width)
        {
            yield return new OrientedPart(part.Width, part.Length, true);
        }
    }

    private static IEnumerable<OrientedPart> GetNewShelfOrientations(ExpandedPart part, bool allowRotation, decimal usableWidth)
    {
        return GetExistingShelfOrientations(part, allowRotation)
            .OrderByDescending(orientation => ShouldPreferCurrentWidth(orientation, usableWidth))
            .ThenBy(orientation => orientation.Height)
            .ThenByDescending(orientation => orientation.Length)
            .ThenBy(orientation => orientation.Rotated90);
    }

    private static bool CanFitBlankSheet(ExpandedPart part, bool allowRotation, decimal usableLength, decimal usableWidth)
    {
        return GetExistingShelfOrientations(part, allowRotation)
            .Any(option => FitsWithin(option.Length, usableLength) && FitsWithin(option.Height, usableWidth));
    }

    private static bool IsValidMaterial(Material material)
    {
        if (material.SheetLength <= 0 || material.SheetWidth <= 0)
        {
            return false;
        }

        var usableLength = material.SheetLength - (material.DefaultEdgeMargin * 2);
        var usableWidth = material.SheetWidth - (material.DefaultEdgeMargin * 2);
        return usableLength > 0 && usableWidth > 0;
    }

    private static void AddRowUnplacedItems(
        PartRow row,
        string reasonCode,
        string reasonDescription,
        ICollection<UnplacedItem> unplacedItems)
    {
        var partCount = row.Quantity > 0 ? row.Quantity : 1;
        for (var instanceNumber = 1; instanceNumber <= partCount; instanceNumber++)
        {
            var basePartId = GetBasePartId(row);
            var partId = partCount == 1 ? basePartId : $"{basePartId}#{instanceNumber}";

            unplacedItems.Add(new UnplacedItem
            {
                PartId = partId,
                ReasonCode = reasonCode,
                ReasonDescription = reasonDescription
            });
        }
    }

    private static bool IsRowError(PartRow row) =>
        string.Equals(row.ValidationStatus, ValidationStatuses.Error, StringComparison.OrdinalIgnoreCase);

    private static string DescribeRow(PartRow row)
    {
        if (row.ValidationMessages.Count == 0)
        {
            return "Part row failed validation before nesting.";
        }

        return string.Join("; ", row.ValidationMessages);
    }

    private static string GetBasePartId(PartRow row) =>
        string.IsNullOrWhiteSpace(row.ImportedId) ? row.RowId : row.ImportedId;

    private static IReadOnlyList<ExpandedPart> SortParts(IEnumerable<ExpandedPart> parts) =>
        parts
            .OrderByDescending(part => part.Length * part.Width)
            .ThenByDescending(part => Math.Max(part.Length, part.Width))
            .ThenByDescending(part => Math.Min(part.Length, part.Width))
            .ThenBy(part => part.PartId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<GroupedPartBatch> BuildGroupedPartBatches(
        IReadOnlyList<PartRow> sourceRows,
        IReadOnlyList<ExpandedPart> expandedParts,
        string materialName)
    {
        var namedGroupsInOrder = new List<string>();
        var seenNamedGroups = new HashSet<string>(StringComparer.Ordinal);
        var hasUngroupedRows = false;

        foreach (var row in sourceRows)
        {
            if (!string.Equals(row.MaterialName, materialName, StringComparison.Ordinal))
            {
                continue;
            }

            var groupKey = NormalizeGroupKey(row.Group);
            if (groupKey.Length == 0)
            {
                hasUngroupedRows = true;
                continue;
            }

            if (seenNamedGroups.Add(groupKey))
            {
                namedGroupsInOrder.Add(groupKey);
            }
        }

        if (namedGroupsInOrder.Count == 0)
        {
            return Array.Empty<GroupedPartBatch>();
        }

        if (hasUngroupedRows)
        {
            namedGroupsInOrder.Add(string.Empty);
        }

        var partsByGroup = expandedParts
            .GroupBy(part => NormalizeGroupKey(part.Group), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => SortParts(group),
                StringComparer.Ordinal);

        var batches = new GroupedPartBatch[namedGroupsInOrder.Count];
        for (var index = 0; index < namedGroupsInOrder.Count; index++)
        {
            var groupKey = namedGroupsInOrder[index];
            partsByGroup.TryGetValue(groupKey, out var groupedParts);
            batches[index] = new GroupedPartBatch(groupKey, groupedParts ?? Array.Empty<ExpandedPart>());
        }

        return batches;
    }

    private static string? NormalizeGroup(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string NormalizeGroupKey(string? value) =>
        NormalizeGroup(value) ?? string.Empty;

    private static NestResponse CreateFailureResponse(IReadOnlyList<UnplacedItem> unplacedItems) =>
        new()
        {
            Success = false,
            UnplacedItems = unplacedItems,
            Summary = new MaterialSummary
            {
                TotalSheets = 0,
                TotalPlaced = 0,
                TotalUnplaced = unplacedItems.Count,
                OverallUtilization = 0m
            }
        };

    private static NestResponse BuildResponse(
        bool success,
        IReadOnlyList<NestSheet> sheets,
        IReadOnlyList<NestPlacement> placements,
        IReadOnlyList<UnplacedItem> unplacedItems,
        decimal totalUsedArea,
        decimal sheetArea) =>
        new()
        {
            Success = success,
            Sheets = sheets,
            Placements = placements,
            UnplacedItems = unplacedItems,
            Summary = new MaterialSummary
            {
                TotalSheets = sheets.Count,
                TotalPlaced = placements.Count,
                TotalUnplaced = unplacedItems.Count,
                OverallUtilization = sheets.Count == 0 ? 0m : ToPercent(totalUsedArea, sheetArea * sheets.Count)
            }
        };

    private static UnplacedItem CreateRunFailure(string reasonCode, string description) =>
        new()
        {
            PartId = string.Empty,
            ReasonCode = reasonCode,
            ReasonDescription = description
        };

    private static bool FitsWithin(decimal value, decimal limit) => value <= limit + FitTolerance;

    private static bool Exceeds(decimal value, decimal limit) => value > limit + FitTolerance;

    private static bool ShouldPreferCurrentWidth(OrientedPart orientation, decimal usableWidth) =>
        !orientation.Rotated90 &&
        FitsWithin(orientation.Height, usableWidth) &&
        FitsWithin(usableWidth, orientation.Height);

    private static decimal ToPercent(decimal numerator, decimal denominator)
    {
        if (denominator <= 0)
        {
            return 0m;
        }

        return decimal.Round((numerator / denominator) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private sealed class SheetState
    {
        public SheetState(int sheetNumber)
        {
            SheetNumber = sheetNumber;
            SheetId = $"sheet-{sheetNumber}";
        }

        public string SheetId { get; }

        public int SheetNumber { get; }

        public List<ShelfState> Shelves { get; } = [];

        public decimal UsedArea { get; set; }

        public int PlacementCount { get; set; }
    }

    private sealed class ShelfState(decimal yOffset, decimal height)
    {
        public decimal YOffset { get; } = yOffset;

        public decimal Height { get; } = height;

        public decimal UsedLength { get; set; }

        public int ItemCount { get; set; }
    }

    private sealed record PlacementCandidate(
        ShelfState Shelf,
        OrientedPart Orientation,
        decimal XOffset,
        decimal YOffset,
        bool CreatesNewShelf);

    private readonly record struct OrientedPart(decimal Length, decimal Height, bool Rotated90);

    private sealed record GroupedPartBatch(string GroupKey, IReadOnlyList<ExpandedPart> Parts);
}
