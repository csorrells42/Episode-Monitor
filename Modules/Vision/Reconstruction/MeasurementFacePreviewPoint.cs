namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class MeasurementFacePreviewPoint
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    public string Role { get; set; } = "";

    public string Provenance { get; set; } = "";

    public double ConfidencePercent { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }
}
