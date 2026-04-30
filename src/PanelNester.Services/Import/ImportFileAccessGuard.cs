using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

internal static class ImportFileAccessGuard
{
    private const int SharingViolationHResult = unchecked((int)0x80070020);
    private const int LockViolationHResult = unchecked((int)0x80070021);
    private const FileShare SharedReadWriteDelete = FileShare.ReadWrite | FileShare.Delete;

    public static FileStream OpenReadShared(string filePath) =>
        new(filePath, FileMode.Open, FileAccess.Read, SharedReadWriteDelete);

    public static ValidationError CreateCsvReadError(string filePath, Exception exception) =>
        CreateReadError(filePath, exception, "file-read-failed", "CSV file");

    public static ValidationError CreateXlsxReadError(string filePath, Exception exception) =>
        CreateReadError(filePath, exception, "xlsx-read-failed", "Excel workbook");

    private static ValidationError CreateReadError(
        string filePath,
        Exception exception,
        string fallbackCode,
        string fileKind)
    {
        var fileName = Path.GetFileName(filePath);

        return IsFileInUse(exception)
            ? new ValidationError(
                "file-in-use",
                $"{fileKind} '{fileName}' is currently open in another application. Close the file and try importing again.")
            : new ValidationError(
                fallbackCode,
                $"{fileKind} '{fileName}' could not be read. {exception.Message}");
    }

    private static bool IsFileInUse(Exception exception) =>
        exception is IOException ioException &&
        (ioException.HResult == SharingViolationHResult || ioException.HResult == LockViolationHResult);
}
