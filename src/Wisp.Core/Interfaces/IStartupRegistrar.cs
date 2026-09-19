namespace Wisp.Core.Interfaces;

public interface IStartupRegistrar
{
    bool IsRegistered();
    void Register(string executablePath);
    void Unregister();
}
