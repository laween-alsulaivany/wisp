using Wisp.Core.Enums;

namespace Wisp.Core.Interfaces;

public interface IHotkeyManager
{
    bool RegisterPickHotkey(HotkeyModifiers modifiers, uint virtualKey);
    event EventHandler PickHotkeyPressed;
}
