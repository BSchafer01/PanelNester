using Google.FlatBuffers;
using PanelNester.Domain.Models;
using Fb = PanelNester.Services.Persistence.FlatBuffers;
using PanelNester.Services.Projects;

namespace PanelNester.Services.Persistence;

internal sealed class ProjectFlatBufferSerializer
{
    private const ushort DefaultFlags = 0;
    private const int InitialBufferSize = 1024;

    internal async Task<Project> LoadAsync(
        string filePath,
        ProjectFileHeader header,
        CancellationToken cancellationToken = default)
    {
        if (header.Version != ProjectFileHeader.FlatBufferVersion)
        {
            throw new ProjectPersistenceException(
                "project-unsupported-version",
                $"Project file format version '{header.Version}' is not supported.");
        }

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (bytes.Length <= ProjectFileHeader.HeaderLength)
        {
            throw new ProjectPersistenceException("project-corrupt", "Project file is missing FlatBuffers payload.");
        }

        var payload = new byte[bytes.Length - ProjectFileHeader.HeaderLength];
        Buffer.BlockCopy(bytes, ProjectFileHeader.HeaderLength, payload, 0, payload.Length);

        try
        {
            var buffer = new ByteBuffer(payload);
            var document = Fb.ProjectDocument.GetRootAsProjectDocument(buffer);
            var project = ReadProject(document);

            if (project.Version != Project.CurrentVersion)
            {
                throw new ProjectPersistenceException(
                    "project-unsupported-version",
                    $"Project version '{project.Version}' is not supported.");
            }

            return project;
        }
        catch (ProjectPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProjectPersistenceException("project-corrupt", "Project file is not a valid FlatBuffers document.", exception);
        }
    }

