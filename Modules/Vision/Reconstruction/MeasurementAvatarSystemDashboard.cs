using EpisodeMonitor.Modules.Vision.Personalization;
using EpisodeMonitor.Modules.Vision.Analysis;

namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class MeasurementAvatarSystemDashboard
{
    public string SchemaVersion { get; set; } = "measurement-avatar-system-dashboard-v1";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string SubjectId { get; set; } = PersonalFaceSubject.DefaultSubjectId;

    public string SubjectDisplayName { get; set; } = PersonalFaceSubject.DefaultSubjectDisplayName;

    public bool SubjectConfirmed { get; set; }

    public bool AvatarLearningRequested { get; set; }

    public bool AvatarLearningActive { get; set; }

    public string AvatarLearningStatus { get; set; } = "";

    public string AvatarLearningCorrection { get; set; } = "";

    public PersonalFaceModel FaceModel { get; set; } = new();

    public PersonalFaceMotionModel MotionModel { get; set; } = new();

    public PersonalFaceCorpusReadiness LearningDataReadiness { get; set; } = new();

    public PersonalFaceCollectionAudit CollectionAudit { get; set; } = new();

    public MeasurementAvatarTrainingPackage AvatarPackage { get; set; } = new();

    public MeasurementAvatarCapturePlan CapturePlan { get; set; } = new();

    public PersonalFaceCaptureQualityAssessment CurrentCaptureQuality { get; set; } = PersonalFaceCaptureQualityAssessment.Waiting;

    public HeadPoseEstimate CurrentHeadPose { get; set; } = HeadPoseEstimate.None;

    public LastGoodFeatureMeshStabilityReport LastGoodFeatureStability { get; set; } = new();

    public string FacePreviewHtmlPath { get; set; } = "";

    public string LearningDataReportHtmlPath { get; set; } = "";

    public string CollectionAuditHtmlPath { get; set; } = "";

    public string AvatarPackageHtmlPath { get; set; } = "";

    public string CapturePlanHtmlPath { get; set; } = "";

    public string LastGoodFeaturesHtmlPath { get; set; } = "";

    public string StoragePolicy { get; set; } =
        "Measurement-only avatar system dashboard. Passive learning stores numbers, scores, and reasons; it does not store continuous webcam video or room imagery. The Last 10 Good Features page keeps only a rolling inspection cache of the latest dense landmark meshes.";
}
