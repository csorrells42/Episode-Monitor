namespace EpisodeMonitor.Modules.Vision.Personalization;

public sealed class PersonalFaceCorpusReadinessBuilder
{
    private const double StrongBaselineSampleCount = 360d;
    private const double StrongMotionPairCount = 480d;
    private const double StrongFaceSizeRange = 0.18d;
    private const double StrongHeadYawRangeDegrees = 32d;
    private const double StrongHeadPitchRangeDegrees = 18d;
    private const double StrongHeadRollRangeDegrees = 14d;
    private const double StrongPoseBucketSampleCount = 45d;
    private const double StrongPoseBucketWeight = 36d;
    private const double StrongEyeOpeningRange = 0.16d;
    private const double StrongMouthOpeningRange = 0.22d;
    private const double StrongJawDroopRange = 0.14d;
    private const double StrongBlendshapeRangePercent = 55d;
    private const double StrongIdentitySignatureSampleCount = 240d;
    private const double StrongContourShapeSampleCount = 180d;
    private const double StrongCaptureQualityScorePercent = 86d;
    private const double UsefulMaximumNextSampleInfluencePercent = 5d;
    private const double StrongMaximumNextSampleInfluencePercent = 2d;

    public PersonalFaceCorpusReadiness Build(
        PersonalFaceModel faceModel,
        PersonalFaceMotionModel motionModel,
        IReadOnlyList<PersonalFaceMeasurementSample> recentSamples,
        long measurementJournalBytes,
        long measurementBudgetBytes = PersonalFaceMeasurementJournal.DefaultBudgetBytes)
    {
        var model = new PersonalFaceCorpusReadiness
        {
            SubjectId = faceModel.SubjectId,
            SubjectDisplayName = faceModel.SubjectDisplayName,
            SubjectCollectionMode = faceModel.SubjectCollectionMode,
            UnknownSubjectPolicy = faceModel.UnknownSubjectPolicy,
            CreatedAtUtc = faceModel.CreatedAtUtc != default ? faceModel.CreatedAtUtc : motionModel.CreatedAtUtc,
            UpdatedAtUtc = Max(faceModel.UpdatedAtUtc, motionModel.UpdatedAtUtc),
            AcceptedBaselineSamples = faceModel.AcceptedSamples,
            RecentMeasurementSamplesReviewed = recentSamples.Count,
            MotionUsableObservations = motionModel.UsableObservationCount,
            MotionPairs = motionModel.MotionPairCount,
            IdentitySignatureSamples = faceModel.IdentitySignatureSamples,
            LeftEyeShapeSamples = faceModel.LeftEyeShape.SampleCount,
            RightEyeShapeSamples = faceModel.RightEyeShape.SampleCount,
            OuterLipShapeSamples = faceModel.OuterLipShape.SampleCount,
            InnerLipShapeSamples = faceModel.InnerLipShape.SampleCount,
            JawShapeSamples = faceModel.JawShape.SampleCount,
            MeasurementJournalBytes = Math.Max(0L, measurementJournalBytes),
            MeasurementBudgetBytes = Math.Max(1L, measurementBudgetBytes)
        };

        model.MeasurementBudgetUsedPercent = Round(100d * model.MeasurementJournalBytes / model.MeasurementBudgetBytes);
        model.FaceWidthRange = Range(faceModel.FaceWidth);
        model.FaceHeightRange = Range(faceModel.FaceHeight);
        model.HeadYawRangeDegrees = Range(faceModel.HeadYawDegrees);
        model.HeadPitchRangeDegrees = Range(faceModel.HeadPitchDegrees);
        model.HeadRollRangeDegrees = Range(faceModel.HeadRollDegrees);
        model.EyeOpeningRange = Range(faceModel.AverageEyeOpeningRatio);
        model.MouthOpeningRange = Range(faceModel.MouthOpeningRatio);
        model.JawDroopRange = Range(faceModel.JawDroopRatio);
        model.MediaPipeBlinkRangePercent = Range(faceModel.MediaPipeAverageEyeBlinkPercent);
        model.MediaPipeJawOpenRangePercent = Range(faceModel.MediaPipeJawOpenPercent);
        model.FaceAspectRatioRange = Range(faceModel.FaceAspectRatio);
        model.InterEyeDistanceToFaceWidthRange = Range(faceModel.InterEyeDistanceToFaceWidth);
        model.MouthWidthToFaceWidthRange = Range(faceModel.MouthWidthToFaceWidth);
        model.EyeArtifactSuppressedRate = Rate(faceModel.EyeArtifactSuppressedSamples, faceModel.AcceptedSamples);
        model.EyeReconstructedRate = Rate(faceModel.LeftEyeReconstructedSamples + faceModel.RightEyeReconstructedSamples, Math.Max(1, faceModel.AcceptedSamples * 2));
        model.MouthReconstructedRate = Rate(faceModel.MouthReconstructedSamples, faceModel.AcceptedSamples);
        model.LearningAnchorPercent = Round(faceModel.LearningStability.AnchorPercent);
        model.LearningAnchorStatus = faceModel.LearningStability.AnchorStatus;
        model.MaximumNextSampleInfluencePercent = Round(faceModel.LearningStability.MaximumNextSampleInfluencePercent);
        model.MaximumEventLikeNextSampleInfluencePercent = Round(faceModel.LearningStability.MaximumEventLikeNextSampleInfluencePercent);
        model.PoseBuckets = NormalizePoseBuckets(faceModel.PoseBuckets);
        model.PoseBucketRequiredCount = model.PoseBuckets.Count(static bucket => bucket.RequiredForAvatarCoverage);
        model.PoseBucketCoveredCount = model.PoseBuckets.Count(static bucket => bucket.RequiredForAvatarCoverage && bucket.SampleCount > 0);
        model.PoseBucketCoveragePercent = ScorePoseBucketCoverage(model.PoseBuckets);
        UpdateCaptureQuality(model, recentSamples);

        model.BaselineCoveragePercent = ScoreCount(faceModel.AcceptedSamples, StrongBaselineSampleCount);
        model.LearningStabilityCoveragePercent = LearningStabilityScore(faceModel.LearningStability);
        model.MotionCoveragePercent = ScoreCount(motionModel.MotionPairCount, StrongMotionPairCount);
        var poseRangeCoverage = Average(
            ScoreRange(model.HeadYawRangeDegrees, StrongHeadYawRangeDegrees),
            ScoreRange(model.HeadPitchRangeDegrees, StrongHeadPitchRangeDegrees),
            ScoreRange(model.HeadRollRangeDegrees, StrongHeadRollRangeDegrees));
        model.PoseCoveragePercent = Round(poseRangeCoverage * 0.55d + model.PoseBucketCoveragePercent * 0.45d);
        model.DistanceCoveragePercent = Average(
            ScoreRange(model.FaceWidthRange, StrongFaceSizeRange),
            ScoreRange(model.FaceHeightRange, StrongFaceSizeRange));
        model.ExpressionCoveragePercent = Average(
            ScoreRange(model.EyeOpeningRange, StrongEyeOpeningRange),
            ScoreRange(model.MouthOpeningRange, StrongMouthOpeningRange),
            ScoreRange(model.JawDroopRange, StrongJawDroopRange),
            ScoreOptionalRange(model.MediaPipeBlinkRangePercent, StrongBlendshapeRangePercent, fallback: 45d),
            ScoreOptionalRange(model.MediaPipeJawOpenRangePercent, StrongBlendshapeRangePercent, fallback: 45d));
        model.IdentityCoveragePercent = Average(
            ScoreCount(faceModel.IdentitySignatureSamples, StrongIdentitySignatureSampleCount),
            ScoreIdentityDistribution(faceModel.FaceAspectRatio),
            ScoreIdentityDistribution(faceModel.InterEyeDistanceToFaceWidth),
            ScoreIdentityDistribution(faceModel.MouthWidthToFaceWidth),
            ScoreIdentityDistribution(faceModel.EyeMidlineYToFaceHeight),
            ScoreIdentityDistribution(faceModel.MouthCenterYToFaceHeight));
        model.ContourShapeCoveragePercent = Average(
            ScoreContourShapeProfile(faceModel.LeftEyeShape),
            ScoreContourShapeProfile(faceModel.RightEyeShape),
            ScoreContourShapeProfile(faceModel.OuterLipShape),
            ScoreContourShapeProfile(faceModel.InnerLipShape),
            ScoreContourShapeProfile(faceModel.JawShape));
        model.EyeBehindGlassesTrustPercent = ScoreEyeBehindGlassesTrust(faceModel, model);
        model.MouthJawTrustPercent = ScoreMouthJawTrust(faceModel, model);
        model.DirectFeatureMeasurementTrustPercent = Round(
            model.EyeBehindGlassesTrustPercent * 0.52d
            + model.MouthJawTrustPercent * 0.38d
            + model.ContourShapeCoveragePercent * 0.10d);
        model.QualityCoveragePercent = QualityScore(faceModel, motionModel, model);
        model.CaptureQualityCoveragePercent = CaptureQualityScore(model);
        model.StorageHealthPercent = StorageScore(model.MeasurementBudgetUsedPercent);
        model.OverallReadinessPercent = Round(
            model.BaselineCoveragePercent * 0.11d
            + model.LearningStabilityCoveragePercent * 0.06d
            + model.MotionCoveragePercent * 0.15d
            + model.PoseCoveragePercent * 0.11d
            + model.DistanceCoveragePercent * 0.08d
            + model.ExpressionCoveragePercent * 0.15d
            + model.IdentityCoveragePercent * 0.07d
            + model.ContourShapeCoveragePercent * 0.05d
            + model.DirectFeatureMeasurementTrustPercent * 0.10d
            + model.QualityCoveragePercent * 0.06d
            + model.CaptureQualityCoveragePercent * 0.04d
            + model.StorageHealthPercent * 0.02d);

        AddStrengths(model);
        AddWarningsAndSuggestions(model, faceModel, motionModel);
        return model;
    }

