namespace Wisp.Core.Interfaces;

public interface ITagDictionaryProvider
{
    string? Resolve(int tagId);
}
