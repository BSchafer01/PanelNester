using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using PanelNester.Domain.Models;
using PanelNester.Domain.Contracts;
using PanelNester.Services.Projects;
using PanelNester.Services.Tests.Specifications;

namespace PanelNester.Services.Tests.Projects;

public sealed class ProjectPersistenceSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.ProjectPersistenceSpecs.{Guid.NewGuid():N}");

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
    [InlineData(true, true, 2, "project-unsupported-version")]
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
        var project = Phase03ProjectPersistenceSpec.CreateSampleProject();

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
        var project = Phase03ProjectPersistenceSpec.CreateSampleProject();
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