    internal async Task SaveAsync(Project project, string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        EnsureDirectory(filePath);
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        var versionedProject = project with { Version = Project.CurrentVersion };

        try
        {
            var builder = new FlatBufferBuilder(InitialBufferSize);
            var root = WriteProject(builder, versionedProject);
            Fb.ProjectDocument.FinishProjectDocumentBuffer(builder, root);
            var payload = builder.SizedByteArray();

            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None))
            {
                ProjectFileHeader.Write(stream, new ProjectFileHeader(ProjectFileHeader.FlatBufferVersion, DefaultFlags));
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ProjectPersistenceException("project-save-failed", "Project could not be saved.", exception);
        }
        catch (IOException exception)
        {
            throw new ProjectPersistenceException("project-save-failed", "Project could not be saved.", exception);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static Offset<Fb.ProjectDocument> WriteProject(FlatBufferBuilder builder, Project project)
    {
        var projectId = CreateString(builder, project.ProjectId);
        var metadata = WriteMetadata(builder, project.Metadata);
        var settings = WriteSettings(builder, project.Settings);
        var snapshots = WriteMaterials(builder, project.MaterialSnapshots);
        var state = WriteState(builder, project.State);

        Fb.ProjectDocument.StartProjectDocument(builder);
        Fb.ProjectDocument.AddVersion(builder, project.Version);
        Fb.ProjectDocument.AddProjectId(builder, projectId);
        Fb.ProjectDocument.AddMetadata(builder, metadata);
        Fb.ProjectDocument.AddSettings(builder, settings);
        Fb.ProjectDocument.AddMaterialSnapshots(builder, snapshots);
        Fb.ProjectDocument.AddState(builder, state);
        return Fb.ProjectDocument.EndProjectDocument(builder);
    }

    private static Offset<Fb.ProjectMetadata> WriteMetadata(FlatBufferBuilder builder, ProjectMetadata metadata)
    {
        var projectName = CreateString(builder, metadata.ProjectName);
        var projectNumber = CreateString(builder, metadata.ProjectNumber);
        var customerName = CreateString(builder, metadata.CustomerName);
        var estimator = CreateString(builder, metadata.Estimator);
        var drafter = CreateString(builder, metadata.Drafter);
        var pm = CreateString(builder, metadata.Pm);
        var revision = CreateString(builder, metadata.Revision);
        var notes = CreateString(builder, metadata.Notes);
        var hasDate = metadata.Date.HasValue;
        var dateTicks = metadata.Date is { } date ? date.ToBinary() : 0;

        Fb.ProjectMetadata.StartProjectMetadata(builder);
        Fb.ProjectMetadata.AddProjectName(builder, projectName);
        Fb.ProjectMetadata.AddProjectNumber(builder, projectNumber);
        Fb.ProjectMetadata.AddCustomerName(builder, customerName);
        Fb.ProjectMetadata.AddEstimator(builder, estimator);
        Fb.ProjectMetadata.AddDrafter(builder, drafter);
        Fb.ProjectMetadata.AddPm(builder, pm);
        Fb.ProjectMetadata.AddDateTicks(builder, dateTicks);
        Fb.ProjectMetadata.AddHasDate(builder, hasDate);
        Fb.ProjectMetadata.AddRevision(builder, revision);
        Fb.ProjectMetadata.AddNotes(builder, notes);
        return Fb.ProjectMetadata.EndProjectMetadata(builder);
    }

    private static Offset<Fb.ProjectSettings> WriteSettings(FlatBufferBuilder builder, ProjectSettings settings)
    {
        var reportSettings = WriteReportSettings(builder, settings.ReportSettings);

        Fb.ProjectSettings.StartProjectSettings(builder);
        Fb.ProjectSettings.AddKerfWidth(builder, (double)settings.KerfWidth);
        Fb.ProjectSettings.AddReportSettings(builder, reportSettings);
        return Fb.ProjectSettings.EndProjectSettings(builder);
    }

    private static Offset<Fb.ReportSettings> WriteReportSettings(FlatBufferBuilder builder, ReportSettings settings)
    {
        var companyName = CreateString(builder, settings.CompanyName);
        var reportTitle = CreateString(builder, settings.ReportTitle);
        var projectJobName = CreateString(builder, settings.ProjectJobName);
        var projectJobNumber = CreateString(builder, settings.ProjectJobNumber);
        var notes = CreateString(builder, settings.Notes);
        var hasReportDate = settings.ReportDate.HasValue;
        var reportDateTicks = settings.ReportDate is { } reportDate ? reportDate.ToBinary() : 0;

        Fb.ReportSettings.StartReportSettings(builder);
        Fb.ReportSettings.AddCompanyName(builder, companyName);
        Fb.ReportSettings.AddReportTitle(builder, reportTitle);
        Fb.ReportSettings.AddProjectJobName(builder, projectJobName);
        Fb.ReportSettings.AddProjectJobNumber(builder, projectJobNumber);
        Fb.ReportSettings.AddReportDateTicks(builder, reportDateTicks);
        Fb.ReportSettings.AddHasReportDate(builder, hasReportDate);
        Fb.ReportSettings.AddNotes(builder, notes);
        return Fb.ReportSettings.EndReportSettings(builder);
    }

    private static VectorOffset WriteMaterials(FlatBufferBuilder builder, IReadOnlyList<Material> materials)
    {
        if (materials is null || materials.Count == 0)
        {
            return default;
        }

        var offsets = new Offset<Fb.Material>[materials.Count];
        for (var i = 0; i < materials.Count; i++)
        {
            offsets[i] = WriteMaterial(builder, materials[i]);
        }

        return Fb.ProjectDocument.CreateMaterialSnapshotsVector(builder, offsets);
    }

    private static Offset<Fb.Material> WriteMaterial(FlatBufferBuilder builder, Material material)
    {
        var materialId = CreateString(builder, material.MaterialId);
        var name = CreateString(builder, material.Name);
        var colorFinish = CreateString(builder, material.ColorFinish);
        var notes = CreateString(builder, material.Notes);
        var hasCostPerSheet = material.CostPerSheet.HasValue;
        var costPerSheet = material.CostPerSheet is { } cost ? (double)cost : 0;

        Fb.Material.StartMaterial(builder);
        Fb.Material.AddMaterialId(builder, materialId);
        Fb.Material.AddName(builder, name);
        Fb.Material.AddSheetLength(builder, (double)material.SheetLength);
        Fb.Material.AddSheetWidth(builder, (double)material.SheetWidth);
        Fb.Material.AddAllowRotation(builder, material.AllowRotation);
        Fb.Material.AddDefaultSpacing(builder, (double)material.DefaultSpacing);
        Fb.Material.AddDefaultEdgeMargin(builder, (double)material.DefaultEdgeMargin);
        Fb.Material.AddColorFinish(builder, colorFinish);
        Fb.Material.AddNotes(builder, notes);
        Fb.Material.AddCostPerSheet(builder, costPerSheet);
        Fb.Material.AddHasCostPerSheet(builder, hasCostPerSheet);
        return Fb.Material.EndMaterial(builder);
    }

    private static Offset<Fb.ProjectState> WriteState(FlatBufferBuilder builder, ProjectState state)
    {
        var sourceFilePath = CreateString(builder, state.SourceFilePath);
        var selectedMaterialId = CreateString(builder, state.SelectedMaterialId);
        var parts = WriteParts(builder, state.Parts);
        var lastNestingResult = state.LastNestingResult is null ? default : WriteNestResponse(builder, state.LastNestingResult);
        var lastBatchNestingResult = state.LastBatchNestingResult is null ? default : WriteBatchNestResponse(builder, state.LastBatchNestingResult);

        Fb.ProjectState.StartProjectState(builder);
        Fb.ProjectState.AddSourceFilePath(builder, sourceFilePath);
        Fb.ProjectState.AddParts(builder, parts);
        Fb.ProjectState.AddSelectedMaterialId(builder, selectedMaterialId);
        if (state.LastNestingResult is not null)
        {
            Fb.ProjectState.AddLastNestingResult(builder, lastNestingResult);
        }

        if (state.LastBatchNestingResult is not null)
        {
            Fb.ProjectState.AddLastBatchNestingResult(builder, lastBatchNestingResult);
        }

        return Fb.ProjectState.EndProjectState(builder);
    }

    private static VectorOffset WriteParts(FlatBufferBuilder builder, IReadOnlyList<PartRow> parts)
    {
        if (parts is null || parts.Count == 0)
        {
            return default;
        }

        var offsets = new Offset<Fb.PartRow>[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            offsets[i] = WritePartRow(builder, parts[i]);
        }

        return Fb.ProjectState.CreatePartsVector(builder, offsets);
    }

    private static Offset<Fb.PartRow> WritePartRow(FlatBufferBuilder builder, PartRow part)
    {
        var rowId = CreateString(builder, part.RowId);
        var importedId = CreateString(builder, part.ImportedId);
        var lengthText = CreateString(builder, part.LengthText);
        var widthText = CreateString(builder, part.WidthText);
        var quantityText = CreateString(builder, part.QuantityText);
        var materialName = CreateString(builder, part.MaterialName);
        var validationStatus = CreateString(builder, part.ValidationStatus);
        var validationMessages = WriteValidationMessages(builder, part.ValidationMessages);
        var group = CreateString(builder, part.Group);

        Fb.PartRow.StartPartRow(builder);
        Fb.PartRow.AddRowId(builder, rowId);
        Fb.PartRow.AddImportedId(builder, importedId);
        Fb.PartRow.AddLengthText(builder, lengthText);
        Fb.PartRow.AddLength(builder, (double)part.Length);
        Fb.PartRow.AddWidthText(builder, widthText);
        Fb.PartRow.AddWidth(builder, (double)part.Width);
        Fb.PartRow.AddQuantityText(builder, quantityText);
        Fb.PartRow.AddQuantity(builder, part.Quantity);
        Fb.PartRow.AddMaterialName(builder, materialName);
        Fb.PartRow.AddValidationStatus(builder, validationStatus);
        Fb.PartRow.AddValidationMessages(builder, validationMessages);
        Fb.PartRow.AddGroup(builder, group);
        return Fb.PartRow.EndPartRow(builder);
    }

    private static VectorOffset WriteValidationMessages(FlatBufferBuilder builder, IReadOnlyList<string> messages)
    {
        if (messages is null || messages.Count == 0)
        {
            return default;
        }

        var offsets = new StringOffset[messages.Count];
        for (var i = 0; i < messages.Count; i++)
        {
            offsets[i] = CreateString(builder, messages[i]);
        }

        return Fb.PartRow.CreateValidationMessagesVector(builder, offsets);
    }

    private static Offset<Fb.NestResponse> WriteNestResponse(FlatBufferBuilder builder, NestResponse? response)
    {
        response ??= new NestResponse();
        var sheets = WriteNestSheets(builder, response.Sheets);
        var placements = WriteNestPlacements(builder, response.Placements);
        var unplaced = WriteUnplacedItems(builder, response.UnplacedItems);
        var summary = WriteMaterialSummary(builder, response.Summary);

        Fb.NestResponse.StartNestResponse(builder);
        Fb.NestResponse.AddSuccess(builder, response.Success);
        Fb.NestResponse.AddSheets(builder, sheets);
        Fb.NestResponse.AddPlacements(builder, placements);
        Fb.NestResponse.AddUnplacedItems(builder, unplaced);
        Fb.NestResponse.AddSummary(builder, summary);
        return Fb.NestResponse.EndNestResponse(builder);
    }

    private static VectorOffset WriteNestSheets(FlatBufferBuilder builder, IReadOnlyList<NestSheet> sheets)
    {
        if (sheets is null || sheets.Count == 0)
        {
            return default;
        }

        var offsets = new Offset<Fb.NestSheet>[sheets.Count];
        for (var i = 0; i < sheets.Count; i++)
        {
            offsets[i] = WriteNestSheet(builder, sheets[i]);
        }

        return Fb.NestResponse.CreateSheetsVector(builder, offsets);
    }

    private static Offset<Fb.NestSheet> WriteNestSheet(FlatBufferBuilder builder, NestSheet sheet)
    {
        var sheetId = CreateString(builder, sheet.SheetId);
        var materialName = CreateString(builder, sheet.MaterialName);

        Fb.NestSheet.StartNestSheet(builder);
        Fb.NestSheet.AddSheetId(builder, sheetId);
        Fb.NestSheet.AddSheetNumber(builder, sheet.SheetNumber);
        Fb.NestSheet.AddMaterialName(builder, materialName);
        Fb.NestSheet.AddSheetLength(builder, (double)sheet.SheetLength);
        Fb.NestSheet.AddSheetWidth(builder, (double)sheet.SheetWidth);
        Fb.NestSheet.AddUtilizationPercent(builder, (double)sheet.UtilizationPercent);
        return Fb.NestSheet.EndNestSheet(builder);
    }

    private static VectorOffset WriteNestPlacements(FlatBufferBuilder builder, IReadOnlyList<NestPlacement> placements)
    {
        if (placements is null || placements.Count == 0)
        {
            return default;
        }

        var offsets = new Offset<Fb.NestPlacement>[placements.Count];
        for (var i = 0; i < placements.Count; i++)
        {
            offsets[i] = WriteNestPlacement(builder, placements[i]);
        }

        return Fb.NestResponse.CreatePlacementsVector(builder, offsets);
    }

    private static Offset<Fb.NestPlacement> WriteNestPlacement(FlatBufferBuilder builder, NestPlacement placement)
    {
        var placementId = CreateString(builder, placement.PlacementId);
        var sheetId = CreateString(builder, placement.SheetId);
        var partId = CreateString(builder, placement.PartId);
        var group = CreateString(builder, placement.Group);

        Fb.NestPlacement.StartNestPlacement(builder);
        Fb.NestPlacement.AddPlacementId(builder, placementId);
        Fb.NestPlacement.AddSheetId(builder, sheetId);
        Fb.NestPlacement.AddPartId(builder, partId);
        Fb.NestPlacement.AddX(builder, (double)placement.X);
        Fb.NestPlacement.AddY(builder, (double)placement.Y);
        Fb.NestPlacement.AddWidth(builder, (double)placement.Width);
        Fb.NestPlacement.AddHeight(builder, (double)placement.Height);
        Fb.NestPlacement.AddRotated90(builder, placement.Rotated90);
        Fb.NestPlacement.AddGroup(builder, group);
        return Fb.NestPlacement.EndNestPlacement(builder);
    }

    private static VectorOffset WriteUnplacedItems(FlatBufferBuilder builder, IReadOnlyList<UnplacedItem> items)
    {
        if (items is null || items.Count == 0)
        {
            return default;
        }

        var offsets = new Offset<Fb.UnplacedItem>[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            offsets[i] = WriteUnplacedItem(builder, items[i]);
        }

        return Fb.NestResponse.CreateUnplacedItemsVector(builder, offsets);
    }

    private static Offset<Fb.UnplacedItem> WriteUnplacedItem(FlatBufferBuilder builder, UnplacedItem item)
    {
        var partId = CreateString(builder, item.PartId);
        var reasonCode = CreateString(builder, item.ReasonCode);
        var reasonDescription = CreateString(builder, item.ReasonDescription);

        Fb.UnplacedItem.StartUnplacedItem(builder);
        Fb.UnplacedItem.AddPartId(builder, partId);
        Fb.UnplacedItem.AddReasonCode(builder, reasonCode);
        Fb.UnplacedItem.AddReasonDescription(builder, reasonDescription);
        return Fb.UnplacedItem.EndUnplacedItem(builder);
    }

    private static Offset<Fb.MaterialSummary> WriteMaterialSummary(FlatBufferBuilder builder, MaterialSummary summary)
    {
        Fb.MaterialSummary.StartMaterialSummary(builder);
        Fb.MaterialSummary.AddTotalSheets(builder, summary.TotalSheets);
        Fb.MaterialSummary.AddTotalPlaced(builder, summary.TotalPlaced);
        Fb.MaterialSummary.AddTotalUnplaced(builder, summary.TotalUnplaced);
        Fb.MaterialSummary.AddOverallUtilization(builder, (double)summary.OverallUtilization);
        return Fb.MaterialSummary.EndMaterialSummary(builder);
    }

    private static Offset<Fb.BatchNestResponse> WriteBatchNestResponse(FlatBufferBuilder builder, BatchNestResponse? response)
    {
        response ??= new BatchNestResponse();
        var legacyResult = response.LegacyResult is null ? default : WriteNestResponse(builder, response.LegacyResult);
        var materialResults = WriteMaterialResults(builder, response.MaterialResults);

        Fb.BatchNestResponse.StartBatchNestResponse(builder);
        Fb.BatchNestResponse.AddSuccess(builder, response.Success);
        if (response.LegacyResult is not null)
        {
            Fb.BatchNestResponse.AddLegacyResult(builder, legacyResult);
        }

        Fb.BatchNestResponse.AddMaterialResults(builder, materialResults);
        return Fb.BatchNestResponse.EndBatchNestResponse(builder);
    }

    private static VectorOffset WriteMaterialResults(FlatBufferBuilder builder, IReadOnlyList<MaterialNestResult> results)
    {
        if (results is null || results.Count == 0)
        {
            return default;
        }

        var offsets = new Offset<Fb.MaterialNestResult>[results.Count];
        for (var i = 0; i < results.Count; i++)
        {
            offsets[i] = WriteMaterialResult(builder, results[i]);
        }

        return Fb.BatchNestResponse.CreateMaterialResultsVector(builder, offsets);
    }

    private static Offset<Fb.MaterialNestResult> WriteMaterialResult(FlatBufferBuilder builder, MaterialNestResult result)
    {
        var materialName = CreateString(builder, result.MaterialName);
        var materialId = CreateString(builder, result.MaterialId);
        var response = WriteNestResponse(builder, result.Result);

        Fb.MaterialNestResult.StartMaterialNestResult(builder);
        Fb.MaterialNestResult.AddMaterialName(builder, materialName);
        Fb.MaterialNestResult.AddMaterialId(builder, materialId);
        Fb.MaterialNestResult.AddResult(builder, response);
        return Fb.MaterialNestResult.EndMaterialNestResult(builder);
    }

    private static Project ReadProject(Fb.ProjectDocument document)
    {
        var metadata = document.Metadata;
        var settings = document.Settings;
        var state = document.State;

        return new Project
        {
            Version = document.Version,
            ProjectId = document.ProjectId ?? string.Empty,
            Metadata = ReadMetadata(metadata),
            Settings = ReadSettings(settings),
            MaterialSnapshots = ReadMaterials(document),
            State = ReadState(state)
        };
    }

    private static ProjectMetadata ReadMetadata(Fb.ProjectMetadata? metadata)
    {
        if (metadata is null)
        {
            return new ProjectMetadata();
        }

        var value = metadata.Value;
        var date = value.HasDate ? DateTime.FromBinary(value.DateTicks) : (DateTime?)null;

        return new ProjectMetadata
        {
            ProjectName = value.ProjectName ?? string.Empty,
            ProjectNumber = value.ProjectNumber,
            CustomerName = value.CustomerName,
            Estimator = value.Estimator,
            Drafter = value.Drafter,
            Pm = value.Pm,
            Date = date,
            Revision = value.Revision,
            Notes = value.Notes
        };
    }

    private static ProjectSettings ReadSettings(Fb.ProjectSettings? settings)
    {
        const decimal DefaultKerfWidth = 0.0625m;

        if (settings is null)
        {
            return new ProjectSettings { KerfWidth = DefaultKerfWidth };
        }

        var value = settings.Value;
        var reportSettings = ReadReportSettings(value.ReportSettings);
        var kerfWidth = value.KerfWidth;

        return new ProjectSettings
        {
            KerfWidth = kerfWidth > 0 ? (decimal)kerfWidth : DefaultKerfWidth,
            ReportSettings = reportSettings
        };
    }

    private static ReportSettings ReadReportSettings(Fb.ReportSettings? settings)
    {
        if (settings is null)
        {
            return new ReportSettings();
        }

        var value = settings.Value;
        var reportDate = value.HasReportDate ? DateTime.FromBinary(value.ReportDateTicks) : (DateTime?)null;

        return new ReportSettings
        {
            CompanyName = value.CompanyName,
            ReportTitle = value.ReportTitle,
            ProjectJobName = value.ProjectJobName,
            ProjectJobNumber = value.ProjectJobNumber,
            ReportDate = reportDate,
            Notes = value.Notes
        };
    }

    private static IReadOnlyList<Material> ReadMaterials(Fb.ProjectDocument document)
    {
        if (document.MaterialSnapshotsLength == 0)
        {
            return Array.Empty<Material>();
        }

        var results = new List<Material>(document.MaterialSnapshotsLength);
        for (var i = 0; i < document.MaterialSnapshotsLength; i++)
        {
            var material = document.MaterialSnapshots(i);
            if (material is null)
            {
                continue;
            }

            var value = material.Value;
            var cost = value.HasCostPerSheet ? (decimal?)value.CostPerSheet : null;

            results.Add(new Material
            {
                MaterialId = value.MaterialId ?? string.Empty,
                Name = value.Name ?? string.Empty,
                SheetLength = (decimal)value.SheetLength,
                SheetWidth = (decimal)value.SheetWidth,
                AllowRotation = value.AllowRotation,
                DefaultSpacing = (decimal)value.DefaultSpacing,
                DefaultEdgeMargin = (decimal)value.DefaultEdgeMargin,
                ColorFinish = value.ColorFinish,
                Notes = value.Notes,
                CostPerSheet = cost
            });
        }

        return results;
    }

    private static ProjectState ReadState(Fb.ProjectState? state)
    {
        if (state is null)
        {
            return new ProjectState();
        }

        var value = state.Value;
        var parts = ReadParts(value);
        var lastNest = value.LastNestingResult is { } lastNesting ? ReadNestResponse(lastNesting) : null;
        var lastBatch = value.LastBatchNestingResult is { } lastBatchResult ? ReadBatchNestResponse(lastBatchResult) : null;

        return new ProjectState
        {
            SourceFilePath = value.SourceFilePath,
            Parts = parts,
            SelectedMaterialId = value.SelectedMaterialId,
            LastNestingResult = lastNest,
            LastBatchNestingResult = lastBatch
        };
    }

    private static IReadOnlyList<PartRow> ReadParts(Fb.ProjectState state)
    {
        if (state.PartsLength == 0)
        {
            return Array.Empty<PartRow>();
        }

        var parts = new List<PartRow>(state.PartsLength);
        for (var i = 0; i < state.PartsLength; i++)
        {
            var part = state.Parts(i);
            if (part is null)
            {
                continue;
            }

            var value = part.Value;
            var messages = ReadStringVector(value.ValidationMessagesLength, value.ValidationMessages);

            parts.Add(new PartRow
            {
                RowId = value.RowId ?? string.Empty,
                ImportedId = value.ImportedId ?? string.Empty,
                LengthText = value.LengthText,
                Length = (decimal)value.Length,
                WidthText = value.WidthText,
                Width = (decimal)value.Width,
                QuantityText = value.QuantityText,
                Quantity = value.Quantity,
                MaterialName = value.MaterialName ?? string.Empty,
                Group = string.IsNullOrWhiteSpace(value.Group) ? null : value.Group,
                ValidationStatus = value.ValidationStatus ?? ValidationStatuses.Valid,
                ValidationMessages = messages
            });
        }

        return parts;
    }

    private static IReadOnlyList<string> ReadStringVector(int length, Func<int, string?> accessor)
    {
        if (length == 0)
        {
            return Array.Empty<string>();
        }

        var values = new string[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = accessor(i) ?? string.Empty;
        }

        return values;
    }

    private static NestResponse ReadNestResponse(Fb.NestResponse response)
    {
        return new NestResponse
        {
            Success = response.Success,
            Sheets = ReadNestSheets(response),
            Placements = ReadNestPlacements(response),
            UnplacedItems = ReadUnplacedItems(response),
            Summary = ReadMaterialSummary(response.Summary)
        };
    }

    private static IReadOnlyList<NestSheet> ReadNestSheets(Fb.NestResponse response)
    {
        if (response.SheetsLength == 0)
        {
            return Array.Empty<NestSheet>();
        }

        var sheets = new List<NestSheet>(response.SheetsLength);
        for (var i = 0; i < response.SheetsLength; i++)
        {
            var sheet = response.Sheets(i);
            if (sheet is null)
            {
                continue;
            }

            var value = sheet.Value;
            sheets.Add(new NestSheet
            {
                SheetId = value.SheetId ?? string.Empty,
                SheetNumber = value.SheetNumber,
                MaterialName = value.MaterialName ?? string.Empty,
                SheetLength = (decimal)value.SheetLength,
                SheetWidth = (decimal)value.SheetWidth,
                UtilizationPercent = (decimal)value.UtilizationPercent
            });
        }

        return sheets;
    }

    private static IReadOnlyList<NestPlacement> ReadNestPlacements(Fb.NestResponse response)
    {
        if (response.PlacementsLength == 0)
        {
            return Array.Empty<NestPlacement>();
        }

        var placements = new List<NestPlacement>(response.PlacementsLength);
        for (var i = 0; i < response.PlacementsLength; i++)
        {
            var placement = response.Placements(i);
            if (placement is null)
            {
                continue;
            }

            var value = placement.Value;
            placements.Add(new NestPlacement
            {
                PlacementId = value.PlacementId ?? string.Empty,
                SheetId = value.SheetId ?? string.Empty,
                PartId = value.PartId ?? string.Empty,
                Group = value.Group,
                X = (decimal)value.X,
                Y = (decimal)value.Y,
                Width = (decimal)value.Width,
                Height = (decimal)value.Height,
                Rotated90 = value.Rotated90
            });
        }

        return placements;
    }

    private static IReadOnlyList<UnplacedItem> ReadUnplacedItems(Fb.NestResponse response)
    {
        if (response.UnplacedItemsLength == 0)
        {
            return Array.Empty<UnplacedItem>();
        }

        var items = new List<UnplacedItem>(response.UnplacedItemsLength);
        for (var i = 0; i < response.UnplacedItemsLength; i++)
        {
            var item = response.UnplacedItems(i);
            if (item is null)
            {
                continue;
            }

            var value = item.Value;
            items.Add(new UnplacedItem
            {
                PartId = value.PartId ?? string.Empty,
                ReasonCode = value.ReasonCode ?? string.Empty,
                ReasonDescription = value.ReasonDescription ?? string.Empty
            });
        }

        return items;
    }

    private static MaterialSummary ReadMaterialSummary(Fb.MaterialSummary? summary)
    {
        if (summary is null)
        {
            return new MaterialSummary();
        }

        var value = summary.Value;
        return new MaterialSummary
        {
            TotalSheets = value.TotalSheets,
            TotalPlaced = value.TotalPlaced,
            TotalUnplaced = value.TotalUnplaced,
            OverallUtilization = (decimal)value.OverallUtilization
        };
    }

    private static BatchNestResponse ReadBatchNestResponse(Fb.BatchNestResponse response)
    {
        var legacy = response.LegacyResult is { } legacyResult ? ReadNestResponse(legacyResult) : null;
        var materials = ReadMaterialResults(response);

        return new BatchNestResponse
        {
            Success = response.Success,
            LegacyResult = legacy,
            MaterialResults = materials
        };
    }

    private static IReadOnlyList<MaterialNestResult> ReadMaterialResults(Fb.BatchNestResponse response)
    {
        if (response.MaterialResultsLength == 0)
        {
            return Array.Empty<MaterialNestResult>();
        }

        var results = new List<MaterialNestResult>(response.MaterialResultsLength);
        for (var i = 0; i < response.MaterialResultsLength; i++)
        {
            var result = response.MaterialResults(i);
            if (result is null)
            {
                continue;
            }

            var value = result.Value;
            var nestResponse = value.Result is { } responseValue
                ? ReadNestResponse(responseValue)
                : new NestResponse();
            results.Add(new MaterialNestResult
            {
                MaterialName = value.MaterialName ?? string.Empty,
                MaterialId = value.MaterialId,
                Result = nestResponse
            });
        }

        return results;
    }

    private static StringOffset CreateString(FlatBufferBuilder builder, string? value) =>
        string.IsNullOrEmpty(value) ? default : builder.CreateString(value);

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

