namespace PanelNester.Domain.Models;

public static class ExtrusionCategories
{
    public const string PanelToPanel = "Panel-to-panel";
    public const string Edge = "Edge";
}

public static class ExtrusionEdgeNames
{
    public const string Top = "top";
    public const string Right = "right";
    public const string Bottom = "bottom";
    public const string Left = "left";
}

public sealed record ExtrusionLayoutState
{
    public string PanelToPanelExtrusionName { get; init; } = "Panel Joint";

    public string EdgeExtrusionName { get; init; } = "Perimeter Edge";

    public decimal PanelToPanelStickLengthFeet { get; init; } = 20m;

    public decimal EdgeStickLengthFeet { get; init; } = 20m;

    public IReadOnlyList<ExtrusionGroupLayout> Groups { get; init; } = Array.Empty<ExtrusionGroupLayout>();
}

public sealed record ExtrusionGroupLayout
{
    public string GroupName { get; init; } = string.Empty;

    public int Rows { get; init; }

    public int Columns { get; init; }

    public IReadOnlyList<ExtrusionGridCell> Cells { get; init; } = Array.Empty<ExtrusionGridCell>();

    public IReadOnlyList<ExtrusionEdgeAssignment> EdgeAssignments { get; init; } = Array.Empty<ExtrusionEdgeAssignment>();

    public IReadOnlyList<ExtrusionJointAssignment> JointAssignments { get; init; } = Array.Empty<ExtrusionJointAssignment>();
}

public sealed record ExtrusionPanelInstance
{
    public string InstanceId { get; init; } = string.Empty;

    public string SourceRowId { get; init; } = string.Empty;

    public string ImportedId { get; init; } = string.Empty;

    public int QuantityIndex { get; init; }

    public string Label { get; init; } = string.Empty;

    public string MaterialName { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public decimal Length { get; init; }

    public decimal Width { get; init; }

    public bool IsStale { get; init; }
}

public sealed record ExtrusionGridCell
{
    public string InstanceId { get; init; } = string.Empty;

    public int Row { get; init; }

    public int Column { get; init; }
}

public sealed record ExtrusionEdgeAssignment
{
    public string InstanceId { get; init; } = string.Empty;

    public string Edge { get; init; } = string.Empty;

    public string ExtrusionName { get; init; } = "Perimeter Edge";
}

public sealed record ExtrusionJointAssignment
{
    public string JointId { get; init; } = string.Empty;

    public string FirstInstanceId { get; init; } = string.Empty;

    public string SecondInstanceId { get; init; } = string.Empty;

    public string Edge { get; init; } = string.Empty;

    public string ExtrusionName { get; init; } = "Panel Joint";

    public bool IsEnabled { get; init; } = true;
}

public sealed record ExtrusionLayoutRequest
{
    public Project Project { get; init; } = new();
}

public sealed record ExtrusionReportRequest
{
    public Project Project { get; init; } = new();
}

public sealed record ExtrusionReportData
{
    public string? CompanyLogoPath { get; init; }

    public ProjectMetadata ProjectMetadata { get; init; } = new();

    public ReportSettings ReportSettings { get; init; } = new();

    public ExtrusionLayoutState Layout { get; init; } = new();

    public IReadOnlyList<ExtrusionPanelInstance> Panels { get; init; } = Array.Empty<ExtrusionPanelInstance>();

    public IReadOnlyList<ExtrusionLengthSummary> OverallLengths { get; init; } = Array.Empty<ExtrusionLengthSummary>();

    public IReadOnlyList<ExtrusionGroupSummary> Groups { get; init; } = Array.Empty<ExtrusionGroupSummary>();

    public IReadOnlyList<ExtrusionSegmentDetail> Segments { get; init; } = Array.Empty<ExtrusionSegmentDetail>();

    public bool HasTakeoff { get; init; }
}

public sealed record ExtrusionLengthSummary
{
    public string Category { get; init; } = string.Empty;

    public string ExtrusionName { get; init; } = string.Empty;

    public decimal TotalLengthInches { get; init; }

    public int SegmentCount { get; init; }

    public decimal TotalLinearFeet { get; init; }

    public decimal StickLengthFeet { get; init; }

    public int RequiredStickCount { get; init; }
}

public sealed record ExtrusionGroupSummary
{
    public string GroupName { get; init; } = string.Empty;

    public IReadOnlyList<ExtrusionLengthSummary> Lengths { get; init; } = Array.Empty<ExtrusionLengthSummary>();
}

public sealed record ExtrusionSegmentDetail
{
    public string GroupName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string ExtrusionName { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public decimal LengthInches { get; init; }
}
