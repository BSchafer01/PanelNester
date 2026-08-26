using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

internal static class ImportFileAccessGuard
{
    private static readonly byte[] CompoundFileSignature =
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private const int SharingViolationHResult = unchecked((int)0x80070020);
    private const int LockViolationHResult = unchecked((int)0x80070021);
    private const FileShare SharedReadWriteDelete = FileShare.ReadWrite | FileShare.Delete;

    public static FileStream OpenReadShared(string filePath) =>
        new(filePath, FileMode.Open, FileAccess.Read, SharedReadWriteDelete);

    public static void RejectEncryptedOpenXmlPackage(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var originalPosition = stream.Position;
        Span<byte> signature = stackalloc byte[CompoundFileSignature.Length];
        var bytesRead = stream.Read(signature);
        stream.Position = originalPosition;

        if (bytesRead == CompoundFileSignature.Length && signature.SequenceEqual(CompoundFileSignature))
        {
            throw new EncryptedWorkbookException(
                "This Workbook is encrypted or protected. Save an unencrypted copy in Excel and import that copy.");
        }
    }

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
