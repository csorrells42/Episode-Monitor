using EpisodeMonitor.Modules.Vision.Personalization;

namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class MeasurementAvatarCapturePlanBuilder
{
    private const long EstimatedMeasurementBytesPerMinute = 60_000L;

    public MeasurementAvatarCapturePlan Build(
        PersonalFaceModel faceModel,
        PersonalFaceMotionModel motionModel,
        PersonalFaceCorpusReadiness readiness,
        FaceReconstructionSubjectGate subjectGate,
        long measurementJournalBytes,
        long measurementBudgetBytes = PersonalFaceMeasurementJournal.DefaultBudgetBytes)
    {
        ArgumentNullException.ThrowIfNull(faceModel);
        ArgumentNullException.ThrowIfNull(motionModel);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(subjectGate);

        var plan = new MeasurementAvatarCapturePlan
        {
            SubjectId = faceModel.SubjectId,
            SubjectDisplayName = faceModel.SubjectDisplayName,
            SubjectCollectionMode = faceModel.SubjectCollectionMode,
            UnknownSubjectPolicy = faceModel.UnknownSubjectPolicy,
            SubjectGate = subjectGate,
            MeasurementJournalBytes = Math.Max(0L, measurementJournalBytes),
            MeasurementBudgetBytes = Math.Max(1L, measurementBudgetBytes)
        };

        plan.MeasurementBudgetUsedPercent = Round(100d * plan.MeasurementJournalBytes / plan.MeasurementBudgetBytes);
        plan.CanCollectMeasurements = string.Equals(subjectGate.GateDecision, "accepted", StringComparison.OrdinalIgnoreCase);
        plan.LowestReadinessScorePercent = Round(new[]
        {
            readiness.BaselineCoveragePercent,
            readiness.MotionCoveragePercent,
            readiness.PoseCoveragePercent,
            readiness.PoseBucketCoveragePercent,
            readiness.DistanceCoveragePercent,
            readiness.ExpressionCoveragePercent,
            readiness.IdentityCoveragePercent,
            readiness.ContourShapeCoveragePercent,
            readiness.EyeBehindGlassesTrustPercent,
            readiness.MouthJawTrustPercent,
            readiness.DirectFeatureMeasurementTrustPercent,
            readiness.QualityCoveragePercent,
            readiness.CaptureQualityCoveragePercent,
            readiness.StorageHealthPercent
        }.Min());

        AddDefaultRules(plan);
        AddCollectionItems(plan, readiness, motionModel);
        if (plan.Items.Count == 0)
        {
            AddMaintenanceItem(plan, readiness);
        }

        plan.Items = plan.Items
            .OrderBy(static item => item.Priority)
            .ThenBy(static item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        plan.TotalTargetMinutes = plan.Items.Sum(static item => Math.Max(0, item.TargetMinutes));
        plan.EstimatedMeasurementBytes = plan.Items.Sum(static item => Math.Max(0L, item.EstimatedMeasurementBytes));
        plan.CollectionDecision = BuildDecision(plan);
        return plan;
    }

    private static void AddDefaultRules(MeasurementAvatarCapturePlan plan)
    {
        plan.PreSessionChecks.Add("Confirm the checkbox says this is the enrolled subject before collecting measurements.");
        plan.PreSessionChecks.Add("Use the normal glasses/camera/lighting setup whenever possible so the corpus matches real use.");
        plan.PreSessionChecks.Add("Use 4K tracking when practical, disable face filters/background effects, and keep lighting soft enough to reduce glasses glare.");
        plan.StopRules.Add("Stop or pause collection immediately if sleepiness/cataplexy symptoms begin.");
        plan.StopRules.Add("Stop if someone else is in front of the camera or the subject checkbox is not correct.");
        plan.StopRules.Add("Do not collect raw media for passive learning; use explicit training-media sessions only when a future 3D worker asks for it.");
    }

    private static void AddCollectionItems(
        MeasurementAvatarCapturePlan plan,
        PersonalFaceCorpusReadiness readiness,
        PersonalFaceMotionModel motionModel)
    {
        if (!plan.CanCollectMeasurements)
        {
            AddItem(
                plan,
                "confirm-subject",
                1,
                "Subject Gate",
                "Confirm enrolled subject",
                "Turn on the subject confirmation checkbox only when the enrolled subject is in front of the camera.",
                "This prevents the long-running face model from blending multiple people.",
                "SubjectGate",
                0d,
                0,
                "The subject gate is accepted and the collection decision no longer says paused.");
            return;
        }

        if (readiness.StorageHealthPercent < 85d || readiness.MeasurementBudgetUsedPercent > 80d)
        {
            AddItem(
                plan,
                "storage-health",
                Priority(readiness.StorageHealthPercent),
                "Storage",
                "Protect the measurement budget",
                "Keep the output folder on the external drive and archive explicit training media before collecting more.",
                "The measurement corpus must stay under the 10 GB target without silently filling the local drive.",
                "StorageHealthPercent",
                readiness.StorageHealthPercent,
                0,
                "Storage health is above 85% and budget use is below 80%.");
        }

        if (readiness.BaselineCoveragePercent < 70d)
        {
            AddItem(
                plan,
                "alert-baseline",
                Priority(readiness.BaselineCoveragePercent),
                "Baseline",
                "Alert neutral baseline",
                "Sit normally, symptom-free, eyes naturally open, lips relaxed/closed, and let the camera collect clean frames.",
                "Stable neutral measurements keep the avatar from changing its basic face every day.",
                "BaselineCoveragePercent",
                readiness.BaselineCoveragePercent,
                5,
                "Baseline coverage is at least 70% with clean accepted samples.");
        }

        if (readiness.MotionCoveragePercent < 70d || motionModel.MotionPairCount < 240)
        {
            AddItem(
                plan,
                "natural-motion",
                Priority(readiness.MotionCoveragePercent),
                "Motion",
                "Natural facial motion",
                "Talk normally, do a few slow blinks, relax and close the mouth, then repeat with small natural head movements.",
                "The future avatar needs motion timing, not just a static face shape.",
                "MotionCoveragePercent",
                readiness.MotionCoveragePercent,
                6,
                "Motion coverage is at least 70% and the motion model has at least 240 usable pairs.");
        }

        if (readiness.DistanceCoveragePercent < 70d)
        {
            AddItem(
                plan,
                "distance-ladder",
                Priority(readiness.DistanceCoveragePercent),
                "Distance",
                "Distance ladder",
                "Collect short symptom-free segments leaning close, at normal desk distance, and leaning back across the room.",
                "The webcam will see the face at different sizes; the model needs all common distances.",
                "DistanceCoveragePercent",
                readiness.DistanceCoveragePercent,
                6,
                "Face width and height ranges cover close, normal, and leaned-back positions.");
        }

        if (readiness.PoseCoveragePercent < 70d)
        {
            AddItem(
                plan,
                "pose-sweep",
                Priority(readiness.PoseCoveragePercent),
                "Pose",
                "Head pose sweep",
                "Slowly turn left/right, look slightly up/down, and add a few gentle head-roll positions while keeping the face visible.",
                "Face reconstruction and mouth/eye tracking need the same person across realistic head angles.",
                "PoseCoveragePercent",
                readiness.PoseCoveragePercent,
                6,
                "Yaw, pitch, and roll coverage are all broad enough for the readiness report.");
        }

        if (readiness.PoseBucketCoveragePercent < 70d && readiness.PoseBuckets.Count > 0)
        {
            foreach (var bucket in readiness.PoseBuckets
                         .Where(static bucket => bucket.RequiredForAvatarCoverage && bucket.SampleCount < 45)
                         .OrderBy(static bucket => bucket.SampleCount)
                         .Take(2))
            {
                AddItem(
                    plan,
                    $"pose-bucket-{bucket.BucketId}",
                    Priority(readiness.PoseBucketCoveragePercent),
                    "Pose Bucket",
                    bucket.Label,
                    bucket.CaptureInstruction,
                    "Pose-specific buckets keep straight-on identity, turned-head evidence, and animation correction from being averaged into the same face shape.",
                    "PoseBucketCoveragePercent",
                    readiness.PoseBucketCoveragePercent,
                    bucket.PrimaryNeutralReference ? 5 : 3,
                    $"{bucket.Label} has enough accepted subject-gated measurements for the pose bucket profile.");
            }
        }

        if (readiness.ExpressionCoveragePercent < 75d)
        {
            AddItem(
                plan,
                "expression-ladder",
                Priority(readiness.ExpressionCoveragePercent),
                "Expression",
                "Eye mouth jaw expression ladder",
                "While alert, cycle through eyes open, relaxed eyelids, slow blinks, lips closed, lips slightly open, speech, and deliberate jaw drop.",
                "Extreme accuracy needs eyelid, lip, and jaw ranges gathered while awake so sleepy/cataplexy events stand out.",
                "ExpressionCoveragePercent",
                readiness.ExpressionCoveragePercent,
                8,
                "Eye opening, mouth opening, jaw droop, and dense blendshape ranges are all broad enough.");
        }

        if (readiness.IdentityCoveragePercent < 70d)
        {
            AddItem(
                plan,
                "identity-lock",
                Priority(readiness.IdentityCoveragePercent),
                "Identity",
                "Identity signature strengthening",
                "Collect a stable symptom-free session with glasses in the normal position, no filters, and the face clearly visible.",
                "The system needs a non-image owner signature so it can pause before learning from someone else.",
                "IdentityCoveragePercent",
                readiness.IdentityCoveragePercent,
                5,
                "Identity coverage is at least 70% with enough accepted signature samples.");
        }

        if (readiness.ContourShapeCoveragePercent < 70d)
        {
            AddItem(
                plan,
                "contour-shape-pass",
                Priority(readiness.ContourShapeCoveragePercent),
                "Contour Shape",
                "Direct eye lip jaw outline pass",
                "Collect stable alert frames with eyes visible behind glasses, lips closed and slightly open, and the lower face fully visible.",
                "The future avatar needs learned eyelid, lip, and jaw outlines, not only opening ratios.",
                "ContourShapeCoveragePercent",
                readiness.ContourShapeCoveragePercent,
                5,
                "Contour shape coverage is at least 70% for direct eye, lip, and jaw observations.");
        }

        if (readiness.EyeBehindGlassesTrustPercent < 70d)
        {
            AddItem(
                plan,
                "eye-glasses-trust",
                Priority(readiness.EyeBehindGlassesTrustPercent),
                "Eye Trust",
                "Behind-glasses eye accuracy pass",
                "Collect alert frames with glasses on, both eyes visible, screen glare minimized, and slow open-relaxed-blink eyelid changes.",
                "Eye closure is the primary sleep/cataplexy cue, so avatar and narcolepsy tracking need direct eye evidence behind glasses.",
                "EyeBehindGlassesTrustPercent",
                readiness.EyeBehindGlassesTrustPercent,
                6,
                "Eye-behind-glasses trust is at least 70% with direct, non-reconstructed eye contours.");
        }

        if (readiness.MouthJawTrustPercent < 70d)
        {
            AddItem(
                plan,
                "mouth-jaw-trust",
                Priority(readiness.MouthJawTrustPercent),
                "Mouth Jaw Trust",
                "Mouth and jaw visibility pass",
                "Collect alert frames with the lower face fully visible, lips closed, lips slightly open, natural speech, and gentle jaw drop.",
                "Jaw droop and lip opening are supporting sleep/cataplexy cues and important facial motion channels for the future avatar.",
                "MouthJawTrustPercent",
                readiness.MouthJawTrustPercent,
                5,
                "Mouth/jaw trust is at least 70% with direct mouth and jaw contours.");
        }

        if (readiness.QualityCoveragePercent < 75d)
        {
            AddItem(
                plan,
                "quality-lighting",
                Priority(readiness.QualityCoveragePercent),
                "Quality",
                "Lighting and glasses glare check",
                "Adjust room lighting, monitor brightness, camera angle, or glasses angle, then collect a short neutral segment.",
                "Glare and low contrast are direct threats to eyelid accuracy behind glasses.",
                "QualityCoveragePercent",
                readiness.QualityCoveragePercent,
                4,
                "Quality coverage is above 75% and eye glare/artifact warnings stop dominating the report.");
        }

        if (readiness.CaptureQualityCoveragePercent < 75d)
        {
            AddItem(
                plan,
                "avatar-grade-capture",
                Priority(readiness.CaptureQualityCoveragePercent),
                "Capture Quality",
                "Avatar-grade capture pass",
                "Set the camera to explicit 4K/30fps, keep the face comfortably large in frame, reduce glasses glare, and collect a short neutral segment.",
                "Extreme accuracy needs measurements that were good enough to enter the long-term corpus, not just frames that were reviewable.",
                "CaptureQualityCoveragePercent",
                readiness.CaptureQualityCoveragePercent,
                4,
                "Capture quality coverage is above 75%, collectable rate is high, and avatar-grade samples are present.");
        }
    }

    private static void AddMaintenanceItem(MeasurementAvatarCapturePlan plan, PersonalFaceCorpusReadiness readiness)
    {
        AddItem(
            plan,
            "maintenance",
            9,
            "Maintenance",
            "Balanced maintenance session",
            "Run a short symptom-free session that includes normal posture, a few head turns, natural speech, slow blinks, and a distance change.",
            "The corpus is strong enough for maintenance; balanced measurements keep it current without rapid daily jumps.",
            "OverallReadinessPercent",
            readiness.OverallReadinessPercent,
            8,
            "Readiness remains strong after routine use.");
    }

    private static void AddItem(
        MeasurementAvatarCapturePlan plan,
        string id,
        int priority,
        string category,
        string title,
        string instructions,
        string whyItMatters,
        string relatedScoreName,
        double relatedScorePercent,
        int targetMinutes,
        string completeWhen)
    {
        plan.Items.Add(new MeasurementAvatarCapturePlanItem
        {
            Id = id,
            Priority = priority,
            Category = category,
            Title = title,
            Instructions = instructions,
            WhyItMatters = whyItMatters,
            RelatedScoreName = relatedScoreName,
            RelatedScorePercent = Round(relatedScorePercent),
            TargetMinutes = Math.Max(0, targetMinutes),
            EstimatedMeasurementBytes = Math.Max(0, targetMinutes) * EstimatedMeasurementBytesPerMinute,
            CompleteWhen = completeWhen
        });
    }

    private static string BuildDecision(MeasurementAvatarCapturePlan plan)
    {
        if (!plan.CanCollectMeasurements)
        {
            return $"paused by subject gate: {plan.SubjectGate.Reason}";
        }

        if (plan.MeasurementBudgetUsedPercent > 90d)
        {
            return "storage review first; measurement budget is close to full";
        }

        var first = plan.Items.FirstOrDefault();
        return first is null
            ? "no capture plan items available"
            : $"next recommended capture: {first.Title}";
    }

    private static int Priority(double score)
    {
        if (score < 35d)
        {
            return 1;
        }

        if (score < 55d)
        {
            return 2;
        }

        if (score < 70d)
        {
            return 3;
        }

        return 4;
    }

    private static double Round(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0d
            : Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }
}
