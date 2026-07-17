using EpisodeMonitor.Modules.Vision.Common;

namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class LastGoodFeatureMeshSample
{
    public string SampleId { get; init; } = "";

    public DateTime CapturedAtUtc { get; init; }

    public string Source { get; init; } = "";

    public string DenseMeshTopology { get; init; } = "";

    public string CoordinateSpace { get; init; } =
        "Dense points carry X/Y/Z positions for this frame. A/B/C rotation comes from the current pose estimator when available: A around X, B around Y, C around Z.";

    public int PointCount { get; init; }

    public double TrackingConfidencePercent { get; init; }

    public double EyeConfidencePercent { get; init; }

    public double MouthConfidencePercent { get; init; }

    public double OverallQualityPercent { get; init; }

    public double EyeQualityPercent { get; init; }

    public double MouthQualityPercent { get; init; }

    public double BrowQualityPercent { get; init; }

    public double FaceReliabilityPercent { get; init; }

    public double FaceContinuityPercent { get; init; }

    public double EyeReliabilityPercent { get; init; }

    public double MouthReliabilityPercent { get; init; }

    public double HeadYawDegrees { get; init; }

    public double HeadPitchDegrees { get; init; }

    public double HeadRollDegrees { get; init; }

    public double ARotationAroundXDegrees => HeadPitchDegrees;

    public double BRotationAroundYDegrees => HeadYawDegrees;

    public double CRotationAroundZDegrees => HeadRollDegrees;

    public double XHorizontalPercent { get; init; }

    public double YVerticalPercent { get; init; }

    public double? DistanceInches { get; init; }

    public double? ApparentDistanceUnits { get; init; }

    public double? RelativeDistanceScale { get; init; }

    public double? InterEyeFrameWidthPercent { get; init; }

    public double ZConfidencePercent { get; init; }

    public bool DistanceCalibrated { get; init; }

    public bool ZUsesCameraFov { get; init; }

    public bool ZUsesLearnedReference { get; init; }

    public string ZEstimateKind { get; init; } = "";

    public string ZQualityLabel { get; init; } = "";

    public string RotationSource { get; init; } = "";

    public string DistanceSource { get; init; } = "";

    public string ReferenceScaleSource { get; init; } = "";

    public double? LeftBrowHeightRatio { get; init; }

    public double? RightBrowHeightRatio { get; init; }

    public double? AverageBrowHeightRatio { get; init; }

    public double? BrowAsymmetryPercent { get; init; }

    public bool PossibleOneEyeArtifact { get; init; }

    public bool LeftEyeReconstructed { get; init; }

    public bool RightEyeReconstructed { get; init; }

    public bool MouthReconstructed { get; init; }

    public bool EyeArtifactSuppressed { get; init; }

    public string CaptureQualityLabel { get; init; } = "";

    public double CaptureQualityScorePercent { get; init; }

    public string GoodFeatureReason { get; init; } = "";

    public IReadOnlyList<double> FacialTransformationMatrix { get; init; } = [];

    public List<FaceMeshLandmarkPoint> Points { get; init; } = [];

    public List<LastGoodFeatureMeshWireframeEdge> WireframeEdges { get; init; } = [];

    public List<LastGoodFeatureMeshFeatureGroup> FeatureGroups { get; init; } = [];
}
