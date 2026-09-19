namespace Wisp.Core.Interfaces;

public interface ILibraryFoldersParser
{
    IReadOnlyList<string> GetLibraryPaths(string steamPath);
}
