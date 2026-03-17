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
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.ProjectBridgeSpecs.{Guid.NewGuid():N}");

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
