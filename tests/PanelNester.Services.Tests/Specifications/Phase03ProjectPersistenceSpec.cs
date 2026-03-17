using PanelNester.Domain.Models;

namespace PanelNester.Services.Tests.Specifications;

internal static class Phase03ProjectPersistenceSpec
{
    internal static IReadOnlyList<Material> SnapshotReferencedMaterials(
        IReadOnlyCollection<Material> liveLibrary,
        IReadOnlyCollection<string> selectedMaterialIds,
        IReadOnlyCollection<PartRow> parts)
    {
        var liveById = liveLibrary
            .Where(material => !string.IsNullOrWhiteSpace(material.MaterialId))
            .ToDictionary(material => material.MaterialId, StringComparer.Ordinal);
        var liveByName = liveLibrary
            .Where(material => !string.IsNullOrWhiteSpace(material.Name))
            .ToDictionary(material => material.Name, StringComparer.Ordinal);
        var snapshots = new Dictionary<string, Material>(StringComparer.Ordinal);

        foreach (var selectedMaterialId in selectedMaterialIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            if (liveById.TryGetValue(selectedMaterialId, out var material))
            {
                snapshots[material.MaterialId] = material;
            }
        }

        foreach (var materialName in parts
                     .Select(part => part.MaterialName)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.Ordinal))
        {
            if (liveByName.TryGetValue(materialName, out var material))
            {
                snapshots[material.MaterialId] = material;
            }
        }

        return snapshots.Values
            .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(material => material.MaterialId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<Material> RestoreProjectMaterials(
        IReadOnlyCollection<Material> materialSnapshots,
        IReadOnlyCollection<Material> liveLibrary) =>
        materialSnapshots
            .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(material => material.MaterialId, StringComparer.Ordinal)
            .ToArray();

    internal static string? ClassifyLoadFailure(bool fileExists, bool jsonIsValid, int version)
    {
        if (!fileExists)
        {
            return "project-not-found";
        }

        if (!jsonIsValid)
        {
            return "project-corrupt";
        }

        return version == Project.CurrentVersion
            ? null
            : "project-unsupported-version";
    }

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
