using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;

namespace PanelNester.Services.Tests.Import;

public sealed class FileImportDispatcherSpecs
{
    [Fact]
    public async Task File_dispatcher_routes_requests_by_extension_without_changing_the_import_response_shape()
    {
        var csvImportService = new RecordingImportService("csv-route");
        var xlsxImportService = new RecordingImportService("xlsx-route");
        var dispatcher = new FileImportDispatcher(csvImportService, xlsxImportService);

        var csvResponse = await dispatcher.ImportAsync(new ImportRequest { FilePath = @"C:\imports\parts.csv" });
        var xlsxResponse = await dispatcher.ImportAsync(new ImportRequest { FilePath = @"C:\imports\parts.xlsx" });

        Assert.True(csvResponse.Success);
        Assert.True(xlsxResponse.Success);
        Assert.Equal("csv-route", Assert.Single(csvResponse.Parts).RowId);
        Assert.Equal("xlsx-route", Assert.Single(xlsxResponse.Parts).RowId);
        Assert.Equal([@"C:\imports\parts.csv"], csvImportService.RequestedPaths);
        Assert.Equal([@"C:\imports\parts.xlsx"], xlsxImportService.RequestedPaths);
    }

    [Fact]
    public async Task File_dispatcher_preserves_optional_group_mappings_when_routing_requests()
    {
        var csvImportService = new RecordingImportService("csv-route");
        var dispatcher = new FileImportDispatcher(csvImportService, new RecordingImportService("xlsx-route"));
        var options = new ImportOptions
        {
            ColumnMappings =
            [
                new ImportColumnMapping { SourceColumn = "Part Id", TargetField = ImportFieldNames.Id },
                new ImportColumnMapping { SourceColumn = "Panel Group", TargetField = "Group" }
            ]
        };

        await dispatcher.ImportAsync(new ImportRequest { FilePath = @"C:\imports\parts.csv", Options = options });

        Assert.Same(options, csvImportService.RequestedOptions.Single());
        Assert.Contains(
            csvImportService.RequestedOptions.Single().ColumnMappings,
            mapping => mapping.SourceColumn == "Panel Group" && mapping.TargetField == "Group");
    }

    [Fact]
    public async Task Unsupported_extensions_return_an_actionable_error()
    {
        var dispatcher = new FileImportDispatcher(new RecordingImportService("csv-route"), new RecordingImportService("xlsx-route"));

        var response = await dispatcher.ImportAsync(new ImportRequest { FilePath = @"C:\imports\parts.txt" });

        Assert.False(response.Success);
        var error = Assert.Single(response.Errors);
        Assert.Equal("unsupported-file-type", error.Code);
        Assert.Empty(response.Parts);
    }

    private sealed class RecordingImportService(string rowId) : IImportService
    {
        public List<string> RequestedPaths { get; } = [];
        public List<ImportOptions> RequestedOptions { get; } = [];

        public Task<ImportResponse> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default)
        {
            RequestedPaths.Add(request.FilePath);
            RequestedOptions.Add(request.Options);
            return Task.FromResult(
                new ImportResponse
                {
                    Success = true,
                    Parts = [new PartRow { RowId = rowId }]
                });
        }

        public Task<ImportResponse> ImportAsync(
            TextReader reader,
            ImportOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportResponse { Success = true });
    }
}
