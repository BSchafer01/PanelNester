using System.Buffers.Binary;
using PanelNester.Services.Projects;

namespace PanelNester.Services.Persistence;

internal readonly record struct ProjectFileHeader(ushort Version, ushort Flags)
{
    internal const int HeaderLength = 8;
    internal const ushort FlatBufferVersion = 2;
    private const uint Magic = 0x54534E50;

    internal static bool TryRead(string filePath, out ProjectFileHeader header)
    {
        header = default;

        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            if (stream.Length < 4)
            {
                return false;
            }

            Span<byte> magicBytes = stackalloc byte[4];
            var read = stream.Read(magicBytes);
            if (read < 4)
            {
                return false;
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(magicBytes) != Magic)
            {
                return false;
            }

            Span<byte> headerBytes = stackalloc byte[4];
            read = stream.Read(headerBytes);
            if (read < 4)
            {
                throw new ProjectPersistenceException("project-corrupt", "Project file header is incomplete.");
            }

            header = new ProjectFileHeader(
                BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[..2]),
                BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.Slice(2, 2)));
            return true;
        }
        catch (FileNotFoundException exception)
        {
            throw new ProjectPersistenceException("project-not-found", "Project file was not found.", exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new ProjectPersistenceException("project-not-found", "Project file was not found.", exception);
        }
    }

    internal static void Write(Stream stream, ProjectFileHeader header)
    {
        Span<byte> buffer = stackalloc byte[HeaderLength];
        buffer[0] = (byte)'P';
        buffer[1] = (byte)'N';
        buffer[2] = (byte)'S';
        buffer[3] = (byte)'T';
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(4, 2), header.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(6, 2), header.Flags);
        stream.Write(buffer);
    }
}
