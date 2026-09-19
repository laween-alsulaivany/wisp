using Wisp.Core.Dtos;

namespace Wisp.Core.Interfaces;

public interface ISteamWindowTracker
{
    event EventHandler<SteamWindowBoundsChangedEventArgs> BoundsChanged;
    event EventHandler SteamFocusLost;
    event EventHandler SteamFocusGained;
    bool IsBigPictureMode { get; }
}
