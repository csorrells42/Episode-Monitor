using EpisodeMonitor.Modules.Vision.Personalization;

namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class MeasurementAvatarEasyModeInput
{
    public bool SubjectConfirmed { get; set; }

    public bool AvatarLearningRequested { get; set; }

    public bool CameraActive { get; set; }

    public bool FaceLocked { get; set; }

    public bool HistoricalDataSuspect { get; set; }

    public string HistoricalDataAuditSummary { get; set; } = "";

    public bool TrackingAuditHold { get; set; }

    public string TrackingAuditHoldSummary { get; set; } = "";

    public PersonalFaceCaptureQualityAssessment CaptureQuality { get; set; } = PersonalFaceCaptureQualityAssessment.Waiting;

    public MeasurementAvatarCapturePlan? CapturePlan { get; set; }

    public string CapturePlanHtmlPath { get; set; } = "";
}
