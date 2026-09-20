using System.ComponentModel;
using System.Diagnostics;

namespace Wisp.SteamIntegration.Sessions;

internal sealed record ProcessSnapshot(int ProcessId, string ExecutablePath);

internal interface IProcessSnapshotProvider
{
    IReadOnlyList<ProcessSnapshot> GetProcesses();
}

internal sealed class ProcessSnapshotProvider : IProcessSnapshotProvider
{
    public IReadOnlyList<ProcessSnapshot> GetProcesses()
    {
        var snapshots = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } path)
                        snapshots.Add(new(process.Id, path));
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
                    or NotSupportedException)
                {
                    // Protected processes and processes exiting during enumeration cannot be matched.
                }
            }
        }
        return snapshots;
    }
}
