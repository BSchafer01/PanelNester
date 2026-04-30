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

        var panels = ExpandPanels(request.Project.State.Parts);
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

        var panels = ExpandPanels(request.Project.State.Parts);
        var layout = NormalizeLayout(request.Project.State.ExtrusionLayout, panels);
        var segments = BuildSegments(layout, panels);
        var overall = BuildLengthSummaries(segments, layout);
        var groupSummaries = segments
            .GroupBy(segment => segment.GroupName, StringComparer.Ordinal)
            .OrderBy(group => DisplayGroupName(group.Key), StringComparer.OrdinalIgnoreCase)
            .Select(group => new ExtrusionGroupSummary
            {
                GroupName = group.Key,
                Lengths = BuildLengthSummaries(group.ToArray(), layout)
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
                Segments = segments,
                HasTakeoff = segments.Count > 0
            });
    }

    internal static IReadOnlyList<ExtrusionPanelInstance> ExpandPanels(IReadOnlyList<PartRow>? parts)
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
            for (var index = 1; index <= part.Quantity; index++)
            {
                panels.Add(
                    new ExtrusionPanelInstance
                    {
                        InstanceId = $"{sourceRowId}#{index}",
                        SourceRowId = sourceRowId,
                        ImportedId = part.ImportedId,
                        QuantityIndex = index,
                        Label = $"{baseLabel}#{index}",
                        MaterialName = part.MaterialName,
                        GroupName = NormalizeGroupName(part.Group),
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
        var existingGroups = layout.Groups
            .GroupBy(group => NormalizeGroupName(group.GroupName), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        var groups = panels
            .GroupBy(panel => panel.GroupName, StringComparer.Ordinal)
            .OrderBy(group => DisplayGroupName(group.Key), StringComparer.OrdinalIgnoreCase)
            .Select(group => NormalizeGroupLayout(
                group.Key,
                group.ToArray(),
                existingGroups.GetValueOrDefault(group.Key),
                layout))
            .ToArray();

        var staleGroups = layout.Groups
            .Where(group => !groups.Any(next => string.Equals(next.GroupName, NormalizeGroupName(group.GroupName), StringComparison.Ordinal)))
            .Select(group => group with
            {
                Cells = group.Cells
                    .Where(cell => panelIds.Contains(cell.InstanceId))
                    .ToArray(),
                EdgeAssignments = group.EdgeAssignments
                    .Where(edge => panelIds.Contains(edge.InstanceId))
                    .ToArray(),
                JointAssignments = group.JointAssignments
                    .Where(joint => panelIds.Contains(joint.FirstInstanceId) && panelIds.Contains(joint.SecondInstanceId))
                    .ToArray()
            })
            .Where(group => group.Cells.Count > 0)
            .ToArray();

        return layout with
        {
            PanelToPanelExtrusionName = NormalizeName(layout.PanelToPanelExtrusionName, "Panel Joint"),
            EdgeExtrusionName = NormalizeName(layout.EdgeExtrusionName, "Perimeter Edge"),
            PanelToPanelStickLengthFeet = NormalizeStickLength(layout.PanelToPanelStickLengthFeet),
            EdgeStickLengthFeet = NormalizeStickLength(layout.EdgeStickLengthFeet),
            Groups = groups.Concat(staleGroups).ToArray()
        };
    }

    private static ExtrusionGroupLayout NormalizeGroupLayout(
        string groupName,
        IReadOnlyList<ExtrusionPanelInstance> panels,
        ExtrusionGroupLayout? existing,
        ExtrusionLayoutState layout)
    {
        var defaultColumns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(panels.Count)));
        var defaultRows = Math.Max(1, (int)Math.Ceiling(panels.Count / (decimal)defaultColumns));
        var rows = Math.Max(existing?.Rows ?? defaultRows, 1);
        var columns = Math.Max(existing?.Columns ?? defaultColumns, 1);
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
            .Where(joint => panelIds.Contains(joint.FirstInstanceId) && panelIds.Contains(joint.SecondInstanceId))
            .Select(joint => joint with { ExtrusionName = NormalizeName(joint.ExtrusionName, layout.PanelToPanelExtrusionName) })
            .Distinct()
            .ToArray();

        return new ExtrusionGroupLayout
        {
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

        return segments
            .Where(segment => segment.LengthInches > 0)
            .OrderBy(segment => DisplayGroupName(segment.GroupName), StringComparer.OrdinalIgnoreCase)
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

                if (!hasNeighbor || isDisabledJoint || explicitEdges.Contains(EdgeLocationKey(cell.InstanceId, edge)))
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
                if (assignment is { IsEnabled: false })
                {
                    continue;
                }

                yield return new ExtrusionSegmentDetail
                {
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
    }

    private static IReadOnlyList<ExtrusionLengthSummary> BuildLengthSummaries(
        IReadOnlyList<ExtrusionSegmentDetail> segments,
        ExtrusionLayoutState layout) =>
        segments
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

    private static IEnumerable<ExtrusionPanelInstance> SortPanelsForRows(IEnumerable<ExtrusionPanelInstance> panels) =>
        panels
            .OrderByDescending(panel => panel.Width)
            .ThenByDescending(panel => panel.Length)
            .ThenBy(panel => panel.Label, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeGroupName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Ungrouped : value.Trim();

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

    private static string NormalizeJointId(ExtrusionJointAssignment joint) =>
        string.IsNullOrWhiteSpace(joint.JointId)
            ? BuildJointId(joint.FirstInstanceId, joint.SecondInstanceId)
            : joint.JointId;

    private static string BuildJointId(string first, string second) =>
        string.CompareOrdinal(first, second) <= 0
            ? $"{first}|{second}"
            : $"{second}|{first}";
}
