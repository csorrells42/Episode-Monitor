namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class MeasurementAvatarEasyModeState
{
    public string Title { get; set; } = "Easy Avatar Mode";

    public string Detail { get; set; } = "Waiting for capture guidance.";

    public string ActionText { get; set; } = "Easy Avatar Mode";

    public string Severity { get; set; } = MeasurementAvatarEasyModeSeverity.Idle;

    public bool CanStartLearning { get; set; }

    public string CapturePlanItemId { get; set; } = "";

    public string CapturePlanHtmlPath { get; set; } = "";
}

public static class MeasurementAvatarEasyModeSeverity
{
    public const string Good = "good";

    public const string Warning = "warning";

    public const string Blocked = "blocked";

    public const string Idle = "idle";
}
