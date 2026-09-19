namespace Wisp.Core.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
