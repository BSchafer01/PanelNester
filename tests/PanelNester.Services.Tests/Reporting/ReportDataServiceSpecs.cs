using PanelNester.Domain.Models;
using PanelNester.Services.Reporting;

namespace PanelNester.Services.Tests.Reporting;

public sealed class ReportDataServiceSpecs
{
    [Fact]
    public async Task Report_data_defaults_settings_from_project_metadata()
    {
        var material = BuildMaterial("mat-birch", "Baltic Birch");
        var metadata = new ProjectMetadata
        {
            ProjectName = "Shop Cabinet",
            ProjectNumber = "PN-42",
            CustomerName = "Acme Millwork",
            Date = new DateTime(2026, 03, 14, 0, 0, 0, DateTimeKind.Utc),
            Notes = "Export notes"
        };
        var nestResponse = CreateNestResponse(material);
        var batch = new BatchNestResponse
        {
            Success = true,
            LegacyResult = nestResponse,
            MaterialResults =
            [
                new MaterialNestResult
                {
                    MaterialName = material.Name,
                    MaterialId = material.MaterialId,
                    Result = nestResponse
                }
            ]
        };
        var project = new Project
        {
            ProjectId = "project-001",
            Metadata = metadata,
            Settings = new ProjectSettings { KerfWidth = 0.125m },
            MaterialSnapshots = [material],
            State = new ProjectState
            {
                LastBatchNestingResult = batch
            }
        };

        var service = new ReportDataService();

        var report = await service.BuildReportDataAsync(new ReportDataRequest { Project = project });

        Assert.Equal("Acme Millwork", report.Settings.CompanyName);
        Assert.Equal("Shop Cabinet Nesting Report", report.Settings.ReportTitle);
        Assert.Equal("Shop Cabinet", report.Settings.ProjectJobName);
        Assert.Equal("PN-42", report.Settings.ProjectJobNumber);
        Assert.Equal(metadata.Date, report.Settings.ReportDate);
        Assert.Equal("Export notes", report.Settings.Notes);
        Assert.True(report.HasResults);

        var section = Assert.Single(report.Materials);
        Assert.Equal(material.Name, section.MaterialName);
        Assert.Single(section.Sheets);
        var placement = Assert.Single(section.Sheets[0].Placements);
        Assert.Equal("Casework", GetPlacementGroup(placement));
        Assert.Single(report.UnplacedItems);
    }

    [Fact]
    public async Task Report_data_falls_back_to_last_single_result_when_batch_is_missing()
    {
        var material = BuildMaterial("mat-maple", "Maple Ply");
        var nestResponse = CreateNestResponse(material);
        var project = new Project
        {
            ProjectId = "project-002",
            Metadata = new ProjectMetadata { ProjectName = "Fallback Run" },
            Settings = new ProjectSettings(),
            MaterialSnapshots = [material],
            State = new ProjectState
            {
                SelectedMaterialId = material.MaterialId,
                LastNestingResult = nestResponse
            }
        };

        var service = new ReportDataService();

        var report = await service.BuildReportDataAsync(new ReportDataRequest { Project = project });

        var section = Assert.Single(report.Materials);
        Assert.Equal(material.Name, section.MaterialName);
        Assert.Single(section.Sheets);
    }

    [Fact]
    public async Task Report_data_returns_an_empty_state_when_batch_and_stored_results_are_missing()
    {
        var project = new Project
        {
            ProjectId = "project-empty",
            Metadata = new ProjectMetadata { ProjectName = "Empty Report" },
            Settings = new ProjectSettings()
        };

        var service = new ReportDataService();

        var report = await service.BuildReportDataAsync(new ReportDataRequest { Project = project });

        Assert.False(report.HasResults);
        Assert.Empty(report.Materials);
        Assert.Empty(report.UnplacedItems);
    }

