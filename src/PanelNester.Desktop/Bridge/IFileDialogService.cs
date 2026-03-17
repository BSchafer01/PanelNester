namespace PanelNester.Desktop.Bridge;

public interface IFileDialogService
{
    Task<OpenFileDialogResponse> OpenAsync(
        OpenFileDialogRequest request,
        CancellationToken cancellationToken = default);

    Task<SaveFileDialogResponse> SaveAsync(
        SaveFileDialogRequest request,
        CancellationToken cancellationToken = default);
}
