using Wisp.Core.Enums;

namespace Wisp.SteamIntegration.Win32;

internal interface IHotkeyNative : IDisposable
{
    bool Register(HotkeyModifiers modifiers, uint virtualKey, Action pressed);
}
