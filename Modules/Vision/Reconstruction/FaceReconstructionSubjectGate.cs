using EpisodeMonitor.Modules.Vision.Personalization;

namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class FaceReconstructionSubjectGate
{
    public string SubjectId { get; set; } = PersonalFaceSubject.DefaultSubjectId;

    public string SubjectDisplayName { get; set; } = PersonalFaceSubject.DefaultSubjectDisplayName;

    public string SubjectCollectionMode { get; set; } = PersonalFaceSubject.ManualConfirmationMode;

    public string UnknownSubjectPolicy { get; set; } = PersonalFaceSubject.UnknownSubjectPolicy;

    public bool ManualSubjectConfirmed { get; set; }

    public double? IdentityConfidencePercent { get; set; }

    public string GateDecision { get; set; } = "paused";

    public string Reason { get; set; } = "subject not confirmed";

    public static FaceReconstructionSubjectGate FromPersonalModel(
        PersonalFaceModel model,
        bool manualSubjectConfirmed,
        double? identityConfidencePercent = null,
        string? reason = null)
    {
        var accepted = manualSubjectConfirmed
            && (identityConfidencePercent is null or >= 80d);
        return new FaceReconstructionSubjectGate
        {
            SubjectId = string.IsNullOrWhiteSpace(model.SubjectId)
                ? PersonalFaceSubject.DefaultSubjectId
                : model.SubjectId,
            SubjectDisplayName = string.IsNullOrWhiteSpace(model.SubjectDisplayName)
                ? PersonalFaceSubject.DefaultSubjectDisplayName
                : model.SubjectDisplayName,
            SubjectCollectionMode = string.IsNullOrWhiteSpace(model.SubjectCollectionMode)
                ? PersonalFaceSubject.ManualConfirmationMode
                : model.SubjectCollectionMode,
            UnknownSubjectPolicy = string.IsNullOrWhiteSpace(model.UnknownSubjectPolicy)
                ? PersonalFaceSubject.UnknownSubjectPolicy
                : model.UnknownSubjectPolicy,
            ManualSubjectConfirmed = manualSubjectConfirmed,
            IdentityConfidencePercent = identityConfidencePercent,
            GateDecision = accepted ? "accepted" : "paused",
            Reason = reason ?? (accepted
                ? "subject confirmed for reconstruction learning data"
                : "subject not confirmed strongly enough for reconstruction learning data")
        };
    }
}
