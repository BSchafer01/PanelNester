using System.IO.Compression;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

public sealed record WorkbookSafetyLimits
{
    public static WorkbookSafetyLimits DesktopDefault { get; } = new();

    public long WarningCompressedBytes { get; init; } = 32L * 1024 * 1024;

    public long MaximumCompressedBytes { get; init; } = 128L * 1024 * 1024;

    public long WarningUncompressedBytes { get; init; } = 64L * 1024 * 1024;

    public long MaximumUncompressedBytes { get; init; } = 256L * 1024 * 1024;

    public int WarningPackageEntryCount { get; init; } = 5_000;

    public int MaximumPackageEntryCount { get; init; } = 20_000;

    public long WarningLargestEntryBytes { get; init; } = 32L * 1024 * 1024;

    public long MaximumLargestEntryBytes { get; init; } = 192L * 1024 * 1024;

    public double WarningCompressionRatio { get; init; } = 100d;

    public double MaximumCompressionRatio { get; init; } = 500d;
}

public sealed class WorkbookSafetyException(string message) : Exception(message);

public static class WorkbookPackagePreflight
{
    public static WorkbookPreflightAssessment Inspect(
        string workbookPath,
        WorkbookSafetyLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        cancellationToken.ThrowIfCancellationRequested();
        var policy = limits ?? WorkbookSafetyLimits.DesktopDefault;
        var compressedBytes = new FileInfo(workbookPath).Length;
        EnforceCeiling(
            compressedBytes,
            policy.MaximumCompressedBytes,
            $"Workbook compressed size is {FormatBytes(compressedBytes)}, above the {FormatBytes(policy.MaximumCompressedBytes)} desktop safety ceiling.");

        long uncompressedBytes = 0;
        long largestEntryBytes = 0;
        var packageEntryCount = 0;
        using (var stream = ImportFileAccessGuard.OpenReadShared(workbookPath))
        {
            ImportFileAccessGuard.RejectEncryptedOpenXmlPackage(stream);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                packageEntryCount++;
                largestEntryBytes = Math.Max(largestEntryBytes, entry.Length);

                EnforceCeiling(
                    packageEntryCount,
                    policy.MaximumPackageEntryCount,
                    $"Workbook package contains more than {policy.MaximumPackageEntryCount:N0} entries, above the desktop safety ceiling.");
                EnforceCeiling(
                    largestEntryBytes,
                    policy.MaximumLargestEntryBytes,
                    $"Workbook contains a package part above the {FormatBytes(policy.MaximumLargestEntryBytes)} desktop safety ceiling.");
                if (entry.Length > policy.MaximumUncompressedBytes - uncompressedBytes)
                {
                    throw new WorkbookSafetyException(
                        $"Workbook expanded package size is above the {FormatBytes(policy.MaximumUncompressedBytes)} desktop safety ceiling.");
                }
                uncompressedBytes += entry.Length;
            }
        }

        var compressionRatio = compressedBytes == 0
            ? 0d
            : (double)uncompressedBytes / compressedBytes;
        if (compressionRatio > policy.MaximumCompressionRatio)
        {
            throw new WorkbookSafetyException(
                $"Workbook package expands {compressionRatio:N1}×, above the {policy.MaximumCompressionRatio:N0}× desktop safety ceiling. Remove unused formatting or split the Workbook before importing.");
        }

        var warnings = new List<string>();
        AddWarning(
            compressedBytes > policy.WarningCompressedBytes,
            $"This Workbook is {FormatBytes(compressedBytes)} compressed; close other memory-intensive applications before importing.",
            warnings);
        AddWarning(
            uncompressedBytes > policy.WarningUncompressedBytes,
            $"This Workbook expands to {FormatBytes(uncompressedBytes)}; importing may take several minutes.",
            warnings);
        AddWarning(
            packageEntryCount > policy.WarningPackageEntryCount,
            $"This Workbook contains {packageEntryCount:N0} package entries; consider removing unused content.",
            warnings);
        AddWarning(
            largestEntryBytes > policy.WarningLargestEntryBytes,
            $"This Workbook contains a {FormatBytes(largestEntryBytes)} package part; consider splitting the Workbook.",
            warnings);
        AddWarning(
            compressionRatio > policy.WarningCompressionRatio,
            $"This Workbook expands {compressionRatio:N1}×; remove unused formatting or split it if import is slow.",
            warnings);

        return new WorkbookPreflightAssessment
        {
            CompressedBytes = compressedBytes,
            UncompressedBytes = uncompressedBytes,
            PackageEntryCount = packageEntryCount,
            LargestEntryBytes = largestEntryBytes,
            CompressionRatio = compressionRatio,
            Warnings = warnings
        };
    }

    private static void EnforceCeiling(long actual, long maximum, string message)
    {
        if (actual > maximum)
        {
            throw new WorkbookSafetyException(message);
        }
    }

    private static void AddWarning(bool condition, string message, ICollection<string> warnings)
    {
        if (condition)
        {
            warnings.Add(message);
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double mebibyte = 1024d * 1024d;
        const double gibibyte = 1024d * mebibyte;
        return bytes >= gibibyte
            ? $"{bytes / gibibyte:N1} GiB"
            : $"{bytes / mebibyte:N1} MiB";
    }
}
