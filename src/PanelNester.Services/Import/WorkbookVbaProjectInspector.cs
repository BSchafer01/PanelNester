using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using Kavod.Vba.Compression;
using OpenMcdf;

namespace PanelNester.Services.Import;

internal static class WorkbookVbaProjectInspector
{
    public static WorkbookVbaProject? Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var originalPosition = stream.Position;
        try
        {
            using var document = SpreadsheetDocument.Open(stream, isEditable: false);
            var vbaProjectPart = document.WorkbookPart?.VbaProjectPart;
            if (vbaProjectPart is null)
            {
                return null;
            }

            using (var vbaStream = vbaProjectPart.GetStream(FileMode.Open, FileAccess.Read))
            {
                using var contents = new MemoryStream();
                vbaStream.CopyTo(contents);
                contents.Position = 0;
                return new WorkbookVbaProject(VbaModuleSourceReader.Read(contents));
            }
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}

internal static class VbaModuleSourceReader
{
    private const ushort ProjectCodePage = 0x0003;
    private const ushort ProjectModules = 0x000F;
    private const ushort ModuleName = 0x0019;
    private const ushort ModuleStreamName = 0x001A;
    private const ushort ModuleStreamNameUnicode = 0x0032;
    private const ushort ModuleOffset = 0x0031;
    private const ushort ModuleTerminator = 0x002B;

    public static IReadOnlyList<string> Read(Stream projectStream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var root = RootStorage.Open(projectStream, StorageModeFlags.LeaveOpen);
        var vbaStorage = root.OpenStorage("VBA");
        using var directoryStream = vbaStorage.OpenStream("dir");
        var directory = VbaCompression.Decompress(ReadAllBytes(directoryStream));
        var codePage = FindCodePage(directory);
        var encoding = Encoding.GetEncoding(codePage);
        var modules = FindModules(directory, encoding);

        return modules.Select(module => ReadModule(vbaStorage, module, encoding)).ToArray();
    }

    private static string ReadModule(Storage vbaStorage, VbaModule module, Encoding encoding)
    {
        using var moduleStream = vbaStorage.OpenStream(module.StreamName);
        var contents = ReadAllBytes(moduleStream);
        if (module.Offset < 0 || module.Offset >= contents.Length)
        {
            throw new InvalidDataException($"VBA module '{module.StreamName}' has an invalid source offset.");
        }

        return encoding.GetString(VbaCompression.Decompress(contents[module.Offset..]));
    }

    private static ushort FindCodePage(byte[] directory)
    {
        for (var index = 0; index <= directory.Length - 8; index++)
        {
            if (ReadUInt16(directory, index) == ProjectCodePage &&
                ReadUInt32(directory, index + 2) == sizeof(ushort))
            {
                return ReadUInt16(directory, index + 6);
            }
        }

        throw new InvalidDataException("The VBA project code page is missing.");
    }

    private static IReadOnlyList<VbaModule> FindModules(byte[] directory, Encoding encoding)
    {
        for (var index = 0; index <= directory.Length - 8; index++)
        {
            if (ReadUInt16(directory, index) != ProjectModules ||
                ReadUInt32(directory, index + 2) != sizeof(ushort))
            {
                continue;
            }

            if (TryReadModules(directory, index + 8, ReadUInt16(directory, index + 6), encoding, out var modules))
            {
                return modules;
            }
        }

        throw new InvalidDataException("The VBA project module directory is invalid.");
    }

    private static bool TryReadModules(
        byte[] directory,
        int position,
        int count,
        Encoding encoding,
        out IReadOnlyList<VbaModule> modules)
    {
        var parsed = new List<VbaModule>(count);
        try
        {
            SkipSizedRecord(directory, ref position); // PROJECTCOOKIE
            for (var moduleIndex = 0; moduleIndex < count; moduleIndex++)
            {
                if (ReadUInt16(directory, position) != ModuleName)
                {
                    modules = [];
                    return false;
                }

                string? streamName = null;
                var offset = -1;
                while (true)
                {
                    var id = ReadUInt16(directory, position);
                    position += sizeof(ushort);
                    if (id == ModuleTerminator)
                    {
                        position += sizeof(uint); // reserved
                        break;
                    }

                    var size = checked((int)ReadUInt32(directory, position));
                    position += sizeof(uint);
                    EnsureAvailable(directory, position, size);
                    if (id == ModuleStreamName)
                    {
                        streamName = encoding.GetString(directory, position, size);
                    }
                    else if (id == ModuleStreamNameUnicode)
                    {
                        streamName = Encoding.Unicode.GetString(directory, position, size);
                    }
                    else if (id == ModuleOffset && size == sizeof(uint))
                    {
                        offset = checked((int)ReadUInt32(directory, position));
                    }

                    position += size;
                }

                if (string.IsNullOrWhiteSpace(streamName) || offset < 0)
                {
                    modules = [];
                    return false;
                }

                parsed.Add(new VbaModule(streamName.TrimEnd('\0'), offset));
            }

            modules = parsed;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            modules = [];
            return false;
        }
    }

    private static void SkipSizedRecord(byte[] contents, ref int position)
    {
        EnsureAvailable(contents, position, sizeof(ushort) + sizeof(uint));
        position += sizeof(ushort);
        var size = checked((int)ReadUInt32(contents, position));
        position += sizeof(uint);
        EnsureAvailable(contents, position, size);
        position += size;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var contents = new MemoryStream();
        stream.CopyTo(contents);
        return contents.ToArray();
    }

    private static ushort ReadUInt16(byte[] contents, int position)
    {
        EnsureAvailable(contents, position, sizeof(ushort));
        return BitConverter.ToUInt16(contents, position);
    }

    private static uint ReadUInt32(byte[] contents, int position)
    {
        EnsureAvailable(contents, position, sizeof(uint));
        return BitConverter.ToUInt32(contents, position);
    }

    private static void EnsureAvailable(byte[] contents, int position, int count)
    {
        if (position < 0 || count < 0 || position > contents.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
    }

    private sealed record VbaModule(string StreamName, int Offset);
}

internal sealed class WorkbookVbaProject
{
    private static readonly Regex FunctionDeclaration = new(
        @"^\s*(?:(?:Public|Private|Friend)\s+)?(?:Static\s+)?Function\s+([A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private readonly HashSet<string> _functionNames;

    public WorkbookVbaProject(IEnumerable<string> moduleBodies)
    {
        ArgumentNullException.ThrowIfNull(moduleBodies);
        _functionNames = moduleBodies
            .SelectMany(body => FunctionDeclaration.Matches(body ?? string.Empty).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool DeclaresFunction(string identifier) => _functionNames.Contains(identifier);
}