    [Fact]
    public async Task Report_data_keeps_material_sections_but_marks_zero_sheet_batches_as_no_results()
    {
        var material = BuildMaterial("mat-empty", "Empty Sheets");
        var project = new Project
        {
            ProjectId = "project-zero-sheets",
            Metadata = new ProjectMetadata { ProjectName = "Zero Sheets" },
            Settings = new ProjectSettings(),
            MaterialSnapshots = [material]
        };
        var batch = new BatchNestResponse
        {
            Success = true,
            MaterialResults =
            [
                new MaterialNestResult
                {
                    MaterialName = material.Name,
                    MaterialId = material.MaterialId,
                    Result = new NestResponse
                    {
                        Success = true,
                        Summary = new MaterialSummary()
                    }
                }
            ]
        };

        var service = new ReportDataService();

        var report = await service.BuildReportDataAsync(new ReportDataRequest { Project = project, BatchResult = batch });

        var section = Assert.Single(report.Materials);
        Assert.Equal(material.Name, section.MaterialName);
        Assert.Empty(section.Sheets);
        Assert.False(report.HasResults);
    }

    [Fact]
    public async Task Report_data_marks_zero_placement_sheets_as_no_results()
    {
        var material = BuildMaterial("mat-zero-placement", "Zero Placement");
        var project = new Project
        {
            ProjectId = "project-zero-placement",
            Metadata = new ProjectMetadata { ProjectName = "Zero Placement" },
            Settings = new ProjectSettings(),
            MaterialSnapshots = [material]
        };
        var batch = new BatchNestResponse
        {
            Success = true,
            MaterialResults =
            [
                new MaterialNestResult
                {
                    MaterialName = material.Name,
                    MaterialId = material.MaterialId,
                    Result = new NestResponse
                    {
                        Success = true,
                        Sheets =
                        [
                            new NestSheet
                            {
                                SheetId = "sheet-001",
                                SheetNumber = 1,
                                MaterialName = material.Name,
                                SheetLength = material.SheetLength,
                                SheetWidth = material.SheetWidth,
                                UtilizationPercent = 0m
                            }
                        ],
                        Summary = new MaterialSummary
                        {
                            TotalSheets = 1,
                            TotalPlaced = 0,
                            TotalUnplaced = 0,
                            OverallUtilization = 0m
                        }
                    }
                }
            ]
        };

        var service = new ReportDataService();

        var report = await service.BuildReportDataAsync(new ReportDataRequest { Project = project, BatchResult = batch });

        var section = Assert.Single(report.Materials);
        var sheet = Assert.Single(section.Sheets);
        Assert.Empty(sheet.Placements);
        Assert.False(report.HasResults);
    }

    private static NestResponse CreateNestResponse(Material material) =>
        new()
        {
            Success = true,
            Sheets =
            [
                new NestSheet
                {
                    SheetId = "sheet-001",
                    SheetNumber = 1,
                    MaterialName = material.Name,
                    SheetLength = material.SheetLength,
                    SheetWidth = material.SheetWidth,
                    UtilizationPercent = 50m
                }
            ],
            Placements =
            [
                new NestPlacement
                {
                    PlacementId = "placement-001",
                    SheetId = "sheet-001",
                    PartId = "P-001",
                    Group = "Casework",
                    X = 0.5m,
                    Y = 0.5m,
                    Width = 24m,
                    Height = 12m
                }
            ],
            UnplacedItems =
            [
                new UnplacedItem
                {
                    PartId = "P-404",
                    ReasonCode = NestingFailureCodes.NoLayoutSpace,
                    ReasonDescription = "No space."
                }
            ],
            Summary = new MaterialSummary
            {
                TotalSheets = 1,
                TotalPlaced = 1,
                TotalUnplaced = 1,
                OverallUtilization = 50m
            }
        };

    private static Material BuildMaterial(string materialId, string name) =>
        new()
        {
            MaterialId = materialId,
            Name = name,
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        };

    private static string? GetPlacementGroup(NestPlacement placement)
    {
        var groupProperty = typeof(NestPlacement).GetProperty("Group");
        Assert.True(groupProperty is not null, "NestPlacement.Group should exist so grouped report review and export keep explicit placement ownership.");
        return groupProperty!.GetValue(placement) as string;
    }
}
