using System.Text;

namespace Wisp.SteamIntegration.Tests;

internal sealed class AppInfoFixture : IDisposable
{
    private readonly bool indexedKeys;
    private readonly List<string> strings = [];
    private readonly MemoryStream stream = new();
    private readonly BinaryWriter writer;

    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wisp-appinfo-{Guid.NewGuid():N}.vdf");

    internal AppInfoFixture(uint magic = 0x07564428)
    {
        indexedKeys = magic == 0x07564429;
        writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(magic);
        writer.Write(1u);
        if (indexedKeys)
            writer.Write(0L); // Filled with the string table offset on Save.
    }

    internal byte[] Blob(Action<BinaryWriter> write)
    {
        using var bytes = new MemoryStream();
        using var output = new BinaryWriter(bytes);
        write(output);
        return bytes.ToArray();
    }

    internal void Key(BinaryWriter output, byte type, string key)
    {
        output.Write(type);
        if (indexedKeys)
        {
            var index = strings.IndexOf(key);
            if (index < 0)
            {
                index = strings.Count;
                strings.Add(key);
            }
            output.Write(index);
        }
        else
            CString(output, key);
    }

    internal void String(BinaryWriter output, string key, string value)
    {
        Key(output, 1, key);
        CString(output, value);
    }

    internal void Integer(BinaryWriter output, string key, int value)
    {
        Key(output, 2, key);
        output.Write(value);
    }

    internal byte[] ValidBlob(string? controller = "full", string? category = null) => Blob(output =>
    {
        Key(output, 0, "appinfo");
        Key(output, 0, "common");
        Key(output, 0, "store_tags");
        Integer(output, "0", 19);
        String(output, "1", "1742");
        Integer(output, "2", 1654);
        output.Write((byte)8);
        Key(output, 0, "genres");
        String(output, "0", "1");
        Integer(output, "1", 23);
        output.Write((byte)8);
        if (controller is not null)
            String(output, "controller_support", controller);
        if (category is not null)
        {
            Key(output, 0, "categories");
            Integer(output, category, 1);
            output.Write((byte)8);
        }
        output.Write(new byte[] { 8, 8, 8 }); // common, appinfo, document
    });

    internal void Entry(uint appId, byte[] payload)
    {
        writer.Write(appId);
        writer.Write((uint)(60 + payload.Length));
        writer.Write(2u);
        writer.Write(1700000000u);
        writer.Write(123456789UL);
        writer.Write(Enumerable.Repeat((byte)0xAB, 20).ToArray());
        writer.Write(17u);
        writer.Write(Enumerable.Repeat((byte)0xCD, 20).ToArray());
        writer.Write(payload);
    }

    internal void RawEntry(uint appId, uint declaredSize, byte[] data)
    {
        writer.Write(appId);
        writer.Write(declaredSize);
        writer.Write(data);
    }

    internal void Save(bool sentinel = true)
    {
        if (sentinel)
            writer.Write(0u);
        if (indexedKeys)
        {
            var offset = stream.Position;
            writer.Write((uint)strings.Count);
            foreach (var value in strings)
                CString(writer, value);
            stream.Position = 8;
            writer.Write(offset);
        }
        File.WriteAllBytes(Path, stream.ToArray());
    }

    private static void CString(BinaryWriter output, string value)
    {
        output.Write(Encoding.UTF8.GetBytes(value));
        output.Write((byte)0);
    }

    public void Dispose()
    {
        writer.Dispose();
        stream.Dispose();
        File.Delete(Path);
    }
}
