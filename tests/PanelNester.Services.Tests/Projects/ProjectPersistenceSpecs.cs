using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.FlatBuffers;
using PanelNester.Domain.Models;
using PanelNester.Domain.Contracts;
using PanelNester.Services.Projects;
using PanelNester.Services.Tests.Specifications;
using Fb = PanelNester.Services.Persistence.FlatBuffers;

namespace PanelNester.Services.Tests.Projects;

public sealed class ProjectPersistenceSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.ProjectPersistenceSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Project_kind_survives_a_real_save_and_load_round_trip()
    {
        var filePath = Path.Combine(_workspacePath, "stock-length-project.pnest");
        var service = new ProjectService(
            new FakeMaterialService(),
            idGenerator: () => "stock-project-001");

        var created = await service.NewAsync(projectKind: ProjectKind.StockLength);
        var saved = await service.SaveAsync(created.Project!, filePath);
        var reopened = await service.LoadAsync(filePath);

        Assert.True(created.Success);
        Assert.Equal(ProjectKind.StockLength, created.Project!.ProjectKind);
        Assert.True(saved.Success);
        Assert.True(reopened.Success);
        Assert.Equal(Project.CurrentVersion, reopened.Project!.Version);
        Assert.Equal(ProjectKind.StockLength, reopened.Project.ProjectKind);
        Assert.Equal("stock-project-001", reopened.Project.ProjectId);
    }

    [Fact]
    public async Task Empty_project_can_change_kind_while_retaining_identity_and_general_metadata()
    {
        var service = new ProjectService(
            new FakeMaterialService(),
            idGenerator: () => "stable-project-id");
        var created = await service.NewAsync(
            metadata: new ProjectMetadata
            {
                ProjectName = "Mesa Canopy",
                ProjectNumber = "MC-2407",
                CustomerName = "Desert Builders"
            });
        var project = created.Project! with
        {
            Settings = created.Project.Settings with
            {
                ReportSettings = new ReportSettings
                {
                    CompanyName = "Configured Company",
                    ReportTitle = "Fabrication Report",
                    ProjectJobName = "Custom Job Name",
                    ProjectJobNumber = "Custom Job Number",
                    ReleaseId = "Release 7",
                    Status = "Issued",
                    ReportDate = new DateTime(2026, 8, 26),
                    Notes = "Keep these report notes"
                },
                StiffenerTakeoff = created.Project.Settings.StiffenerTakeoff with
                {
                    Enabled = true,
                    ReportTitle = "Kind-specific stiffener report"
                }
            },
            MaterialSnapshots = [BuildMaterial("mat-1", "Aluminum")],
            State = created.Project.State with
            {
                SourceFilePath = @"C:\imports\panels.csv",
                SelectedMaterialId = "mat-1",
                ExtrusionLayout = new ExtrusionLayoutState
                {
                    GroupingMode = ExtrusionGroupingModes.SheetNumber,
                    AdditionalLineItems =
                    [
                        new ExtrusionAdditionalLineItem { Id = "ext-1", Name = "Angle" }
                    ]
                }
            }
        };

        var changed = await service.ChangeKindAsync(project, ProjectKind.StockLength);

        Assert.True(changed.Success);
        Assert.Equal(ProjectKind.StockLength, changed.Project!.ProjectKind);
        Assert.Equal("stable-project-id", changed.Project.ProjectId);
        Assert.Equal("Mesa Canopy", changed.Project.Metadata.ProjectName);
        Assert.Equal("MC-2407", changed.Project.Metadata.ProjectNumber);
        Assert.Equal("Desert Builders", changed.Project.Metadata.CustomerName);
        Assert.Equal(0m, changed.Project.Settings.KerfWidth);
        Assert.Equal(project.Settings.ReportSettings, changed.Project.Settings.ReportSettings);
        Assert.Equal(new StiffenerTakeoffSettings(), changed.Project.Settings.StiffenerTakeoff);
        Assert.Empty(changed.Project.MaterialSnapshots);
        Assert.Null(changed.Project.State.SourceFilePath);
        Assert.Null(changed.Project.State.SelectedMaterialId);
        Assert.Equal(string.Empty, changed.Project.State.ExtrusionLayout.GroupingMode);
        Assert.Empty(changed.Project.State.ExtrusionLayout.AdditionalLineItems);
    }

    [Fact]
    public async Task Project_kind_cannot_change_while_sheet_parts_exist()
    {
        var service = new ProjectService(new FakeMaterialService());
        var project = Phase03ProjectPersistenceSpec.CreateSampleProject();

        var changed = await service.ChangeKindAsync(project, ProjectKind.StockLength);

        Assert.False(changed.Success);
        var error = Assert.Single(changed.Errors);
        Assert.Equal("project-kind-change-not-empty", error.Code);
        Assert.Contains("no sheet parts or Required Pieces", error.Message);
    }

    [Fact]
    public async Task New_projects_start_without_an_empty_optimization_group()
    {
        var service = new ProjectService(
            new FakeMaterialService(),
            idGenerator: () => "generated-id");

        var result = await service.NewAsync();

        Assert.True(result.Success);
        Assert.Empty(result.Project!.State.OptimizationGroups);
    }

    [Fact]
    public async Task Empty_current_projects_with_import_source_metadata_do_not_gain_a_legacy_group()
    {
        var filePath = Path.Combine(_workspacePath, "empty-import-audit.pnest");
        var service = new ProjectService(
            new FakeMaterialService(),
            idGenerator: () => "generated-id");
        var created = await service.NewAsync();
        var project = created.Project! with
        {
            State = created.Project!.State with
            {
                ImportSource = new ImportSourceMetadata
                {
                    ImportSourcePath = "Sheet1.xlsx",
                    ContentFingerprint = "FINGERPRINT",
                    ContentLength = 100,
                    SnapshotCapturedAtUtc = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc)
                }
            }
        };

        var saved = await service.SaveAsync(project, filePath);

        Assert.True(saved.Success);
        Assert.Empty(saved.Project!.State.OptimizationGroups);
    }

    [Fact]
    public async Task Current_csv_projects_migrate_into_a_source_named_group_without_reinterpreting_part_groups()
    {
        var filePath = Path.Combine(_workspacePath, "migrated-csv.pnest");
        var service = new ProjectService(
            new FakeMaterialService(),
            idGenerator: () => "unused-id");
        var project = Phase03ProjectPersistenceSpec.CreateSampleProject() with
        {
            State = Phase03ProjectPersistenceSpec.CreateSampleProject().State with
            {
                SourceFilePath = @"C:\imports\Lobby Panels.csv",
                Parts =
                [
                    new PartRow
                    {
                        RowId = "row-001",
                        ImportedId = "A-100",
                        MaterialName = "Baltic Birch",
                        Group = "Casework"
                    }
                ]
            }
        };

        var result = await service.SaveAsync(project, filePath);

        Assert.True(result.Success);
        var group = Assert.Single(result.Project!.State.OptimizationGroups);
        Assert.Equal("project-phase3-001", group.OptimizationGroupId);
        Assert.Equal("Lobby Panels", group.Name);
        Assert.Equal("Casework", Assert.Single(group.Parts).Group);
    }

    [Fact]
    public async Task Migration_marks_saved_results_stale_when_a_placement_references_an_unknown_sheet()
    {
        var filePath = Path.Combine(_workspacePath, "stale-result.pnest");
        var service = new ProjectService(new FakeMaterialService());
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var invalidResult = sample.State.LastNestingResult! with
        {
            Placements =
            [
                sample.State.LastNestingResult!.Placements[0] with
                {
                    SheetId = "missing-sheet"
                }
            ]
        };
        var project = sample with
        {
            State = sample.State with
            {
                LastNestingResult = invalidResult,
                LastBatchNestingResult = null
            }
        };

        var result = await service.SaveAsync(project, filePath);

        Assert.True(result.Success);
        var group = Assert.Single(result.Project!.State.OptimizationGroups);
        Assert.Equal(OptimizationResultStatus.Stale, group.ResultStatus);
        Assert.Equal("missing-sheet", Assert.Single(group.LastNestingResult!.Placements).SheetId);
    }

    [Fact]
    public async Task Current_schema_round_trips_optimization_group_identity_order_parts_and_valid_results()
    {
        var filePath = Path.Combine(_workspacePath, "optimization-group-roundtrip.pnest");
        var serializer = new ProjectSerializer();
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var group = new OptimizationGroup
        {
            OptimizationGroupId = "group-stable-001",
            Name = "Lobby Panels",
            Order = 0,
            Origin = OptimizationGroupOrigin.ImportSource,
            Parts = [sample.State.Parts[0] with { Group = "Casework" }],
            LastNestingResult = sample.State.LastNestingResult,
            LastBatchNestingResult = sample.State.LastBatchNestingResult,
            ResultStatus = OptimizationResultStatus.Valid
        };
        var project = sample with
        {
            State = sample.State with { OptimizationGroups = [group] }
        };

        await serializer.SaveAsync(project, filePath);
        var restored = await serializer.LoadAsync(filePath);

        Assert.Equal(Project.CurrentVersion, restored.Version);
        Assert.Equal(ProjectKind.Sheet, restored.ProjectKind);
        var restoredGroup = Assert.Single(restored.State.OptimizationGroups);
        Assert.Equal("group-stable-001", restoredGroup.OptimizationGroupId);
        Assert.Equal("Lobby Panels", restoredGroup.Name);
        Assert.Equal(0, restoredGroup.Order);
        Assert.Equal(OptimizationGroupOrigin.ImportSource, restoredGroup.Origin);
        Assert.Equal("Casework", Assert.Single(restoredGroup.Parts).Group);
        Assert.Equal(OptimizationResultStatus.Valid, restoredGroup.ResultStatus);
        Assert.Equivalent(group.LastBatchNestingResult, restoredGroup.LastBatchNestingResult, strict: true);
    }

    [Fact]
    public async Task Current_schema_round_trips_import_configuration_and_source_metadata_without_snapshot_bytes()
    {
        var filePath = Path.Combine(_workspacePath, "import-context-roundtrip.pnest");
        var importSourcePath = Path.Combine(_workspacePath, "Lobby.xlsx");
        const string sourceOnlyMarker = "SOURCE-BYTES-MUST-NOT-BE-EMBEDDED-7C1E6D1A";
        EnsureWorkspace();
        await File.WriteAllTextAsync(importSourcePath, sourceOnlyMarker);
        var serializer = new ProjectSerializer();
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var project = sample with
        {
            State = sample.State with
            {
                ImportSource = new ImportSourceMetadata
                {
                    ImportSourcePath = importSourcePath,
                    ContentFingerprint = "A1B2C3",
                    ContentLength = 1234,
                    SnapshotCapturedAtUtc = new DateTime(2026, 8, 25, 12, 30, 0, DateTimeKind.Utc)
                },
                ImportConfiguration = new ImportConfiguration
                {
                    Options = new ImportOptions
                    {
                        ColumnMappings =
                        [
                            new ImportColumnMapping
                            {
                                SourceColumn = "Panel ID",
                                TargetField = ImportFieldNames.Id
                            }
                        ]
                    },
                    PartOverrides =
                    [
                        new PartOverride
                        {
                            RowId = "row-7",
                            ImportedValues = new PartRow { RowId = "row-7", LengthText = "bad" },
                            CurrentValues = new PartRow { RowId = "row-7", LengthText = "48", Length = 48 },
                            SourceReferences =
                            [
                                new SourceReference
                                {
                                    WorksheetName = "Parts",
                                    WorksheetPosition = 1,
                                    PhysicalRow = 7,
                                    SourceFingerprint = "ROW-7-FINGERPRINT"
                                }
                            ]
                        }
                    ],
                    Worksheets =
                    [
                        new ImportWorksheetConfiguration
                        {
                            WorksheetName = "Parts",
                            OriginalPosition = 1,
                            HeadingRange = "R1C1:R1C5",
                            ColumnMappings =
                            [
                                new ImportColumnMapping
                                {
                                    SourceColumn = "Panel ID",
                                    TargetField = ImportFieldNames.Id
                                }
                            ],
                            OptimizationGroupId = "group-stable-001",
                            ExcludedSourceRows =
                            [
                                new ExcludedSourceRow
                                {
                                    RowId = "row-8",
                                    SourceReference = new SourceReference
                                    {
                                        WorksheetName = "Parts",
                                        WorksheetPosition = 1,
                                        PhysicalRow = 8,
                                        SourceFingerprint = "ROW-8-FINGERPRINT"
                                    },
                                    OriginalValidationError = new SourceRowValidationError
                                    {
                                        Code = "missing-id",
                                        Message = "Id is required."
                                    }
                                }
                            ]
                        }
                    ]
                },
                Parts = sample.State.Parts.Select(part => part with
                {
                    SourceReferences =
                    [
                        new SourceReference
                        {
                            WorksheetName = "Parts",
                            WorksheetPosition = 1,
                            PhysicalRow = 2,
                            SourceFingerprint = "ROW-FINGERPRINT"
                        }
                    ]
                }).ToArray()
            }
        };

        await serializer.SaveAsync(project, filePath);
        var restored = await serializer.LoadAsync(filePath);
        var persistedContent = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(filePath));

        Assert.Equivalent(project.State.ImportSource, restored.State.ImportSource, strict: true);
        Assert.Equivalent(project.State.ImportConfiguration, restored.State.ImportConfiguration, strict: true);
        Assert.Equivalent(
            project.State.Parts[0].SourceReferences,
            restored.State.Parts[0].SourceReferences,
            strict: true);
        Assert.DoesNotContain(sourceOnlyMarker, persistedContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_json_projects_migrate_to_the_current_schema_with_valid_results_intact()
    {
        var filePath = Path.Combine(_workspacePath, "legacy-v1-json.pnest");
        var serializer = new ProjectSerializer();
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var legacy = sample with
        {
            Version = 1,
            State = sample.State with
            {
                SourceFilePath = @"C:\imports\North Lobby.xlsx",
                Parts = [sample.State.Parts[0] with { Group = "Casework" }],
                OptimizationGroups = []
            }
        };

        EnsureWorkspace();
        await File.WriteAllTextAsync(
            filePath,
            JsonSerializer.Serialize(legacy, CreateLegacyJsonOptions()));

        var restored = await serializer.LoadAsync(filePath);

        Assert.Equal(5, Project.CurrentVersion);
        Assert.Equal(Project.CurrentVersion, restored.Version);
        var group = Assert.Single(restored.State.OptimizationGroups);
        Assert.Equal("project-phase3-001", group.OptimizationGroupId);
        Assert.Equal("North Lobby", group.Name);
        Assert.Equal("Casework", Assert.Single(group.Parts).Group);
        Assert.Equal(OptimizationResultStatus.Valid, group.ResultStatus);
        Assert.Equivalent(legacy.State.LastBatchNestingResult, group.LastBatchNestingResult, strict: true);
    }

    [Fact]
    public async Task Version_three_projects_migrate_only_import_created_group_origins()
    {
        var filePath = Path.Combine(_workspacePath, "version-three-group-origins.pnest");
        var sourcePart = new PartRow { RowId = "source", ImportedId = "SOURCE" };
        var userGroupPart = new PartRow { RowId = "user", ImportedId = "USER" };
        var project = new Project
        {
            Version = 3,
            ProjectId = "version-three-project",
            State = new ProjectState
            {
                SourceFilePath = "existing.xlsx",
                ImportSource = new ImportSourceMetadata { ImportSourcePath = "existing.xlsx" },
                ImportConfiguration = new ImportConfiguration
                {
                    Worksheets =
                    [
                        new ImportWorksheetConfiguration
                        {
                            WorksheetName = "Source Created",
                            OriginalPosition = 1,
                            OptimizationGroupId = "import-session-1"
                        },
                        new ImportWorksheetConfiguration
                        {
                            WorksheetName = "User Assigned",
                            OriginalPosition = 2,
                            OptimizationGroupId = "user-group"
                        }
                    ]
                },
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "import-session-1",
                        Name = "Source Created",
                        Order = 0,
                        Parts = [sourcePart]
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "user-group",
                        Name = "User Assigned",
                        Order = 1,
                        Parts = [userGroupPart]
                    }
                ]
            }
        };
        EnsureWorkspace();
        await File.WriteAllTextAsync(
            filePath,
            JsonSerializer.Serialize(project, CreateLegacyJsonOptions()));

        var restored = await new ProjectSerializer().LoadAsync(filePath);

        Assert.Equal(Project.CurrentVersion, restored.Version);
        Assert.Equal(
            OptimizationGroupOrigin.ImportSource,
            restored.State.OptimizationGroups.Single(group => group.OptimizationGroupId == "import-session-1").Origin);
        Assert.Equal(
            OptimizationGroupOrigin.Project,
            restored.State.OptimizationGroups.Single(group => group.OptimizationGroupId == "user-group").Origin);
    }

    [Fact]
    public async Task Legacy_flatbuffer_projects_migrate_with_parts_and_part_groups_intact()
    {
        var filePath = Path.Combine(_workspacePath, "legacy-v1-flatbuffer.pnest");
        EnsureWorkspace();
        WriteLegacyFlatBufferProject(filePath);

        var restored = await new ProjectSerializer().LoadAsync(filePath);

        Assert.Equal(Project.CurrentVersion, restored.Version);
        var group = Assert.Single(restored.State.OptimizationGroups);
        Assert.Equal("legacy-project-001", group.OptimizationGroupId);
        Assert.Equal("Legacy Workbook", group.Name);
        var part = Assert.Single(group.Parts);
        Assert.Equal("P-001", part.ImportedId);
        Assert.Equal("Casework", part.Group);
        Assert.Equal(OptimizationResultStatus.None, group.ResultStatus);
    }

    [Fact]
    public async Task Future_project_schema_versions_are_rejected_at_the_persistence_boundary()
    {
        var filePath = Path.Combine(_workspacePath, "future-schema.pnest");
        var futureProject = Phase03ProjectPersistenceSpec.CreateSampleProject() with
        {
            Version = Project.CurrentVersion + 1
        };
        EnsureWorkspace();
        await File.WriteAllTextAsync(
            filePath,
            JsonSerializer.Serialize(futureProject, CreateLegacyJsonOptions()));

        var result = await new ProjectService(new FakeMaterialService()).LoadAsync(filePath);

        Assert.False(result.Success);
        Assert.Equal("project-unsupported-version", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task Project_service_preserves_an_edited_optimization_group_name_and_identity()
    {
        var filePath = Path.Combine(_workspacePath, "edited-group-name.pnest");
        var service = new ProjectService(new FakeMaterialService());
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var project = sample with
        {
            State = sample.State with
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "stable-group-id",
                        Name = "Edited Group Name",
                        Order = 0,
                        Parts = sample.State.Parts,
                        LastNestingResult = sample.State.LastNestingResult,
                        LastBatchNestingResult = sample.State.LastBatchNestingResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ]
            }
        };

        var saved = await service.SaveAsync(project, filePath);
        var loaded = await service.LoadAsync(filePath);

        Assert.True(saved.Success);
        Assert.True(loaded.Success);
        var group = Assert.Single(loaded.Project!.State.OptimizationGroups);
        Assert.Equal("stable-group-id", group.OptimizationGroupId);
        Assert.Equal("Edited Group Name", group.Name);
    }

    [Fact]
    public async Task Project_service_round_trips_multiple_groups_without_losing_order_parts_or_part_groups()
    {
        var filePath = Path.Combine(_workspacePath, "multiple-groups.pnest");
        var service = new ProjectService(new FakeMaterialService());
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var firstPart = sample.State.Parts[0] with { Group = "Phase A" };
        var secondPart = firstPart with
        {
            RowId = "row-002",
            ImportedId = "B-200",
            Group = "Phase B"
        };
        var project = sample with
        {
            State = sample.State with
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "group-b",
                        Name = "Panels",
                        Order = 20,
                        Parts = [secondPart]
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "group-a",
                        Name = "Panels",
                        Order = 10,
                        Parts = [firstPart],
                        LastNestingResult = sample.State.LastNestingResult,
                        LastBatchNestingResult = sample.State.LastBatchNestingResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ]
            }
        };

        var saved = await service.SaveAsync(project, filePath);
        var loaded = await service.LoadAsync(filePath);

        Assert.True(saved.Success);
        Assert.True(loaded.Success);
        Assert.Collection(
            loaded.Project!.State.OptimizationGroups,
            group =>
            {
                Assert.Equal("group-a", group.OptimizationGroupId);
                Assert.Equal("Panels", group.Name);
                Assert.Equal(10, group.Order);
                Assert.Equal("Phase A", Assert.Single(group.Parts).Group);
                Assert.NotNull(group.LastBatchNestingResult);
            },
            group =>
            {
                Assert.Equal("group-b", group.OptimizationGroupId);
                Assert.Equal("Panels (2)", group.Name);
                Assert.Equal(20, group.Order);
                Assert.Equal("Phase B", Assert.Single(group.Parts).Group);
            });
    }

    [Fact]
    public async Task Migration_marks_saved_results_stale_when_a_placement_references_an_unknown_part()
    {
        var filePath = Path.Combine(_workspacePath, "unknown-result-part.pnest");
        var service = new ProjectService(new FakeMaterialService());
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var invalidResult = sample.State.LastNestingResult! with
        {
            Placements =
            [
                sample.State.LastNestingResult.Placements[0] with
                {
                    PartId = "not-a-project-part"
                }
            ],
            Summary = sample.State.LastNestingResult.Summary with { TotalPlaced = 1 }
        };
        var project = sample with
        {
            State = sample.State with
            {
                LastNestingResult = invalidResult,
                LastBatchNestingResult = null
            }
        };

        var result = await service.SaveAsync(project, filePath);

        Assert.True(result.Success);
        Assert.Equal(
            OptimizationResultStatus.Stale,
            Assert.Single(result.Project!.State.OptimizationGroups).ResultStatus);
    }

    [Fact]
    public async Task Migration_marks_saved_results_stale_when_a_part_instance_is_duplicated_and_another_is_missing()
    {
        var filePath = Path.Combine(_workspacePath, "duplicate-result-part.pnest");
        var service = new ProjectService(new FakeMaterialService());
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var duplicatedPlacement = sample.State.LastNestingResult!.Placements[0];
        var invalidResult = sample.State.LastNestingResult with
        {
            Placements =
            [
                duplicatedPlacement,
                duplicatedPlacement with { PlacementId = "placement-duplicate" }
            ]
        };
        var project = sample with
        {
            State = sample.State with
            {
                LastNestingResult = invalidResult,
                LastBatchNestingResult = null
            }
        };

        var result = await service.SaveAsync(project, filePath);

        Assert.True(result.Success);
        Assert.Equal(
            OptimizationResultStatus.Stale,
            Assert.Single(result.Project!.State.OptimizationGroups).ResultStatus);
    }

    [Fact]
    public async Task Migration_marks_a_partial_standalone_material_result_stale()
    {
        var filePath = Path.Combine(_workspacePath, "partial-result.pnest");
        var service = new ProjectService(new FakeMaterialService());
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var partialResult = sample.State.LastNestingResult! with
        {
            Placements = [sample.State.LastNestingResult.Placements[0]],
            Summary = sample.State.LastNestingResult.Summary with { TotalPlaced = 1 }
        };
        var project = sample with
        {
            State = sample.State with
            {
                LastNestingResult = partialResult,
                LastBatchNestingResult = null
            }
        };

        var result = await service.SaveAsync(project, filePath);

        Assert.True(result.Success);
        Assert.Equal(
            OptimizationResultStatus.Stale,
            Assert.Single(result.Project!.State.OptimizationGroups).ResultStatus);
    }

    [Fact]
    public void Saving_a_project_snapshots_selected_materials_and_exact_import_matches_only()
    {
        var liveLibrary = new[]
        {
            BuildMaterial("mat-birch", "Baltic Birch", notes: "Snapshot me"),
            BuildMaterial("mat-acm", "Black ACM", notes: "Used by imported parts"),
            BuildMaterial("mat-demo", "Demo Material", notes: "Do not include")
        };

        var snapshots = Phase03ProjectPersistenceSpec.SnapshotReferencedMaterials(
            liveLibrary,
            ["mat-birch"],
            [
                new PartRow { RowId = "row-1", ImportedId = "P-001", MaterialName = "Black ACM" },
                new PartRow { RowId = "row-2", ImportedId = "P-002", MaterialName = "black acm" }
            ]);

        Assert.Equal(["mat-birch", "mat-acm"], snapshots.Select(material => material.MaterialId).ToArray());
        Assert.DoesNotContain(snapshots, material => material.MaterialId == "mat-demo");
    }

    [Fact]
    public void Opening_a_saved_project_prefers_the_projects_snapshots_over_the_live_library()
    {
        var snapshot = BuildMaterial("mat-birch", "Baltic Birch", sheetLength: 96m, notes: "Saved with estimate A");
        var liveRevision = snapshot with
        {
            SheetLength = 120m,
            Notes = "Library edited after save",
            CostPerSheet = 165m
        };

        var restored = Phase03ProjectPersistenceSpec.RestoreProjectMaterials([snapshot], [liveRevision]);

        var restoredMaterial = Assert.Single(restored);
        Assert.Equal(96m, restoredMaterial.SheetLength);
        Assert.Equal("Saved with estimate A", restoredMaterial.Notes);
        Assert.Equal(142.75m, restoredMaterial.CostPerSheet);
    }

    [Theory]
    [InlineData(false, true, 1, "project-not-found")]
    [InlineData(true, false, 1, "project-corrupt")]
    [InlineData(true, true, 6, "project-unsupported-version")]
    [InlineData(true, true, 5, null)]
    [InlineData(true, true, 4, null)]
    [InlineData(true, true, 3, null)]
    [InlineData(true, true, 2, null)]
    [InlineData(true, true, 1, null)]
    public void Project_open_failures_stay_specific_and_user_actionable(
        bool fileExists,
        bool jsonIsValid,
        int version,
        string? expectedCode)
    {
        var actual = Phase03ProjectPersistenceSpec.ClassifyLoadFailure(fileExists, jsonIsValid, version);

        Assert.Equal(expectedCode, actual);
    }

    [Fact]
    public async Task Project_serializer_round_trips_metadata_parts_results_and_material_snapshots()
    {
        var filePath = Path.Combine(_workspacePath, "serializer-roundtrip.pnest");
        var serializer = new ProjectSerializer();
        var project = CreateCurrentSampleProject();

        await serializer.SaveAsync(project, filePath);
        AssertFlatBufferHeader(filePath, FlatBufferVersion);
        var restored = await serializer.LoadAsync(filePath);

        Assert.Equivalent(project, restored, strict: true);
    }

    [Fact]
    public async Task Project_serializer_reads_legacy_json_and_resaves_as_flatbuffers()
    {
        var legacyPath = Path.Combine(_workspacePath, "legacy-json.pnest");
        var resavePath = Path.Combine(_workspacePath, "legacy-resave.pnest");
        var serializer = new ProjectSerializer();
        var project = CreateCurrentSampleProject();
        var json = JsonSerializer.Serialize(project, CreateLegacyJsonOptions());

        EnsureWorkspace();
        await File.WriteAllTextAsync(legacyPath, json);
        var restored = await serializer.LoadAsync(legacyPath);
        await serializer.SaveAsync(restored, resavePath);

        Assert.Equivalent(project, restored, strict: true);
        AssertFlatBufferHeader(resavePath, FlatBufferVersion);
    }

    [Fact]
    public async Task Kerf_width_persists_across_project_save_open_cycle()
    {
        var filePath = Path.Combine(_workspacePath, "kerf-roundtrip.pnest");
        var serializer = new ProjectSerializer();
        var project = Phase03ProjectPersistenceSpec.CreateSampleProject();
        project = project with
        {
            Settings = project.Settings with { KerfWidth = 0.125m }
        };

        EnsureWorkspace();
        await serializer.SaveAsync(project, filePath);
        var restored = await serializer.LoadAsync(filePath);

        Assert.Equal(0.125m, restored.Settings.KerfWidth);
    }

    [Fact]
    public async Task Required_date_persists_across_project_save_open_cycle()
    {
        var filePath = Path.Combine(_workspacePath, "required-date-roundtrip.pnest");
        var serializer = new ProjectSerializer();
        var sampleProject = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var project = sampleProject with
        {
            Metadata = sampleProject.Metadata with
            {
                RequiredDate = new DateTime(2026, 05, 20, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        EnsureWorkspace();
        await serializer.SaveAsync(project, filePath);
        var restored = await serializer.LoadAsync(filePath);

        Assert.Equal(project.Metadata.RequiredDate, restored.Metadata.RequiredDate);
    }

    [Fact]
    public async Task Stiffener_takeoff_settings_persist_across_project_save_open_cycle()
    {
        var filePath = Path.Combine(_workspacePath, "stiffener-roundtrip.pnest");
        var serializer = new ProjectSerializer();
        var project = Phase03ProjectPersistenceSpec.CreateSampleProject() with
        {
            Settings = Phase03ProjectPersistenceSpec.CreateSampleProject().Settings with
            {
                StiffenerTakeoff = new StiffenerTakeoffSettings
                {
                    Enabled = true,
                    MinimumLengthInches = 40m,
                    MinimumWidthInches = 36m,
                    WidthDeductionInches = 3.5m,
                    StockLengthFeet = 24m,
                    ReportTitle = "Project Stiffener Takeoff",
                    Extrusion = "2x1 aluminum tube",
                    ReleaseId = "REL-04B",
                    PoNumber = "PO-88210",
                    Color = "Bone White",
                    ColorNumber = "BW-11",
                    Manufacturer = "Kovach",
                    Status = "Ready for production"
                }
            }
        };

        EnsureWorkspace();
        await serializer.SaveAsync(project, filePath);
        var restored = await serializer.LoadAsync(filePath);

        Assert.Equal(project.Settings.StiffenerTakeoff, restored.Settings.StiffenerTakeoff);
    }

    [Fact]
    public async Task Report_settings_persist_release_and_status_across_project_save_open_cycle()
    {
        var filePath = Path.Combine(_workspacePath, "report-settings-roundtrip.pnest");
        var serializer = new ProjectSerializer();
        var sampleProject = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var project = sampleProject with
        {
            Settings = sampleProject.Settings with
            {
                ReportSettings = sampleProject.Settings.ReportSettings with
                {
                    CompanyName = "Acme Panels",
                    ReportTitle = "Batch Nest Release 04",
                    ReleaseId = "REL-04",
                    Status = "Issued for fabrication"
                }
            }
        };

        EnsureWorkspace();
        await serializer.SaveAsync(project, filePath);
        var restored = await serializer.LoadAsync(filePath);

        Assert.Equal(project.Settings.ReportSettings, restored.Settings.ReportSettings);
    }

    [Fact]
    public async Task Loading_legacy_json_without_stiffener_takeoff_settings_applies_safe_defaults()
    {
        var filePath = Path.Combine(_workspacePath, "legacy-no-stiffener.pnest");
        var serializer = new ProjectSerializer();
        var project = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var json = JsonSerializer.SerializeToNode(project, CreateLegacyJsonOptions());
        Assert.NotNull(json);

        var root = Assert.IsType<JsonObject>(json);
        var settings = Assert.IsType<JsonObject>(root["settings"]);
        settings.Remove("stiffenerTakeoff");

        EnsureWorkspace();
        await File.WriteAllTextAsync(filePath, root.ToJsonString(CreateLegacyJsonOptions()));

        var restored = await serializer.LoadAsync(filePath);

        Assert.Equal(
            new StiffenerTakeoffSettings(),
            restored.Settings.StiffenerTakeoff);
    }

    [Fact]
    public async Task Project_service_flags_corrupt_flatbuffer_payloads()
    {
        var filePath = Path.Combine(_workspacePath, "corrupt-flatbuffer.pnest");
        EnsureWorkspace();
        WriteFlatBufferFile(filePath, FlatBufferVersion, [0x00, 0x01, 0x02]);
        var service = new ProjectService(new FakeMaterialService());

        var result = await service.LoadAsync(filePath);

        Assert.False(result.Success);
        Assert.Equal("project-corrupt", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task Project_service_flags_unsupported_flatbuffer_versions()
    {
        var filePath = Path.Combine(_workspacePath, "unsupported-flatbuffer.pnest");
        EnsureWorkspace();
        WriteFlatBufferFile(filePath, (ushort)(FlatBufferVersion + 1), []);
        var service = new ProjectService(new FakeMaterialService());

        var result = await service.LoadAsync(filePath);

        Assert.False(result.Success);
        Assert.Equal("project-unsupported-version", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task Project_service_save_handles_duplicate_material_names()
    {
        var filePath = Path.Combine(_workspacePath, "duplicate-materials.pnest");
        var project = new Project
        {
            ProjectId = "project-duplicate",
            Metadata = new ProjectMetadata(),
            Settings = new ProjectSettings(),
            State = new ProjectState
            {
                Parts =
                [
                    new PartRow
                    {
                        RowId = "row-001",
                        ImportedId = "P-001",
                        MaterialName = "Birch"
                    }
                ]
            }
        };

        var service = new ProjectService(new FakeMaterialService(
            BuildMaterial("mat-alpha", "Birch", notes: "First"),
            BuildMaterial("mat-beta", "Birch", notes: "Second")));

        var result = await service.SaveAsync(project, filePath);

        Assert.True(result.Success);
        var snapshot = Assert.Single(result.Project!.MaterialSnapshots);
        Assert.Equal("mat-beta", snapshot.MaterialId);
    }

    [Fact]
    public async Task Project_service_updates_metadata_without_rereading_live_materials_on_open()
    {
        var originalMaterial = BuildMaterial("mat-birch", "Baltic Birch", notes: "Saved with estimate A");
        var updatedLibraryMaterial = originalMaterial with
        {
            Notes = "Library edited after save",
            CostPerSheet = 165m
        };

        var filePath = Path.Combine(_workspacePath, "service-roundtrip.pnest");
        var saveService = new ProjectService(new FakeMaterialService([originalMaterial]), idGenerator: () => "project-generated-001");
        var project = Phase03ProjectPersistenceSpec.CreateSampleProject(originalMaterial) with
        {
            ProjectId = string.Empty
        };

        var saved = await saveService.SaveAsync(project, filePath);
        var loadService = new ProjectService(new FakeMaterialService([updatedLibraryMaterial]));
        var loaded = await loadService.LoadAsync(filePath);
        var updated = await loadService.UpdateMetadataAsync(
            loaded.Project!,
            loaded.Project!.Metadata with { ProjectName = "North Lobby Panels Rev B" },
            loaded.Project.Settings with { KerfWidth = 0.08m });

        Assert.True(saved.Success);
        Assert.True(loaded.Success);
        Assert.True(updated.Success);
        Assert.Equal("project-generated-001", saved.Project!.ProjectId);
        Assert.Equal("Saved with estimate A", Assert.Single(saved.Project.MaterialSnapshots).Notes);
        Assert.Equal("Saved with estimate A", Assert.Single(loaded.Project!.MaterialSnapshots).Notes);
        Assert.Equal("North Lobby Panels Rev B", updated.Project!.Metadata.ProjectName);
        Assert.Equal(0.08m, updated.Project.Settings.KerfWidth);
        Assert.Equal("Saved with estimate A", Assert.Single(updated.Project.MaterialSnapshots).Notes);
    }

    [Fact]
    public async Task Project_serializer_preserves_part_row_text_inputs_for_post_import_editing_round_trips()
    {
        var filePath = Path.Combine(_workspacePath, "part-row-inputs.pnest");
        var serializer = new ProjectSerializer();
        var project = Phase03ProjectPersistenceSpec.CreateSampleProject() with
        {
            State = Phase03ProjectPersistenceSpec.CreateSampleProject().State with
            {
                Parts =
                [
                    new PartRow
                    {
                        RowId = "row-001",
                        ImportedId = "A-100",
                        LengthText = "oops",
                        Length = 0m,
                        WidthText = "12",
                        Width = 12m,
                        QuantityText = "2",
                        Quantity = 2,
                        MaterialName = "Baltic Birch",
                        ValidationStatus = ValidationStatuses.Error,
                        ValidationMessages = ["Length must be a decimal value."]
                    }
                ]
            }
        };

        await serializer.SaveAsync(project, filePath);
        var restored = await serializer.LoadAsync(filePath);

        var restoredRow = Assert.Single(restored.State.Parts);
        Assert.Equal("oops", restoredRow.LengthText);
        Assert.Equal("12", restoredRow.WidthText);
        Assert.Equal("2", restoredRow.QuantityText);
        Assert.Equal(ValidationStatuses.Error, restoredRow.ValidationStatus);
    }

    [Fact]
    public async Task Project_serializer_round_trips_optional_group_assignments_for_imported_rows()
    {
        var filePath = Path.Combine(_workspacePath, "part-row-groups.pnest");
        var serializer = new ProjectSerializer();
        var sampleProject = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var project = sampleProject with
        {
            State = sampleProject.State with
            {
                Parts =
                [
                    new PartRow
                    {
                        RowId = "row-001",
                        ImportedId = "A-100",
                        LengthText = "24",
                        Length = 24m,
                        WidthText = "12",
                        Width = 12m,
                        QuantityText = "1",
                        Quantity = 1,
                        MaterialName = "Baltic Birch",
                        Group = "Casework",
                        ValidationStatus = ValidationStatuses.Valid
                    }
                ]
            }
        };

        await serializer.SaveAsync(project, filePath);
        var restored = await serializer.LoadAsync(filePath);

        var restoredRow = Assert.Single(restored.State.Parts);
        Assert.Equal("Casework", restoredRow.Group);
    }

    [Fact]
    public async Task Project_serializer_round_trips_optional_group_assignments_for_nest_placements()
    {
        var filePath = Path.Combine(_workspacePath, "placement-groups.pnest");
        var serializer = new ProjectSerializer();
        var sampleProject = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var lastNestingResult = sampleProject.State.LastNestingResult!;
        var lastBatchResult = sampleProject.State.LastBatchNestingResult!;
        var lastBatchLegacyResult = lastBatchResult.LegacyResult!;

        var project = sampleProject with
        {
            State = sampleProject.State with
            {
                LastNestingResult = lastNestingResult with
                {
                    Placements =
                    [
                        lastNestingResult.Placements[0] with { Group = "Casework" }
                    ]
                },
                LastBatchNestingResult = lastBatchResult with
                {
                    LegacyResult = lastBatchLegacyResult with
                    {
                        Placements =
                        [
                            lastBatchLegacyResult.Placements[0] with { Group = "Casework" }
                        ]
                    },
                    MaterialResults =
                    [
                        lastBatchResult.MaterialResults[0] with
                        {
                            Result = lastBatchResult.MaterialResults[0].Result with
                            {
                                Placements =
                                [
                                    lastBatchResult.MaterialResults[0].Result.Placements[0] with { Group = "Casework" }
                                ]
                            }
                        }
                    ]
                }
            }
        };

        await serializer.SaveAsync(project, filePath);
        var restored = await serializer.LoadAsync(filePath);
        var restoredBatchResult = restored.State.LastBatchNestingResult!;
        var restoredBatchLegacyResult = restoredBatchResult.LegacyResult!;

        Assert.Equal("Casework", Assert.Single(restored.State.LastNestingResult!.Placements).Group);
        Assert.Equal("Casework", Assert.Single(restoredBatchLegacyResult.Placements).Group);
        Assert.Equal("Casework", Assert.Single(restoredBatchResult.MaterialResults[0].Result.Placements).Group);
    }

    private static Material BuildMaterial(
        string materialId,
        string name,
        decimal sheetLength = 96m,
        string? notes = null) =>
        new()
        {
            MaterialId = materialId,
            Name = name,
            SheetLength = sheetLength,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m,
            Notes = notes,
            CostPerSheet = 142.75m
        };

    private static Project CreateCurrentSampleProject()
    {
        var project = Phase03ProjectPersistenceSpec.CreateSampleProject();
        return project with
        {
            State = project.State with
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = project.ProjectId,
                        Name = "north-lobby",
                        Order = 0,
                        Parts = project.State.Parts,
                        LastNestingResult = project.State.LastNestingResult,
                        LastBatchNestingResult = project.State.LastBatchNestingResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ]
            }
        };
    }

    private static JsonSerializerOptions CreateLegacyJsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    private static void AssertFlatBufferHeader(string filePath, ushort expectedVersion)
    {
        var data = File.ReadAllBytes(filePath);
        Assert.True(data.Length >= FlatBufferHeaderLength);
        Assert.Equal("PNST", Encoding.ASCII.GetString(data.AsSpan(0, 4)));
        Assert.Equal(expectedVersion, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4, 2)));
    }

    private static void WriteFlatBufferFile(string filePath, ushort version, byte[] payload)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        Span<byte> header = stackalloc byte[FlatBufferHeaderLength];
        header[0] = (byte)'P';
        header[1] = (byte)'N';
        header[2] = (byte)'S';
        header[3] = (byte)'T';
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4, 2), version);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(6, 2), 0);
        stream.Write(header);
        if (payload.Length > 0)
        {
            stream.Write(payload);
        }
    }

    private static void WriteLegacyFlatBufferProject(string filePath)
    {
        var builder = new FlatBufferBuilder(1024);
        var rowId = builder.CreateString("row-001");
        var importedId = builder.CreateString("P-001");
        var materialName = builder.CreateString("Baltic Birch");
        var partGroup = builder.CreateString("Casework");

        Fb.PartRow.StartPartRow(builder);
        Fb.PartRow.AddRowId(builder, rowId);
        Fb.PartRow.AddImportedId(builder, importedId);
        Fb.PartRow.AddLength(builder, 24);
        Fb.PartRow.AddWidth(builder, 12);
        Fb.PartRow.AddQuantity(builder, 1);
        Fb.PartRow.AddMaterialName(builder, materialName);
        Fb.PartRow.AddGroup(builder, partGroup);
        var part = Fb.PartRow.EndPartRow(builder);
        var parts = Fb.ProjectState.CreatePartsVector(builder, [part]);
        var sourcePath = builder.CreateString(@"C:\imports\Legacy Workbook.xlsx");

        Fb.ProjectState.StartProjectState(builder);
        Fb.ProjectState.AddSourceFilePath(builder, sourcePath);
        Fb.ProjectState.AddParts(builder, parts);
        var state = Fb.ProjectState.EndProjectState(builder);
        var projectId = builder.CreateString("legacy-project-001");

        Fb.ProjectDocument.StartProjectDocument(builder);
        Fb.ProjectDocument.AddVersion(builder, 1);
        Fb.ProjectDocument.AddProjectId(builder, projectId);
        Fb.ProjectDocument.AddState(builder, state);
        var document = Fb.ProjectDocument.EndProjectDocument(builder);
        Fb.ProjectDocument.FinishProjectDocumentBuffer(builder, document);

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        Span<byte> header = stackalloc byte[FlatBufferHeaderLength];
        "PNST"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4, 2), FlatBufferVersion);
        stream.Write(header);
        stream.Write(builder.SizedByteArray());
    }

    private const ushort FlatBufferVersion = 2;
    private const int FlatBufferHeaderLength = 8;

    private void EnsureWorkspace() => Directory.CreateDirectory(_workspacePath);

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private sealed class FakeMaterialService(params Material[] materials) : IMaterialService
    {
        private readonly IReadOnlyList<Material> _materials = materials;

        public Task<IReadOnlyList<Material>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_materials);

        public Task<MaterialOperationResult> GetAsync(string materialId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialOperationResult> CreateAsync(Material material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialOperationResult> UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialDeleteResult> DeleteAsync(string materialId, bool isInUse = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
