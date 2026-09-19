using System.Globalization;
using ValveKeyValue;

namespace Wisp.SteamIntegration.Manifests;

internal static class TextVdf
{
    internal static KVObject? ReadRoot(string path, string rootName)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var document = KVSerializer.Create(KVSerializationFormat.KeyValues1Text)
                .Deserialize(stream, new KVSerializerOptions { HasEscapeSequences = true });
            return string.Equals(document.Name, rootName, StringComparison.OrdinalIgnoreCase)
                && document.Root.IsCollection ? document.Root : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or KeyValueException)
        {
            // Steam can replace these files while running, or leave an incomplete write.
            return null;
        }
    }

    internal static KVObject? Child(KVObject? parent, string name)
    {
        if (parent?.IsCollection != true)
            return null;

        foreach (var (key, value) in parent)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null;
    }

    internal static string? String(KVObject? value) =>
        value is null || value.IsCollection || value.IsArray || value.IsNull || value.ValueType == KVValueType.BinaryBlob
            ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    internal static long NonNegativeInteger(KVObject? value)
    {
        if (value is null)
            return 0;

        var text = value.ValueType switch
        {
            KVValueType.String => (string)value,
            KVValueType.Int16 or KVValueType.Int32 or KVValueType.Int64 =>
                value.ToInt64().ToString(CultureInfo.InvariantCulture),
            KVValueType.UInt16 or KVValueType.UInt32 or KVValueType.UInt64 =>
                value.ToUInt64().ToString(CultureInfo.InvariantCulture),
            _ => null
        };
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }
}
