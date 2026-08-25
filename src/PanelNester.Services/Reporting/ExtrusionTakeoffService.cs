using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Reporting;

public sealed class ExtrusionTakeoffService : IExtrusionTakeoffService
{
    private const string Ungrouped = "Ungrouped";

    public Task<ExtrusionLayoutState> BuildLayoutAsync(
        ExtrusionLayoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);

        cancellationToken.ThrowIfCancellationRequested();

        var panels = ExpandPanels(request.Project);
        var existing = request.Project.State.ExtrusionLayout ?? new ExtrusionLayoutState();
        return Task.FromResult(NormalizeLayout(existing, panels));
    }

    public Task<ExtrusionReportData> BuildReportAsync(
        ExtrusionReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);

        cancellationToken.ThrowIfCancellationRequested();

        var panels = ExpandPanels(request.Project);
        var layout = NormalizeLayout(request.Project.State.ExtrusionLayout, panels);
        var segments = BuildSegments(layout, panels);
        var overall = BuildLengthSummaries(segments, layout);
        var groupSummaries = segments
            .GroupBy(segment => LayoutKey(segment.OptimizationGroupId, segment.GroupName), StringComparer.Ordinal)
            .Select(group => new ExtrusionGroupSummary
            {
                OptimizationGroupId = group.First().OptimizationGroupId,
                OptimizationGroupName = group.First().OptimizationGroupName,
                GroupName = group.First().GroupName,
                Lengths = BuildLengthSummaries(group.ToArray(), layout)
            })
            .ToArray();
        var optimizationGroups = request.Project.State.OptimizationGroups
            .OrderBy(group => group.Order)
            .ThenBy(group => group.OptimizationGroupId, StringComparer.Ordinal)
            .Select(group => new ExtrusionOptimizationGroupSummary
            {
                OptimizationGroupId = group.OptimizationGroupId,
                Name = group.Name,
                Order = group.Order,
                OverallLengths = BuildLengthSummaries(
                    segments.Where(segment => string.Equals(
                        segment.OptimizationGroupId,
                        group.OptimizationGroupId,
                        StringComparison.Ordinal)).ToArray(),
                    layout),
                PartGroups = groupSummaries
                    .Where(summary => string.Equals(
                        summary.OptimizationGroupId,
                        group.OptimizationGroupId,
                        StringComparison.Ordinal))
                    .ToArray()
            })
            .ToArray();

        return Task.FromResult(
            new ExtrusionReportData
            {
                ProjectMetadata = request.Project.Metadata ?? new ProjectMetadata(),
                ReportSettings = request.Project.Settings?.ReportSettings ?? new ReportSettings(),
                Layout = layout,
                Panels = panels,
                OverallLengths = overall,
                Groups = groupSummaries,
                OptimizationGroups = optimizationGroups,
                Segments = segments,
                HasTakeoff = segments.Count > 0
            });
    }

    private static IReadOnlyList<ExtrusionPanelInstance> ExpandPanels(Project project)
    {
        var ownership = project.State.OptimizationGroups
            .SelectMany(group => group.Parts.Select(part => new
            {
                part.RowId,
                group.OptimizationGroupId,
                OptimizationGroupName = group.Name,
                OptimizationGroupOrder = group.Order
            }))
            .Where(item => !string.IsNullOrWhiteSpace(item.RowId))
            .GroupBy(item => item.RowId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var parts = project.State.Parts.Count > 0
            ? project.State.Parts
            : project.State.OptimizationGroups.SelectMany(group => group.Parts).ToArray();

        return ExpandPanels(parts, part => ownership.TryGetValue(part.RowId, out var owner)
            ? (owner.OptimizationGroupId, owner.OptimizationGroupName, owner.OptimizationGroupOrder)
            : (string.Empty, string.Empty, 0));
    }

    internal static IReadOnlyList<ExtrusionPanelInstance> ExpandPanels(IReadOnlyList<PartRow>? parts) =>
        ExpandPanels(parts, _ => (string.Empty, string.Empty, 0));

    private static IReadOnlyList<ExtrusionPanelInstance> ExpandPanels(
        IReadOnlyList<PartRow>? parts,
        Func<PartRow, (string Id, string Name, int Order)> resolveOptimizationGroup)
    {
        var panels = new List<ExtrusionPanelInstance>();
        foreach (var part in parts ?? Array.Empty<PartRow>())
        {
            if (!IsReadyPart(part))
            {
                continue;
            }

            var baseLabel = string.IsNullOrWhiteSpace(part.ImportedId) ? part.RowId : part.ImportedId;
            if (string.IsNullOrWhiteSpace(baseLabel))
            {
                baseLabel = $"Panel {panels.Count + 1}";
            }

            var sourceRowId = string.IsNullOrWhiteSpace(part.RowId) ? baseLabel : part.RowId;
            var optimizationGroup = resolveOptimizationGroup(part);
            for (var index = 1; index <= part.Quantity; index++)
            {
                panels.Add(
                    new ExtrusionPanelInstance
                    {
                        OptimizationGroupId = optimizationGroup.Id,
                        OptimizationGroupName = optimizationGroup.Name,
                        OptimizationGroupOrder = optimizationGroup.Order,
                        InstanceId = $"{sourceRowId}#{index}",
                        SourceRowId = sourceRowId,
                        ImportedId = part.ImportedId,
                        QuantityIndex = index,
                        Label = $"{baseLabel}#{index}",
                        MaterialName = part.MaterialName,
                        GroupName = NormalizeGroupName(part.Group),
                        SheetGroupName = NormalizeSheetGroupName(part.SheetNumber),
                        SheetNumber = part.SheetNumber,
                        RowNumber = part.RowNumber,
                        ColumnNumber = part.ColumnNumber,
                        Length = part.Length,
                        Width = part.Width
                    });
            }
        }

        return panels;
    }

    private static bool IsReadyPart(PartRow part) =>
        !string.Equals(part.ValidationStatus, ValidationStatuses.Error, StringComparison.Ordinal) &&
        part.Quantity > 0 &&
        part.Length > 0 &&
        part.Width > 0;

    private static ExtrusionLayoutState NormalizeLayout(
        ExtrusionLayoutState? layout,
        IReadOnlyList<ExtrusionPanelInstance> panels)
    {
        layout ??= new ExtrusionLayoutState();
        var panelIds = panels.Select(panel => panel.InstanceId).ToHashSet(StringComparer.Ordinal);
        var groupingMode = NormalizeGroupingMode(layout.GroupingMode, panels);
        var existingGroups = layout.Groups
            .GroupBy(group => LayoutKey(group.OptimizationGroupId, NormalizeGroupName(group.GroupName)), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        var groups = panels
            .GroupBy(
                panel => LayoutKey(panel.OptimizationGroupId, GetPanelGroupName(panel, groupingMode)),
                StringComparer.Ordinal)
            .OrderBy(group => group.First().OptimizationGroupOrder)
            .ThenBy(group => DisplayGroupName(GetPanelGroupName(group.First(), groupingMode)), StringComparer.OrdinalIgnoreCase)
            .Select(group => NormalizeGroupLayout(
                group.First().OptimizationGroupId,
                group.First().OptimizationGroupName,
                GetPanelGroupName(group.First(), groupingMode),
                group.ToArray(),
                existingGroups.GetValueOrDefault(group.Key) ??
                    existingGroups.GetValueOrDefault(LayoutKey(string.Empty, GetPanelGroupName(group.First(), groupingMode))),
                layout))
            .ToArray();

        var staleGroups = layout.Groups
            .Where(group => !groups.Any(next => string.Equals(
                LayoutKey(next.OptimizationGroupId, next.GroupName),
                LayoutKey(group.OptimizationGroupId, NormalizeGroupName(group.GroupName)),
                StringComparison.Ordinal)))
            .Where(group => !groups.Any(next => next.Cells.Any(cell =>
                group.Cells.Any(existingCell => string.Equals(
                    existingCell.InstanceId,
                    cell.InstanceId,
                    StringComparison.Ordinal)))))
            .Select(group => group with
            {
                Cells = group.Cells
                    .Where(cell => panelIds.Contains(cell.InstanceId))
                    .ToArray(),
                EdgeAssignments = group.EdgeAssignments
                    .Where(edge => panelIds.Contains(edge.InstanceId))
                    .ToArray(),
                JointAssignments = group.JointAssignments
                    .Where(joint =>
                        panelIds.Contains(joint.FirstInstanceId) &&
                        (string.IsNullOrWhiteSpace(joint.SecondInstanceId) || panelIds.Contains(joint.SecondInstanceId)))
                    .ToArray()
            })
            .Where(group => group.Cells.Count > 0)
            .ToArray();

        return layout with
        {
            GroupingMode = groupingMode,
            PanelToPanelExtrusionName = NormalizeName(layout.PanelToPanelExtrusionName, "Panel Joint"),
            EdgeExtrusionName = NormalizeName(layout.EdgeExtrusionName, "Perimeter Edge"),
            PanelToPanelStickLengthFeet = NormalizeStickLength(layout.PanelToPanelStickLengthFeet),
            EdgeStickLengthFeet = NormalizeStickLength(layout.EdgeStickLengthFeet),
            AdditionalLineItems = NormalizeAdditionalLineItems(layout.AdditionalLineItems),
            Groups = groups.Concat(staleGroups).ToArray()
        };
    }

    private static ExtrusionGroupLayout NormalizeGroupLayout(
        string optimizationGroupId,
        string optimizationGroupName,
        string groupName,
        IReadOnlyList<ExtrusionPanelInstance> panels,
        ExtrusionGroupLayout? existing,
        ExtrusionLayoutState layout)
    {
        var defaultColumns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(panels.Count)));
        var defaultRows = Math.Max(1, (int)Math.Ceiling(panels.Count / (decimal)defaultColumns));
        var rows = Math.Max(existing?.Rows ?? defaultRows, 1);
        var columns = Math.Max(existing?.Columns ?? defaultColumns, 1);
        var maximumImportedRowNumber = panels
            .Where(panel => panel.RowNumber is not null)
            .Select(panel => panel.RowNumber!.Value)
            .DefaultIfEmpty(0)
            .Max();
        rows = Math.Max(rows, maximumImportedRowNumber);
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        var cells = new List<ExtrusionGridCell>();

        foreach (var cell in existing?.Cells ?? Array.Empty<ExtrusionGridCell>())
        {
            if (!panels.Any(panel => string.Equals(panel.InstanceId, cell.InstanceId, StringComparison.Ordinal)))
            {
                continue;
            }

            var row = Math.Max(0, cell.Row);
            var column = Math.Max(0, cell.Column);
            rows = Math.Max(rows, row + 1);
            columns = Math.Max(columns, column + 1);
            var key = CellKey(row, column);
            if (!occupied.Add(key))
            {
                continue;
            }

            cells.Add(cell with { Row = row, Column = column });
        }

        foreach (var panel in SortPanelsForRows(panels.Where(panel => cells.All(cell => cell.InstanceId != panel.InstanceId))))
        {
            if (panel.RowNumber is { } rowNumber && panel.ColumnNumber is { } columnNumber)
            {
                var row = rows - rowNumber;
                var column = columnNumber - 1;
                var key = CellKey(row, column);
                if (occupied.Add(key))
                {
                    rows = Math.Max(rows, row + 1);
                    columns = Math.Max(columns, column + 1);
                    cells.Add(new ExtrusionGridCell
                    {
                        InstanceId = panel.InstanceId,
                        Row = row,
                        Column = column
                    });
                    continue;
                }
            }

            var placed = false;
            for (var row = 0; row < rows && !placed; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var key = CellKey(row, column);
                    if (!occupied.Add(key))
                    {
                        continue;
                    }

                    cells.Add(new ExtrusionGridCell
                    {
                        InstanceId = panel.InstanceId,
                        Row = row,
                        Column = column
                    });
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                var row = rows;
                rows++;
                occupied.Add(CellKey(row, 0));
                cells.Add(new ExtrusionGridCell
                {
                    InstanceId = panel.InstanceId,
                    Row = row,
                    Column = 0
                });
            }
        }

        var panelIds = panels.Select(panel => panel.InstanceId).ToHashSet(StringComparer.Ordinal);
        var edges = (existing?.EdgeAssignments ?? Array.Empty<ExtrusionEdgeAssignment>())
            .Where(edge => panelIds.Contains(edge.InstanceId) && IsKnownEdge(edge.Edge))
            .Select(edge => edge with { ExtrusionName = NormalizeName(edge.ExtrusionName, layout.EdgeExtrusionName) })
            .Distinct()
            .ToArray();
        var joints = (existing?.JointAssignments ?? Array.Empty<ExtrusionJointAssignment>())
            .Where(joint =>
                panelIds.Contains(joint.FirstInstanceId) &&
                (string.IsNullOrWhiteSpace(joint.SecondInstanceId) || panelIds.Contains(joint.SecondInstanceId)))
            .Select(joint => joint with { ExtrusionName = NormalizeName(joint.ExtrusionName, layout.PanelToPanelExtrusionName) })
            .Distinct()
            .ToArray();

        return new ExtrusionGroupLayout
        {
            OptimizationGroupId = optimizationGroupId,
            OptimizationGroupName = optimizationGroupName,
            GroupName = groupName,
            Rows = rows,
            Columns = columns,
            Cells = cells
                .OrderBy(cell => cell.Row)
                .ThenBy(cell => cell.Column)
                .ThenBy(cell => cell.InstanceId, StringComparer.Ordinal)
                .ToArray(),
            EdgeAssignments = edges,
            JointAssignments = joints
        };
    }

    private static IReadOnlyList<ExtrusionSegmentDetail> BuildSegments(
        ExtrusionLayoutState layout,
        IReadOnlyList<ExtrusionPanelInstance> panels)
    {
        var panelById = panels.ToDictionary(panel => panel.InstanceId, StringComparer.Ordinal);
        var segments = new List<ExtrusionSegmentDetail>();

        foreach (var group in layout.Groups)
        {
            var cellById = group.Cells.ToDictionary(cell => cell.InstanceId, StringComparer.Ordinal);
            var edgeAssignmentByLocation = group.EdgeAssignments
                .Where(edge => IsKnownEdge(edge.Edge))
                .GroupBy(edge => EdgeLocationKey(edge.InstanceId, edge.Edge), StringComparer.Ordinal)
                .ToDictionary(grouping => grouping.Key, grouping => grouping.Last(), StringComparer.Ordinal);
            var emittedEdges = new HashSet<string>(StringComparer.Ordinal);

            foreach (var edge in DetectVisibleEdges(group, panels, layout.EdgeExtrusionName))
            {
                if (!panelById.TryGetValue(edge.InstanceId, out var panel) ||
                    !cellById.ContainsKey(edge.InstanceId) ||
                    !emittedEdges.Add(EdgeLocationKey(edge.InstanceId, edge.Edge)))
                {
                    continue;
                }

                edgeAssignmentByLocation.TryGetValue(EdgeLocationKey(edge.InstanceId, edge.Edge), out var assignment);
                segments.Add(
                    new ExtrusionSegmentDetail
                    {
                        OptimizationGroupId = group.OptimizationGroupId,
                        OptimizationGroupName = group.OptimizationGroupName,
                        GroupName = group.GroupName,
                        Category = ExtrusionCategories.Edge,
                        ExtrusionName = NormalizeName(assignment?.ExtrusionName, layout.EdgeExtrusionName),
                        Location = $"{panel.Label} {edge.Edge}",
                        LengthInches = IsHorizontal(edge.Edge) ? panel.Length : panel.Width
                    });
            }

            foreach (var joint in DetectJoints(group, panels, layout.PanelToPanelExtrusionName))
            {
                segments.Add(joint);
            }
        }

        var layoutOrder = layout.Groups
            .Select((group, index) => new { Key = LayoutKey(group.OptimizationGroupId, group.GroupName), Index = index })
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.Ordinal);

        return segments
            .Where(segment => segment.LengthInches > 0)
            .OrderBy(segment => layoutOrder.GetValueOrDefault(
                LayoutKey(segment.OptimizationGroupId, segment.GroupName),
                int.MaxValue))
            .ThenBy(segment => segment.Category, StringComparer.Ordinal)
            .ThenBy(segment => segment.ExtrusionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(segment => segment.LengthInches)
            .ToArray();
    }

    private static IEnumerable<ExtrusionEdgeAssignment> DetectVisibleEdges(
        ExtrusionGroupLayout group,
        IReadOnlyList<ExtrusionPanelInstance> panels,
        string defaultName)
    {
        var panelIds = panels.Select(panel => panel.InstanceId).ToHashSet(StringComparer.Ordinal);
        var cellByPosition = group.Cells.ToDictionary(cell => CellKey(cell.Row, cell.Column), StringComparer.Ordinal);
        var assignmentByJoint = group.JointAssignments
            .GroupBy(joint => NormalizeJointId(joint), StringComparer.Ordinal)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.Last(), StringComparer.Ordinal);
        var explicitEdges = group.EdgeAssignments
            .Where(edge => panelIds.Contains(edge.InstanceId) && IsKnownEdge(edge.Edge))
            .Select(edge => EdgeLocationKey(edge.InstanceId, edge.Edge))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var cell in group.Cells)
        {
            if (!panelIds.Contains(cell.InstanceId))
            {
                continue;
            }

            foreach (var (edge, row, column) in NeighborPositions(cell))
            {
                var hasNeighbor = cellByPosition.TryGetValue(CellKey(row, column), out var neighbor);
                var isDisabledJoint = false;
                if (hasNeighbor && neighbor is not null)
                {
                    var jointId = BuildJointId(cell.InstanceId, neighbor.InstanceId);
                    assignmentByJoint.TryGetValue(jointId, out var assignment);
                    isDisabledJoint = assignment is { IsEnabled: false };
                }

                var explicitEdge = explicitEdges.Contains(EdgeLocationKey(cell.InstanceId, edge));
                if ((!hasNeighbor || isDisabledJoint || explicitEdge) && !IsIgnoredEdge(group, cell.InstanceId, edge))
                {
                    yield return new ExtrusionEdgeAssignment
                    {
                        InstanceId = cell.InstanceId,
                        Edge = edge,
                        ExtrusionName = defaultName
                    };
                }
            }
        }
    }

    private static IEnumerable<ExtrusionSegmentDetail> DetectJoints(
        ExtrusionGroupLayout group,
        IReadOnlyList<ExtrusionPanelInstance> panels,
        string defaultName)
    {
        var panelById = panels.ToDictionary(panel => panel.InstanceId, StringComparer.Ordinal);
        var cellByPosition = group.Cells.ToDictionary(cell => CellKey(cell.Row, cell.Column), StringComparer.Ordinal);
        var assignmentByJoint = group.JointAssignments
            .GroupBy(joint => NormalizeJointId(joint), StringComparer.Ordinal)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.Last(), StringComparer.Ordinal);

        foreach (var cell in group.Cells)
        {
            if (!panelById.TryGetValue(cell.InstanceId, out var first))
            {
                continue;
            }

            foreach (var (edge, row, column) in new[]
                     {
                         (ExtrusionEdgeNames.Right, cell.Row, cell.Column + 1),
                         (ExtrusionEdgeNames.Bottom, cell.Row + 1, cell.Column)
                     })
            {
                if (!cellByPosition.TryGetValue(CellKey(row, column), out var neighbor) ||
                    !panelById.TryGetValue(neighbor.InstanceId, out var second))
                {
                    continue;
                }

                var jointId = BuildJointId(first.InstanceId, second.InstanceId);
                assignmentByJoint.TryGetValue(jointId, out var assignment);
                if (assignment is { IsEnabled: false } ||
                    IsIgnoredJoint(group, first.InstanceId, second.InstanceId, edge))
                {
                    continue;
                }

                yield return new ExtrusionSegmentDetail
                {
                    OptimizationGroupId = group.OptimizationGroupId,
                    OptimizationGroupName = group.OptimizationGroupName,
                    GroupName = group.GroupName,
                    Category = ExtrusionCategories.PanelToPanel,
                    ExtrusionName = NormalizeName(assignment?.ExtrusionName, defaultName),
                    Location = $"{first.Label} / {second.Label}",
                    LengthInches = edge == ExtrusionEdgeNames.Right
                        ? Math.Min(first.Width, second.Width)
                        : Math.Min(first.Length, second.Length)
                };
            }
        }

        foreach (var assignment in group.JointAssignments)
        {
            if (assignment.IsEnabled == false ||
                !string.IsNullOrWhiteSpace(assignment.SecondInstanceId) ||
                !IsKnownEdge(assignment.Edge) ||
                !panelById.TryGetValue(assignment.FirstInstanceId, out var panel) ||
                IsIgnoredEdge(group, assignment.FirstInstanceId, assignment.Edge))
            {
                continue;
            }

            yield return new ExtrusionSegmentDetail
            {
                OptimizationGroupId = group.OptimizationGroupId,
                OptimizationGroupName = group.OptimizationGroupName,
                GroupName = group.GroupName,
                Category = ExtrusionCategories.PanelToPanel,
                ExtrusionName = NormalizeName(assignment.ExtrusionName, defaultName),
                Location = $"{panel.Label} {assignment.Edge}",
                LengthInches = IsHorizontal(assignment.Edge) ? panel.Length : panel.Width
            };
        }
    }

    private static IReadOnlyList<ExtrusionLengthSummary> BuildLengthSummaries(
        IReadOnlyList<ExtrusionSegmentDetail> segments,
        ExtrusionLayoutState layout)
    {
        var baseSummaries = segments
            .GroupBy(segment => new
            {
                segment.Category,
                segment.ExtrusionName
            })
            .OrderBy(group => group.Key.Category, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ExtrusionName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var totalLengthInches = group.Sum(segment => segment.LengthInches);
                var totalLinearFeet = totalLengthInches / 12m;
                var stickLengthFeet = group.Key.Category == ExtrusionCategories.Edge
                    ? NormalizeStickLength(layout.EdgeStickLengthFeet)
                    : NormalizeStickLength(layout.PanelToPanelStickLengthFeet);
                return new ExtrusionLengthSummary
                {
                    Category = group.Key.Category,
                    ExtrusionName = group.Key.ExtrusionName,
                    TotalLengthInches = totalLengthInches,
                    SegmentCount = group.Count(),
                    TotalLinearFeet = totalLinearFeet,
                    StickLengthFeet = stickLengthFeet,
                    RequiredStickCount = (int)Math.Ceiling(totalLinearFeet / stickLengthFeet)
                };
            })
            .ToArray();

        var additionalSummaries = layout.AdditionalLineItems
            .Select(item => BuildAdditionalLineItemSummary(item, segments))
            .Where(summary => summary is not null)
            .Select(summary => summary!)
            .ToArray();

        return baseSummaries.Concat(additionalSummaries).ToArray();
    }

    private static ExtrusionLengthSummary? BuildAdditionalLineItemSummary(
        ExtrusionAdditionalLineItem item,
        IReadOnlyList<ExtrusionSegmentDetail> segments)
    {
        var matching = segments
            .Where(segment => IncludesCategory(item.QuantityBasis, segment.Category))
            .ToArray();
        var totalLengthInches = matching.Sum(segment => segment.LengthInches);
        var totalLinearFeet = totalLengthInches / 12m;
        var stickLengthFeet = NormalizeStickLength(item.StickLengthFeet);

        return new ExtrusionLengthSummary
        {
            Category = ExtrusionCategories.AdditionalLineItem,
            ExtrusionName = item.Name,
            TotalLengthInches = totalLengthInches,
            SegmentCount = matching.Length,
            TotalLinearFeet = totalLinearFeet,
            StickLengthFeet = stickLengthFeet,
            RequiredStickCount = totalLinearFeet <= 0 ? 0 : (int)Math.Ceiling(totalLinearFeet / stickLengthFeet)
        };
    }

    private static bool IncludesCategory(string quantityBasis, string category) =>
        string.Equals(quantityBasis, ExtrusionLineItemQuantityBases.Both, StringComparison.Ordinal) ||
        (string.Equals(quantityBasis, ExtrusionLineItemQuantityBases.Edge, StringComparison.Ordinal) &&
            string.Equals(category, ExtrusionCategories.Edge, StringComparison.Ordinal)) ||
        (string.Equals(quantityBasis, ExtrusionLineItemQuantityBases.PanelToPanel, StringComparison.Ordinal) &&
            string.Equals(category, ExtrusionCategories.PanelToPanel, StringComparison.Ordinal));

    private static IReadOnlyList<ExtrusionAdditionalLineItem> NormalizeAdditionalLineItems(
        IReadOnlyList<ExtrusionAdditionalLineItem>? items) =>
        (items ?? Array.Empty<ExtrusionAdditionalLineItem>())
            .Select((item, index) => new ExtrusionAdditionalLineItem
            {
                Id = NormalizeName(item.Id, $"line-item-{index + 1}"),
                Name = NormalizeName(item.Name, $"Line Item {index + 1}"),
                QuantityBasis = NormalizeQuantityBasis(item.QuantityBasis),
                StickLengthFeet = NormalizeStickLength(item.StickLengthFeet)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();

    private static string NormalizeQuantityBasis(string? value) =>
        string.Equals(value, ExtrusionLineItemQuantityBases.PanelToPanel, StringComparison.Ordinal)
            ? ExtrusionLineItemQuantityBases.PanelToPanel
            : string.Equals(value, ExtrusionLineItemQuantityBases.Edge, StringComparison.Ordinal)
                ? ExtrusionLineItemQuantityBases.Edge
                : ExtrusionLineItemQuantityBases.Both;

    private static IEnumerable<ExtrusionPanelInstance> SortPanelsForRows(IEnumerable<ExtrusionPanelInstance> panels) =>
        panels
            .OrderByDescending(panel => panel.Width)
            .ThenByDescending(panel => panel.Length)
            .ThenBy(panel => panel.Label, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeGroupName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Ungrouped : value.Trim();

    private static string NormalizeSheetGroupName(string? sheetNumber) =>
        string.IsNullOrWhiteSpace(sheetNumber) ? Ungrouped : $"Sheet {sheetNumber.Trim()}";

    private static string NormalizeGroupingMode(string? value, IReadOnlyList<ExtrusionPanelInstance> panels) =>
        string.Equals(value, ExtrusionGroupingModes.Group, StringComparison.Ordinal)
            ? ExtrusionGroupingModes.Group
            : string.Equals(value, ExtrusionGroupingModes.SheetNumber, StringComparison.Ordinal)
                ? ExtrusionGroupingModes.SheetNumber
                : panels.Any(panel => panel.SheetNumber is not null)
                    ? ExtrusionGroupingModes.SheetNumber
                    : ExtrusionGroupingModes.Group;

    private static string GetPanelGroupName(ExtrusionPanelInstance panel, string groupingMode) =>
        string.Equals(groupingMode, ExtrusionGroupingModes.SheetNumber, StringComparison.Ordinal)
            ? panel.SheetGroupName
            : panel.GroupName;

    private static string DisplayGroupName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Ungrouped : value.Trim();

    private static string NormalizeName(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool IsKnownEdge(string? edge) =>
        string.Equals(edge, ExtrusionEdgeNames.Top, StringComparison.Ordinal) ||
        string.Equals(edge, ExtrusionEdgeNames.Right, StringComparison.Ordinal) ||
        string.Equals(edge, ExtrusionEdgeNames.Bottom, StringComparison.Ordinal) ||
        string.Equals(edge, ExtrusionEdgeNames.Left, StringComparison.Ordinal);

    private static bool IsHorizontal(string edge) =>
        string.Equals(edge, ExtrusionEdgeNames.Top, StringComparison.Ordinal) ||
        string.Equals(edge, ExtrusionEdgeNames.Bottom, StringComparison.Ordinal);

    private static bool IsIgnoredEdge(ExtrusionGroupLayout group, string instanceId, string edge) =>
        group.EdgeAssignments.Any(assignment =>
            string.Equals(assignment.InstanceId, instanceId, StringComparison.Ordinal) &&
            string.Equals(assignment.Edge, edge, StringComparison.Ordinal) &&
            assignment.IsIgnored);

    private static bool IsIgnoredJoint(ExtrusionGroupLayout group, string firstInstanceId, string secondInstanceId, string firstEdge)
    {
        var secondEdge = firstEdge switch
        {
            ExtrusionEdgeNames.Right => ExtrusionEdgeNames.Left,
            ExtrusionEdgeNames.Bottom => ExtrusionEdgeNames.Top,
            ExtrusionEdgeNames.Left => ExtrusionEdgeNames.Right,
            _ => ExtrusionEdgeNames.Bottom
        };

        return IsIgnoredEdge(group, firstInstanceId, firstEdge) ||
               IsIgnoredEdge(group, secondInstanceId, secondEdge);
    }

    private static IEnumerable<(string Edge, int Row, int Column)> NeighborPositions(ExtrusionGridCell cell)
    {
        yield return (ExtrusionEdgeNames.Top, cell.Row - 1, cell.Column);
        yield return (ExtrusionEdgeNames.Right, cell.Row, cell.Column + 1);
        yield return (ExtrusionEdgeNames.Bottom, cell.Row + 1, cell.Column);
        yield return (ExtrusionEdgeNames.Left, cell.Row, cell.Column - 1);
    }

    private static decimal NormalizeStickLength(decimal value) =>
        value <= 0 ? 20m : value;

    private static string EdgeLocationKey(string instanceId, string edge) => $"{instanceId}|{edge}";

    private static string CellKey(int row, int column) => $"{row}:{column}";

    private static string LayoutKey(string? optimizationGroupId, string? partGroupName) =>
        $"{optimizationGroupId?.Trim() ?? string.Empty}\u001f{NormalizeGroupName(partGroupName)}";

    private static string NormalizeJointId(ExtrusionJointAssignment joint) =>
        string.IsNullOrWhiteSpace(joint.JointId)
            ? BuildJointId(joint.FirstInstanceId, joint.SecondInstanceId)
            : joint.JointId;

    private static string BuildJointId(string first, string second) =>
        string.CompareOrdinal(first, second) <= 0
            ? $"{first}|{second}"
            : $"{second}|{first}";
}
