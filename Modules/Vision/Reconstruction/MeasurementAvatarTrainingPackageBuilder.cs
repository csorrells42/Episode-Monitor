using EpisodeMonitor.Modules.Vision.Personalization;

namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class MeasurementAvatarTrainingPackageBuilder
{
    public MeasurementAvatarTrainingPackage Build(
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

        var package = new MeasurementAvatarTrainingPackage
        {
            CreatedAtUtc = faceModel.CreatedAtUtc != default ? faceModel.CreatedAtUtc : DateTime.UtcNow,
            UpdatedAtUtc = Max(faceModel.UpdatedAtUtc, motionModel.UpdatedAtUtc, readiness.UpdatedAtUtc),
            SubjectId = faceModel.SubjectId,
            SubjectDisplayName = faceModel.SubjectDisplayName,
            SubjectCollectionMode = faceModel.SubjectCollectionMode,
            UnknownSubjectPolicy = faceModel.UnknownSubjectPolicy,
            IdentityGatePolicy = faceModel.IdentityGatePolicy,
            SubjectGate = subjectGate,
            ObservedSamples = faceModel.ObservedSamples,
            AcceptedBaselineSamples = faceModel.AcceptedSamples,
            AcceptedSampleWeight = Round(faceModel.AcceptedSampleWeight),
            LearningStability = faceModel.LearningStability,
            MotionUsableObservations = motionModel.UsableObservationCount,
            MotionPairs = motionModel.MotionPairCount,
            IdentitySignatureSamples = faceModel.IdentitySignatureSamples,
            MeasurementJournalBytes = Math.Max(0L, measurementJournalBytes),
            MeasurementBudgetBytes = Math.Max(1L, measurementBudgetBytes),
            Readiness = new MeasurementAvatarReadinessScores
            {
                OverallReadinessPercent = Round(readiness.OverallReadinessPercent),
                BaselineCoveragePercent = Round(readiness.BaselineCoveragePercent),
                LearningStabilityCoveragePercent = Round(readiness.LearningStabilityCoveragePercent),
                MotionCoveragePercent = Round(readiness.MotionCoveragePercent),
                PoseCoveragePercent = Round(readiness.PoseCoveragePercent),
                PoseBucketCoveragePercent = Round(readiness.PoseBucketCoveragePercent),
                DistanceCoveragePercent = Round(readiness.DistanceCoveragePercent),
                ExpressionCoveragePercent = Round(readiness.ExpressionCoveragePercent),
                IdentityCoveragePercent = Round(readiness.IdentityCoveragePercent),
                ContourShapeCoveragePercent = Round(readiness.ContourShapeCoveragePercent),
                EyeBehindGlassesTrustPercent = Round(readiness.EyeBehindGlassesTrustPercent),
                MouthJawTrustPercent = Round(readiness.MouthJawTrustPercent),
                DirectFeatureMeasurementTrustPercent = Round(readiness.DirectFeatureMeasurementTrustPercent),
                QualityCoveragePercent = Round(readiness.QualityCoveragePercent),
                CaptureQualityCoveragePercent = Round(readiness.CaptureQualityCoveragePercent),
                StorageHealthPercent = Round(readiness.StorageHealthPercent)
            }
        };

        package.MeasurementBudgetUsedPercent = Round(100d * package.MeasurementJournalBytes / package.MeasurementBudgetBytes);
        package.MeasurementContributionPercent = CalculateMeasurementContribution(faceModel);
        package.TemplatePriorContributionPercent = Round(100d - package.MeasurementContributionPercent);
        package.CanUseForAvatarTraining = CanUseForAvatarTraining(faceModel, motionModel, subjectGate);
        package.TrainingDecision = BuildTrainingDecision(package, subjectGate, readiness);
        package.PoseCoverageProfile = readiness.PoseBuckets.Count > 0
            ? readiness.PoseBuckets
            : faceModel.PoseBuckets;

        AddNeutralFaceProfile(package, faceModel);
        AddMotionProfile(package, motionModel);
        AddIdentityProfile(package, faceModel);
        AddContourShapeProfiles(package, faceModel);
        AddQualityProfile(package, faceModel, motionModel, readiness);
        AddSourceArtifacts(package);
        AddGuidance(package, readiness, motionModel);
        return package;
    }

    private static bool CanUseForAvatarTraining(
        PersonalFaceModel faceModel,
        PersonalFaceMotionModel motionModel,
        FaceReconstructionSubjectGate subjectGate)
    {
        return string.Equals(subjectGate.GateDecision, "accepted", StringComparison.OrdinalIgnoreCase)
            && faceModel.AcceptedSamples > 0
            && motionModel.UsableObservationCount > 0;
    }

    private static string BuildTrainingDecision(
        MeasurementAvatarTrainingPackage package,
        FaceReconstructionSubjectGate subjectGate,
        PersonalFaceCorpusReadiness readiness)
    {
        if (!string.Equals(subjectGate.GateDecision, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            return $"paused by subject gate: {subjectGate.Reason}";
        }

        if (package.AcceptedBaselineSamples <= 0)
        {
            return "waiting for accepted subject-confirmed baseline measurements";
        }

        if (package.MotionUsableObservations <= 0)
        {
            return "neutral profile available; motion profile waiting for accepted measurement pairs";
        }

        return readiness.OverallReadinessPercent switch
        {
            >= 85d => "strong measurement package for avatar and motion consumers",
            >= 70d => "useful measurement package; continue broadening pose, distance, and expression coverage",
            >= 45d => "warming measurement package; useful for preview and early integration only",
            _ => "early measurement package; collect more subject-confirmed data before avatar fitting"
        };
    }

    private static void AddNeutralFaceProfile(MeasurementAvatarTrainingPackage package, PersonalFaceModel model)
    {
        Add(package.NeutralFaceProfile, "FaceCenterX", "Face center X", "normalized frame ratio", "Keeps expected desk position separate from face shape.", model.FaceCenterX);
        Add(package.NeutralFaceProfile, "FaceCenterY", "Face center Y", "normalized frame ratio", "Keeps expected desk position separate from face shape.", model.FaceCenterY);
        Add(package.NeutralFaceProfile, "FaceWidth", "Face width", "normalized frame ratio", "Seeds normalized face scale and distance changes.", model.FaceWidth);
        Add(package.NeutralFaceProfile, "FaceHeight", "Face height", "normalized frame ratio", "Seeds normalized face scale and distance changes.", model.FaceHeight);
        Add(package.NeutralFaceProfile, "HeadYawDegrees", "Head yaw", "degrees", "Seeds neutral pose and pose range.", model.HeadYawDegrees);
        Add(package.NeutralFaceProfile, "HeadPitchDegrees", "Head pitch", "degrees", "Seeds neutral pose and pose range.", model.HeadPitchDegrees);
        Add(package.NeutralFaceProfile, "HeadRollDegrees", "Head roll", "degrees", "Seeds neutral pose and pose range.", model.HeadRollDegrees);
        Add(package.NeutralFaceProfile, "LeftEyeOpeningRatio", "Left eye opening", "eye height / eye width", "Seeds eyelid aperture for the left eye.", model.LeftEyeOpeningRatio);
        Add(package.NeutralFaceProfile, "RightEyeOpeningRatio", "Right eye opening", "eye height / eye width", "Seeds eyelid aperture for the right eye.", model.RightEyeOpeningRatio);
        Add(package.NeutralFaceProfile, "AverageEyeOpeningRatio", "Average eye opening", "eye height / eye width", "Primary neutral eyelid aperture signal.", model.AverageEyeOpeningRatio);
        Add(package.NeutralFaceProfile, "EyeAgreementPercent", "Eye agreement", "percent", "Flags whether left/right eyelid evidence is balanced.", model.EyeAgreementPercent);
        Add(package.NeutralFaceProfile, "MouthOpeningRatio", "Mouth opening", "mouth height / mouth width", "Seeds neutral lip aperture.", model.MouthOpeningRatio);
        Add(package.NeutralFaceProfile, "JawDroopRatio", "Jaw droop", "jaw offset / face width", "Seeds jaw relaxation and droop reference.", model.JawDroopRatio);
        Add(package.NeutralFaceProfile, "MediaPipeAverageEyeBlinkPercent", "MediaPipe blink", "percent", "Dense-landmark corroboration for eyelid closure.", model.MediaPipeAverageEyeBlinkPercent);
        Add(package.NeutralFaceProfile, "MediaPipeJawOpenPercent", "MediaPipe jaw open", "percent", "Dense-landmark corroboration for jaw opening.", model.MediaPipeJawOpenPercent);
        Add(package.NeutralFaceProfile, "MediaPipeMouthClosePercent", "MediaPipe mouth close", "percent", "Dense-landmark corroboration for closed-mouth state.", model.MediaPipeMouthClosePercent);
    }

    private static void AddMotionProfile(MeasurementAvatarTrainingPackage package, PersonalFaceMotionModel model)
    {
        Add(package.MotionProfile, "EyeClosingVelocityPerSecond", "Eye closing velocity", "ratio / second", "Animation prior for eyelids closing over time.", model.EyeClosingVelocityPerSecond);
        Add(package.MotionProfile, "EyeOpeningVelocityPerSecond", "Eye opening velocity", "ratio / second", "Animation prior for eyelid recovery.", model.EyeOpeningVelocityPerSecond);
        Add(package.MotionProfile, "MouthOpeningVelocityPerSecond", "Mouth opening velocity", "ratio / second", "Animation prior for lip aperture opening.", model.MouthOpeningVelocityPerSecond);
        Add(package.MotionProfile, "MouthClosingVelocityPerSecond", "Mouth closing velocity", "ratio / second", "Animation prior for lip aperture recovery.", model.MouthClosingVelocityPerSecond);
        Add(package.MotionProfile, "JawDroopVelocityPerSecond", "Jaw droop velocity", "ratio / second", "Animation prior for jaw relaxation/droop onset.", model.JawDroopVelocityPerSecond);
        Add(package.MotionProfile, "JawRecoveryVelocityPerSecond", "Jaw recovery velocity", "ratio / second", "Animation prior for jaw recovery.", model.JawRecoveryVelocityPerSecond);
        Add(package.MotionProfile, "HeadYawVelocityDegreesPerSecond", "Head yaw velocity", "degrees / second", "Animation prior for side-to-side head motion.", model.HeadYawVelocityDegreesPerSecond);
        Add(package.MotionProfile, "HeadPitchVelocityDegreesPerSecond", "Head pitch velocity", "degrees / second", "Animation prior for nodding head motion.", model.HeadPitchVelocityDegreesPerSecond);
        Add(package.MotionProfile, "HeadRollVelocityDegreesPerSecond", "Head roll velocity", "degrees / second", "Animation prior for head roll motion.", model.HeadRollVelocityDegreesPerSecond);
        AddValue(package.MotionProfile, "EyeClosingWithMouthOpeningRate", "Eye closing with mouth opening", "rate", "Coupling hint for sleepy facial state transitions.", model.EyeClosingWithMouthOpeningRate, model.MotionPairCount);
        AddValue(package.MotionProfile, "EyeClosingWithJawDroopRate", "Eye closing with jaw droop", "rate", "Coupling hint for eyelid closure with jaw relaxation.", model.EyeClosingWithJawDroopRate, model.MotionPairCount);
        AddValue(package.MotionProfile, "MouthOpeningWithJawDroopRate", "Mouth opening with jaw droop", "rate", "Coupling hint for lip opening with jaw relaxation.", model.MouthOpeningWithJawDroopRate, model.MotionPairCount);
    }

    private static void AddIdentityProfile(MeasurementAvatarTrainingPackage package, PersonalFaceModel model)
    {
        Add(package.IdentityProfile, "FaceAspectRatio", "Face aspect ratio", "face height / face width", "Measurement-only owner signature; helps avoid mixed-person learning.", model.FaceAspectRatio);
        Add(package.IdentityProfile, "InterEyeDistanceToFaceWidth", "Eye spacing", "inter-eye distance / face width", "Measurement-only owner signature; helps avoid mixed-person learning.", model.InterEyeDistanceToFaceWidth);
        Add(package.IdentityProfile, "LeftEyeWidthToFaceWidth", "Left eye width", "eye width / face width", "Measurement-only owner signature; helps avoid mixed-person learning.", model.LeftEyeWidthToFaceWidth);
        Add(package.IdentityProfile, "RightEyeWidthToFaceWidth", "Right eye width", "eye width / face width", "Measurement-only owner signature; helps avoid mixed-person learning.", model.RightEyeWidthToFaceWidth);
        Add(package.IdentityProfile, "MouthWidthToFaceWidth", "Mouth width", "mouth width / face width", "Measurement-only owner signature; helps avoid mixed-person learning.", model.MouthWidthToFaceWidth);
        Add(package.IdentityProfile, "EyeMidlineYToFaceHeight", "Eye midline height", "eye midline Y / face height", "Measurement-only owner signature; helps avoid mixed-person learning.", model.EyeMidlineYToFaceHeight);
        Add(package.IdentityProfile, "MouthCenterYToFaceHeight", "Mouth center height", "mouth center Y / face height", "Measurement-only owner signature; helps avoid mixed-person learning.", model.MouthCenterYToFaceHeight);
        Add(package.IdentityProfile, "EyeToMouthYDistanceToFaceHeight", "Eye-to-mouth distance", "vertical distance / face height", "Measurement-only owner signature; helps avoid mixed-person learning.", model.EyeToMouthYDistanceToFaceHeight);
    }

    private static void AddContourShapeProfiles(MeasurementAvatarTrainingPackage package, PersonalFaceModel model)
    {
        AddContourShapeProfile(package, model.LeftEyeShape);
        AddContourShapeProfile(package, model.RightEyeShape);
        AddContourShapeProfile(package, model.OuterLipShape);
        AddContourShapeProfile(package, model.InnerLipShape);
        AddContourShapeProfile(package, model.JawShape);
    }

    private static void AddContourShapeProfile(MeasurementAvatarTrainingPackage package, PersonalFaceContourShapeProfile profile)
    {
        if (profile.HasProfile)
        {
            package.ContourShapeProfiles[profile.FeatureId] = profile;
        }
    }

    private static void AddQualityProfile(
        MeasurementAvatarTrainingPackage package,
        PersonalFaceModel faceModel,
        PersonalFaceMotionModel motionModel,
        PersonalFaceCorpusReadiness readiness)
    {
        AddValue(package.QualityProfile, "AverageFaceReliabilityPercent", "Face reliability", "percent", "Overall lock quality for accepted measurements.", faceModel.AverageFaceReliabilityPercent, faceModel.AcceptedSamples);
        AddValue(package.QualityProfile, "AverageFaceContinuityPercent", "Face continuity", "percent", "Temporal face lock continuity.", faceModel.AverageFaceContinuityPercent, faceModel.AcceptedSamples);
        AddValue(package.QualityProfile, "AverageEyeReliabilityPercent", "Eye reliability", "percent", "Eyelid measurement reliability.", faceModel.AverageEyeReliabilityPercent, faceModel.AcceptedSamples);
        AddValue(package.QualityProfile, "AverageMouthReliabilityPercent", "Mouth reliability", "percent", "Lip/jaw measurement reliability.", faceModel.AverageMouthReliabilityPercent, faceModel.AcceptedSamples);
        AddValue(package.QualityProfile, "LearningAnchorPercent", "Learning anchor", "percent", "How strongly the slow weighted model is anchored by accumulated accepted measurement weight.", faceModel.LearningStability.AnchorPercent, faceModel.AcceptedSamples);
        AddValue(package.QualityProfile, "MaximumNextSampleInfluencePercent", "Maximum next-sample influence", "percent", "Upper bound on how much one new high-quality measurement can move the weighted profile.", faceModel.LearningStability.MaximumNextSampleInfluencePercent, faceModel.AcceptedSamples);
        AddValue(package.QualityProfile, "MaximumEventLikeNextSampleInfluencePercent", "Maximum event-like influence", "percent", "Upper bound on how much one accepted event-like measurement can move the weighted profile.", faceModel.LearningStability.MaximumEventLikeNextSampleInfluencePercent, faceModel.AcceptedSamples);
        AddValue(package.QualityProfile, "AverageObservationQualityPercent", "Motion observation quality", "percent", "Quality of observations used by the motion model.", motionModel.AverageObservationQualityPercent, motionModel.UsableObservationCount);
        AddValue(package.QualityProfile, "PoseBucketCoveragePercent", "Pose bucket coverage", "percent", "How much straight-on, yaw, pitch, and roll evidence exists without mixing all poses into one identity average.", readiness.PoseBucketCoveragePercent, readiness.PoseBucketRequiredCount);
        AddValue(package.QualityProfile, "CaptureQualityCoveragePercent", "Capture quality coverage", "percent", "How often recent measurements pass the camera/face/glasses/storage gate.", readiness.CaptureQualityCoveragePercent, readiness.CaptureQualitySamples);
        AddValue(package.QualityProfile, "AverageCaptureQualityScorePercent", "Average capture quality", "percent", "Average capture-quality score for recent measurement rows.", readiness.AverageCaptureQualityScorePercent, readiness.CaptureQualitySamples);
        AddValue(package.QualityProfile, "MinimumCaptureQualityScorePercent", "Minimum capture quality", "percent", "Weakest recent capture-quality score; useful for finding unstable sessions.", readiness.MinimumCaptureQualityScorePercent, readiness.CaptureQualitySamples);
        AddValue(package.QualityProfile, "CaptureQualityCanCollectRate", "Capture collectable rate", "rate", "Fraction of recent measurement rows allowed into long-term learning.", readiness.CaptureQualityCanCollectRate, readiness.CaptureQualitySamples);
        AddValue(package.QualityProfile, "CaptureQualityAvatarGradeRate", "Capture avatar-grade rate", "rate", "Fraction of recent measurement rows strong enough for avatar learning.", readiness.CaptureQualityAvatarGradeRate, readiness.CaptureQualitySamples);
        AddValue(package.QualityProfile, "AverageCaptureQualityEyeScorePercent", "Capture eye evidence", "percent", "Average eye-evidence score inside the capture-quality gate.", readiness.AverageCaptureQualityEyeScorePercent, readiness.CaptureQualitySamples);
        AddValue(package.QualityProfile, "AverageCaptureQualityMouthScorePercent", "Capture mouth evidence", "percent", "Average mouth/jaw evidence score inside the capture-quality gate.", readiness.AverageCaptureQualityMouthScorePercent, readiness.CaptureQualitySamples);
        AddValue(package.QualityProfile, "AverageCaptureQualityGlassesScorePercent", "Capture glasses score", "percent", "Average glasses/artifact score inside the capture-quality gate.", readiness.AverageCaptureQualityGlassesScorePercent, readiness.CaptureQualitySamples);
        Add(package.QualityProfile, "EyeGlarePercent", "Eye glare", "percent", "Glasses glare context for eyelid accuracy.", faceModel.EyeGlarePercent);
        Add(package.QualityProfile, "EyeContrastPercent", "Eye contrast", "percent", "Eye-region contrast context for eyelid accuracy.", faceModel.EyeContrastPercent);
        Add(package.QualityProfile, "EyeSharpnessPercent", "Eye sharpness", "percent", "Eye-region sharpness context for eyelid accuracy.", faceModel.EyeSharpnessPercent);
        AddValue(package.QualityProfile, "EyeArtifactSuppressedRate", "Eye artifact suppressed", "rate", "How often the tracker suppressed likely glasses/contour artifacts.", readiness.EyeArtifactSuppressedRate, faceModel.AcceptedSamples);
        AddValue(package.QualityProfile, "EyeReconstructedRate", "Eye reconstructed", "rate", "How often temporal reconstruction filled one or both eyes.", readiness.EyeReconstructedRate, faceModel.AcceptedSamples);
        AddValue(package.QualityProfile, "MouthReconstructedRate", "Mouth reconstructed", "rate", "How often temporal reconstruction filled mouth evidence.", readiness.MouthReconstructedRate, faceModel.AcceptedSamples);
    }

    private static void AddSourceArtifacts(MeasurementAvatarTrainingPackage package)
    {
        package.SourceArtifacts.AddRange([
            Artifact("Personal face model", "personal_face_model.json", "personal-model", "Weighted neutral face, identity, quality, and acceptance distributions."),
            Artifact("Motion model", "personal_face_motion_model.json", "motion-model", "Velocity and coupling distributions for facial movement."),
            Artifact("Learning data health", "personal_face_corpus_readiness.json", "readiness-report", "Coverage scores, warnings, and next capture suggestions."),
            Artifact("Measurement preview", "measurement_face_preview.json", "wireframe-preview", "Normalized measurement-only face preview geometry."),
            Artifact("Canonical face seed prior", "built-in", "low-trust-template-prior", "Temporary scaffold for early preview topology; never counted as observed personal measurements."),
            Artifact("Avatar capture plan", "measurement_avatar_capture_plan.json", "capture-plan", "Subject-gated measurement-only plan for the next data collection session."),
            Artifact("Measurement journal", "measurements/*.jsonl", "accepted-measurement-journal", "Append-only accepted measurement records, bounded by the passive learning budget."),
            Artifact("Aggregate contour shape profiles", "personal_face_model.json", "shape-profile", "Weighted face-local eye, lip, and jaw shape distributions; not per-frame contours."),
            Artifact("Pose bucket profiles", "personal_face_model.json", "pose-profile", "Straight-on, yaw, pitch, and roll measurement buckets so neutral identity and turned-head motion are not averaged together.")
        ]);
    }

    private static void AddGuidance(
        MeasurementAvatarTrainingPackage package,
        PersonalFaceCorpusReadiness readiness,
        PersonalFaceMotionModel motionModel)
    {
        package.Strengths.AddRange(readiness.Strengths);
        package.Warnings.AddRange(readiness.Warnings);
        package.NextCaptureSuggestions.AddRange(readiness.NextCaptureSuggestions);
        package.IntegrationNotes.Add("Use the neutral face profile to seed a measurement-only face rig before photoreal reconstruction exists.");
        package.IntegrationNotes.Add("Prefer the front-neutral pose bucket for identity and neutral geometry; use yaw/pitch/roll buckets for pose-aware correction and animation coverage.");
        package.IntegrationNotes.Add($"Template prior contribution: {package.TemplatePriorContributionPercent:0.#}%; measured contribution: {package.MeasurementContributionPercent:0.#}%. Treat template prior as visual scaffolding, not subject evidence.");
        package.IntegrationNotes.Add("Use contour shape profiles to seed eyelid, lip, and jaw outline controls; they are aggregate distributions, not raw frame landmarks.");
        package.IntegrationNotes.Add("Use the motion profile as a slow-changing animation prior; do not treat one new session as an instant identity or motion rewrite.");
        package.IntegrationNotes.Add($"Learning stability: {readiness.SubjectDisplayName} model is {package.LearningStability.AnchorStatus}; one new stable measurement can influence the profile by at most {package.LearningStability.MaximumNextSampleInfluencePercent:0.##}%.");
        package.IntegrationNotes.Add("Use the identity profile only to decide whether learning should continue for the enrolled subject; it is not a public identity credential.");
        package.IntegrationNotes.Add("Any future photoreal worker should request explicit training media separately and keep that media outside passive learning.");

        if (motionModel.Warnings.Count > 0)
        {
            foreach (var warning in motionModel.Warnings.Take(3))
            {
                if (!package.Warnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
                {
                    package.Warnings.Add(warning);
                }
            }
        }
    }

    private static MeasurementAvatarTrainingArtifact Artifact(
        string name,
        string fileName,
        string kind,
        string description)
    {
        return new MeasurementAvatarTrainingArtifact
        {
            Name = name,
            FileName = fileName,
            Kind = kind,
            Description = description,
            ContainsRawPixels = false,
            ContainsRawContinuousVideo = false
        };
    }

    private static void Add(
        IDictionary<string, MeasurementAvatarTrainingMetric> target,
        string key,
        string label,
        string units,
        string avatarUse,
        PersonalMetricDistribution distribution)
    {
        target[key] = new MeasurementAvatarTrainingMetric
        {
            Label = label,
            Units = units,
            AvatarUse = avatarUse,
            SampleCount = distribution.SampleCount,
            TotalWeight = Round(distribution.TotalWeight),
            Average = Round(distribution.Average),
            Minimum = Round(distribution.Minimum),
            Maximum = Round(distribution.Maximum),
            StandardDeviation = Round(distribution.StandardDeviation),
            ExponentialMovingAverage = Round(distribution.ExponentialMovingAverage),
            NormalLow = Round(distribution.NormalLow),
            NormalHigh = Round(distribution.NormalHigh)
        };
    }

    private static void AddValue(
        IDictionary<string, MeasurementAvatarTrainingMetric> target,
        string key,
        string label,
        string units,
        string avatarUse,
        double? value,
        int sampleCount)
    {
        target[key] = new MeasurementAvatarTrainingMetric
        {
            Label = label,
            Units = units,
            AvatarUse = avatarUse,
            SampleCount = Math.Max(0, sampleCount),
            TotalWeight = Math.Max(0, sampleCount),
            Average = Round(value),
            Minimum = Round(value),
            Maximum = Round(value),
            ExponentialMovingAverage = Round(value),
            NormalLow = Round(value),
            NormalHigh = Round(value)
        };
    }

    private static DateTime Max(params DateTime[] values)
    {
        return values
            .Where(static value => value != default)
            .DefaultIfEmpty(DateTime.UtcNow)
            .Max();
    }

    private static double? Round(double? value)
    {
        return value is double number ? Round(number) : null;
    }

    private static double Round(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0d
            : Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    private static double CalculateMeasurementContribution(PersonalFaceModel model)
    {
        if (model.AcceptedSamples <= 0 || model.AcceptedSampleWeight <= 0d)
        {
            return 0d;
        }

        var sampleContribution = Math.Clamp(model.AcceptedSamples / 60d * 55d, 0d, 55d);
        var weightContribution = Math.Clamp(model.AcceptedSampleWeight / 90d * 25d, 0d, 25d);
        var contourContribution = new[]
        {
            model.LeftEyeShape.HasProfile,
            model.RightEyeShape.HasProfile,
            model.OuterLipShape.HasProfile,
            model.InnerLipShape.HasProfile,
            model.JawShape.HasProfile
        }.Count(static hasProfile => hasProfile) / 5d * 20d;
        return Round(Math.Clamp(sampleContribution + weightContribution + contourContribution, 0d, 100d));
    }
}
