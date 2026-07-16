namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class MeasurementAvatarReadinessScores
{
    public double OverallReadinessPercent { get; set; }

    public double BaselineCoveragePercent { get; set; }

    public double LearningStabilityCoveragePercent { get; set; }

    public double MotionCoveragePercent { get; set; }

    public double PoseCoveragePercent { get; set; }

    public double PoseBucketCoveragePercent { get; set; }

    public double DistanceCoveragePercent { get; set; }

    public double ExpressionCoveragePercent { get; set; }

    public double IdentityCoveragePercent { get; set; }

    public double ContourShapeCoveragePercent { get; set; }

    public double EyeBehindGlassesTrustPercent { get; set; }

    public double MouthJawTrustPercent { get; set; }

    public double DirectFeatureMeasurementTrustPercent { get; set; }

    public double QualityCoveragePercent { get; set; }

    public double CaptureQualityCoveragePercent { get; set; }

    public double StorageHealthPercent { get; set; }
}