    private static void AddStrengths(PersonalFaceCorpusReadiness model)
    {
        if (model.BaselineCoveragePercent >= 70d)
        {
            model.Strengths.Add("Baseline sample count is becoming useful for stable per-person averages.");
        }

        if (model.LearningStabilityCoveragePercent >= 70d)
        {
            model.Strengths.Add("Learning stability is becoming useful; individual new samples have limited influence on the personal model.");
        }

        if (model.MotionCoveragePercent >= 70d)
        {
            model.Strengths.Add("Motion-pair coverage is becoming useful for animation timing and facial movement trends.");
        }

        if (model.ExpressionCoveragePercent >= 70d)
        {
            model.Strengths.Add("Eye, mouth, and jaw ranges are broad enough to start characterizing facial movement.");
        }

        if (model.IdentityCoveragePercent >= 70d)
        {
            model.Strengths.Add("Measurement-only identity signature is becoming useful for avoiding mixed-person learning.");
        }

        if (model.ContourShapeCoveragePercent >= 70d)
        {
            model.Strengths.Add("Aggregate eye, lip, and jaw shape profiles are becoming useful for avatar outline controls.");
        }

        if (model.DirectFeatureMeasurementTrustPercent >= 70d)
        {
            model.Strengths.Add("Direct eye and mouth/jaw measurement trust is becoming useful for high-accuracy avatar fitting.");
        }

        if (model.QualityCoveragePercent >= 75d)
        {
            model.Strengths.Add("Recent measurements are high quality enough for long-term weighting.");
        }

        if (model.CaptureQualityCoveragePercent >= 75d)
        {
            model.Strengths.Add("Capture-quality gates are passing often enough for long-term measurement collection.");
        }
    }

