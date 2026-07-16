namespace EpisodeMonitor.Modules.Vision.Common;

public interface IStatefulFaceLandmarkTracker : IFaceLandmarkTracker
{
    void Reset();
}
