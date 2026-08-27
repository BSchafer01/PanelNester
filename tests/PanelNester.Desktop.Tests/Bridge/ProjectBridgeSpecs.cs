using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using PanelNester.Desktop.Bridge;
using PanelNester.Desktop.Tests.Specifications;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Materials;
using PanelNester.Services.Nesting;
using PanelNester.Services.Projects;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class ProjectBridgeSpecs : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = BridgeJson.SerializerOptions;
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.ProjectBridgeSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Project_kind_creation_and_change_flow_through_the_desktop_bridge()
    {
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "materials.json"));
        var materialService = new MaterialService(repository);
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-kind-001"),
            new CsvImportService(repository),
            new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var created = await DispatchAsync<NewProjectResponse>(
            dispatcher,
            BridgeMessageTypes.NewProject,
            new NewProjectRequest(ProjectKind: ProjectKind.StockLength));
        var stockProject = Assert.IsType<Project>(created.Project);

        var changed = await DispatchAsync<ChangeProjectKindResponse>(
            dispatcher,
            BridgeMessageTypes.ChangeProjectKind,
            new ChangeProjectKindRequest(stockProject, ProjectKind.Sheet));

        Assert.True(created.Success);
        Assert.Equal(ProjectKind.StockLength, stockProject.ProjectKind);
        Assert.True(changed.Success);
        Assert.Equal(ProjectKind.Sheet, changed.Project!.ProjectKind);
        Assert.Equal("project-kind-001", changed.Project.ProjectId);
    }

    [Fact]
    public async Task Manual_required_piece_workflow_round_trips_through_the_desktop_bridge()
    {
        var projectPath = Path.Combine(_workspacePath, "manual-stock.pnest");
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "materials.json"));
        var materialService = new MaterialService(repository);
        var ids = new Queue<string>(["stock-project", "frames", "piece-1"]);
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: ids.Dequeue),
            new CsvImportService(repository),
            new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var created = await DispatchAsync<NewProjectResponse>(
            dispatcher,
            BridgeMessageTypes.NewProject,
            new NewProjectRequest(ProjectKind: ProjectKind.StockLength));
        var grouped = await DispatchAsync<UpdateOptimizationGroupsResponse>(
            dispatcher,
            BridgeMessageTypes.UpdateOptimizationGroups,
            new UpdateOptimizationGroupsRequest(created.Project!, new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Create,
                Name = "Frames",
                StockLength = "240"
            }));
        var added = await DispatchAsync<UpdateRequiredPiecesResponse>(
            dispatcher,
            BridgeMessageTypes.UpdateRequiredPieces,
            new UpdateRequiredPiecesRequest(grouped.Project!, new RequiredPieceChange
            {
                Type = RequiredPieceChangeType.Create,
                OptimizationGroupId = "frames",
                Quantity = "2",
                Length = "18 7/16",
                ProfileNumber = "H-120",
                Finish = "Clear"
            }));

        Assert.True(added.Success);
        var group = Assert.Single(added.Project!.State.OptimizationGroups);
        Assert.Equal(240m, group.StockLength);
        Assert.Equal(18.4375m, Assert.Single(group.RequiredPieces).Length);
        Assert.Empty(added.Project.MaterialSnapshots);

        var edited = await DispatchAsync<UpdateRequiredPiecesResponse>(
            dispatcher,
            BridgeMessageTypes.UpdateRequiredPieces,
            new UpdateRequiredPiecesRequest(added.Project, new RequiredPieceChange
            {
                Type = RequiredPieceChangeType.Update,
                OptimizationGroupId = "frames",
                RequiredPieceId = "piece-1",
                Quantity = "3",
                Length = "18 1/2",
                ProfileNumber = "H-120",
                Finish = "Clear"
            }));
        var saved = await DispatchAsync<SaveProjectResponse>(
            dispatcher,
            BridgeMessageTypes.SaveProject,
            new SaveProjectRequest(edited.Project!, projectPath));
        var reopened = await DispatchAsync<OpenProjectResponse>(
            dispatcher,
            BridgeMessageTypes.OpenProject,
            new OpenProjectRequest(projectPath));
        var deleted = await DispatchAsync<UpdateRequiredPiecesResponse>(
            dispatcher,
            BridgeMessageTypes.UpdateRequiredPieces,
            new UpdateRequiredPiecesRequest(reopened.Project!, new RequiredPieceChange
            {
                Type = RequiredPieceChangeType.Delete,
                OptimizationGroupId = "frames",
                RequiredPieceId = "piece-1"
            }));

        Assert.True(edited.Success);
        Assert.True(saved.Success);
        Assert.True(reopened.Success);
        Assert.Equal(18.5m, Assert.Single(reopened.Project!.State.OptimizationGroups[0].RequiredPieces).Length);
        Assert.True(deleted.Success);
        Assert.Empty(deleted.Project!.State.OptimizationGroups[0].RequiredPieces);
    }

    [Fact]
    public async Task Generate_Selected_Cut_Plan_round_trips_through_the_bridge_and_project_persistence()
    {
        var projectPath = Path.Combine(_workspacePath, "generated-stock.pnest");
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "generated-materials.json"));
        var materialService = new MaterialService(repository);
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService),
            new CsvImportService(repository),
            new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
        var project = new Project
        {
            ProjectId = "stock-project",
            ProjectKind = ProjectKind.StockLength,
            Settings = new ProjectSettings { KerfWidth = 0.125m },
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "frames",
                        Name = "Frames",
                        StockLength = 120,
                        RequiredPieces =
                        [
                            new RequiredPiece
                            {
                                RequiredPieceId = "piece-1", Quantity = 2, Length = 48,
                                ProfileNumber = "P-100", Finish = "Clear"
                            }
                        ]
                    }
                ]
            }
        };

        var generated = await DispatchAsync<GenerateSelectedCutPlanResponse>(
            dispatcher,
            BridgeMessageTypes.GenerateSelectedCutPlan,
            new GenerateSelectedCutPlanRequest(project, "frames"));
        var saved = await DispatchAsync<SaveProjectResponse>(
            dispatcher,
            BridgeMessageTypes.SaveProject,
            new SaveProjectRequest(generated.Project!, projectPath));
        var reopened = await DispatchAsync<OpenProjectResponse>(
            dispatcher,
            BridgeMessageTypes.OpenProject,
            new OpenProjectRequest(projectPath));

        Assert.True(generated.Success, generated.Message);
        Assert.Equal(CutPlanStatus.Complete, generated.Result?.Status);
        Assert.DoesNotContain("__stock__", JsonSerializer.Serialize(generated, SerializerOptions), StringComparison.Ordinal);
        Assert.True(saved.Success);
        Assert.True(reopened.Success);
        var restoredGroup = Assert.Single(reopened.Project!.State.OptimizationGroups);
        var restored = restoredGroup.LastStockLengthOptimizationResult;
        Assert.Equal(OptimizationResultStatus.Valid, restoredGroup.ResultStatus);
        Assert.Equal(CutPlanStatus.Complete, restored?.Status);
        Assert.Equal(2, Assert.Single(Assert.Single(restored!.CutPlans).StockItems).CutSequence.Count);
    }

    [Fact]
    public async Task Generate_All_Stale_returns_successful_results_with_failed_group_diagnostics()
    {
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "all-stale-materials.json"));
        var materialService = new MaterialService(repository);
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService),
            new CsvImportService(repository),
            new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
        var project = new Project
        {
            ProjectId = "all-stale-project",
            ProjectKind = ProjectKind.StockLength,
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "successful", Name = "Successful", Order = 0,
                        StockLength = 120, ResultStatus = OptimizationResultStatus.Stale,
                        RequiredPieces =
                        [
                            new RequiredPiece
                            {
                                RequiredPieceId = "successful-piece", Quantity = 1, Length = 40,
                                ProfileNumber = "P-100"
                            }
                        ]
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "failed", Name = "Failed", Order = 1,
                        ResultStatus = OptimizationResultStatus.Stale,
                        RequiredPieces =
                        [
                            new RequiredPiece
                            {
                                RequiredPieceId = "failed-piece", Quantity = 1, Length = 30,
                                ProfileNumber = "P-200"
                            }
                        ]
                    }
                ]
            }
        };

        var generated = await DispatchAsync<GenerateAllStaleCutPlansResponse>(
            dispatcher,
            BridgeMessageTypes.GenerateAllStaleCutPlans,
            new GenerateAllStaleCutPlansRequest(project));

        Assert.False(generated.Success);
        Assert.NotNull(generated.Project);
        Assert.Equal(CutPlanStatus.Complete,
            generated.Project.State.OptimizationGroups[0].LastStockLengthOptimizationResult?.Status);
        var failure = Assert.Single(generated.Failures);
        Assert.Equal("failed", failure.OptimizationGroupId);
        Assert.Equal("cut-plan-invalid-input", failure.Code);
        Assert.Equal(failure.Code,
            generated.Project.State.OptimizationGroups[1].LastStockLengthGenerationError?.Code);
    }

    [Fact]
    public async Task Generate_Selected_returns_the_project_with_application_error_diagnostics()
    {
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "selected-failure-materials.json"));
        var materialService = new MaterialService(repository);
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(), materialService, new ProjectService(materialService),
            new CsvImportService(repository), new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
        var project = new Project
        {
            ProjectId = "selected-failure", ProjectKind = ProjectKind.StockLength,
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "frames", Name = "Frames",
                        RequiredPieces =
                        [
                            new RequiredPiece
                            {
                                RequiredPieceId = "piece-1", Quantity = 1, Length = 20,
                                ProfileNumber = "P-100"
                            }
                        ]
                    }
                ]
            }
        };

        var generated = await DispatchAsync<GenerateSelectedCutPlanResponse>(
            dispatcher, BridgeMessageTypes.GenerateSelectedCutPlan,
            new GenerateSelectedCutPlanRequest(project, "frames"));
        var projectPath = Path.Combine(_workspacePath, "selected-failure.pnest");
        var saved = await DispatchAsync<SaveProjectResponse>(
            dispatcher, BridgeMessageTypes.SaveProject,
            new SaveProjectRequest(generated.Project!, projectPath));
        var reopened = await DispatchAsync<OpenProjectResponse>(
            dispatcher, BridgeMessageTypes.OpenProject,
            new OpenProjectRequest(projectPath));

        Assert.False(generated.Success);
        var group = Assert.Single(generated.Project!.State.OptimizationGroups);
        Assert.Equal("cut-plan-invalid-input", group.LastStockLengthGenerationError?.Code);
        Assert.Equal(OptimizationResultStatus.None, group.ResultStatus);
        Assert.True(saved.Success);
        Assert.Equal("cut-plan-invalid-input",
            Assert.Single(reopened.Project!.State.OptimizationGroups).LastStockLengthGenerationError?.Code);
    }

    [Fact]
    public async Task Required_Piece_metadata_edits_preserve_current_results_but_geometry_edits_invalidate_them()
    {
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "freshness-materials.json"));
        var materialService = new MaterialService(repository);
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(), materialService, new ProjectService(materialService),
            new CsvImportService(repository), new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
        var piece = new RequiredPiece
        {
            RequiredPieceId = "piece-1", Quantity = 1, Length = 40,
            ProfileNumber = "P-100", PartName = "Original", PartNumber = "A-1",
            IsManual = true
        };
        var project = new Project
        {
            ProjectId = "freshness-project", ProjectKind = ProjectKind.StockLength,
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "frames", Name = "Frames", StockLength = 120,
                        RequiredPieces = [piece]
                    }
                ]
            }
        };
        var generated = await DispatchAsync<GenerateSelectedCutPlanResponse>(
            dispatcher, BridgeMessageTypes.GenerateSelectedCutPlan,
            new GenerateSelectedCutPlanRequest(project, "frames"));

        var metadataEdited = await DispatchAsync<UpdateRequiredPiecesResponse>(
            dispatcher, BridgeMessageTypes.UpdateRequiredPieces,
            new UpdateRequiredPiecesRequest(generated.Project!, new RequiredPieceChange
            {
                Type = RequiredPieceChangeType.Update,
                OptimizationGroupId = "frames", RequiredPieceId = "piece-1",
                Quantity = "1", Length = "40", ProfileNumber = "P-100",
                PartName = "Corrected", PartNumber = "A-2"
            }));
        var geometryEdited = await DispatchAsync<UpdateRequiredPiecesResponse>(
            dispatcher, BridgeMessageTypes.UpdateRequiredPieces,
            new UpdateRequiredPiecesRequest(metadataEdited.Project!, new RequiredPieceChange
            {
                Type = RequiredPieceChangeType.Update,
                OptimizationGroupId = "frames", RequiredPieceId = "piece-1",
                Quantity = "1", Length = "41", ProfileNumber = "P-100",
                PartName = "Corrected", PartNumber = "A-2"
            }));

        var metadataGroup = Assert.Single(metadataEdited.Project!.State.OptimizationGroups);
        Assert.Equal(OptimizationResultStatus.Valid, metadataGroup.ResultStatus);
        Assert.NotNull(metadataGroup.LastStockLengthOptimizationResult);
        Assert.Equal("Corrected", Assert.Single(metadataGroup.RequiredPieces).PartName);
        Assert.Equal("A-2", Assert.Single(metadataGroup.RequiredPieces).PartNumber);
        Assert.Equal(OptimizationResultStatus.Stale,
            Assert.Single(geometryEdited.Project!.State.OptimizationGroups).ResultStatus);
    }

    [Fact]
    public void Phase_three_project_bridge_message_names_follow_the_existing_request_response_pattern()
    {
        var responseTypes = Phase03ProjectBridgeExpectations.ProjectMessageTypes
            .Select(BridgeMessageTypes.ToResponseType)
            .ToArray();

        Assert.Equal(
            [
                "new-project-response",
                "open-project-response",
                "save-project-response",
                "save-project-as-response",
                "get-project-metadata-response",
                "update-project-metadata-response"
            ],
            responseTypes);
    }

    [Fact]
    public async Task Project_messages_round_trip_through_the_desktop_bridge_and_native_dialog_contracts()
    {
        var materialFilePath = Path.Combine(_workspacePath, "materials.json");
        var firstSavePath = Path.Combine(_workspacePath, "shop-cabinet-a.pnest");
        var secondSavePath = Path.Combine(_workspacePath, "shop-cabinet-b.pnest");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "maple-ply-18");
        var createdMaterial = await materialService.CreateAsync(
            new Material
            {
                Name = "Maple Ply 18mm",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m,
                Notes = "Snapshot me"
            });

        Assert.True(createdMaterial.Success);
        var material = Assert.IsType<Material>(createdMaterial.Material);

        var dialogs = new RecordingFileDialogService(
            openPaths: [secondSavePath],
            savePaths: [firstSavePath, secondSavePath]);
        var projectService = new ProjectService(materialService, idGenerator: () => "project-001");
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            dialogs,
            materialService,
            projectService,
            new CsvImportService(repository),
            new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        foreach (var messageType in Phase03ProjectBridgeExpectations.ProjectMessageTypes)
        {
            Assert.Contains(messageType, dispatcher.RegisteredTypes);
        }

        var newProjectResponse = await DispatchAsync<NewProjectResponse>(
            dispatcher,
            BridgeMessageTypes.NewProject,
            new NewProjectRequest(
                new ProjectMetadata
                {
                    ProjectName = "  Shop Cabinet  ",
                    ProjectNumber = "PN-300"
                },
                new ProjectSettings
                {
                    KerfWidth = 0.125m
                }));

        Assert.True(newProjectResponse.Success);
        var project = Assert.IsType<Project>(newProjectResponse.Project);

        var updatedMetadataResponse = await DispatchAsync<UpdateProjectMetadataResponse>(
            dispatcher,
            BridgeMessageTypes.UpdateProjectMetadata,
            new UpdateProjectMetadataRequest(
                project with
                {
                    State = CreateProjectState(material)
                },
                new ProjectMetadata
                {
                    ProjectName = "Shop Cabinet",
                    ProjectNumber = "PN-300",
                    CustomerName = "Acme Millwork",
                    Estimator = "Ripley",
                    Drafter = "Dallas",
                    Pm = "Bishop",
                    Revision = "A",
                    Notes = "Phase 3 desktop round-trip",
                    Date = new DateTime(2026, 03, 14, 0, 0, 0, DateTimeKind.Utc)
                },
                new ProjectSettings
                {
                    KerfWidth = 0.1875m
                }));

        Assert.True(updatedMetadataResponse.Success);
        project = Assert.IsType<Project>(updatedMetadataResponse.Project);

        var firstSaveResponse = await DispatchAsync<SaveProjectResponse>(
            dispatcher,
            BridgeMessageTypes.SaveProject,
            new SaveProjectRequest(project));

        Assert.True(firstSaveResponse.Success);
        Assert.Equal(firstSavePath, firstSaveResponse.FilePath);
        project = Assert.IsType<Project>(firstSaveResponse.Project);
        var firstSnapshot = Assert.Single(project.MaterialSnapshots);
        Assert.Equal(material.MaterialId, firstSnapshot.MaterialId);

        var saveAsResponse = await DispatchAsync<SaveProjectAsResponse>(
            dispatcher,
            BridgeMessageTypes.SaveProjectAs,
            new SaveProjectAsRequest(project, SuggestedFileName: "shop-cabinet-rev-a"));

        Assert.True(saveAsResponse.Success);
        Assert.Equal(secondSavePath, saveAsResponse.FilePath);
        Assert.True(File.Exists(secondSavePath));

        Assert.Equal(2, dialogs.SaveRequests.Count);
        Assert.All(dialogs.SaveRequests, request =>
            Assert.Contains(request.Filters!, filter => filter.Extensions.Contains("pnest", StringComparer.Ordinal)));

        var openProjectResponse = await DispatchAsync<OpenProjectResponse>(
            dispatcher,
            BridgeMessageTypes.OpenProject,
            new OpenProjectRequest());

        Assert.True(openProjectResponse.Success);
        Assert.Equal(secondSavePath, openProjectResponse.FilePath);
        var reopenedProject = Assert.IsType<Project>(openProjectResponse.Project);
        Assert.Equal("Shop Cabinet", reopenedProject.Metadata.ProjectName);
        Assert.Equal("Acme Millwork", reopenedProject.Metadata.CustomerName);
        Assert.Equal(0.1875m, reopenedProject.Settings.KerfWidth);
        Assert.Single(reopenedProject.MaterialSnapshots);
        Assert.Equal(material.MaterialId, reopenedProject.MaterialSnapshots[0].MaterialId);
        Assert.Equal(material.Name, reopenedProject.State.Parts[0].MaterialName);
        var reopenedNestingResult = Assert.IsType<NestResponse>(reopenedProject.State.LastNestingResult);
        Assert.Equal("Casework", GetPlacementGroup(reopenedNestingResult.Placements[0]));

        var metadataResponse = await DispatchAsync<GetProjectMetadataResponse>(
            dispatcher,
            BridgeMessageTypes.GetProjectMetadata,
            new GetProjectMetadataRequest(reopenedProject));

        Assert.True(metadataResponse.Success);
        Assert.Equal("Shop Cabinet", metadataResponse.Metadata!.ProjectName);
        Assert.Equal("Bishop", metadataResponse.Metadata.Pm);
        Assert.Equal(0.1875m, metadataResponse.Settings!.KerfWidth);

        var data = await File.ReadAllBytesAsync(secondSavePath);
        Assert.True(data.Length >= FlatBufferHeaderLength);
        Assert.Equal("PNST", Encoding.ASCII.GetString(data.AsSpan(0, 4)));
        Assert.Equal(FlatBufferVersion, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4, 2)));
    }

    [Fact]
    public async Task Open_project_request_with_an_explicit_file_path_bypasses_the_native_dialog()
    {
        Directory.CreateDirectory(_workspacePath);

        var materialFilePath = Path.Combine(_workspacePath, "materials.json");
        var projectPath = Path.Combine(_workspacePath, "startup-open.pnest");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "maple-ply-18");
        var createdMaterial = await materialService.CreateAsync(
            new Material
            {
                Name = "Maple Ply 18mm",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createdMaterial.Success);
        var material = Assert.IsType<Material>(createdMaterial.Material);

        var dialogs = new RecordingFileDialogService();
        var projectService = new ProjectService(materialService, idGenerator: () => "project-001");
        var projectResult = await projectService.NewAsync(
            new ProjectMetadata
            {
                ProjectName = "Startup Open"
            },
            new ProjectSettings
            {
                KerfWidth = 0.125m
            });

        Assert.True(projectResult.Success);
        var project = Assert.IsType<Project>(projectResult.Project) with
        {
            State = CreateProjectState(material)
        };

        var saveResult = await projectService.SaveAsync(project, projectPath);
        Assert.True(saveResult.Success);

        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            dialogs,
            materialService,
            projectService,
            new CsvImportService(repository),
            new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var openProjectResponse = await DispatchAsync<OpenProjectResponse>(
            dispatcher,
            BridgeMessageTypes.OpenProject,
            new OpenProjectRequest(projectPath));

        Assert.True(openProjectResponse.Success);
        Assert.Equal(projectPath, openProjectResponse.FilePath);
        Assert.Empty(dialogs.OpenRequests);
    }

    [Fact]
    public async Task Optimization_group_workflow_round_trips_through_the_desktop_bridge()
    {
        Directory.CreateDirectory(_workspacePath);
        var projectPath = Path.Combine(_workspacePath, "bridge-managed-groups.pnest");
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "materials.json"));
        var materialService = new MaterialService(repository);
        var generatedIds = new Queue<string>(
            ["project-bridge-001", "group-bridge-001", "group-bridge-002"]);
        var projectService = new ProjectService(materialService, idGenerator: () => generatedIds.Dequeue());
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            projectService,
            new CsvImportService(repository),
            new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        Assert.Contains(BridgeMessageTypes.UpdateOptimizationGroups, dispatcher.RegisteredTypes);

        var created = await DispatchAsync<NewProjectResponse>(
            dispatcher,
            BridgeMessageTypes.NewProject,
            new NewProjectRequest());
        var project = created.Project!;
        Assert.Empty(project.State.OptimizationGroups);
        project = (await ChangeGroupsAsync(
            dispatcher,
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Create,
                Name = "Parts"
            })).Project!;
        var originalGroup = Assert.Single(project.State.OptimizationGroups);
        var manualPart = new PartRow
        {
            RowId = "manual-bridge-001",
            ImportedId = "Door",
            Length = 24m,
            Width = 12m,
            Quantity = 1,
            MaterialName = "Maple",
            Group = "Casework",
            IsManual = true,
            ValidationStatus = ValidationStatuses.Valid
        };
        project = project with
        {
            State = project.State with
            {
                OptimizationGroups = [originalGroup with { Parts = [manualPart] }],
                Parts = [manualPart]
            }
        };

        var added = await ChangeGroupsAsync(
            dispatcher,
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Create,
                Name = "Secondary"
            });
        project = added.Project!;
        var secondaryGroup = project.State.OptimizationGroups[1];

        project = (await ChangeGroupsAsync(
            dispatcher,
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Rename,
                OptimizationGroupId = originalGroup.OptimizationGroupId,
                Name = "Primary"
            })).Project!;

        project = (await ChangeGroupsAsync(
            dispatcher,
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.MovePart,
                PartRowId = manualPart.RowId,
                TargetOptimizationGroupId = secondaryGroup.OptimizationGroupId
            })).Project!;

        project = (await ChangeGroupsAsync(
            dispatcher,
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Reorder,
                OrderedOptimizationGroupIds =
                [
                    secondaryGroup.OptimizationGroupId,
                    originalGroup.OptimizationGroupId
                ]
            })).Project!;

        var orderedProjectPath = Path.Combine(_workspacePath, "bridge-ordered-groups.pnest");
        var orderedSave = await DispatchAsync<SaveProjectResponse>(
            dispatcher,
            BridgeMessageTypes.SaveProject,
            new SaveProjectRequest(project, orderedProjectPath));
        var orderedReopen = await DispatchAsync<OpenProjectResponse>(
            dispatcher,
            BridgeMessageTypes.OpenProject,
            new OpenProjectRequest(orderedProjectPath));
        Assert.True(orderedSave.Success);
        Assert.True(orderedReopen.Success);
        project = orderedReopen.Project!;
        Assert.Equal(
            [secondaryGroup.OptimizationGroupId, originalGroup.OptimizationGroupId],
            project.State.OptimizationGroups.Select(group => group.OptimizationGroupId));
        Assert.Equal([0, 1], project.State.OptimizationGroups.Select(group => group.Order));

        var guardedDelete = await ChangeGroupsAsync(
            dispatcher,
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Delete,
                OptimizationGroupId = secondaryGroup.OptimizationGroupId
            });
        Assert.False(guardedDelete.Success);
        Assert.Equal("optimization-group-not-empty", guardedDelete.Error!.Code);

        project = (await ChangeGroupsAsync(
            dispatcher,
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Delete,
                OptimizationGroupId = secondaryGroup.OptimizationGroupId,
                RemoveOwnedContent = true
            })).Project!;

        var saved = await DispatchAsync<SaveProjectResponse>(
            dispatcher,
            BridgeMessageTypes.SaveProject,
            new SaveProjectRequest(project, projectPath));
        var reopened = await DispatchAsync<OpenProjectResponse>(
            dispatcher,
            BridgeMessageTypes.OpenProject,
            new OpenProjectRequest(projectPath));

        Assert.True(saved.Success);
        Assert.True(reopened.Success);
        var reopenedGroup = Assert.Single(reopened.Project!.State.OptimizationGroups);
        Assert.Equal(originalGroup.OptimizationGroupId, reopenedGroup.OptimizationGroupId);
        Assert.Equal("Primary", reopenedGroup.Name);
        Assert.Equal(0, reopenedGroup.Order);
        Assert.Empty(reopened.Project.State.Parts);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private static ProjectState CreateProjectState(Material material)
    {
        var placement = new NestPlacement
        {
            PlacementId = "placement-001",
            SheetId = "sheet-001",
            PartId = "P-001",
            X = 0,
            Y = 0,
            Width = 20m,
            Height = 10m
        };
        SetPlacementGroup(placement, "Casework");

        return new ProjectState
        {
            SourceFilePath = @"C:\imports\parts.csv",
            SelectedMaterialId = material.MaterialId,
            Parts =
            [
                new PartRow
                {
                    RowId = "row-001",
                    ImportedId = "P-001",
                    Length = 20m,
                    Width = 10m,
                    Quantity = 2,
                    MaterialName = material.Name
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
                        MaterialName = material.Name,
                        SheetLength = material.SheetLength,
                        SheetWidth = material.SheetWidth,
                        UtilizationPercent = 0.42m
                    }
                ],
                Placements = [placement],
                Summary = new MaterialSummary
                {
                    TotalSheets = 1,
                    TotalPlaced = 1,
                    TotalUnplaced = 0,
                    OverallUtilization = 0.42m
                }
            }
        };
    }

    private static async Task<TResponse> DispatchAsync<TResponse>(
        BridgeMessageDispatcher dispatcher,
        string type,
        object payload)
    {
        var response = await dispatcher.DispatchAsync(
            new BridgeMessageEnvelope(
                type,
                Guid.NewGuid().ToString("N"),
                JsonSerializer.SerializeToElement(payload, SerializerOptions)));

        Assert.NotNull(response);
        var typed = response!.Payload.Deserialize<TResponse>(SerializerOptions);
        Assert.NotNull(typed);
        return typed!;
    }

    private static Task<UpdateOptimizationGroupsResponse> ChangeGroupsAsync(
        BridgeMessageDispatcher dispatcher,
        Project project,
        OptimizationGroupChange change) =>
        DispatchAsync<UpdateOptimizationGroupsResponse>(
            dispatcher,
            BridgeMessageTypes.UpdateOptimizationGroups,
            new UpdateOptimizationGroupsRequest(project, change));

    private const ushort FlatBufferVersion = 2;
    private const int FlatBufferHeaderLength = 8;

    private static string? GetPlacementGroup(NestPlacement placement)
    {
        var groupProperty = typeof(NestPlacement).GetProperty("Group");
        Assert.True(groupProperty is not null, "NestPlacement.Group should persist through project save and reopen.");
        return groupProperty!.GetValue(placement) as string;
    }

    private static void SetPlacementGroup(NestPlacement placement, string? group)
    {
        var groupProperty = typeof(NestPlacement).GetProperty("Group");
        Assert.True(groupProperty is not null, "NestPlacement.Group should exist before grouped project round-trip coverage can pass.");
        groupProperty!.SetValue(placement, group);
    }

    private sealed class RecordingFileDialogService(
        IEnumerable<string>? openPaths = null,
        IEnumerable<string>? savePaths = null) : IFileDialogService
    {
        private readonly Queue<string> _openPaths = new(openPaths ?? []);
        private readonly Queue<string> _savePaths = new(savePaths ?? []);

        public List<OpenFileDialogRequest> OpenRequests { get; } = [];

        public List<SaveFileDialogRequest> SaveRequests { get; } = [];

        public Task<OpenFileDialogResponse> OpenAsync(
            OpenFileDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            OpenRequests.Add(request);
            return Task.FromResult(
                _openPaths.Count == 0
                    ? OpenFileDialogResponse.Cancelled()
                    : new OpenFileDialogResponse(true, _openPaths.Dequeue(), null, "File selected."));
        }

        public Task<SaveFileDialogResponse> SaveAsync(
            SaveFileDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveRequests.Add(request);
            return Task.FromResult(
                _savePaths.Count == 0
                    ? SaveFileDialogResponse.Cancelled()
                    : new SaveFileDialogResponse(true, _savePaths.Dequeue(), null, "File path selected."));
        }
    }
}
