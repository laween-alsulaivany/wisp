using Microsoft.Win32;
using Wisp.Core.Interfaces;

namespace Wisp.App.Services.Hosting;

public sealed class StartupRegistrar : IStartupRegistrar
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Wisp";

    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
    }

    public void Register(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{Path.GetFullPath(executablePath)}\"", RegistryValueKind.String);
    }

    public void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
