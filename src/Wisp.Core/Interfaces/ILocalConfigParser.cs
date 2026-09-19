using Wisp.Core.Dtos;

namespace Wisp.Core.Interfaces;

public interface ILocalConfigParser
{
    IReadOnlyDictionary<long, PlaytimeRecord> ParsePlaytime(string steamPath, string steamId3);
}
