using PanelNester.Domain.Models;

namespace PanelNester.Domain.Tests.Specifications;

internal static class Phase03ProjectExpectations
{
    internal static IReadOnlyList<string> PersistenceErrorCodes { get; } =
    [
        "project-not-found",
        "project-corrupt",
        "project-unsupported-version",
        "project-save-failed"
    ];

    internal static IReadOnlyList<string> RootPropertyNames { get; } =
    [
        "version",
        "projectId",
        "metadata",
        "settings",
        "materialSnapshots",
        "state"
    ];

    internal static IReadOnlyList<string> MetadataPropertyNames { get; } =
    [
        "projectName",
        "projectNumber",
        "customerName",
        "estimator",
        "drafter",
        "pm",
        "date",
        "revision",
        "notes"
    ];

    internal static IReadOnlyList<string> StatePropertyNames { get; } =
    [
        "sourceFilePath",
        "parts",
        "selectedMaterialId",
        "lastNestingResult",
        "lastBatchNestingResult"
    ];

    internal static Project CreateSampleProject(Material? material = null)
    {
        var selectedMaterial = material ?? CreateSampleMaterial();

        return new Project
        {
            Version = Project.CurrentVersion,
            ProjectId = "project-phase3-001",
            Metadata = new ProjectMetadata
            {
                ProjectName = "North Lobby Panels",
                ProjectNumber = "25-014",
                CustomerName = "Acme Architectural",
                Estimator = "Blake",
                Drafter = "Morgan",
                Pm = "Riley",
                Date = new DateTime(2026, 03, 14, 0, 0, 0, DateTimeKind.Utc),
                Revision = "B",
                Notes = "Phase 3 sample project."
            },
            Settings = new ProjectSettings
            {
                KerfWidth = 0.0625m
            },
            MaterialSnapshots = [selectedMaterial],
            State = new ProjectState
            {
                SourceFilePath = @"C:\jobs\north-lobby.csv",
                SelectedMaterialId = selectedMaterial.MaterialId,
                Parts =
                [
                    new PartRow
                    {
                        RowId = "row-001",
                        ImportedId = "A-100",
                        Length = 24m,
                        Width = 12m,
                        Quantity = 2,
                        MaterialName = selectedMaterial.Name,
                        ValidationStatus = ValidationStatuses.Valid
                    }
                ],
                LastNestingResult = new NestResponse
                {
                    Success = true,
                    Sheets =
                    [
                        new NestSheet
                        {
                            SheetId = "sheet-001",
                            SheetNumber = 1,
                            MaterialName = selectedMaterial.Name,
                            SheetLength = selectedMaterial.SheetLength,
                            SheetWidth = selectedMaterial.SheetWidth,
                            UtilizationPercent = 55.5m
                        }
                    ],
                    Placements =
                    [
                        new NestPlacement
                        {
                            PlacementId = "placement-001",
                            SheetId = "sheet-001",
                            PartId = "row-001#1",
                            X = 0.5m,
                            Y = 0.5m,
                            Width = 24m,
                            Height = 12m,
                            Rotated90 = false
                        }
                    ],
                    Summary = new MaterialSummary
                    {
                        TotalSheets = 1,
                        TotalPlaced = 2,
                        TotalUnplaced = 0,
                        OverallUtilization = 55.5m
                    }
                },
                LastBatchNestingResult = new BatchNestResponse
                {
                    Success = true,
                    LegacyResult = new NestResponse
                    {
                        Success = true,
                        Sheets =
                        [
                            new NestSheet
                            {
                                SheetId = "sheet-001",
                                SheetNumber = 1,
                                MaterialName = selectedMaterial.Name,
                                SheetLength = selectedMaterial.SheetLength,
                                SheetWidth = selectedMaterial.SheetWidth,
                                UtilizationPercent = 55.5m
                            }
                        ],
                        Placements =
                        [
                            new NestPlacement
                            {
                                PlacementId = "placement-001",
                                SheetId = "sheet-001",
                                PartId = "row-001#1",
                                X = 0.5m,
                                Y = 0.5m,
                                Width = 24m,
                                Height = 12m,
                                Rotated90 = false
                            }
                        ],
                        Summary = new MaterialSummary
                        {
                            TotalSheets = 1,
                            TotalPlaced = 2,
                            TotalUnplaced = 0,
                            OverallUtilization = 55.5m
                        }
                    },
                    MaterialResults =
                    [
                        new MaterialNestResult
                        {
                            MaterialName = selectedMaterial.Name,
                            MaterialId = selectedMaterial.MaterialId,
                            Result = new NestResponse
                            {
                                Success = true,
                                Sheets =
                                [
                                    new NestSheet
                                    {
                                        SheetId = "sheet-001",
                                        SheetNumber = 1,
                                        MaterialName = selectedMaterial.Name,
                                        SheetLength = selectedMaterial.SheetLength,
                                        SheetWidth = selectedMaterial.SheetWidth,
                                        UtilizationPercent = 55.5m
                                    }
                                ],
                                Placements =
                                [
                                    new NestPlacement
                                    {
                                        PlacementId = "placement-001",
                                        SheetId = "sheet-001",
                                        PartId = "row-001#1",
                                        X = 0.5m,
                                        Y = 0.5m,
                                        Width = 24m,
                                        Height = 12m,
                                        Rotated90 = false
                                    }
                                ],
                                Summary = new MaterialSummary
                                {
                                    TotalSheets = 1,
                                    TotalPlaced = 2,
                                    TotalUnplaced = 0,
                                    OverallUtilization = 55.5m
                                }
                            }
                        }
                    ]
                }
            }
        };
    }

    internal static Material CreateSampleMaterial() =>
        new()
        {
            MaterialId = "mat-baltic-birch",
            Name = "Baltic Birch",
            SheetLength = 120m,
            SheetWidth = 60m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m,
            ColorFinish = "Clear",
            Notes = "Phase 3 sample material.",
            CostPerSheet = 142.75m
        };
}
