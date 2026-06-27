using System.Windows;

namespace EpisodeMonitor.Video;

public sealed class FaceFeatureDetection
{
    public static FaceFeatureDetection None { get; } = new();

    public bool HasFace { get; init; }

    public Rect FaceBox { get; init; }

    public Rect? LeftEyeBox { get; init; }

    public Rect? RightEyeBox { get; init; }

    public Rect? MouthBox { get; init; }

    public string Source { get; init; } = "";

    public FaceCueGuideLayout ToGuideLayout(FaceCueGuideLayout fallback)
    {
        if (!HasFace || FaceBox.Width <= 0d || FaceBox.Height <= 0d)
        {
            return fallback;
        }

        return new FaceCueGuideLayout(
            (FaceBox.Left + FaceBox.Width / 2d) * 100d,
            (FaceBox.Top + FaceBox.Height / 2d) * 100d,
            FaceBox.Height * 100d);
    }
}
