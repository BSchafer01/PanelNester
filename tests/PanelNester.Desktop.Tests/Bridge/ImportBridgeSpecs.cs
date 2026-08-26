using System.IO;
using System.Text.Json;
using ClosedXML.Excel;
using PanelNester.Desktop.Bridge;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Materials;
using PanelNester.Services.Nesting;
using PanelNester.Services.Projects;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class ImportBridgeSpecs : IDisposable
{
    [Fact]
    public async Task Stock_length_csv_finalizes_its_synthetic_Worksheet_into_Required_Pieces()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "stock.csv");
        await File.WriteAllTextAsync(
            csvPath,
            """
            Quantity,Length,Extrusion,Part Name,Finish,Part Number
            2,144,EX-7,Head,Satin,PN-1
            3,144, ex-7 ,Head,satin,PN-1
            """);
        var dispatcher = CreateDispatcher();
        const string sessionId = "stock-csv-session";

        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest
            {
                SessionId = sessionId,
                ImportSourcePath = csvPath,
                ProjectKind = ProjectKind.StockLength
            });

        var worksheet = Assert.Single(started.Workbook!.Worksheets);
        Assert.Equal("stock.csv", worksheet.WorksheetName);

        var options = new ImportOptions { ProjectKind = ProjectKind.StockLength };
        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId,
                WorksheetName = worksheet.WorksheetName,
                HeadingRange = worksheet.HeadingRange,
                Options = options
            });
        Assert.True(preview.Success);
        Assert.Equal(2, preview.RequiredPieces.Count);
        Assert.Empty(preview.MaterialResolutions);

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project
                {
                    ProjectId = "stock-project",
                    ProjectKind = ProjectKind.StockLength,
                    State = new ProjectState
                    {
                        OptimizationGroups =
                        [
                            new OptimizationGroup
                            {
                                OptimizationGroupId = "stock-group",
                                Name = "Main",
                                StockLength = 120m
                            }
                        ]
                    }
                },
                Worksheets =
                [
                    new ImportWorksheetSelection
                    {
                        WorksheetName = worksheet.WorksheetName,
                        OriginalPosition = worksheet.OriginalPosition,
                        HeadingRange = worksheet.HeadingRange,
                        OptimizationGroupId = "stock-group",
                        OptimizationGroupName = "Main",
                        Options = options with
                        {
                            ColumnMappings = preview.ColumnMappings
                                .Where(mapping => mapping.SourceColumn is not null)
                                .Select(mapping => new ImportColumnMapping
                                {
                                    SourceColumn = mapping.SourceColumn!,
                                    TargetField = mapping.TargetField
                                })
                                .ToArray()
                        }
                    }
                ]
            });

        Assert.True(finalized.Success);
        Assert.True(finalized.Finalized);
        var group = Assert.Single(finalized.Project!.State.OptimizationGroups);
        var piece = Assert.Single(group.RequiredPieces);
        Assert.Equal(5, piece.Quantity);
        Assert.Equal(144m, piece.Length);
        Assert.Equal("EX-7", piece.ProfileNumber);
        Assert.Equal("Satin", piece.Finish);
        Assert.Equal(2, piece.SourceReferences.Count);
        Assert.Empty(group.Parts);
        Assert.Empty(finalized.Project.MaterialSnapshots);
    }

    [Fact]
    public async Task Stock_length_csv_requires_exclusion_or_a_valid_Part_Override_before_finalization()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "stock-review.csv");
        await File.WriteAllTextAsync(
            csvPath,
            """
            Quantity,Length,Profile,Part Number
            bad,12,EX-1,OMIT
            1,10,EX-2,EDIT
            """);
        var dispatcher = CreateDispatcher();
        const string sessionId = "stock-review-session";
        var started = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest
            {
                SessionId = sessionId,
                ImportSourcePath = csvPath,
                ProjectKind = ProjectKind.StockLength
            });
        var worksheet = Assert.Single(started.Workbook!.Worksheets);
        var options = new ImportOptions { ProjectKind = ProjectKind.StockLength };
        var preview = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId,
                WorksheetName = worksheet.WorksheetName,
                HeadingRange = worksheet.HeadingRange,
                Options = options
            });
        Assert.False(preview.Success);
        var invalid = preview.RequiredPieces[0];
        var imported = preview.RequiredPieces[1];
        var sourceReference = Assert.Single(invalid.SourceReferences);
        var importedReference = Assert.Single(imported.SourceReferences);
        var selection = new ImportWorksheetSelection
        {
            WorksheetName = worksheet.WorksheetName,
            OriginalPosition = worksheet.OriginalPosition,
            HeadingRange = worksheet.HeadingRange,
            OptimizationGroupId = "stock-group",
            OptimizationGroupName = "Main",
            Options = options with
            {
                ColumnMappings = preview.ColumnMappings
                    .Where(mapping => mapping.SourceColumn is not null)
                    .Select(mapping => new ImportColumnMapping
                    {
                        SourceColumn = mapping.SourceColumn!,
                        TargetField = mapping.TargetField
                    })
                    .ToArray()
            },
            ExcludedSourceRows =
            [
                new ExcludedSourceRow
                {
                    RowId = invalid.RequiredPieceId,
                    SourceReference = sourceReference,
                    OriginalValidationError = new SourceRowValidationError
                    {
                        Code = "invalid-quantity",
                        Message = "Quantity must be an integer value."
                    }
                }
            ],
            PartOverrides =
            [
                new PartOverride
                {
                    RowId = imported.RequiredPieceId,
                    ImportedRequiredPiece = imported,
                    CurrentRequiredPiece = imported with
                    {
                        LengthText = "10 1/2",
                        PartNumber = "EDITED"
                    },
                    SourceReferences = [importedReference]
                }
            ]
        };

        var finalized = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project
                {
                    ProjectId = "stock-review-project",
                    ProjectKind = ProjectKind.StockLength,
                    State = new ProjectState
                    {
                        OptimizationGroups =
                        [
                            new OptimizationGroup
                            {
                                OptimizationGroupId = "stock-group",
                                Name = "Main",
                                StockLength = 120m
                            }
                        ]
                    }
                },
                Worksheets = [selection]
            });

        Assert.True(finalized.Success);
        var piece = Assert.Single(Assert.Single(finalized.Project!.State.OptimizationGroups).RequiredPieces);
        Assert.Equal(10.5m, piece.Length);
        Assert.Equal("EDITED", piece.PartNumber);
        var configuration = finalized.Project.State.ImportConfiguration!;
        Assert.Single(configuration.PartOverrides);
        Assert.Single(Assert.Single(configuration.Worksheets).ExcludedSourceRows);

        const string reimportSessionId = "stock-review-reimport-session";
        var restarted = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest
            {
                SessionId = reimportSessionId,
                ImportSourcePath = csvPath,
                ProjectKind = ProjectKind.StockLength
            });
        var rediscoveredWorksheet = Assert.Single(restarted.Workbook!.Worksheets);
        var repreview = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = reimportSessionId,
                WorksheetName = rediscoveredWorksheet.WorksheetName,
                HeadingRange = rediscoveredWorksheet.HeadingRange,
                Options = options
            });
        var savedWorksheet = Assert.Single(configuration.Worksheets);
        var reimported = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = reimportSessionId,
                Project = finalized.Project,
                ReplaceExistingImportSource = true,
                Worksheets =
                [
                    new ImportWorksheetSelection
                    {
                        WorksheetName = rediscoveredWorksheet.WorksheetName,
                        OriginalPosition = rediscoveredWorksheet.OriginalPosition,
                        HeadingRange = rediscoveredWorksheet.HeadingRange,
                        OptimizationGroupId = "stock-group",
                        OptimizationGroupName = "Main",
                        Options = options with
                        {
                            ColumnMappings = repreview.ColumnMappings
                                .Where(mapping => mapping.SourceColumn is not null)
                                .Select(mapping => new ImportColumnMapping
                                {
                                    SourceColumn = mapping.SourceColumn!,
                                    TargetField = mapping.TargetField
                                })
                                .ToArray()
                        },
                        ExcludedSourceRows = savedWorksheet.ExcludedSourceRows,
                        PartOverrides = configuration.PartOverrides
                    }
                ]
            });

        Assert.True(reimported.Success);
        var reconciled = Assert.Single(Assert.Single(reimported.Project!.State.OptimizationGroups).RequiredPieces);
        Assert.Equal(piece.RequiredPieceId, reconciled.RequiredPieceId);
        Assert.Equal(10.5m, reconciled.Length);
        Assert.Equal("EDITED", reconciled.PartNumber);
    }

    [Fact]
    public async Task Stock_length_reimport_reconciles_an_override_recorded_after_duplicate_rows_merged()
    {
        var firstReference = new SourceReference
        {
            WorksheetName = "stock.csv", WorksheetPosition = 0, PhysicalRow = 2, SourceFingerprint = "ROW-1"
        };
        var secondReference = new SourceReference
        {
            WorksheetName = "stock.csv", WorksheetPosition = 0, PhysicalRow = 3, SourceFingerprint = "ROW-2"
        };
        var first = new RequiredPiece
        {
            RequiredPieceId = "required-1", Quantity = 2, Length = 10m, LengthText = "10",
            ProfileNumber = "EX-1", IsManual = false, SourceReferences = [firstReference]
        };
        var second = first with
        {
            RequiredPieceId = "required-2", Quantity = 3, SourceReferences = [secondReference]
        };
        var merged = first with
        {
            Quantity = 5, QuantityText = "5", SourceReferences = [firstReference, secondReference]
        };
        var partOverride = new PartOverride
        {
            RowId = first.RequiredPieceId,
            ImportedRequiredPiece = merged,
            CurrentRequiredPiece = merged with { Quantity = 6, QuantityText = "6", LengthText = "10 1/2" },
            SourceReferences = merged.SourceReferences
        };
        var response = new ImportResponse { Success = true, RequiredPieces = [first, second] };
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "merged-override-materials.json"));

        var applied = await ProjectImportFinalizer.ApplyPartOverridesAsync(
            response,
            [partOverride],
            new PartEditorService(repository, new PartRowValidator()),
            CancellationToken.None);

        Assert.True(applied.Success);
        var corrected = Assert.Single(applied.RequiredPieces);
        Assert.Equal(first.RequiredPieceId, corrected.RequiredPieceId);
        Assert.Equal(6, corrected.Quantity);
        Assert.Equal(10.5m, corrected.Length);
        Assert.Equal([firstReference, secondReference], corrected.SourceReferences);

        var reconciled = ProjectImportFinalizer.ReconcilePartOverrides(
            new ImportWorksheetSelection { PartOverrides = [partOverride] },
            response,
            applied);
        Assert.Equal([firstReference, secondReference], Assert.Single(reconciled.PartOverrides).SourceReferences);
    }

    [Fact]
    public void Stock_length_csv_requires_a_positive_Stock_Length_before_finalization()
    {
        var project = new Project
        {
            ProjectKind = ProjectKind.StockLength,
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "stock-group", Name = "Main", StockLength = 0m
                    }
                ]
            }
        };
        var selection = new ImportWorksheetSelection
        {
            WorksheetName = "stock.csv", OptimizationGroupId = "stock-group", OptimizationGroupName = "Main"
        };

        var exception = Assert.Throws<ImportSessionException>(() => ProjectImportFinalizer.FinalizeWorkbook(
            project,
            new ImportSourceMetadata { ImportSourcePath = "stock.csv" },
            [new FinalizedWorksheetImport(selection, new ImportOptions { ProjectKind = ProjectKind.StockLength }, new ImportResponse { Success = true })]));

        Assert.Equal("import-stock-length-required", exception.Code);
    }

    [Fact]
    public async Task Workbook_finalization_allows_an_unresolved_Material_when_all_matching_rows_are_ignored()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "ignored-material.xlsx");
        using (var workbook = new XLWorkbook())
        {
            WriteWorkbookWorksheet(workbook.AddWorksheet("Parts"), "FIRST", "Ignore Me");
            workbook.Worksheet("Parts").Cell("A3").Value = "SECOND";
            workbook.Worksheet("Parts").Cell("B3").Value = 48;
            workbook.Worksheet("Parts").Cell("C3").Value = 24;
            workbook.Worksheet("Parts").Cell("D3").Value = 1;
            workbook.Worksheet("Parts").Cell("E3").Value = "Ignore Me";
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "ignored-material-session";
        await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        var preview = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId,
                WorksheetName = "Parts",
                HeadingRange = "A1:E1",
                Options = RequiredWorkbookOptions()
            });
        var selection = SelectionFromPreview(preview, "parts", "Parts", RequiredWorkbookOptions()) with
        {
            IgnoredMaterialNames = ["Ignore Me"],
            ExcludedSourceRows = preview.Parts.Select(part => new ExcludedSourceRow
            {
                RowId = part.RowId,
                SourceReference = Assert.Single(part.SourceReferences),
                OriginalValidationError = new SourceRowValidationError
                {
                    Code = "ignored-material",
                    Message = "Material was ignored."
                }
            }).ToArray()
        };

        var finalized = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "ignored-material-project" },
                Worksheets = [selection]
            });

        Assert.True(finalized.Success);
        Assert.Empty(finalized.Parts);
        Assert.Equal(2, Assert.Single(finalized.Project!.State.ImportConfiguration!.Worksheets).ExcludedSourceRows.Count);
    }

    [Fact]
    public async Task Workbook_finalization_revalidates_a_Part_Override_marked_valid_by_the_caller()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "invalid-override.xlsx");
        using (var workbook = new XLWorkbook())
        {
            WriteWorkbookWorksheet(workbook.AddWorksheet("Parts"), "PART", "Demo Material");
            workbook.Worksheet("Parts").Cell("B2").Value = "bad";
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "invalid-override-session";
        await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        var preview = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId, WorksheetName = "Parts", HeadingRange = "A1:E1",
                Options = RequiredWorkbookOptions()
            });
        var imported = Assert.Single(preview.Parts);
        var reference = Assert.Single(imported.SourceReferences);
        var forgedCurrentValues = imported with
        {
            LengthText = "48",
            Length = 48,
            RowNumber = -1,
            ValidationStatus = ValidationStatuses.Valid,
            ValidationMessages = []
        };
        var selection = SelectionFromPreview(preview, "parts", "Parts", RequiredWorkbookOptions()) with
        {
            PartOverrides =
            [
                new PartOverride
                {
                    RowId = imported.RowId,
                    ImportedValues = imported,
                    CurrentValues = forgedCurrentValues,
                    SourceReferences = [reference]
                }
            ]
        };

        var finalized = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "invalid-override-project" },
                Worksheets = [selection]
            });

        Assert.False(finalized.Success);
        Assert.Contains(finalized.Errors, error => error.Code == "row-number-out-of-range");
    }

    [Fact]
    public async Task Warning_Worksheet_finalizes_after_a_blocking_Worksheet_is_deselected()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "warning-and-blocker.xlsx");
        using (var workbook = new XLWorkbook())
        {
            WriteWorkbookWorksheet(workbook.AddWorksheet("Warning"), "WARN", "Demo Material");
            workbook.Worksheet("Warning").Cell("D2").Value = 10001;
            WriteWorkbookWorksheet(workbook.AddWorksheet("Blocked"), "BLOCKED", "Demo Material");
            workbook.Worksheet("Blocked").Cell("B2").Value = "bad";
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "warning-and-blocker-session";
        await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        var warningPreview = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId, WorksheetName = "Warning", HeadingRange = "A1:E1",
                Options = RequiredWorkbookOptions()
            });
        var blockedPreview = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId, WorksheetName = "Blocked", HeadingRange = "A1:E1",
                Options = RequiredWorkbookOptions()
            });

        Assert.True(warningPreview.Success);
        Assert.Single(warningPreview.Warnings);
        Assert.Equal("WARN", Assert.Single(warningPreview.Parts).ImportedId);
        Assert.False(blockedPreview.Success);
        Assert.Equal("BLOCKED", Assert.Single(blockedPreview.Parts).ImportedId);
        var warningSelection = SelectionFromPreview(
            warningPreview, "warning", "Warning", RequiredWorkbookOptions());

        var finalized = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "warning-and-blocker-project" },
                Worksheets = [warningSelection]
            });

        Assert.True(finalized.Success);
        Assert.Equal("WARN", Assert.Single(finalized.Parts).ImportedId);
        Assert.Single(finalized.Warnings);
        Assert.Equal("Warning", Assert.Single(finalized.Project!.State.ImportConfiguration!.Worksheets).WorksheetName);
    }

    [Fact]
    public void Corrected_source_rows_retain_override_provenance_and_only_invalidate_their_Optimization_Group()
    {
        var reference = new SourceReference
        {
            WorksheetName = "Parts",
            WorksheetPosition = 1,
            PhysicalRow = 2,
            SourceFingerprint = "SOURCE-FINGERPRINT"
        };
        var imported = new PartRow
        {
            RowId = "row-1",
            ImportedId = "P-1",
            LengthText = "bad",
            WidthText = "24",
            Width = 24,
            QuantityText = "1",
            Quantity = 1,
            MaterialName = "Demo Material",
            ValidationStatus = ValidationStatuses.Error,
            ValidationMessages = ["Length must be a decimal value."],
            SourceReferences = [reference]
        };
        var corrected = imported with
        {
            LengthText = "48",
            Length = 48,
            QuantityText = "10001",
            Quantity = 10001,
            ValidationStatus = ValidationStatuses.Warning,
            ValidationMessages = ["Quantity is very large."]
        };
        var partOverride = new PartOverride
        {
            RowId = imported.RowId,
            ImportedValues = imported with { ImportedId = "FORGED" },
            CurrentValues = corrected,
            SourceReferences = [reference]
        };
        var selection = new ImportWorksheetSelection
        {
            WorksheetName = "Parts",
            OriginalPosition = 1,
            HeadingRange = "A1:E1",
            OptimizationGroupId = "affected",
            OptimizationGroupName = "Affected",
            PartOverrides = [partOverride]
        };
        var importedResponse = new ImportResponse
        {
            Success = false,
            Parts = [imported],
            Errors = [new ValidationError("invalid-length", "Length must be a decimal value.", imported.RowId, reference)],
            Worksheet = new ImportWorksheetDescriptor
            {
                WorksheetName = "Parts", OriginalPosition = 1, HeadingRange = "A1:E1"
            }
        };
        var validatedResponse = importedResponse with
        {
            Success = true,
            Parts = [corrected],
            Errors = [],
            Warnings = [new ValidationWarning("quantity-large", "Quantity is very large.", imported.RowId, reference)]
        };
        selection = ProjectImportFinalizer.ReconcilePartOverrides(
            selection,
            importedResponse,
            validatedResponse);
        var resolved = ProjectImportFinalizer.ResolveSourceRows(
            validatedResponse,
            selection);
        var unaffectedResult = new NestResponse { Success = true };
        var project = new Project
        {
            ProjectId = "override-project",
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "affected", Name = "Affected", Order = 0,
                        LastNestingResult = new NestResponse { Success = true },
                        ResultStatus = OptimizationResultStatus.Valid
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "unaffected", Name = "Unaffected", Order = 1,
                        Parts = [new PartRow { RowId = "other", ImportedId = "OTHER" }],
                        LastNestingResult = unaffectedResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ]
            }
        };

        var finalized = ProjectImportFinalizer.FinalizeWorkbook(
            project,
            new ImportSourceMetadata { ImportSourcePath = "parts.xlsx" },
            [new FinalizedWorksheetImport(selection, new ImportOptions(), resolved)]);

        Assert.True(resolved.Success);
        Assert.Single(resolved.Warnings);
        Assert.Equal(48, Assert.Single(finalized.State.OptimizationGroups[0].Parts).Length);
        Assert.Null(finalized.State.OptimizationGroups[0].LastNestingResult);
        Assert.Same(unaffectedResult, finalized.State.OptimizationGroups[1].LastNestingResult);
        var persisted = Assert.Single(finalized.State.ImportConfiguration!.PartOverrides);
        Assert.Equal("P-1", persisted.ImportedValues.ImportedId);
        Assert.Equal("bad", persisted.ImportedValues.LengthText);
        Assert.Equal("48", persisted.CurrentValues.LengthText);
        Assert.Equal("SOURCE-FINGERPRINT", Assert.Single(persisted.SourceReferences).SourceFingerprint);
    }

    [Fact]
    public async Task Workbook_finalization_accepts_an_explicit_exclusion_and_persists_its_provenance()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "excluded-invalid-row.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            worksheet.Cell("A1").Value = "Id";
            worksheet.Cell("B1").Value = "Length";
            worksheet.Cell("C1").Value = "Width";
            worksheet.Cell("D1").Value = "Quantity";
            worksheet.Cell("E1").Value = "Material";
            worksheet.Cell("A2").Value = "GOOD";
            worksheet.Cell("B2").Value = 48;
            worksheet.Cell("C2").Value = 24;
            worksheet.Cell("D2").Value = 1;
            worksheet.Cell("E2").Value = "Demo Material";
            worksheet.Cell("A3").Value = "BAD";
            worksheet.Cell("B3").Value = "not-a-length";
            worksheet.Cell("C3").Value = 24;
            worksheet.Cell("D3").Value = 1;
            worksheet.Cell("E3").Value = "Demo Material";
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "excluded-invalid-row-session";
        await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        var preview = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId,
                WorksheetName = "Parts",
                HeadingRange = "A1:E1",
                Options = RequiredWorkbookOptions()
            });
        Assert.False(preview.Success);
        var invalidPart = Assert.Single(preview.Parts, part => part.ValidationStatus == ValidationStatuses.Error);
        var sourceReference = Assert.Single(invalidPart.SourceReferences);
        var validationError = Assert.Single(preview.Errors, error => error.RowId == invalidPart.RowId);
        var selection = SelectionFromPreview(preview, "parts", "Parts", RequiredWorkbookOptions()) with
        {
            ExcludedSourceRows =
            [
                new ExcludedSourceRow
                {
                    RowId = invalidPart.RowId,
                    SourceReference = sourceReference,
                    OriginalValidationError = new SourceRowValidationError
                    {
                        Code = "forged-error",
                        Message = "Caller supplied description"
                    }
                }
            ]
        };

        var finalized = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "excluded-invalid-row-project" },
                Worksheets = [selection]
            });

        Assert.True(finalized.Success);
        Assert.Equal("GOOD", Assert.Single(finalized.Parts).ImportedId);
        var persisted = Assert.Single(Assert.Single(finalized.Project!.State.ImportConfiguration!.Worksheets).ExcludedSourceRows);
        Assert.Equal(3, persisted.SourceReference.PhysicalRow);
        Assert.False(string.IsNullOrWhiteSpace(persisted.SourceReference.SourceFingerprint));
        Assert.Equal("invalid-length", persisted.OriginalValidationError.Code);
        Assert.Equal(1, Assert.Single(finalized.PreviewSummary!.Worksheets).ExcludedRowCount);
    }

    [Theory]
    [InlineData(".xlsx", false)]
    [InlineData(".xlsm", true)]
    public async Task Beginning_an_Excel_import_session_discovers_selectable_Worksheets(
        string extension,
        bool macrosPresent)
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, $"bridge-discovery{extension}");
        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("Empty");
            WriteWorkbookWorksheet(workbook.AddWorksheet("First"), "FIRST", "Demo Material");
            WriteWorkbookWorksheet(workbook.AddWorksheet("Hidden"), "HIDDEN", "Demo Material");
            workbook.Worksheet("Hidden").Visibility = XLWorksheetVisibility.Hidden;
            WriteWorkbookWorksheet(workbook.AddWorksheet("Second"), "SECOND", "Demo Material");
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        var response = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest
            {
                SessionId = $"discovery-{Guid.NewGuid():N}",
                ImportSourcePath = workbookPath
            });

        Assert.True(response.Success);
        Assert.Equal(macrosPresent, response.Workbook?.MacrosPresent);
        Assert.Equal("First", response.Workbook?.InitialWorksheetName);
        Assert.Equal(["First", "Second"], response.Workbook?.Worksheets.Select(sheet => sheet.WorksheetName));
        Assert.Empty(response.Parts);
    }

    [Fact]
    public async Task Beginning_a_Workbook_session_reports_truthful_discovery_progress()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "progress.xlsx");
        using (var workbook = new XLWorkbook())
        {
            WriteWorkbookWorksheet(workbook.AddWorksheet("First"), "FIRST", "Demo Material");
            WriteWorkbookWorksheet(workbook.AddWorksheet("Second"), "SECOND", "Demo Material");
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "progress-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        var progress = await DispatchAsync<GetImportSessionProgressResponse>(
            dispatcher,
            BridgeMessageTypes.GetImportSessionProgress,
            new GetImportSessionProgressRequest { SessionId = sessionId });

        Assert.True(started.Success);
        Assert.True(progress.Success);
        Assert.Equal(WorkbookImportPhase.InspectingWorksheets, progress.Progress?.Phase);
        Assert.Equal(2, progress.Progress?.Current);
        Assert.Equal(2, progress.Progress?.Total);
        Assert.True(progress.Progress?.IsDeterminate);
        Assert.Equal(
            [WorkbookImportPhase.OpeningWorkbook, WorkbookImportPhase.InspectingWorksheets],
            progress.History.Select(item => item.Phase).Distinct());
    }

    [Fact]
    public async Task Beginning_a_Workbook_session_rejects_compressed_size_above_the_desktop_ceiling_before_snapshot_capture()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "oversized.xlsx");
        await using (var stream = new FileStream(workbookPath, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(WorkbookSafetyLimits.DesktopDefault.MaximumCompressedBytes + 1);
        }

        var response = await DispatchAsync<ImportSessionResponse>(
            CreateDispatcher(),
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest
            {
                SessionId = "oversized-session",
                ImportSourcePath = workbookPath
            });

        Assert.False(response.Success);
        Assert.Equal("workbook-safety-ceiling-exceeded", response.Error?.Code);
        Assert.Contains("compressed size", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xlsm")]
    public async Task Beginning_an_encrypted_Workbook_session_returns_copy_guidance(string extension)
    {
        var workbookPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Import",
            $"encrypted-parts{extension}");

        var response = await DispatchAsync<ImportSessionResponse>(
            CreateDispatcher(),
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest
            {
                SessionId = $"encrypted-{Guid.NewGuid():N}",
                ImportSourcePath = workbookPath
            });

        Assert.False(response.Success);
        Assert.Equal("encrypted-workbook", response.Error?.Code);
        Assert.Contains("save an unencrypted copy", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Excel_session_previews_a_named_Worksheet_and_finalizes_only_selected_Worksheets_in_source_order()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "selected-worksheets.xlsx");
        using (var workbook = new XLWorkbook())
        {
            WriteWorkbookWorksheet(workbook.AddWorksheet("First"), "FIRST", "Demo Material");
            WriteWorkbookWorksheet(workbook.AddWorksheet("Second"), "SECOND", "Demo Material");
            WriteWorkbookWorksheet(workbook.AddWorksheet("Third"), "THIRD", "Demo Material");
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "selected-worksheets-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.True(started.Success);

        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId, WorksheetName = "Second" });
        Assert.Equal("SECOND", Assert.Single(preview.Parts).ImportedId);
        Assert.Contains(preview.SourceColumns, column => column.Address == "A" && column.Heading == "Id");
        Assert.Contains(
            preview.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Id && mapping.SourceColumn == "A");
        var firstSelection = await PreviewWorksheetSelectionAsync(
            dispatcher, sessionId, "First", 1, "combined", "Combined");
        var thirdSelection = await PreviewWorksheetSelectionAsync(
            dispatcher, sessionId, "Third", 3, "combined", "Combined");
        var progressBeforeFinalization = await DispatchAsync<GetImportSessionProgressResponse>(
            dispatcher,
            BridgeMessageTypes.GetImportSessionProgress,
            new GetImportSessionProgressRequest { SessionId = sessionId });

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "worksheet-project" },
                Worksheets =
                [
                    thirdSelection,
                    firstSelection
                ]
            });

        Assert.True(finalized.Success);
        var project = Assert.IsType<Project>(finalized.Project);
        Assert.Equal(["FIRST", "THIRD"], project.State.Parts.Select(part => part.ImportedId));
        Assert.Equal(
            ["First", "Third"],
            project.State.ImportConfiguration?.Worksheets.Select(sheet => sheet.WorksheetName));
        var group = Assert.Single(project.State.OptimizationGroups);
        Assert.Equal("combined", group.OptimizationGroupId);
        Assert.Equal("Combined", group.Name);
        Assert.Equal(["FIRST", "THIRD"], group.Parts.Select(part => part.ImportedId));
        Assert.Equal(
            [
                WorkbookImportPhase.ReadingWorksheet,
                WorkbookImportPhase.Validating,
                WorkbookImportPhase.ReadingWorksheet,
                WorkbookImportPhase.Validating,
                WorkbookImportPhase.CombiningParts,
                WorkbookImportPhase.Finalizing
            ],
            finalized.ProgressHistory
                .Skip(progressBeforeFinalization.History.Count)
                .Select(item => item.Phase));
        Assert.All(
            project.State.ImportConfiguration!.Worksheets,
            worksheet =>
            {
                Assert.Equal("combined", worksheet.OptimizationGroupId);
                Assert.Contains(
                    worksheet.ColumnMappings,
                    mapping => mapping.TargetField == ImportFieldNames.Id && mapping.SourceColumn == "A");
            });
    }

    [Fact]
    public async Task Excel_session_combines_only_compatible_rows_within_each_Optimization_Group_and_summarizes_the_result()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "combined-worksheets.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var first = workbook.AddWorksheet("First");
            WriteWorkbookWorksheet(first, "P-001", "Demo Material");
            first.Cell("F1").Value = "Group";
            first.Cell("F2").Value = "A";

            var second = workbook.AddWorksheet("Second");
            WriteWorkbookWorksheet(second, "P-001", "Demo Material");
            second.Cell("D2").Value = 2;
            second.Cell("F1").Value = "Group";
            second.Cell("F2").Value = "A";
            second.Cell("A3").Value = "P-001";
            second.Cell("B3").Value = 20;
            second.Cell("C3").Value = 10;
            second.Cell("D3").Value = 4;
            second.Cell("E3").Value = "Demo Material";
            second.Cell("F3").Value = "B";

            var third = workbook.AddWorksheet("Third");
            WriteWorkbookWorksheet(third, "P-002", "Demo Material");
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "combined-worksheets-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.True(started.Success);
        var firstSelection = await PreviewWorksheetSelectionAsync(
            dispatcher, sessionId, "First", 1, "combined", "Combined", "A1:F1");
        var secondSelection = await PreviewWorksheetSelectionAsync(
            dispatcher, sessionId, "Second", 2, "combined", "Combined", "A1:F1");
        var thirdSelection = await PreviewWorksheetSelectionAsync(
            dispatcher, sessionId, "Third", 3, "isolated", "Isolated");

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "combined-worksheet-project" },
                Worksheets =
                [
                    firstSelection,
                    secondSelection,
                    thirdSelection
                ]
            });

        Assert.True(finalized.Success);
        var project = Assert.IsType<Project>(finalized.Project);
        var combinedGroup = Assert.Single(
            project.State.OptimizationGroups,
            group => group.OptimizationGroupId == "combined");
        Assert.Equal(2, combinedGroup.Parts.Count);
        var mergedPart = Assert.Single(combinedGroup.Parts, part => part.Group == "A");
        Assert.Equal(3, mergedPart.Quantity);
        Assert.Equal(
            ["First!2", "Second!2"],
            mergedPart.SourceReferences.Select(reference => $"{reference.WorksheetName}!{reference.PhysicalRow}"));
        Assert.Equal("B", Assert.Single(combinedGroup.Parts, part => part.Group == "B").Group);
        Assert.Single(
            project.State.OptimizationGroups,
            group => group.OptimizationGroupId == "isolated");

        var summary = Assert.IsType<ImportPreviewSummary>(finalized.PreviewSummary);
        Assert.Equal(
            [("First", 1, 0), ("Second", 2, 1), ("Third", 1, 0)],
            summary.Worksheets.Select(item => (item.WorksheetName, item.SourceRowCount, item.IssueCount)));
        Assert.Equal(
            [("combined", 3, 2, 1), ("isolated", 1, 1, 0)],
            summary.OptimizationGroups.Select(item =>
                (item.OptimizationGroupId, item.SourceRowCount, item.CombinedPartCount, item.MergedRowCount)));
    }

    [Fact]
    public async Task Excel_session_previews_and_persists_the_explicit_A1_Heading_Range()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "manual-heading-range.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            worksheet.Cell("A1").Value = "Project title";
            string[] headings = ["Id", "Length", "Width", "Quantity", "Material"];
            for (var index = 0; index < headings.Length; index++)
            {
                worksheet.Cell(3, index + 2).Value = headings[index];
            }
            worksheet.Cell("B4").Value = "P-300";
            worksheet.Cell("C4").Value = 40;
            worksheet.Cell("D4").Value = 20;
            worksheet.Cell("E4").Value = 2;
            worksheet.Cell("F4").Value = "Demo Material";
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "manual-heading-range-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.Equal("B3:F3", Assert.Single(started.Workbook!.Worksheets).HeadingRange);

        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId,
                WorksheetName = "Parts",
                HeadingRange = "B3:F3"
            });
        Assert.Equal("P-300", Assert.Single(preview.Parts).ImportedId);
        var selection = SelectionFromPreview(preview, "parts", "Parts");

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "manual-heading-project" },
                Worksheets = [selection]
            });

        Assert.True(finalized.Success);
        Assert.Equal(
            "B3:F3",
            Assert.Single(finalized.Project!.State.ImportConfiguration!.Worksheets).HeadingRange);
    }

    [Fact]
    public async Task Excel_session_rejects_finalization_without_a_selected_Worksheet()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "no-selection.xlsx");
        using (var workbook = new XLWorkbook())
        {
            WriteWorkbookWorksheet(workbook.AddWorksheet("First"), "FIRST", "Demo Material");
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "no-selection-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.True(started.Success);

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "no-selection-project" }
            });

        Assert.False(finalized.Success);
        Assert.Equal("import-worksheet-selection-required", finalized.Error?.Code);
        Assert.Null(finalized.Project);
    }

    [Fact]
    public async Task Excel_session_rejects_a_selected_Worksheet_that_was_not_confirmed_by_preview()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "not-previewed.xlsx");
        WriteWorkbook(workbookPath, "Demo Material");
        var originalPart = new PartRow
        {
            RowId = "existing",
            ImportedId = "EXISTING",
            Length = 10,
            Width = 10,
            Quantity = 1,
            MaterialName = "Demo Material"
        };
        var project = new Project
        {
            ProjectId = "not-previewed-project",
            State = new ProjectState { Parts = [originalPart] }
        };

        var dispatcher = CreateDispatcher();
        const string sessionId = "not-previewed-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.True(started.Success);

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = project,
                Worksheets =
                [
                    new ImportWorksheetSelection
                    {
                        WorksheetName = "Parts",
                        OriginalPosition = 1,
                        HeadingRange = "A1:E1",
                        Options = RequiredWorkbookOptions(),
                        OptimizationGroupId = "parts",
                        OptimizationGroupName = "Parts"
                    }
                ]
            });

        Assert.False(finalized.Success);
        Assert.False(finalized.Finalized);
        Assert.Null(finalized.Project);
        Assert.Equal("import-worksheet-not-ready", finalized.Error?.Code);
        Assert.Equal("EXISTING", Assert.Single(project.State.Parts).ImportedId);
    }

    [Fact]
    public async Task Excel_session_rejects_a_duplicate_Worksheet_with_a_caller_supplied_position()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "duplicate-selection.xlsx");
        WriteWorkbook(workbookPath, "Demo Material");
        var dispatcher = CreateDispatcher();
        const string sessionId = "duplicate-selection-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.True(started.Success);
        var selection = await PreviewWorksheetSelectionAsync(
            dispatcher, sessionId, "Parts", 1, "parts", "Parts");

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "duplicate-selection-project" },
                Worksheets = [selection, selection with { OriginalPosition = 99 }]
            });

        Assert.False(finalized.Success);
        Assert.Null(finalized.Project);
        Assert.Equal("import-worksheet-not-ready", finalized.Error?.Code);
    }

    [Fact]
    public async Task Excel_session_rejects_a_Material_Resolution_changed_after_preview()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "changed-material-resolution.xlsx");
        WriteWorkbook(workbookPath, "Demo Material");
        var dispatcher = CreateDispatcher();
        const string sessionId = "changed-material-resolution-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.True(started.Success);
        var selection = await PreviewWorksheetSelectionAsync(
            dispatcher, sessionId, "Parts", 1, "parts", "Parts");

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "changed-material-resolution-project" },
                Worksheets =
                [
                    selection with
                    {
                        Options = selection.Options! with
                        {
                            MaterialMappings =
                            [
                                new ImportMaterialMapping
                                {
                                    SourceMaterialName = "Demo Material",
                                    TargetMaterialId = "different-material"
                                }
                            ]
                        }
                    }
                ]
            });

        Assert.False(finalized.Success);
        Assert.Equal("import-worksheet-not-ready", finalized.Error?.Code);
    }

    [Fact]
    public async Task Excel_session_accepts_an_unresolved_preview_only_with_the_same_staged_new_Material()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "staged-new-material.xlsx");
        WriteWorkbook(workbookPath, "New Workbook Material");
        var dispatcher = CreateDispatcher();
        const string sessionId = "staged-new-material-session";
        var stagedMaterial = new ImportNewMaterialRequest
        {
            SourceMaterialName = "New Workbook Material",
            Material = new Material
            {
                Name = "Created Workbook Material",
                SheetLength = 96,
                SheetWidth = 48,
                AllowRotation = true
            }
        };
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.True(started.Success);
        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId,
                WorksheetName = "Parts",
                HeadingRange = "A1:E1",
                Options = RequiredWorkbookOptions(),
                NewMaterials = [stagedMaterial]
            });
        Assert.False(preview.Success);
        Assert.All(preview.Errors, error => Assert.Equal("material-not-found", error.Code));
        var selection = SelectionFromPreview(preview, "parts", "Parts", RequiredWorkbookOptions());

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "staged-new-material-project" },
                NewMaterials = [stagedMaterial],
                Worksheets = [selection]
            });

        Assert.True(finalized.Success);
        Assert.Equal("Created Workbook Material", Assert.Single(finalized.Parts).MaterialName);
    }

    [Fact]
    public async Task Excel_session_rejects_conflicting_Workbook_wide_Material_Resolutions()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "conflicting-materials.xlsx");
        using (var workbook = new XLWorkbook())
        {
            WriteWorkbookWorksheet(workbook.AddWorksheet("First"), "FIRST", "Shared Label");
            WriteWorkbookWorksheet(workbook.AddWorksheet("Second"), "SECOND", "Shared Label");
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "conflicting-material-resolution-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.True(started.Success);
        var firstSelection = await PreviewWorksheetSelectionAsync(
            dispatcher,
            sessionId,
            "First",
            1,
            "first-group",
            "First",
            options: RequiredWorkbookOptions() with
            {
                MaterialMappings =
                [
                    new ImportMaterialMapping
                    {
                        SourceMaterialName = "Shared Label",
                        TargetMaterialId = "material-a"
                    }
                ]
            });
        var secondSelection = await PreviewWorksheetSelectionAsync(
            dispatcher,
            sessionId,
            "Second",
            2,
            "second-group",
            "Second",
            options: RequiredWorkbookOptions() with
            {
                MaterialMappings =
                [
                    new ImportMaterialMapping
                    {
                        SourceMaterialName = "Shared Label",
                        TargetMaterialId = "material-b"
                    }
                ]
            });

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "conflicting-material-project" },
                Worksheets = [firstSelection, secondSelection]
            });

        Assert.False(finalized.Success);
        Assert.Equal("import-material-resolution-conflict", finalized.Error?.Code);
    }

    [Fact]
    public async Task Excel_session_rejects_a_new_Material_that_conflicts_with_a_Workbook_mapping()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "conflicting-new-material.xlsx");
        using (var workbook = new XLWorkbook())
        {
            WriteWorkbookWorksheet(workbook.AddWorksheet("First"), "FIRST", "Shared Label");
            WriteWorkbookWorksheet(workbook.AddWorksheet("Second"), "SECOND", "Shared Label");
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        const string sessionId = "conflicting-new-material-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.True(started.Success);
        var firstSelection = await PreviewWorksheetSelectionAsync(
            dispatcher, sessionId, "First", 1, "first-group", "First");
        var secondSelection = await PreviewWorksheetSelectionAsync(
            dispatcher,
            sessionId,
            "Second",
            2,
            "second-group",
            "Second",
            options: RequiredWorkbookOptions() with
            {
                MaterialMappings =
                [
                    new ImportMaterialMapping
                    {
                        SourceMaterialName = "Shared Label",
                        TargetMaterialId = "material-b"
                    }
                ]
            });

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project { ProjectId = "conflicting-new-material-project" },
                NewMaterials =
                [
                    new ImportNewMaterialRequest
                    {
                        SourceMaterialName = "Shared Label",
                        Material = new Material
                        {
                            Name = "Created Shared Material",
                            SheetLength = 96m,
                            SheetWidth = 48m
                        }
                    }
                ],
                Worksheets = [firstSelection, secondSelection]
            });

        Assert.False(finalized.Success);
        Assert.Equal("import-material-resolution-conflict", finalized.Error?.Code);
    }

    [Fact]
    public void Workbook_finalization_preserves_results_for_an_unchanged_manual_only_group()
    {
        var manualPart = new PartRow { RowId = "manual", ImportedId = "MANUAL", IsManual = true };
        var existingResult = new NestResponse { Success = true };
        var project = new Project
        {
            ProjectId = "result-preservation-project",
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "manual-group",
                        Name = "Manual",
                        Order = 0,
                        Parts = [manualPart],
                        LastNestingResult = existingResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ]
            }
        };
        var importedPart = new PartRow { RowId = "imported", ImportedId = "IMPORTED" };

        var finalized = ProjectImportFinalizer.FinalizeWorkbook(
            project,
            new ImportSourceMetadata { ImportSourcePath = "fixture.xlsx" },
            [
                new FinalizedWorksheetImport(
                    new ImportWorksheetSelection
                    {
                        WorksheetName = "First",
                        OriginalPosition = 1,
                        OptimizationGroupId = "import-group",
                        OptimizationGroupName = "First"
                    },
                    new ImportOptions(),
                    new ImportResponse { Success = true, Parts = [importedPart] })
            ]);

        var manualGroup = finalized.State.OptimizationGroups[0];
        Assert.Same(existingResult, manualGroup.LastNestingResult);
        Assert.Equal(OptimizationResultStatus.Valid, manualGroup.ResultStatus);
    }

    [Theory]
    [InlineData(WorkbookImportPhase.CombiningParts)]
    [InlineData(WorkbookImportPhase.Finalizing)]
    public void Workbook_finalization_honors_phase_cancellation_without_mutating_the_project(
        WorkbookImportPhase cancellationPhase)
    {
        var originalPart = new PartRow { RowId = "original", ImportedId = "ORIGINAL", IsManual = true };
        var project = new Project
        {
            ProjectId = "phase-cancellation-project",
            State = new ProjectState
            {
                Parts = [originalPart],
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "manual-group",
                        Name = "Manual",
                        Parts = [originalPart]
                    }
                ]
            }
        };
        using var cancellation = new CancellationTokenSource();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            ProjectImportFinalizer.FinalizeWorkbook(
                project,
                new ImportSourceMetadata { ImportSourcePath = "fixture.xlsx" },
                [
                    new FinalizedWorksheetImport(
                        new ImportWorksheetSelection
                        {
                            WorksheetName = "Parts",
                            OriginalPosition = 1,
                            OptimizationGroupId = "import-group",
                            OptimizationGroupName = "Imported"
                        },
                        new ImportOptions(),
                        new ImportResponse
                        {
                            Success = true,
                            Parts = [new PartRow { RowId = "imported", ImportedId = "IMPORTED" }]
                        })
                ],
                reportProgress: (phase, _) =>
                {
                    if (phase == cancellationPhase)
                    {
                        cancellation.Cancel();
                    }
                },
                cancellationToken: cancellation.Token));

        Assert.Same(originalPart, Assert.Single(project.State.Parts));
        Assert.Same(originalPart, Assert.Single(project.State.OptimizationGroups).Parts.Single());
        Assert.Null(project.State.ImportSource);
    }

    [Fact]
    public void Workbook_finalization_requires_explicit_confirmation_before_replacing_an_Import_Source()
    {
        var project = new Project
        {
            ProjectId = "replacement-confirmation-project",
            State = new ProjectState
            {
                ImportSource = new ImportSourceMetadata
                {
                    ImportSourcePath = "existing.xlsx",
                    ContentFingerprint = "EXISTING"
                },
                ImportConfiguration = new ImportConfiguration
                {
                    Worksheets =
                    [
                        new ImportWorksheetConfiguration
                        {
                            WorksheetName = "Existing",
                            OriginalPosition = 1,
                            OptimizationGroupId = "existing-group"
                        }
                    ]
                }
            }
        };

        var exception = Assert.Throws<ImportSessionException>(() =>
            ProjectImportFinalizer.FinalizeWorkbook(
                project,
                new ImportSourceMetadata
                {
                    ImportSourcePath = "replacement.xlsx",
                    ContentFingerprint = "REPLACEMENT"
                },
                [
                    new FinalizedWorksheetImport(
                        new ImportWorksheetSelection
                        {
                            WorksheetName = "Replacement",
                            OriginalPosition = 1,
                            OptimizationGroupId = "replacement-group",
                            OptimizationGroupName = "Replacement"
                        },
                        new ImportOptions(),
                        new ImportResponse
                        {
                            Success = true,
                            Parts = [new PartRow { RowId = "replacement-part", ImportedId = "NEW" }]
                        })
                ],
                replaceExistingImportSource: false));

        Assert.Equal("import-source-replacement-confirmation-required", exception.Code);
        Assert.Equal("existing.xlsx", project.State.ImportSource.ImportSourcePath);
    }

    [Fact]
    public void Confirmed_Import_Source_replacement_preserves_manual_work_and_removes_empty_source_groups()
    {
        var retainedManualPart = new PartRow
        {
            RowId = "retained-manual",
            ImportedId = "MANUAL",
            IsManual = true
        };
        var unrelatedManualPart = new PartRow
        {
            RowId = "unrelated-manual",
            ImportedId = "UNRELATED",
            IsManual = true
        };
        var affectedResult = new NestResponse { Success = true };
        var unrelatedResult = new NestResponse { Success = true };
        var project = new Project
        {
            ProjectId = "replacement-project",
            State = new ProjectState
            {
                SourceFilePath = "existing.xlsx",
                ImportSource = new ImportSourceMetadata
                {
                    ImportSourcePath = "existing.xlsx",
                    ContentFingerprint = "EXISTING"
                },
                ImportConfiguration = new ImportConfiguration
                {
                    Worksheets =
                    [
                        new ImportWorksheetConfiguration
                        {
                            WorksheetName = "Keep Group",
                            OriginalPosition = 1,
                            OptimizationGroupId = "source-with-manual"
                        },
                        new ImportWorksheetConfiguration
                        {
                            WorksheetName = "Remove Group",
                            OriginalPosition = 2,
                            OptimizationGroupId = "source-only"
                        },
                        new ImportWorksheetConfiguration
                        {
                            WorksheetName = "User Assigned",
                            OriginalPosition = 3,
                            OptimizationGroupId = "user-assigned"
                        }
                    ]
                },
                Parts =
                [
                    new PartRow { RowId = "old-a", ImportedId = "OLD-A" },
                    new PartRow { RowId = "old-b", ImportedId = "OLD-B" },
                    new PartRow { RowId = "old-user", ImportedId = "OLD-USER" }
                ],
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "source-with-manual",
                        Name = "Keep Group",
                        Order = 0,
                        Origin = OptimizationGroupOrigin.ImportSource,
                        Parts =
                        [
                            retainedManualPart,
                            new PartRow { RowId = "old-a", ImportedId = "OLD-A" }
                        ],
                        LastNestingResult = affectedResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "source-only",
                        Name = "Remove Group",
                        Order = 1,
                        Origin = OptimizationGroupOrigin.ImportSource,
                        Parts = [new PartRow { RowId = "old-b", ImportedId = "OLD-B" }],
                        LastNestingResult = affectedResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "user-assigned",
                        Name = "User Assigned",
                        Order = 2,
                        Parts = [new PartRow { RowId = "old-user", ImportedId = "OLD-USER" }],
                        LastNestingResult = affectedResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "unrelated-manual",
                        Name = "Unrelated Manual",
                        Order = 3,
                        Parts = [unrelatedManualPart],
                        LastNestingResult = unrelatedResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ]
            }
        };
        var replacementPart = new PartRow { RowId = "replacement-part", ImportedId = "NEW" };

        var finalized = ProjectImportFinalizer.FinalizeWorkbook(
            project,
            new ImportSourceMetadata
            {
                ImportSourcePath = "replacement.xlsx",
                ContentFingerprint = "REPLACEMENT"
            },
            [
                new FinalizedWorksheetImport(
                    new ImportWorksheetSelection
                    {
                        WorksheetName = "Replacement",
                        OriginalPosition = 1,
                        OptimizationGroupId = "replacement-group",
                        OptimizationGroupName = "Replacement"
                    },
                    new ImportOptions(),
                    new ImportResponse { Success = true, Parts = [replacementPart] })
            ],
            replaceExistingImportSource: true);

        Assert.Equal("replacement.xlsx", finalized.State.ImportSource!.ImportSourcePath);
        Assert.Equal("REPLACEMENT", finalized.State.ImportSource.ContentFingerprint);
        Assert.Equal("replacement-part", Assert.Single(finalized.State.Parts).RowId);
        Assert.DoesNotContain(finalized.State.OptimizationGroups, group => group.OptimizationGroupId == "source-only");
        var retainedGroup = Assert.Single(
            finalized.State.OptimizationGroups,
            group => group.OptimizationGroupId == "source-with-manual");
        Assert.Same(retainedManualPart, Assert.Single(retainedGroup.Parts));
        Assert.Null(retainedGroup.LastNestingResult);
        Assert.Equal(OptimizationResultStatus.None, retainedGroup.ResultStatus);
        var userAssignedGroup = Assert.Single(
            finalized.State.OptimizationGroups,
            group => group.OptimizationGroupId == "user-assigned");
        Assert.Empty(userAssignedGroup.Parts);
        Assert.Null(userAssignedGroup.LastNestingResult);
        var unrelatedGroup = Assert.Single(
            finalized.State.OptimizationGroups,
            group => group.OptimizationGroupId == "unrelated-manual");
        Assert.Same(unrelatedManualPart, Assert.Single(unrelatedGroup.Parts));
        Assert.Same(unrelatedResult, unrelatedGroup.LastNestingResult);
        Assert.Equal("replacement-part", Assert.Single(
            finalized.State.OptimizationGroups,
            group => group.OptimizationGroupId == "replacement-group").Parts[0].RowId);
    }

    [Fact]
    public void Confirmed_CSV_replacement_creates_a_source_group_after_the_old_source_group_becomes_empty()
    {
        var project = new Project
        {
            ProjectId = "csv-replacement-project",
            State = new ProjectState
            {
                ImportSource = new ImportSourceMetadata { ImportSourcePath = "old.csv" },
                ImportConfiguration = new ImportConfiguration
                {
                    Worksheets =
                    [
                        new ImportWorksheetConfiguration
                        {
                            WorksheetName = "old.csv",
                            OriginalPosition = 1,
                            OptimizationGroupId = "old-source-group"
                        }
                    ]
                },
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "old-source-group",
                        Name = "Old",
                        Origin = OptimizationGroupOrigin.ImportSource,
                        Parts = [new PartRow { RowId = "old", ImportedId = "OLD" }]
                    }
                ]
            }
        };
        var replacementPart = new PartRow { RowId = "new", ImportedId = "NEW" };

        var finalized = ProjectImportFinalizer.Finalize(
            project,
            new ImportSourceMetadata { ImportSourcePath = @"C:\imports\Replacement Parts.csv" },
            new ImportOptions(),
            new ImportResponse
            {
                Success = true,
                Parts = [replacementPart],
                Worksheet = new ImportWorksheetDescriptor
                {
                    WorksheetName = "Replacement Parts.csv",
                    OriginalPosition = 1,
                    HeadingRange = "R1C1:R1C5"
                }
            },
            targetOptimizationGroupId: null,
            replaceExistingImportSource: true);

        var group = Assert.Single(finalized.State.OptimizationGroups);
        Assert.Equal("Replacement Parts", group.Name);
        Assert.Equal("new", Assert.Single(group.Parts).RowId);
        Assert.Equal(group.OptimizationGroupId, Assert.Single(
            finalized.State.ImportConfiguration!.Worksheets).OptimizationGroupId);
    }
    private static readonly JsonSerializerOptions SerializerOptions = BridgeJson.SerializerOptions;
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.ImportBridgeSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Import_file_message_offers_Excel_Workbooks_but_requires_discovery_for_them()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "parts.csv");
        var xlsxPath = Path.Combine(_workspacePath, "parts.xlsx");
        var materialFilePath = Path.Combine(_workspacePath, "materials.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "baltic-birch-18");
        var validator = new PartRowValidator();
        var dialogs = new RecordingFileDialogService(openPaths: [csvPath]);
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            dialogs,
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-import-bridge"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Baltic Birch 18mm",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,{{material.Name}}
            """);
        WriteWorkbook(xlsxPath, material.Name);

        var csvResponse = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest());

        Assert.True(csvResponse.Success);
        Assert.Equal(csvPath, csvResponse.FilePath);
        Assert.Single(csvResponse.Parts);
        Assert.Equal(ImportFieldNames.Required, csvResponse.AvailableColumns);
        Assert.Equal(ImportFieldNames.All.Count, csvResponse.ColumnMappings.Count);
        Assert.All(
            ImportFieldNames.All,
            targetField => Assert.Contains(
                csvResponse.ColumnMappings,
                mapping => mapping.TargetField == targetField));
        Assert.Contains(
            csvResponse.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Group && mapping.SourceColumn is null);
        Assert.Equal("P-001", csvResponse.Parts[0].ImportedId);
        Assert.Equal("20", csvResponse.Parts[0].LengthText);
        Assert.Equal("10", csvResponse.Parts[0].WidthText);
        Assert.Equal("1", csvResponse.Parts[0].QuantityText);
        Assert.Contains(csvResponse.MaterialResolutions, resolution =>
            resolution.SourceMaterialName == material.Name &&
            resolution.Status == ImportMaterialResolutionStatuses.Resolved);

        var dialogRequest = Assert.Single(dialogs.OpenRequests);
        Assert.Contains(dialogRequest.Filters!, filter => filter.Extensions.Contains("csv", StringComparer.Ordinal));
        Assert.Contains(dialogRequest.Filters!, filter => filter.Extensions.Contains("xlsx", StringComparer.Ordinal));
        Assert.Contains(dialogRequest.Filters!, filter => filter.Extensions.Contains("xlsm", StringComparer.Ordinal));

        var xlsxResponse = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = xlsxPath });

        Assert.False(xlsxResponse.Success);
        Assert.Equal(xlsxPath, xlsxResponse.FilePath);
        Assert.Equal("workbook-discovery-required", xlsxResponse.Error?.Code);
        Assert.Empty(xlsxResponse.Parts);
    }

    [Fact]
    public async Task Import_session_uses_one_immutable_snapshot_for_later_previews()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "snapshot-parts.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-snapshot.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "snapshot-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-snapshot"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var materialResult = await materialService.CreateAsync(new Material
        {
            Name = "Snapshot Material",
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        });
        Assert.True(materialResult.Success);

        await File.WriteAllTextAsync(
            csvPath,
            "Id,Length,Width,Quantity,Material\nORIGINAL,20,10,1,Snapshot Material\n");

        const string sessionId = "immutable-snapshot-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });

        Assert.True(started.Success);
        Assert.Empty(started.Parts);

        await File.WriteAllTextAsync(
            csvPath,
            "Id,Length,Width,Quantity,Material\nCHANGED,99,88,1,Snapshot Material\n");

        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });

        Assert.True(preview.Success);
        Assert.Equal(csvPath, preview.ImportSourcePath);
        var row = Assert.Single(preview.Parts);
        Assert.Equal("ORIGINAL", row.ImportedId);
        Assert.Equal(20m, row.Length);
        Assert.Equal(10m, row.Width);
    }

    [Fact]
    public async Task Import_session_retains_single_worksheet_excel_behavior_after_the_source_is_removed()
    {
        Directory.CreateDirectory(_workspacePath);

        var xlsxPath = Path.Combine(_workspacePath, "snapshot-parts.xlsx");
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "xlsx-session-materials.json"));
        var materialService = new MaterialService(repository, idGenerator: () => "xlsx-session-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-xlsx-session"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        await materialService.CreateAsync(new Material
        {
            Name = "Excel Snapshot Material",
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        });
        WriteWorkbook(xlsxPath, "Excel Snapshot Material");

        const string sessionId = "xlsx-snapshot-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = xlsxPath });
        Assert.True(started.Success);

        File.Delete(xlsxPath);
        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });

        Assert.True(preview.Success);
        Assert.Equal(xlsxPath, preview.ImportSourcePath);
        var excelPart = Assert.Single(preview.Parts);
        Assert.Equal("P-001", excelPart.ImportedId);
        var excelSource = Assert.Single(excelPart.SourceReferences);
        Assert.Equal("Parts", excelSource.WorksheetName);
        Assert.Equal(2, excelSource.PhysicalRow);
        Assert.False(string.IsNullOrWhiteSpace(excelSource.SourceFingerprint));

        var cancelled = await DispatchAsync<CancelImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.CancelImportSession,
            new CancelImportSessionRequest { SessionId = sessionId });
        Assert.True(cancelled.Released);
    }

    [Fact]
    public async Task Import_session_finalization_atomically_returns_the_updated_project_and_releases_the_snapshot()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "finalized-parts.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-finalized.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "finalized-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-finalized"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        await materialService.CreateAsync(new Material
        {
            Name = "Finalized Material",
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        });
        await File.WriteAllTextAsync(
            csvPath,
            "Id,Length,Width,Quantity,Material\nNEW-PART,20,10,1,Finalized Material\n");

        var oldPart = new PartRow
        {
            RowId = "row-1",
            ImportedId = "OLD-PART",
            Length = 12m,
            Width = 6m,
            Quantity = 1,
            MaterialName = "Finalized Material"
        };
        var project = new Project
        {
            ProjectId = "atomic-project",
            State = new ProjectState
            {
                SourceFilePath = "old.csv",
                Parts = [oldPart],
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "group-1",
                        Name = "Primary",
                        Order = 0,
                        Parts = [oldPart],
                        LastNestingResult = new NestResponse { Success = true },
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ],
                LastNestingResult = new NestResponse { Success = true }
            }
        };

        const string sessionId = "atomic-finalization-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });
        Assert.True(started.Success);

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = project,
                ReplaceExistingImportSource = true,
                TargetOptimizationGroupId = "group-1"
            });

        Assert.True(finalized.Success);
        Assert.True(finalized.Finalized);
        var finalizedProject = Assert.IsType<Project>(finalized.Project);
        Assert.Equal(csvPath, finalizedProject.State.SourceFilePath);
        Assert.Equal(csvPath, finalizedProject.State.ImportSource?.ImportSourcePath);
        Assert.False(string.IsNullOrWhiteSpace(finalizedProject.State.ImportSource?.ContentFingerprint));
        Assert.True(finalizedProject.State.ImportSource?.ContentLength > 0);
        Assert.NotNull(finalizedProject.State.ImportConfiguration);
        var finalizedPart = Assert.Single(finalizedProject.State.Parts);
        Assert.Equal("NEW-PART", finalizedPart.ImportedId);
        var sourceReference = Assert.Single(finalizedPart.SourceReferences);
        Assert.Equal(Path.GetFileName(csvPath), sourceReference.WorksheetName);
        Assert.Equal(2, sourceReference.PhysicalRow);
        Assert.False(string.IsNullOrWhiteSpace(sourceReference.SourceFingerprint));
        var worksheetConfiguration = Assert.Single(finalizedProject.State.ImportConfiguration!.Worksheets);
        Assert.Equal(Path.GetFileName(csvPath), worksheetConfiguration.WorksheetName);
        Assert.Equal("R1C1:R1C5", worksheetConfiguration.HeadingRange);
        Assert.Equal("group-1", worksheetConfiguration.OptimizationGroupId);
        Assert.Equal(5, worksheetConfiguration.ColumnMappings.Count);
        Assert.Empty(worksheetConfiguration.ExcludedSourceRows);
        var finalizedGroup = Assert.Single(finalizedProject.State.OptimizationGroups);
        Assert.Equal(finalizedGroup.OptimizationGroupId, worksheetConfiguration.OptimizationGroupId);
        Assert.Equal("NEW-PART", Assert.Single(finalizedGroup.Parts).ImportedId);
        Assert.Null(finalizedGroup.LastNestingResult);
        Assert.Equal(OptimizationResultStatus.None, finalizedGroup.ResultStatus);

        Assert.Equal("OLD-PART", Assert.Single(project.State.Parts).ImportedId);
        Assert.NotNull(Assert.Single(project.State.OptimizationGroups).LastNestingResult);

        var afterFinalization = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });
        Assert.False(afterFinalization.Success);
        Assert.Equal("import-session-not-found", afterFinalization.Error?.Code);
    }

    [Fact]
    public async Task Failed_import_session_finalization_cannot_replace_existing_project_state()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "invalid-finalization.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-invalid-finalization.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "invalid-finalization-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-invalid-finalization"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        await File.WriteAllTextAsync(
            csvPath,
            "Id,Length,Width,Quantity,Material\nINVALID,not-a-number,10,1,Unknown\n");

        var oldPart = new PartRow { RowId = "old", ImportedId = "UNCHANGED" };
        var oldResult = new NestResponse { Success = true };
        var project = new Project
        {
            ProjectId = "unchanged-project",
            State = new ProjectState
            {
                SourceFilePath = "existing.csv",
                Parts = [oldPart],
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "group-1",
                        Name = "Primary",
                        Parts = [oldPart],
                        LastNestingResult = oldResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ],
                LastNestingResult = oldResult
            }
        };

        const string sessionId = "failed-finalization-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });
        Assert.True(started.Success);

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = project,
                TargetOptimizationGroupId = "group-1",
                NewMaterials =
                [
                    new ImportNewMaterialRequest
                    {
                        SourceMaterialName = "Unknown",
                        Material = new Material
                        {
                            Name = "Temporary Material",
                            SheetLength = 96m,
                            SheetWidth = 48m
                        }
                    }
                ]
            });

        Assert.False(finalized.Success);
        Assert.False(finalized.Finalized);
        Assert.Null(finalized.Project);
        Assert.Equal("UNCHANGED", Assert.Single(project.State.Parts).ImportedId);
        Assert.Same(oldResult, Assert.Single(project.State.OptimizationGroups).LastNestingResult);
        Assert.DoesNotContain(await materialService.ListAsync(), material => material.Name == "Temporary Material");

        var afterFailure = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });
        Assert.Equal("import-session-not-found", afterFailure.Error?.Code);
    }

    [Fact]
    public async Task Cancellation_requested_before_begin_prevents_a_late_session_from_becoming_active()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "late-begin.csv");
        await File.WriteAllTextAsync(csvPath, "Id\nP-001\n");

        var materialService = new MaterialService(
            new JsonMaterialRepository(Path.Combine(_workspacePath, "late-begin-materials.json")));
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService),
            new FileImportDispatcher(
                new CsvImportService(DemoMaterialCatalog.All, new PartRowValidator()),
                new XlsxImportService(DemoMaterialCatalog.All, new PartRowValidator())),
            new PartEditorService(DemoMaterialCatalog.All),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        const string sessionId = "cancel-before-begin-session";
        var cancelled = await DispatchAsync<CancelImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.CancelImportSession,
            new CancelImportSessionRequest { SessionId = sessionId });
        var lateBegin = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });

        Assert.True(cancelled.Success);
        Assert.False(cancelled.Released);
        Assert.False(lateBegin.Success);
        Assert.Equal("cancelled", lateBegin.Error?.Code);

        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });
        Assert.Equal("import-session-not-found", preview.Error?.Code);
    }

    [Fact]
    public async Task Cancelling_an_Import_Source_replacement_leaves_the_existing_project_unchanged()
    {
        Directory.CreateDirectory(_workspacePath);
        var replacementPath = Path.Combine(_workspacePath, "replacement.csv");
        await File.WriteAllTextAsync(replacementPath, "Id\nNEW\n");
        var oldPart = new PartRow { RowId = "old", ImportedId = "OLD" };
        var oldResult = new NestResponse { Success = true };
        var project = new Project
        {
            ProjectId = "cancelled-replacement-project",
            State = new ProjectState
            {
                SourceFilePath = "existing.csv",
                ImportSource = new ImportSourceMetadata
                {
                    ImportSourcePath = "existing.csv",
                    ContentFingerprint = "EXISTING"
                },
                ImportConfiguration = new ImportConfiguration(),
                Parts = [oldPart],
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "existing-group",
                        Name = "Existing",
                        Parts = [oldPart],
                        LastNestingResult = oldResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ]
            }
        };
        var dispatcher = CreateDispatcher();
        const string sessionId = "cancelled-replacement-session";

        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest
            {
                SessionId = sessionId,
                ImportSourcePath = replacementPath
            });
        var cancelled = await DispatchAsync<CancelImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.CancelImportSession,
            new CancelImportSessionRequest { SessionId = sessionId });

        Assert.True(started.Success);
        Assert.True(cancelled.Success);
        Assert.Equal("existing.csv", project.State.ImportSource.ImportSourcePath);
        Assert.Same(oldPart, Assert.Single(project.State.Parts));
        Assert.Same(oldResult, Assert.Single(project.State.OptimizationGroups).LastNestingResult);
    }

    [Fact]
    public async Task Cancelling_an_import_session_propagates_to_host_work_and_releases_the_snapshot()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "cancelled-session.csv");
        await File.WriteAllTextAsync(csvPath, "Id\nP-001\n");

        var blockingImport = new BlockingImportService();
        var materialService = new MaterialService(
            new JsonMaterialRepository(Path.Combine(_workspacePath, "cancel-materials.json")));
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-cancel-session"),
            blockingImport,
            new PartEditorService(DemoMaterialCatalog.All),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        const string sessionId = "cancelled-import-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });
        Assert.True(started.Success);

        var previewTask = DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });
        await blockingImport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var activeProgress = await DispatchAsync<GetImportSessionProgressResponse>(
            dispatcher,
            BridgeMessageTypes.GetImportSessionProgress,
            new GetImportSessionProgressRequest { SessionId = sessionId });

        var cancelled = await DispatchAsync<CancelImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.CancelImportSession,
            new CancelImportSessionRequest { SessionId = sessionId });
        var latePreviewResponse = await previewTask;

        Assert.True(cancelled.Success);
        Assert.True(cancelled.Released);
        Assert.Equal(WorkbookImportPhase.ReadingWorksheet, activeProgress.Progress?.Phase);
        Assert.False(activeProgress.Progress?.IsDeterminate);
        Assert.True(await blockingImport.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(latePreviewResponse.Success);
        Assert.Equal("cancelled", latePreviewResponse.Error?.Code);

        var afterCancellation = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });
        Assert.Equal("import-session-not-found", afterCancellation.Error?.Code);
    }

    [Fact]
    public async Task Cancelling_finalization_reaches_material_preparation_and_releases_the_snapshot()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "cancelled-preparation.csv");
        await File.WriteAllTextAsync(csvPath, "Id,Length,Width,Quantity,Material\nP-001,20,10,1,New Material\n");

        var materialService = new BlockingMaterialService();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService),
            new BlockingImportService(),
            new PartEditorService(DemoMaterialCatalog.All),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
        const string sessionId = "cancel-material-preparation-session";

        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });
        Assert.True(started.Success);

        var finalizationTask = DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project(),
                NewMaterials =
                [
                    new ImportNewMaterialRequest
                    {
                        SourceMaterialName = "New Material",
                        Material = new Material { Name = "New Material", SheetLength = 96m, SheetWidth = 48m }
                    }
                ]
            });
        await materialService.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancelled = await DispatchAsync<CancelImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.CancelImportSession,
            new CancelImportSessionRequest { SessionId = sessionId });
        var finalization = await finalizationTask;

        Assert.True(cancelled.Released);
        Assert.True(await materialService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(finalization.Success);
        Assert.Equal("cancelled", finalization.Error?.Code);
    }

    [Fact]
    public async Task Part_row_edit_messages_return_full_revalidated_import_responses()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "editable-parts.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-edit.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "edit-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-edit-bridge"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Edit Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,{{material.Name}}
            """);

        var imported = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.True(imported.Success);
        Assert.Single(imported.Parts);

        var afterAdd = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.AddPartRow,
            new AddPartRowRequest
            {
                Parts = imported.Parts,
                Part = new PartRowUpdate
                {
                    ImportedId = "P-001",
                    Length = "18",
                    Width = "30",
                    Quantity = "1",
                    MaterialName = material.Name
                }
            });

        Assert.True(afterAdd.Success);
        Assert.Equal(2, afterAdd.Parts.Count);
        Assert.Equal("row-2", afterAdd.Parts[1].RowId);
        Assert.Equal("18", afterAdd.Parts[1].LengthText);
        var duplicateWarning = Assert.Single(afterAdd.Warnings);
        Assert.Equal("duplicate-id", duplicateWarning.Code);
        Assert.Equal("row-2", duplicateWarning.RowId);

        var afterUpdate = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.UpdatePartRow,
            new UpdatePartRowRequest
            {
                Parts = afterAdd.Parts,
                Part = new PartRowUpdate
                {
                    RowId = "row-2",
                    ImportedId = "P-001",
                    Length = "18",
                    Width = "30",
                    Quantity = "oops",
                    MaterialName = material.Name
                }
            });

        Assert.False(afterUpdate.Success);
        Assert.Equal(2, afterUpdate.Parts.Count);
        Assert.Equal("oops", afterUpdate.Parts[1].QuantityText);
        Assert.Contains(afterUpdate.Errors, error => error.Code == "invalid-quantity" && error.RowId == "row-2");
        Assert.Contains(afterUpdate.Warnings, warning => warning.Code == "duplicate-id" && warning.RowId == "row-2");

        var afterDelete = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.DeletePartRow,
            new DeletePartRowRequest
            {
                Parts = afterUpdate.Parts,
                RowId = "row-2"
            });

        Assert.True(afterDelete.Success);
        Assert.Single(afterDelete.Parts);
        Assert.Empty(afterDelete.Errors);
        Assert.Empty(afterDelete.Warnings);
    }

    [Fact]
    public async Task Import_file_request_can_apply_user_defined_column_mappings()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "mapped-columns.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-mapped-columns.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "mapped-columns-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-mapped-columns"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Mapped Columns Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Part Id,Len,Width,Qty,Sheet Material
            P-001,20,10,1,{{material.Name}}
            """);

        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest
            {
                FilePath = csvPath,
                Options = new ImportOptions
                {
                    ColumnMappings =
                    [
                        new ImportColumnMapping { SourceColumn = "Part Id", TargetField = ImportFieldNames.Id },
                        new ImportColumnMapping { SourceColumn = "Len", TargetField = ImportFieldNames.Length },
                        new ImportColumnMapping { SourceColumn = "Qty", TargetField = ImportFieldNames.Quantity },
                        new ImportColumnMapping { SourceColumn = "Sheet Material", TargetField = ImportFieldNames.Material }
                    ]
                }
            });

        Assert.True(response.Success);
        var row = Assert.Single(response.Parts);
        Assert.Equal("P-001", row.ImportedId);
        Assert.Equal(material.Name, row.MaterialName);
        Assert.Contains(response.ColumnMappings, mapping => mapping.TargetField == ImportFieldNames.Id && mapping.SourceColumn == "Part Id");
    }

    [Fact]
    public async Task Import_file_request_returns_an_actionable_error_when_the_selected_file_is_locked()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "locked.csv");
        await File.WriteAllTextAsync(
            csvPath,
            """
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,Demo Material
            """);

        var materialFilePath = Path.Combine(_workspacePath, "materials-locked.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "locked-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-locked-import"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        using var lockStream = new FileStream(csvPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.False(response.Success);
        Assert.Equal(csvPath, response.FilePath);
        Assert.Empty(response.Parts);
        var error = Assert.Single(response.Errors);
        Assert.Equal("file-in-use", error.Code);
        Assert.Contains("Close the file and try importing again.", response.Message);
    }

    [Fact]
    public async Task Import_file_can_create_a_new_material_and_map_import_rows_to_it()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "create-material-on-import.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-create-on-import.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "created-on-import");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-create-material"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        await File.WriteAllTextAsync(
            csvPath,
            """
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,Import MDF
            """);

        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest
            {
                FilePath = csvPath,
                NewMaterials =
                [
                    new ImportNewMaterialRequest
                    {
                        SourceMaterialName = "Import MDF",
                        Material = new Material
                        {
                            Name = "Import MDF 3/4",
                            SheetLength = 96m,
                            SheetWidth = 48m,
                            AllowRotation = true,
                            DefaultSpacing = 0.125m,
                            DefaultEdgeMargin = 0.5m
                        }
                    }
                ]
            });

        Assert.True(response.Success);
        var row = Assert.Single(response.Parts);
        Assert.Equal("Import MDF 3/4", row.MaterialName);
        var resolution = Assert.Single(response.MaterialResolutions);
        Assert.Equal("Import MDF", resolution.SourceMaterialName);
        Assert.Equal(ImportMaterialResolutionStatuses.Created, resolution.Status);

        var materials = await repository.GetAllAsync();
        Assert.Contains(materials, material => material.MaterialId == "created-on-import" && material.Name == "Import MDF 3/4");
    }

    [Fact]
    public async Task Import_file_and_part_row_edit_messages_round_trip_optional_group_assignments()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "grouped-import.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-grouped-import.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "grouped-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-grouped-import"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Grouped Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material,Group
            P-001,20,10,1,{{material.Name}},Casework
            """);

        var imported = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.True(imported.Success);
        var importedRow = Assert.Single(imported.Parts);
        Assert.Equal("Casework", importedRow.Group);
        Assert.Contains(
            imported.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Group && mapping.SourceColumn == ImportFieldNames.Group);

        var updated = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.UpdatePartRow,
            new UpdatePartRowRequest
            {
                Parts = imported.Parts,
                Part = new PartRowUpdate
                {
                    RowId = importedRow.RowId,
                    ImportedId = importedRow.ImportedId,
                    Length = importedRow.LengthText ?? importedRow.Length.ToString(),
                    Width = importedRow.WidthText ?? importedRow.Width.ToString(),
                    Quantity = importedRow.QuantityText ?? importedRow.Quantity.ToString(),
                    MaterialName = importedRow.MaterialName,
                    Group = "Doors"
                }
            });

        Assert.True(updated.Success);
        Assert.Equal("Doors", Assert.Single(updated.Parts).Group);

        var added = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.AddPartRow,
            new AddPartRowRequest
            {
                Parts = updated.Parts,
                Part = new PartRowUpdate
                {
                    ImportedId = "P-002",
                    Length = "18",
                    Width = "12",
                    Quantity = "1",
                    MaterialName = material.Name,
                    Group = "   "
                }
            });

        Assert.True(added.Success);
        Assert.Null(added.Parts[1].Group);
    }

    [Fact]
    public async Task Import_file_response_keeps_group_alias_columns_visible_for_manual_mapping_review()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "group-alias-review.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-group-alias-review.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "group-alias-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-group-alias-review"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Alias Review Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material,Panel Group
            P-001,20,10,1,{{material.Name}},Casework
            """);

        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.True(response.Success);
        Assert.Contains("Panel Group", response.AvailableColumns);
        Assert.Contains(
            response.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Group && mapping.SourceColumn is null);
        Assert.Null(Assert.Single(response.Parts).Group);
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xlsm")]
    public async Task Stock_length_Workbook_uses_group_lengths_despite_mismatched_Worksheet_values_and_reconciles_reimport(
        string extension)
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, $"stock-multi{extension}");
        using (var workbook = new XLWorkbook())
        {
            WriteStockWorksheet(workbook.AddWorksheet("Frames"), 2, 48, "P-100", "A-1");
            WriteStockWorksheet(workbook.AddWorksheet("Doors"), 3, 48, "P-100", "A-1");
            WriteStockWorksheet(workbook.AddWorksheet("Rails"), 4, 72, "P-200", "B-1");
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        var sessionId = $"stock-multi-{Guid.NewGuid():N}";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest
            {
                SessionId = sessionId,
                ImportSourcePath = workbookPath,
                ProjectKind = ProjectKind.StockLength
            });
        Assert.True(started.Success);
        Assert.Equal(["Frames", "Doors", "Rails"], started.Workbook?.Worksheets.Select(item => item.WorksheetName));

        var stockOptions = new ImportOptions
        {
            ProjectKind = ProjectKind.StockLength,
            ColumnMappings =
            [
                new ImportColumnMapping { SourceColumn = "A", TargetField = ImportFieldNames.Quantity },
                new ImportColumnMapping { SourceColumn = "B", TargetField = ImportFieldNames.Length },
                new ImportColumnMapping { SourceColumn = "C", TargetField = ImportFieldNames.ProfileNumber },
                new ImportColumnMapping { SourceColumn = "D", TargetField = ImportFieldNames.PartName },
                new ImportColumnMapping { SourceColumn = "E", TargetField = ImportFieldNames.Finish },
                new ImportColumnMapping { SourceColumn = "F", TargetField = ImportFieldNames.PartNumber }
            ]
        };
        var frames = await PreviewStockWorksheetSelectionAsync(
            dispatcher, sessionId, "Frames", 1, "shared", "Shared", "A1:F1", stockOptions);
        var doors = await PreviewStockWorksheetSelectionAsync(
            dispatcher, sessionId, "Doors", 2, "shared", "Shared", "A1:F1", stockOptions);
        var rails = await PreviewStockWorksheetSelectionAsync(
            dispatcher, sessionId, "Rails", 3, "rails", "Rails", "A1:F1", stockOptions);
        var project = new Project
        {
            ProjectId = "stock-workbook-project",
            ProjectKind = ProjectKind.StockLength,
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "shared",
                        Name = "Shared",
                        StockLength = 240
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "rails",
                        Name = "Rails",
                        StockLength = 144
                    }
                ]
            }
        };

        // Worksheet-local Stock Length is intentionally absent from the contract. Unknown values from an
        // older or malicious caller must not override the Optimization Group's shared Stock Length.
        var finalizePayload = JsonSerializer.SerializeToNode(
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = project,
                Worksheets = [doors, rails, frames]
            },
            SerializerOptions)!.AsObject();
        var worksheetPayloads = finalizePayload["worksheets"]!.AsArray();
        worksheetPayloads[0]!.AsObject()["stockLength"] = 96;
        worksheetPayloads[1]!.AsObject()["stockLength"] = 120;
        worksheetPayloads[2]!.AsObject()["stockLength"] = 144;
        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            JsonSerializer.SerializeToElement(finalizePayload, SerializerOptions));

        Assert.True(finalized.Success, finalized.Message);
        var groups = finalized.Project!.State.OptimizationGroups.ToDictionary(group => group.OptimizationGroupId);
        var sharedPiece = Assert.Single(groups["shared"].RequiredPieces);
        Assert.Equal(240, groups["shared"].StockLength);
        Assert.Equal(5, sharedPiece.Quantity);
        Assert.Equal(["Frames!2", "Doors!2"], sharedPiece.SourceReferences.Select(reference =>
            $"{reference.WorksheetName}!{reference.PhysicalRow}"));
        var railsPiece = Assert.Single(groups["rails"].RequiredPieces);
        Assert.Equal(144, groups["rails"].StockLength);
        Assert.Equal(4, railsPiece.Quantity);
        Assert.Equal(
            ["Frames", "Doors", "Rails"],
            finalized.Project.State.ImportConfiguration!.Worksheets.Select(item => item.WorksheetName));
        Assert.Equal(
            ["shared", "shared", "rails"],
            finalized.Project.State.ImportConfiguration.Worksheets.Select(item => item.OptimizationGroupId));

        var originalPieces = groups.Values
            .SelectMany(group => group.RequiredPieces)
            .ToDictionary(piece => piece.RequiredPieceId);
        var reimportSessionId = $"stock-multi-reimport-{Guid.NewGuid():N}";
        var restarted = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest
            {
                SessionId = reimportSessionId,
                ImportSourcePath = workbookPath,
                ProjectKind = ProjectKind.StockLength
            });
        Assert.True(restarted.Success);

        var reimportSelections = new List<ImportWorksheetSelection>();
        foreach (var saved in finalized.Project.State.ImportConfiguration.Worksheets)
        {
            var groupId = Assert.IsType<string>(saved.OptimizationGroupId);
            var savedOptions = new ImportOptions
            {
                ProjectKind = ProjectKind.StockLength,
                ColumnMappings = saved.ColumnMappings
            };
            reimportSelections.Add(await PreviewStockWorksheetSelectionAsync(
                dispatcher,
                reimportSessionId,
                saved.WorksheetName,
                saved.OriginalPosition,
                groupId,
                groups[groupId].Name,
                saved.HeadingRange,
                savedOptions));
        }

        var reimported = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = reimportSessionId,
                Project = finalized.Project,
                ReplaceExistingImportSource = true,
                Worksheets = reimportSelections
            });

        Assert.True(reimported.Success, reimported.Message);
        var reconciledPieces = reimported.Project!.State.OptimizationGroups
            .SelectMany(group => group.RequiredPieces)
            .ToDictionary(piece => piece.RequiredPieceId);
        Assert.Equal(originalPieces.Keys.Order(), reconciledPieces.Keys.Order());
        Assert.All(originalPieces, item => Assert.Equal(
            item.Value.SourceReferences.Select(reference => reference.SourceFingerprint),
            reconciledPieces[item.Key].SourceReferences.Select(reference => reference.SourceFingerprint)));
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xlsm")]
    public async Task Stock_length_Workbook_rejects_bulk_assignment_to_a_group_without_shared_Stock_Length(
        string extension)
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, $"stock-mismatched{extension}");
        using (var workbook = new XLWorkbook())
        {
            WriteStockWorksheet(workbook.AddWorksheet("Frames"), 2, 48, "P-100", "A-1");
            WriteStockWorksheet(workbook.AddWorksheet("Doors"), 3, 48, "P-100", "A-1");
            workbook.SaveAs(workbookPath);
        }

        var dispatcher = CreateDispatcher();
        var sessionId = $"stock-mismatched-{Guid.NewGuid():N}";
        var started = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest
            {
                SessionId = sessionId,
                ImportSourcePath = workbookPath,
                ProjectKind = ProjectKind.StockLength
            });
        Assert.True(started.Success);
        var options = new ImportOptions { ProjectKind = ProjectKind.StockLength };
        var frames = await PreviewStockWorksheetSelectionAsync(
            dispatcher, sessionId, "Frames", 1, "missing-length", "Missing Length", "A1:F1", options);
        var doors = await PreviewStockWorksheetSelectionAsync(
            dispatcher, sessionId, "Doors", 2, "missing-length", "Missing Length", "A1:F1", options);

        var finalized = await DispatchAsync<ImportSessionResponse>(dispatcher, BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project
                {
                    ProjectKind = ProjectKind.StockLength,
                    State = new ProjectState
                    {
                        OptimizationGroups =
                        [
                            new OptimizationGroup
                            {
                                OptimizationGroupId = "missing-length", Name = "Missing Length", StockLength = 0
                            },
                            new OptimizationGroup
                            {
                                OptimizationGroupId = "other", Name = "Other", StockLength = 240
                            }
                        ]
                    }
                },
                Worksheets = [frames, doors]
            });

        Assert.False(finalized.Success);
        Assert.Equal("import-stock-length-required", finalized.Error?.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private static void WriteWorkbook(string filePath, string materialName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Parts");
        string[] headers = ["Id", "Length", "Width", "Quantity", "Material"];

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        sheet.Cell(2, 1).Value = "P-001";
        sheet.Cell(2, 2).Value = 20;
        sheet.Cell(2, 3).Value = 10;
        sheet.Cell(2, 4).Value = 1;
        sheet.Cell(2, 5).Value = materialName;

        workbook.SaveAs(filePath);
    }

    private static void WriteWorkbookWorksheet(
        IXLWorksheet worksheet,
        string partId,
        string materialName)
    {
        string[] headers = ["Id", "Length", "Width", "Quantity", "Material"];
        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        worksheet.Cell(2, 1).Value = partId;
        worksheet.Cell(2, 2).Value = 20;
        worksheet.Cell(2, 3).Value = 10;
        worksheet.Cell(2, 4).Value = 1;
        worksheet.Cell(2, 5).Value = materialName;
    }

    private static void WriteStockWorksheet(
        IXLWorksheet worksheet,
        int quantity,
        decimal length,
        string profileNumber,
        string partNumber)
    {
        string[] headers = ["Quantity", "Length", "Profile Number", "Part Name", "Finish", "Part Number"];
        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        worksheet.Cell(2, 1).Value = quantity;
        worksheet.Cell(2, 2).Value = length;
        worksheet.Cell(2, 3).Value = profileNumber;
        worksheet.Cell(2, 4).Value = "Jamb";
        worksheet.Cell(2, 5).Value = "Clear";
        worksheet.Cell(2, 6).Value = partNumber;
    }

    private static async Task<ImportWorksheetSelection> PreviewWorksheetSelectionAsync(
        BridgeMessageDispatcher dispatcher,
        string sessionId,
        string worksheetName,
        int originalPosition,
        string optimizationGroupId,
        string optimizationGroupName,
        string headingRange = "A1:E1",
        ImportOptions? options = null)
    {
        var previewOptions = options ?? RequiredWorkbookOptions();
        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId,
                WorksheetName = worksheetName,
                HeadingRange = headingRange,
                Options = previewOptions
            });
        Assert.Equal(worksheetName, preview.Worksheet?.WorksheetName);
        Assert.Equal(originalPosition, preview.Worksheet?.OriginalPosition);
        return SelectionFromPreview(
            preview,
            optimizationGroupId,
            optimizationGroupName,
            previewOptions);
    }

    private static async Task<ImportWorksheetSelection> PreviewStockWorksheetSelectionAsync(
        BridgeMessageDispatcher dispatcher,
        string sessionId,
        string worksheetName,
        int originalPosition,
        string optimizationGroupId,
        string optimizationGroupName,
        string headingRange,
        ImportOptions options)
    {
        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId,
                WorksheetName = worksheetName,
                HeadingRange = headingRange,
                Options = options
            });
        Assert.True(preview.Success, string.Join("; ", preview.Errors.Select(error => error.Message)));
        Assert.Equal(originalPosition, preview.Worksheet?.OriginalPosition);
        return SelectionFromPreview(preview, optimizationGroupId, optimizationGroupName, options);
    }

    private static ImportWorksheetSelection SelectionFromPreview(
        ImportSessionResponse preview,
        string optimizationGroupId,
        string optimizationGroupName,
        ImportOptions? options = null) =>
        new()
        {
            WorksheetName = preview.Worksheet!.WorksheetName,
            OriginalPosition = preview.Worksheet.OriginalPosition,
            HeadingRange = preview.Worksheet.HeadingRange,
            OptimizationGroupId = optimizationGroupId,
            OptimizationGroupName = optimizationGroupName,
            Options = (options ?? new ImportOptions()) with
            {
                ColumnMappings = preview.ColumnMappings
                    .Where(mapping => !string.IsNullOrWhiteSpace(mapping.SourceColumn))
                    .Select(mapping => new ImportColumnMapping
                    {
                        SourceColumn = mapping.SourceColumn!,
                        TargetField = mapping.TargetField
                    })
                    .ToArray()
            }
        };

    private static ImportOptions RequiredWorkbookOptions() =>
        new()
        {
            ColumnMappings =
            [
                new ImportColumnMapping { SourceColumn = "A", TargetField = ImportFieldNames.Id },
                new ImportColumnMapping { SourceColumn = "B", TargetField = ImportFieldNames.Length },
                new ImportColumnMapping { SourceColumn = "C", TargetField = ImportFieldNames.Width },
                new ImportColumnMapping { SourceColumn = "D", TargetField = ImportFieldNames.Quantity },
                new ImportColumnMapping { SourceColumn = "E", TargetField = ImportFieldNames.Material }
            ]
        };

    private static async Task<TResponse> DispatchAsync<TResponse>(
        BridgeMessageDispatcher dispatcher,
        string type,
        object payload)
    {
        var payloadElement = payload is JsonElement element
            ? element
            : JsonSerializer.SerializeToElement(payload, SerializerOptions);
        var response = await dispatcher.DispatchAsync(
            new BridgeMessageEnvelope(
                type,
                Guid.NewGuid().ToString("N"),
                payloadElement));

        Assert.NotNull(response);
        var typed = response!.Payload.Deserialize<TResponse>(SerializerOptions);
        Assert.NotNull(typed);
        return typed!;
    }

    private BridgeMessageDispatcher CreateDispatcher()
    {
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "discovery-materials.json"));
        var materialService = new MaterialService(repository, idGenerator: () => "discovery-material");
        var validator = new PartRowValidator();
        return DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "discovery-project"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
    }

    private sealed class RecordingFileDialogService(IEnumerable<string>? openPaths = null) : IFileDialogService
    {
        private readonly Queue<string> _openPaths = new(openPaths ?? []);

        public List<OpenFileDialogRequest> OpenRequests { get; } = [];

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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SaveFileDialogResponse.Cancelled());
    }

    private sealed class BlockingImportService : IImportService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ImportResponse> ImportAsync(
            ImportRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new ImportResponse { Success = true };
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult(true);
                throw;
            }
        }

        public Task<ImportResponse> ImportAsync(
            TextReader reader,
            ImportOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingMaterialService : IMaterialService
    {
        public TaskCompletionSource CreateStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<Material>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Material>>(Array.Empty<Material>());

        public Task<MaterialOperationResult> GetAsync(string materialId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialOperationResult());

        public async Task<MaterialOperationResult> CreateAsync(
            Material material,
            CancellationToken cancellationToken = default)
        {
            CreateStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new MaterialOperationResult { Success = true, Material = material };
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult(true);
                throw;
            }
        }

        public Task<MaterialOperationResult> UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialOperationResult());

        public Task<MaterialDeleteResult> DeleteAsync(
            string materialId,
            bool isInUse = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialDeleteResult { Success = true });
    }
}
