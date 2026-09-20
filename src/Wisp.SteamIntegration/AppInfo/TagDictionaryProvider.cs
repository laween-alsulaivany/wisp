using System.Collections.Frozen;
using System.Text.Json;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.AppInfo;

public sealed class TagDictionaryProvider : ITagDictionaryProvider
{
    private static readonly FrozenDictionary<int, string> Tags = LoadTags();

    public string? Resolve(int tagId) => Tags.GetValueOrDefault(tagId);

    private static FrozenDictionary<int, string> LoadTags()
    {
        // Per the PRD, refresh this starter set by hand in future app releases;
        // never fetch tag names during metadata sync.
        using var stream = typeof(TagDictionaryProvider).Assembly
            .GetManifestResourceStream("Wisp.SteamIntegration.AppInfo.Tags.json")
            ?? throw new InvalidOperationException("Bundled tag dictionary is missing.");
        return (JsonSerializer.Deserialize<Dictionary<int, string>>(stream)
            ?? throw new InvalidDataException("Bundled tag dictionary is empty.")).ToFrozenDictionary();
    }
}