    private static void AddWarningsAndSuggestions(
        PersonalFaceCorpusReadiness model,
        PersonalFaceModel faceModel,
        PersonalFaceMotionModel motionModel)
    {
        if (model.AcceptedBaselineSamples < 60)
        {
            model.Warnings.Add("The corpus is early: fewer than 60 accepted awake baseline samples.");
            model.NextCaptureSuggestions.Add("Collect several alert, symptom-free sessions with the subject confirmation checkbox enabled.");
        }

        if (model.LearningAnchorPercent < 50d || model.MaximumNextSampleInfluencePercent > UsefulMaximumNextSampleInfluencePercent)
        {
            model.Warnings.Add($"The personal model is still weakly anchored: anchor {model.LearningAnchorPercent:0.#}%, one new stable sample can move the weighted profile by up to {model.MaximumNextSampleInfluencePercent:0.#}%.");
            model.NextCaptureSuggestions.Add($"Continue subject-confirmed alert sessions until max next-sample influence is below {UsefulMaximumNextSampleInfluencePercent:0.#}%; below {StrongMaximumNextSampleInfluencePercent:0.#}% is the stronger avatar target.");
        }

        if (model.MotionPairs < 60)
        {
            model.Warnings.Add("Motion coverage is sparse: fewer than 60 usable motion pairs.");
            model.NextCaptureSuggestions.Add("Record longer subject-confirmed sessions with natural talking, slow blinks, relaxed mouth, and head movement.");
        }

        if (model.DistanceCoveragePercent < 55d)
        {
            model.Warnings.Add("Face size/distance coverage is narrow.");
            model.NextCaptureSuggestions.Add("Capture alert sessions leaning close, normal desk distance, and leaning back so the model sees face scale changes.");
        }

        if (model.PoseCoveragePercent < 55d)
        {
            model.Warnings.Add("Head pose coverage is narrow.");
            model.NextCaptureSuggestions.Add("Capture alert left/right turns, slight up/down tilt, and a few rolled head positions while keeping glasses visible.");
        }

        if (model.PoseBucketCoveragePercent < 55d)
        {
            model.Warnings.Add($"Pose bucket coverage is early: {model.PoseBucketCoveredCount}/{model.PoseBucketRequiredCount} required pose buckets have measurements.");
            foreach (var bucket in model.PoseBuckets
                         .Where(static bucket => bucket.RequiredForAvatarCoverage && bucket.SampleCount < StrongPoseBucketSampleCount)
                         .OrderBy(static bucket => bucket.SampleCount)
                         .Take(3))
            {
                model.NextCaptureSuggestions.Add(bucket.CaptureInstruction);
            }
        }

        if (model.ExpressionCoveragePercent < 55d)
        {
            model.Warnings.Add("Eye/mouth/jaw expression coverage is narrow.");
            model.NextCaptureSuggestions.Add("Capture intentional awake expressions: eyes open/relaxed/slow blink, lips closed/slightly open, speech, and jaw drop.");
        }

        if (model.IdentityCoveragePercent < 55d)
        {
            model.Warnings.Add("Measurement-only identity coverage is early.");
            model.NextCaptureSuggestions.Add("Collect more subject-confirmed alert frames in stable lighting with no face filters so the app can learn a non-image identity signature.");
        }

        if (model.ContourShapeCoveragePercent < 55d)
        {
            model.Warnings.Add("Aggregate eye/lip/jaw contour shape coverage is early.");
            model.NextCaptureSuggestions.Add("Collect direct, high-quality eye, lip, and jaw observations: reduce glasses glare, keep the full lower face visible, and include relaxed closed-mouth plus slightly open-mouth poses.");
        }

        if (model.EyeBehindGlassesTrustPercent < 60d)
        {
            model.Warnings.Add("Eye-behind-glasses trust is early or limited.");
            model.NextCaptureSuggestions.Add("Run a short alert eye pass: reduce monitor reflections, keep both eyes visible behind glasses, and include open, relaxed, and slow-blink eyelid positions.");
        }

        if (model.MouthJawTrustPercent < 60d)
        {
            model.Warnings.Add("Mouth/jaw trust is early or limited.");
            model.NextCaptureSuggestions.Add("Run a short alert mouth/jaw pass with the lower face fully visible: lips closed, slightly open, natural speech, and a gentle jaw drop.");
        }

        if (model.QualityCoveragePercent < 65d)
        {
            model.Warnings.Add("Measurement quality is limiting the corpus.");
            model.NextCaptureSuggestions.Add("Improve lighting, reduce glasses glare, keep the camera at 4K when practical, and avoid face filters/background effects.");
        }

        if (model.CaptureQualitySamples > 0 && model.CaptureQualityCanCollectRate < 0.85d)
        {
            model.Warnings.Add($"Capture-quality gate is rejecting too many recent samples: {model.CaptureQualityCanCollectRate:P0} collectable.");
            model.NextCaptureSuggestions.Add("Use explicit 4K/30fps capture, keep the face large enough in frame, and reduce glasses glare before collecting avatar measurements.");
        }

        if (model.CaptureQualitySamples > 0 && model.CaptureQualityAvatarGradeRate < 0.30d)
        {
            model.Warnings.Add($"Avatar-grade capture coverage is low: {model.CaptureQualityAvatarGradeRate:P0} of recent samples.");
            model.NextCaptureSuggestions.Add("Collect a short high-quality avatar pass with stable lighting, full face visible, glasses glare minimized, and the camera in 4K mode.");
        }

        foreach (var issue in model.CaptureQualityIssueLabels.Take(3))
        {
            model.Warnings.Add($"Capture-quality issue: {issue}.");
        }

        if (model.MeasurementBudgetUsedPercent > 80d)
        {
            model.Warnings.Add("Measurement journal storage is approaching the 10 GB budget.");
            model.NextCaptureSuggestions.Add("Move the output folder to a larger external drive or archive old explicit training media.");
        }

        if (motionModel.Warnings.Count > 0)
        {
            foreach (var warning in motionModel.Warnings.Take(3))
            {
                model.Warnings.Add(warning);
            }
        }

        if (faceModel.MediaPipeAverageEyeBlinkPercent.SampleCount == 0 || faceModel.MediaPipeJawOpenPercent.SampleCount == 0)
        {
            model.NextCaptureSuggestions.Add("Enable the MediaPipe dense landmark sidecar when possible so blendshape evidence can corroborate glasses-heavy eye and mouth contours.");
        }
    }

