using Wisp.Core.Dtos;

namespace Wisp.Core.Interfaces;

public interface ISessionTracker
{
    event EventHandler<SessionStartedEventArgs> SessionStarted;
    event EventHandler<SessionEndedEventArgs> SessionEnded;
    void Start();
    void Stop();
    void NotifyRecommendationLaunch(long appId);
}
