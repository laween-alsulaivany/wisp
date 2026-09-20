using Wisp.Core.Dtos;

namespace Wisp.Core.Interfaces;

public interface IForegroundWindowMonitor
{
    int? CurrentForegroundProcessId { get; }
    event EventHandler<ForegroundProcessChangedEventArgs> ForegroundProcessChanged;
}
