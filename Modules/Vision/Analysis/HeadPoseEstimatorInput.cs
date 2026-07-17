using EpisodeMonitor.Modules.Vision.Common;

namespace EpisodeMonitor.Modules.Vision.Analysis;

public sealed class HeadPoseEstimatorInput
{
    public FaceLandmarkFrame Frame { get; init; } = FaceLandmarkFrame.None;

    public int? FrameWidthPixels { get; init; }

    public int? FrameHeightPixels { get; init; }

    public HeadPoseCalibration Calibration { get; init; } = HeadPoseCalibration.None;
}
