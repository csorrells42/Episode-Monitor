namespace EpisodeMonitor.Modules.Vision.Personalization;

public sealed class PersonalFaceContourShapeProfile
{
    public string FeatureId { get; set; } = "";

    public string Label { get; set; } = "";

    public string CoordinateSpace { get; set; } =
        "Face-local normalized contour profile. Points are fixed resampled aggregate distributions, not per-frame raw contours.";

    public bool Closed { get; set; }

    public int PointCount { get; set; }

    public int SampleCount { get; set; }

    public double TotalWeight { get; set; }

    public List<PersonalFaceContourShapePointProfile> Points { get; set; } = [];

    public bool HasProfile => SampleCount > 0 && Points.Count >= Math.Max(2, PointCount);
}

public sealed class PersonalFaceContourShapePointProfile
{
    public int Index { get; set; }

    public PersonalMetricDistribution X { get; set; } = new();

    public PersonalMetricDistribution Y { get; set; } = new();
}
