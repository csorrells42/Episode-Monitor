using System.Windows.Media.Imaging;

namespace EpisodeMonitor.Video;

public interface IFaceCueAnalyzer
{
    void Reset();

    FaceCueAnalysis Analyze(BitmapSource bitmap, FaceCueGuideLayout layout);
}
