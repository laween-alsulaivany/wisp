namespace Wisp.SteamIntegration.AppInfo;

internal static class BinaryKvValidation
{
    internal static void Validate(byte[] payload, bool indexedKeys, CancellationToken ct)
    {
        // ValveKeyValue 0.70 recursively reads objects without a depth limit. Check
        // structure iteratively first: stack overflow cannot be caught per entry.
        if (payload.Length == 0 || payload[0] != 0)
            throw new InvalidDataException("Appinfo KV root must be an object.");
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream);
        var depth = 0;
        while (stream.Position < stream.Length)
        {
            ct.ThrowIfCancellationRequested();
            var type = reader.ReadByte();
            if (type == 8)
            {
                if (depth-- == 0)
                {
                    if (stream.Position != stream.Length)
                        throw new InvalidDataException("Data after KV terminator.");
                    return;
                }
                continue;
            }
            if (depth == 0 && stream.Position != 1)
                throw new InvalidDataException("Multiple appinfo KV roots.");

            if (indexedKeys)
                _ = reader.ReadInt32();
            else
                SkipString(reader);

            switch (type)
            {
                case 0:
                    if (++depth > 64)
                        throw new InvalidDataException("Appinfo KV nesting is too deep.");
                    break;
                case 1:
                    SkipString(reader);
                    break;
                case 2:
                case 3:
                case 4:
                case 6:
                    _ = reader.ReadUInt32();
                    break;
                case 7:
                case 10:
                    _ = reader.ReadUInt64();
                    break;
                default:
                    throw new InvalidDataException($"Unsupported appinfo KV type: {type}.");
            }
        }
        throw new InvalidDataException("Missing KV terminator.");
    }

    private static void SkipString(BinaryReader reader)
    {
        while (reader.ReadByte() != 0) { }
    }
}
