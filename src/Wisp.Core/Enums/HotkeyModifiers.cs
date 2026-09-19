namespace Wisp.Core.Enums;

/// <summary>
/// Sections 3.2 and 4.2 require combinable modifier keys for hotkey registration,
/// including Control and Shift for the default binding. Values are domain flags.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}
