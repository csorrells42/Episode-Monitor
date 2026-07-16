using EpisodeMonitor.Modules.Vision.Analysis;
using EpisodeMonitor.Modules.Vision.Common;

namespace EpisodeMonitor.Modules.Vision.Personalization;

public sealed class PersonalFaceCaptureQualityInput
{
    public int? VideoWidth { get; init; }

    public int? VideoHeight { get; init; }

    public double? FramesPerSecond { get; init; }

    public string? InputFormat { get; init; }

    public bool IsAutoCameraMode { get; init; }

    public FaceLandmarkFrame LandmarkFrame { get; init; } = FaceLandmarkFrame.None;

    public FaceLandmarkMetrics Metrics { get; init; } = FaceLandmarkMetrics.None;

    public FaceLockStabilityAnalysis Stability { get; init; } = FaceLockStabilityAnalysis.Waiting;

    public PersonalFaceModelUpdate PersonalModelUpdate { get; init; } = new(
        false,
        PersonalFaceModelRejectionKind.NoFace,
        "not started",
        0d,
        new PersonalFaceModel());

    public long MeasurementJournalBytes { get; init; }

    public long MeasurementBudgetBytes { get; init; } = PersonalFaceMeasurementJournal.DefaultBudgetBytes;
}