    private static double QualityScore(
        PersonalFaceModel faceModel,
        PersonalFaceMotionModel motionModel,
        PersonalFaceCorpusReadiness readiness)
    {
        var reliability = Math.Clamp(faceModel.AverageFaceReliabilityPercent, 0d, 100d);
        var continuity = Math.Clamp(faceModel.AverageFaceContinuityPercent, 0d, 100d);
        var motionQuality = Math.Clamp(motionModel.AverageObservationQualityPercent, 0d, 100d);
        if (motionModel.UsableObservationCount <= 0)
        {
            motionQuality = (reliability + continuity) / 2d;
        }

        var artifactPenalty =
            Math.Clamp((readiness.EyeArtifactSuppressedRate ?? 0d) * 35d, 0d, 20d)
            + Math.Clamp((readiness.EyeReconstructedRate ?? 0d) * 30d, 0d, 18d)
            + Math.Clamp((readiness.MouthReconstructedRate ?? 0d) * 25d, 0d, 14d);
        return Round(Math.Clamp(reliability * 0.38d + continuity * 0.27d + motionQuality * 0.35d - artifactPenalty, 0d, 100d));
    }

    private static void UpdateCaptureQuality(
        PersonalFaceCorpusReadiness model,
        IReadOnlyList<PersonalFaceMeasurementSample> recentSamples)
    {
        var samples = recentSamples
            .Where(static sample => !string.IsNullOrWhiteSpace(sample.CaptureQualityLabel) || sample.CaptureQualityScorePercent > 0d)
            .ToList();
        model.CaptureQualitySamples = samples.Count;
        model.CaptureQualityCanCollectSamples = samples.Count(static sample => sample.CaptureQualityCanCollect);
        model.CaptureQualityAvatarGradeSamples = samples.Count(static sample => sample.CaptureQualityAvatarGrade);
        model.CaptureQualityCanCollectRate = RateValue(model.CaptureQualityCanCollectSamples, model.CaptureQualitySamples);
        model.CaptureQualityAvatarGradeRate = RateValue(model.CaptureQualityAvatarGradeSamples, model.CaptureQualitySamples);
        model.AverageCaptureQualityScorePercent = AverageOptional(samples.Select(static sample => (double?)sample.CaptureQualityScorePercent));
        model.MinimumCaptureQualityScorePercent = MinimumOptional(samples.Select(static sample => (double?)sample.CaptureQualityScorePercent));
        model.AverageCaptureQualityCameraModeScorePercent = AverageOptional(samples.Select(static sample => (double?)sample.CaptureQualityCameraModeScorePercent));
        model.AverageCaptureQualityFaceScaleScorePercent = AverageOptional(samples.Select(static sample => (double?)sample.CaptureQualityFaceScaleScorePercent));
        model.AverageCaptureQualityEyeScorePercent = AverageOptional(samples.Select(static sample => (double?)sample.CaptureQualityEyeScorePercent));
        model.AverageCaptureQualityMouthScorePercent = AverageOptional(samples.Select(static sample => (double?)sample.CaptureQualityMouthScorePercent));
        model.AverageCaptureQualityStabilityScorePercent = AverageOptional(samples.Select(static sample => (double?)sample.CaptureQualityStabilityScorePercent));
        model.AverageCaptureQualityGlassesScorePercent = AverageOptional(samples.Select(static sample => (double?)sample.CaptureQualityGlassesScorePercent));
        model.CaptureQualityIssueLabels = samples
            .SelectMany(static sample => sample.CaptureQualityIssues)
            .Where(static issue => !string.IsNullOrWhiteSpace(issue))
            .GroupBy(static issue => issue.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(static group => $"{group.Key} ({group.Count()})")
            .ToList();
    }

    private static double CaptureQualityScore(PersonalFaceCorpusReadiness model)
    {
        if (model.CaptureQualitySamples <= 0)
        {
            return 0d;
        }

        var score = model.AverageCaptureQualityScorePercent is double averageScore
            ? Math.Clamp(averageScore / StrongCaptureQualityScorePercent * 100d, 0d, 100d)
            : 0d;
        var minimum = model.MinimumCaptureQualityScorePercent is double minimumScore
            ? Math.Clamp(minimumScore / 70d * 100d, 0d, 100d)
            : 0d;
        var collectable = Math.Clamp(model.CaptureQualityCanCollectRate * 100d, 0d, 100d);
        var avatarGrade = Math.Clamp(model.CaptureQualityAvatarGradeRate / 0.45d * 100d, 0d, 100d);
        return Round(score * 0.42d + minimum * 0.18d + collectable * 0.26d + avatarGrade * 0.14d);
    }

    private static double StorageScore(double budgetUsedPercent)
    {
        if (budgetUsedPercent <= 65d)
        {
            return 100d;
        }

        if (budgetUsedPercent <= 100d)
        {
            return Round(100d - (budgetUsedPercent - 65d) / 35d * 70d);
        }

        return 10d;
    }

    private static double LearningStabilityScore(PersonalFaceLearningStability stability)
    {
        var anchorScore = Math.Clamp(stability.AnchorPercent, 0d, 100d);
        var influence = Math.Max(0d, stability.MaximumNextSampleInfluencePercent);
        var influenceScore = influence <= StrongMaximumNextSampleInfluencePercent
            ? 100d
            : influence >= 18d
                ? 0d
                : 100d - (influence - StrongMaximumNextSampleInfluencePercent) / 16d * 100d;

        return Round(anchorScore * 0.45d + influenceScore * 0.55d);
    }

    private static double ScoreCount(int count, double strongCount)
    {
        return Round(Math.Clamp(count / strongCount * 100d, 0d, 100d));
    }

    private static double ScoreRange(double? range, double strongRange)
    {
        return range is double value
            ? Round(Math.Clamp(value / strongRange * 100d, 0d, 100d))
            : 0d;
    }

    private static double ScoreOptionalRange(double? range, double strongRange, double fallback)
    {
        return range is double value ? ScoreRange(value, strongRange) : fallback;
    }

    private static double ScoreIdentityDistribution(PersonalMetricDistribution distribution)
    {
        if (distribution.SampleCount <= 0 || distribution.Average is null)
        {
            return 0d;
        }

        var countScore = ScoreCount(distribution.SampleCount, StrongIdentitySignatureSampleCount);
        var stabilityScore = distribution.StandardDeviation is double standardDeviation
            ? Round(Math.Clamp(100d - standardDeviation / 0.08d * 100d, 0d, 100d))
            : countScore * 0.5d;
        return Round(countScore * 0.65d + stabilityScore * 0.35d);
    }

    private static double ScoreContourShapeProfile(PersonalFaceContourShapeProfile profile)
    {
        if (!profile.HasProfile)
        {
            return 0d;
        }

        var countScore = ScoreCount(profile.SampleCount, StrongContourShapeSampleCount);
        var expectedPointCount = Math.Max(2, profile.PointCount);
        var populatedPoints = profile.Points.Count(point =>
            (point.X.Average.HasValue || point.X.ExponentialMovingAverage.HasValue)
            && (point.Y.Average.HasValue || point.Y.ExponentialMovingAverage.HasValue));
        var pointScore = Math.Clamp(populatedPoints / (double)expectedPointCount * 100d, 0d, 100d);
        var weightScore = Math.Clamp(profile.TotalWeight / StrongContourShapeSampleCount * 100d, 0d, 100d);
        return Round(countScore * 0.52d + pointScore * 0.28d + weightScore * 0.20d);
    }

    private static List<PersonalFacePoseBucketProfile> NormalizePoseBuckets(
        IReadOnlyList<PersonalFacePoseBucketProfile>? buckets)
    {
        var byId = (buckets ?? [])
            .Where(static bucket => !string.IsNullOrWhiteSpace(bucket.BucketId))
            .GroupBy(static bucket => bucket.BucketId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        return PersonalFacePoseBuckets.Definitions
            .Select(definition => byId.TryGetValue(definition.BucketId, out var bucket)
                ? bucket
                : EmptyPoseBucket(definition))
            .ToList();
    }

    private static PersonalFacePoseBucketProfile EmptyPoseBucket(PersonalFacePoseBucketDefinition definition)
    {
        return new PersonalFacePoseBucketProfile
        {
            BucketId = definition.BucketId,
            Label = definition.Label,
            Description = definition.Description,
            CaptureInstruction = definition.CaptureInstruction,
            PrimaryNeutralReference = definition.PrimaryNeutralReference,
            RequiredForAvatarCoverage = definition.RequiredForAvatarCoverage
        };
    }

    private static double ScorePoseBucketCoverage(IReadOnlyList<PersonalFacePoseBucketProfile> buckets)
    {
        var required = buckets
            .Where(static bucket => bucket.RequiredForAvatarCoverage)
            .ToList();
        if (required.Count == 0)
        {
            return 0d;
        }

        return Round(required.Average(ScorePoseBucket));
    }

    private static double ScorePoseBucket(PersonalFacePoseBucketProfile bucket)
    {
        var sampleScore = ScoreCount(bucket.SampleCount, StrongPoseBucketSampleCount);
        var weightScore = Round(Math.Clamp(bucket.TotalWeight / StrongPoseBucketWeight * 100d, 0d, 100d));
        var identityScore = bucket.HasIdentityProfile
            ? Average(
                ScoreIdentityDistribution(bucket.FaceAspectRatio),
                ScoreIdentityDistribution(bucket.InterEyeDistanceToFaceWidth),
                ScoreIdentityDistribution(bucket.MouthWidthToFaceWidth))
            : 0d;
        var reliability = Average(
            Math.Clamp(bucket.AverageFaceReliabilityPercent, 0d, 100d),
            Math.Clamp(bucket.AverageEyeReliabilityPercent, 0d, 100d),
            Math.Clamp(bucket.AverageMouthReliabilityPercent, 0d, 100d));
        if (bucket.SampleCount <= 0)
        {
            reliability = 0d;
        }

        return Round(sampleScore * 0.40d + weightScore * 0.25d + identityScore * 0.20d + reliability * 0.15d);
    }

    private static double ScoreEyeBehindGlassesTrust(
        PersonalFaceModel faceModel,
        PersonalFaceCorpusReadiness readiness)
    {
        var leftShape = ScoreContourShapeProfile(faceModel.LeftEyeShape);
        var rightShape = ScoreContourShapeProfile(faceModel.RightEyeShape);
        var shapeScore = Average(leftShape, rightShape);
        var eyeEvidence = ScoreOptionalPercent(readiness.AverageCaptureQualityEyeScorePercent, fallback: faceModel.AverageEyeReliabilityPercent);
        var glassesEvidence = ScoreOptionalPercent(readiness.AverageCaptureQualityGlassesScorePercent, fallback: 62d);
        var directEyeRate = Average(
            ScoreDirectRate(readiness.EyeReconstructedRate),
            ScoreDirectRate(readiness.EyeArtifactSuppressedRate));
        var openingRangeScore = ScoreRange(readiness.EyeOpeningRange, StrongEyeOpeningRange);

        return Round(
            shapeScore * 0.28d
            + eyeEvidence * 0.24d
            + glassesEvidence * 0.18d
            + directEyeRate * 0.18d
            + openingRangeScore * 0.12d);
    }

    private static double ScoreMouthJawTrust(
        PersonalFaceModel faceModel,
        PersonalFaceCorpusReadiness readiness)
    {
        var shapeScore = Average(
            ScoreContourShapeProfile(faceModel.OuterLipShape),
            ScoreContourShapeProfile(faceModel.InnerLipShape),
            ScoreContourShapeProfile(faceModel.JawShape));
        var mouthEvidence = ScoreOptionalPercent(readiness.AverageCaptureQualityMouthScorePercent, fallback: faceModel.AverageMouthReliabilityPercent);
        var directMouthRate = ScoreDirectRate(readiness.MouthReconstructedRate);
        var expressionRangeScore = Average(
            ScoreRange(readiness.MouthOpeningRange, StrongMouthOpeningRange),
            ScoreRange(readiness.JawDroopRange, StrongJawDroopRange));

        return Round(
            shapeScore * 0.30d
            + mouthEvidence * 0.28d
            + directMouthRate * 0.20d
            + expressionRangeScore * 0.22d);
    }

    private static double ScoreOptionalPercent(double? value, double fallback)
    {
        return value is double number
            ? Round(Math.Clamp(number, 0d, 100d))
            : Round(Math.Clamp(fallback, 0d, 100d));
    }

    private static double ScoreDirectRate(double? reconstructedOrSuppressedRate)
    {
        return reconstructedOrSuppressedRate is double rate
            ? Round(Math.Clamp((1d - rate) * 100d, 0d, 100d))
            : 0d;
    }

    private static double? Range(PersonalMetricDistribution distribution)
    {
        return distribution.Minimum is double min && distribution.Maximum is double max && max >= min
            ? Round(max - min)
            : null;
    }

    private static double? Rate(int count, int total)
    {
        return total <= 0 ? null : Round(count / (double)total);
    }

    private static double RateValue(int count, int total)
    {
        return total <= 0 ? 0d : Round(count / (double)total);
    }

    private static double? AverageOptional(IEnumerable<double?> values)
    {
        var valid = values
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .Where(static value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToList();
        return valid.Count == 0 ? null : Round(valid.Average());
    }

    private static double? MinimumOptional(IEnumerable<double?> values)
    {
        var valid = values
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .Where(static value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToList();
        return valid.Count == 0 ? null : Round(valid.Min());
    }

    private static double Average(params double[] values)
    {
        return values.Length == 0 ? 0d : Round(values.Average());
    }

    private static DateTime Max(DateTime first, DateTime second)
    {
        return first >= second ? first : second;
    }

    private static double Round(double value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }
}
