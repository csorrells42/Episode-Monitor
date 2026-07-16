using System.Windows.Media.Imaging;
using EpisodeMonitor.Modules.Vision.Common;

namespace EpisodeMonitor.Modules.Vision.Analysis;

public interface IFaceCueAnalyzer
{
    void Reset();

    FaceCueBaselineSnapshot ExportBaseline();

    bool TryImportBaseline(FaceCueBaselineSnapshot? baseline);

    FaceCueAnalysis Analyze(BitmapSource bitmap, FaceCueGuideLayout layout);
}
