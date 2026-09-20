using Wisp.Core.Interfaces;

namespace Wisp.App.Services.Hosting;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
