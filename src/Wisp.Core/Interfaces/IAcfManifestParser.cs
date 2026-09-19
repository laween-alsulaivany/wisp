using Wisp.Core.Dtos;

namespace Wisp.Core.Interfaces;

public interface IAcfManifestParser
{
    IReadOnlyList<InstalledGameManifest> ParseLibrary(string libraryPath);
}
