using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using ValveKeyValue;
using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.Manifests;

namespace Wisp.SteamIntegration.AppInfo;

public sealed class AppInfoParser : IAppInfoParser
{
    private const uint Magic40 = 0x07564428;
    private const uint Magic41 = 0x07564429;
    private const int FixedFieldsSize = 60;
    // Corrupt lengths must not cause unbounded allocations during metadata sync.
    private const int MaxEntrySize = 16 * 1024 * 1024;
    private const int MaxStringTableSize = 64 * 1024 * 1024;

    public async IAsyncEnumerable<AppInfoRecord> ParseAsync(string appInfoVdfPath,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = Open(appInfoVdfPath);
        if (stream is null)
            yield break;

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var options = new KVSerializerOptions();
        long entriesEnd;
        try
        {
            entriesEnd = ReadHeader(reader, options, ct);
        }
        catch (Exception exception) when (IsParseFailure(exception))
        {
            Trace.TraceWarning($"Cannot read appinfo header: {exception.Message}");
            yield break;
        }

        while (stream.Position < entriesEnd)
        {
            ct.ThrowIfCancellationRequested();
            uint appId;
            uint size;
            long entryEnd;
            try
            {
                if (entriesEnd - stream.Position < sizeof(uint))
                    throw new InvalidDataException("Truncated app ID.");
                appId = reader.ReadUInt32();
                if (appId == 0)
                    yield break;
                if (entriesEnd - stream.Position < sizeof(uint))
                    throw new InvalidDataException("Truncated entry size.");
                size = reader.ReadUInt32();
                entryEnd = stream.Position + size;
                if (entryEnd > entriesEnd)
                    throw new InvalidDataException("Entry extends beyond appinfo data.");
            }
            catch (Exception exception) when (IsParseFailure(exception))
            {
                // Without a reliable size there is no safe next-entry boundary to seek to.
                Trace.TraceWarning($"Stopping appinfo at an unreadable entry boundary: {exception.Message}");
                yield break;
            }

            AppInfoRecord? record = null;
            try
            {
                if (size < FixedFieldsSize || size > MaxEntrySize)
                    throw new InvalidDataException($"Invalid appinfo entry size: {size}.");

                _ = reader.ReadUInt32(); // InfoState
                _ = reader.ReadUInt32(); // LastUpdated
                _ = reader.ReadUInt64(); // PICS token
                _ = reader.ReadBytes(20); // Text SHA-1
                _ = reader.ReadUInt32(); // ChangeNumber
                _ = reader.ReadBytes(20); // Binary SHA-1 (v40 and v41)

                var payload = new byte[(int)size - FixedFieldsSize];
                await stream.ReadExactlyAsync(payload, ct);
                BinaryKvValidation.Validate(payload, options.StringTable is not null, ct);
                using var blob = new MemoryStream(payload, writable: false);
                var document = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary)
                    .Deserialize(blob, options);
                if (blob.Position != blob.Length || !document.Root.IsCollection
                    || !string.Equals(document.Name, "appinfo", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unexpected appinfo KV root or trailing data.");

                var common = TextVdf.Child(document.Root, "common");
                var controller = TextVdf.String(TextVdf.Child(common, "controller_support"));
                var categories = TextVdf.Child(common, "categories");
                record = new AppInfoRecord
                {
                    AppId = appId,
                    TagIds = ReadIds(TextVdf.Child(common, "store_tags")),
                    GenreIds = ReadIds(TextVdf.Child(common, "genres")),
                    SupportsController = string.Equals(controller, "full", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(controller, "partial", StringComparison.OrdinalIgnoreCase)
                        || TextVdf.NonNegativeInteger(TextVdf.Child(categories, "22")) == 1
                        || TextVdf.NonNegativeInteger(TextVdf.Child(categories, "28")) == 1
                };
            }
            catch (Exception exception) when (IsParseFailure(exception))
            {
                Trace.TraceWarning($"Skipping appinfo app {appId}: {exception.Message}");
            }

            try
            {
                // A bounded blob keeps a missing KV terminator from reading the next entry.
                stream.Position = entryEnd;
            }
            catch (IOException exception)
            {
                Trace.TraceWarning($"Cannot resume appinfo after app {appId}: {exception.Message}");
                yield break;
            }
            ct.ThrowIfCancellationRequested();
            if (record is not null)
                yield return record;
        }
    }

    private static FileStream? Open(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Cannot open appinfo: {exception.Message}");
            return null;
        }
    }

    private static long ReadHeader(BinaryReader reader, KVSerializerOptions options, CancellationToken ct)
    {
        // Layout: https://github.com/ValveResourceFormat/SteamAppInfo/blob/master/README.md
        var magic = reader.ReadUInt32();
        if (magic is not (Magic40 or Magic41))
            throw new InvalidDataException($"Unrecognized appinfo magic: 0x{magic:X8}.");
        _ = reader.ReadUInt32(); // Universe
        if (magic == Magic40)
            return reader.BaseStream.Length;

        var tableOffset = reader.ReadInt64();
        var entriesStart = reader.BaseStream.Position;
        var tableSize = reader.BaseStream.Length - tableOffset;
        if (tableOffset < entriesStart || tableSize < sizeof(uint) || tableSize > MaxStringTableSize)
            throw new InvalidDataException("Invalid appinfo string table offset or size.");

        reader.BaseStream.Position = tableOffset;
        var count = reader.ReadUInt32();
        if (count > 1_000_000 || count > tableSize - sizeof(uint))
            throw new InvalidDataException("Invalid appinfo string count.");
        var strings = new List<string>();
        for (uint i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var bytes = new MemoryStream();
            byte value;
            while ((value = reader.ReadByte()) != 0)
            {
                if (bytes.Length >= 16 * 1024)
                    throw new InvalidDataException("Appinfo string table key is too long.");
                bytes.WriteByte(value);
            }
            strings.Add(Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length));
        }
        options.StringTable = new StringTable(strings);
        reader.BaseStream.Position = entriesStart;
        return tableOffset;
    }

    private static int[] ReadIds(KVObject? values)
    {
        if (values?.IsCollection != true)
            return [];
        var ids = new List<int>();
        var seen = new HashSet<int>();
        foreach (var (_, value) in values)
        {
            var id = TextVdf.NonNegativeInteger(value);
            if (id is > 0 and <= int.MaxValue && seen.Add((int)id))
                ids.Add((int)id);
        }
        return ids.ToArray();
    }

    private static bool IsParseFailure(Exception exception) =>
        exception is IOException or InvalidDataException or KeyValueException
            or ArgumentException or IndexOutOfRangeException or InvalidCastException or OverflowException;
}
