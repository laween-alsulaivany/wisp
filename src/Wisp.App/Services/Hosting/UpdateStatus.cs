using Wisp.Core.Dtos;

namespace Wisp.App.Services.Hosting;

public sealed class UpdateStatus
{
    private UpdateCheckResult current = new(false, null);
    public UpdateCheckResult Current => Volatile.Read(ref current);
    public event EventHandler? Changed;

    public void Set(UpdateCheckResult result)
    {
        Volatile.Write(ref current, result);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
