using EpisodeMonitor.Modules.Vision.Personalization;

namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class MeasurementFacePreviewBuilder
{
    private const int StrongSampleCount = 240;
    private const double StrongSampleWeight = 180d;

    public MeasurementFacePreviewModel Build(PersonalFaceModel model, FaceReconstructionSubjectGate subjectGate)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(subjectGate);

        var preview = CreateBasePreview(model, subjectGate);
        if (!IsGateAccepted(model, subjectGate, out var gateWarning))
        {
            preview.RenderDecision = "paused by subject gate";
            preview.Warnings.Add(gateWarning);
            return preview;
        }

        preview.CanRender = true;
        ConfigureProvenance(model, preview);
        preview.RenderDecision = model.AcceptedSamples <= 0
            ? "renderable canonical seed scaffold; waiting for accepted personal measurements"
            : "renderable measurement-only preview";
        AddConfidenceWarnings(model, preview);
        AddMetrics(model, preview);
        AddContourShapeProfiles(model, preview);
        AddGeometry(model, preview);
        return preview;
    }

    private static MeasurementFacePreviewModel CreateBasePreview(
        PersonalFaceModel model,
        FaceReconstructionSubjectGate subjectGate)
    {
        return new MeasurementFacePreviewModel
        {
            SubjectId = model.SubjectId,
            SubjectDisplayName = model.SubjectDisplayName,
            SubjectCollectionMode = model.SubjectCollectionMode,
            UnknownSubjectPolicy = model.UnknownSubjectPolicy,
            SubjectGate = subjectGate,
            ObservedSamples = model.ObservedSamples,
            AcceptedSamples = model.AcceptedSamples,
            RejectedSamples = model.RejectedSamples,
            AcceptedSampleWeight = Round(model.AcceptedSampleWeight),
            AcceptedRate = Round(model.AcceptedRate),
            ConfidencePercent = CalculateConfidence(model)
        };
    }

    private static bool IsGateAccepted(
        PersonalFaceModel model,
        FaceReconstructionSubjectGate subjectGate,
        out string warning)
    {
        if (!string.Equals(subjectGate.GateDecision, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            warning = string.IsNullOrWhiteSpace(subjectGate.Reason)
                ? "Subject gate is not accepted."
                : subjectGate.Reason;
            return false;
        }

        if (!string.Equals(model.SubjectId, subjectGate.SubjectId, StringComparison.OrdinalIgnoreCase))
        {
            warning = $"Subject mismatch: model is '{model.SubjectId}', gate is '{subjectGate.SubjectId}'.";
            return false;
        }

        warning = "";
        return true;
    }

    private static void AddConfidenceWarnings(PersonalFaceModel model, MeasurementFacePreviewModel preview)
    {
        if (model.AcceptedSamples <= 0)
        {
            preview.Warnings.Add("Preview is seeded from a canonical face scaffold only; no personal measurements have been accepted yet.");
            preview.Warnings.Add("Seed geometry is for early visualization and rig topology only. It is not evidence about the subject's face.");
        }
        else if (model.AcceptedSamples < 60)
        {
            preview.Warnings.Add("Preview is early: fewer than 60 accepted measurements.");
        }
        else if (model.AcceptedSamples < StrongSampleCount)
        {
            preview.Warnings.Add("Preview is warming: more samples will make the face shape steadier.");
        }

        if (model.RejectedSamples > model.AcceptedSamples)
        {
            preview.Warnings.Add("Rejected samples exceed accepted samples; inspect lighting, glasses glare, or subject gate timing.");
        }

        if (model.MediaPipeAverageEyeBlinkPercent.SampleCount == 0
            && model.MediaPipeJawOpenPercent.SampleCount == 0)
        {
            preview.Warnings.Add(model.AcceptedSamples <= 0
                ? "MediaPipe blendshape baselines are not populated yet; preview uses neutral canonical eyelid and jaw values."
                : "MediaPipe blendshape baselines are not populated yet; preview is based on contour measurements.");
        }

        if (model.AcceptedSamples > 0)
        {
            preview.Warnings.Add("Neutral face preview separates feature shape from pose: turned-head samples shape the eye/lip/jaw outlines but should not slide neutral features sideways.");
            var neutralPose = GetNeutralPoseProfile(model);
            if (neutralPose is null || neutralPose.SampleCount < 12)
            {
                preview.Warnings.Add("Straight-on pose coverage is early; collect more front-facing alert measurements before trusting neutral face proportions.");
            }
        }
    }

    private static void AddMetrics(PersonalFaceModel model, MeasurementFacePreviewModel preview)
    {
        var neutralPose = GetNeutralPoseProfile(model);
        preview.Metrics["MeasurementContributionPercent"] = preview.MeasurementContributionPercent;
        preview.Metrics["TemplatePriorContributionPercent"] = preview.TemplatePriorContributionPercent;
        preview.Metrics["PoseBucketCount"] = model.PoseBuckets.Count;
        preview.Metrics["CoveredPoseBucketCount"] = model.PoseBuckets.Count(static bucket => bucket.SampleCount > 0);
        preview.Metrics["NeutralPoseSamples"] = neutralPose?.SampleCount ?? 0;
        preview.Metrics["NeutralPoseWeight"] = Round(neutralPose?.TotalWeight ?? 0d);
        var averageEyeOpening = Metric(PreferNeutral(neutralPose, static pose => pose.AverageEyeOpeningRatio, model.AverageEyeOpeningRatio), 0.26d, 0.02d, 0.60d);
        preview.Metrics["FaceCenterX"] = Metric(model.FaceCenterX, 0.50d, 0d, 1d);
        preview.Metrics["FaceCenterY"] = Metric(model.FaceCenterY, 0.50d, 0d, 1d);
        preview.Metrics["FaceWidth"] = Metric(model.FaceWidth, 0.42d, 0.16d, 0.95d);
        preview.Metrics["FaceHeight"] = Metric(model.FaceHeight, 0.62d, 0.24d, 1.20d);
        preview.Metrics["FaceAspectRatio"] = Metric(PreferNeutral(neutralPose, static pose => pose.FaceAspectRatio, model.FaceAspectRatio), 1.55d, 1.15d, 2.20d);
        preview.Metrics["HeadYawDegrees"] = Metric(model.HeadYawDegrees, 0d, -45d, 45d);
        preview.Metrics["HeadPitchDegrees"] = Metric(model.HeadPitchDegrees, 0d, -35d, 35d);
        preview.Metrics["HeadRollDegrees"] = Metric(model.HeadRollDegrees, 0d, -45d, 45d);
        preview.Metrics["LeftEyeOpeningRatio"] = Metric(model.LeftEyeOpeningRatio, averageEyeOpening, 0.02d, 0.60d);
        preview.Metrics["RightEyeOpeningRatio"] = Metric(model.RightEyeOpeningRatio, averageEyeOpening, 0.02d, 0.60d);
        preview.Metrics["AverageEyeOpeningRatio"] = averageEyeOpening;
        preview.Metrics["MouthOpeningRatio"] = Metric(PreferNeutral(neutralPose, static pose => pose.MouthOpeningRatio, model.MouthOpeningRatio), 0.07d, 0.01d, 0.70d);
        preview.Metrics["JawDroopRatio"] = Metric(PreferNeutral(neutralPose, static pose => pose.JawDroopRatio, model.JawDroopRatio), 0d, -0.05d, 0.28d);
        preview.Metrics["MediaPipeAverageEyeBlinkPercent"] = NullableMetric(model.MediaPipeAverageEyeBlinkPercent);
        preview.Metrics["MediaPipeJawOpenPercent"] = NullableMetric(model.MediaPipeJawOpenPercent);
        preview.Metrics["MediaPipeMouthClosePercent"] = NullableMetric(model.MediaPipeMouthClosePercent);
        preview.Metrics["EyeGlarePercent"] = NullableMetric(model.EyeGlarePercent);
        preview.Metrics["EyeContrastPercent"] = NullableMetric(model.EyeContrastPercent);
        preview.Metrics["EyeSharpnessPercent"] = NullableMetric(model.EyeSharpnessPercent);
        preview.Metrics["AverageFaceReliabilityPercent"] = Round(model.AverageFaceReliabilityPercent);
        preview.Metrics["AverageFaceContinuityPercent"] = Round(model.AverageFaceContinuityPercent);
        preview.Metrics["AverageEyeReliabilityPercent"] = Round(model.AverageEyeReliabilityPercent);
        preview.Metrics["AverageMouthReliabilityPercent"] = Round(model.AverageMouthReliabilityPercent);
    }

    private static void AddGeometry(PersonalFaceModel model, MeasurementFacePreviewModel preview)
    {
        const double canonicalFaceHeight = 0.72d;
        const double neutralFaceAspectRatio = 1.55d;
        var neutralPose = GetNeutralPoseProfile(model);
        var measuredFaceAspectRatio = Metric(PreferNeutral(neutralPose, static pose => pose.FaceAspectRatio, model.FaceAspectRatio), neutralFaceAspectRatio, 1.15d, 2.20d);
        var shapeMaturity = CalculateShapeMaturity(model);
        var previewFaceAspectRatio = Blend(neutralFaceAspectRatio, measuredFaceAspectRatio, shapeMaturity);
        var faceHeight = canonicalFaceHeight;
        var faceWidth = Math.Clamp(faceHeight / previewFaceAspectRatio, 0.36d, 0.58d);
        preview.Metrics["PreviewFaceWidth"] = Round(faceWidth);
        preview.Metrics["PreviewFaceHeight"] = Round(faceHeight);
        preview.Metrics["PreviewShapeMaturityPercent"] = Round(shapeMaturity * 100d);
        var averageEyeOpening = Metric(PreferNeutral(neutralPose, static pose => pose.AverageEyeOpeningRatio, model.AverageEyeOpeningRatio), 0.26d, 0.02d, 0.60d);
        var leftEyeOpening = Metric(model.LeftEyeOpeningRatio, averageEyeOpening, 0.02d, 0.60d);
        var rightEyeOpening = Metric(model.RightEyeOpeningRatio, averageEyeOpening, 0.02d, 0.60d);
        var mouthOpening = Metric(PreferNeutral(neutralPose, static pose => pose.MouthOpeningRatio, model.MouthOpeningRatio), 0.07d, 0.01d, 0.70d);
        var jawDroop = Metric(PreferNeutral(neutralPose, static pose => pose.JawDroopRatio, model.JawDroopRatio), 0d, -0.05d, 0.28d);
        var yaw = Metric(model.HeadYawDegrees, 0d, -45d, 45d);
        var pitch = Metric(model.HeadPitchDegrees, 0d, -35d, 35d);
        var interEyeDistance = Metric(PreferNeutral(neutralPose, static pose => pose.InterEyeDistanceToFaceWidth, model.InterEyeDistanceToFaceWidth), 0.40d, 0.22d, 0.58d) * faceWidth;
        var leftEyeWidth = Metric(model.LeftEyeWidthToFaceWidth, 0.18d, 0.08d, 0.34d) * faceWidth;
        var rightEyeWidth = Metric(model.RightEyeWidthToFaceWidth, 0.18d, 0.08d, 0.34d) * faceWidth;
        var mouthWidth = Metric(PreferNeutral(neutralPose, static pose => pose.MouthWidthToFaceWidth, model.MouthWidthToFaceWidth), 0.36d, 0.16d, 0.62d) * faceWidth;
        var eyeMidlineY = (Metric(PreferNeutral(neutralPose, static pose => pose.EyeMidlineYToFaceHeight, model.EyeMidlineYToFaceHeight), 0.32d, 0.18d, 0.46d) - 0.5d) * faceHeight
            + pitch / 45d * faceHeight * 0.025d;
        var learnedMouthY = (Metric(PreferNeutral(neutralPose, static pose => pose.MouthCenterYToFaceHeight, model.MouthCenterYToFaceHeight), 0.66d, 0.52d, 0.84d) - 0.5d) * faceHeight;

        var halfFaceWidth = faceWidth / 2d;
        var halfFaceHeight = faceHeight / 2d;
        var jawOffset = jawDroop * faceHeight * 0.35d;
        var leftEyeX = -interEyeDistance / 2d;
        var rightEyeX = interEyeDistance / 2d;
        var leftEyeHalfWidth = Math.Max(faceWidth * 0.045d, leftEyeWidth / 2d);
        var rightEyeHalfWidth = Math.Max(faceWidth * 0.045d, rightEyeWidth / 2d);
        var mouthY = learnedMouthY + jawOffset * 0.30d;
        var mouthHalfWidth = Math.Max(faceWidth * 0.08d, mouthWidth / 2d);
        var mouthInnerHalfHeight = Math.Max(faceHeight * 0.006d, mouthHalfWidth * mouthOpening * 0.55d);
        var mouthOuterHalfHeight = faceHeight * 0.023d + mouthInnerHalfHeight * 0.55d;
        var jawCenterY = faceHeight * 0.32d + jawOffset * 0.45d;
        var jawHalfHeight = faceHeight * 0.18d + Math.Abs(jawOffset) * 0.50d;

        AddFaceOval(preview, halfFaceWidth, halfFaceHeight, jawOffset);
        if (!TryAddAnchoredContourProfile(preview, model.JawShape, "jaw", "Jaw contour", "jaw", 0d, jawCenterY, faceWidth * 0.34d, jawHalfHeight, faceWidth, faceHeight, z: 0.02d))
        {
            AddJawLine(preview, faceWidth, faceHeight, jawOffset);
        }

        if (!TryAddAnchoredContourProfile(preview, model.LeftEyeShape, "left_eye", "Left eye learned contour", "eye", leftEyeX, eyeMidlineY, leftEyeHalfWidth, EyeHalfHeight(leftEyeHalfWidth, leftEyeOpening, faceHeight), faceWidth, faceHeight, z: 0.05d))
        {
            AddEllipse(preview, "left_eye", "Left eye opening", "eye", leftEyeX, eyeMidlineY, leftEyeHalfWidth, EyeHalfHeight(leftEyeHalfWidth, leftEyeOpening, faceHeight), 0.05d, 12);
        }

        if (!TryAddAnchoredContourProfile(preview, model.RightEyeShape, "right_eye", "Right eye learned contour", "eye", rightEyeX, eyeMidlineY, rightEyeHalfWidth, EyeHalfHeight(rightEyeHalfWidth, rightEyeOpening, faceHeight), faceWidth, faceHeight, z: 0.05d))
        {
            AddEllipse(preview, "right_eye", "Right eye opening", "eye", rightEyeX, eyeMidlineY, rightEyeHalfWidth, EyeHalfHeight(rightEyeHalfWidth, rightEyeOpening, faceHeight), 0.05d, 12);
        }

        if (!TryAddAnchoredContourProfile(preview, model.OuterLipShape, "mouth_outer", "Outer lip learned contour", "mouth", 0d, mouthY, mouthHalfWidth, mouthOuterHalfHeight, faceWidth, faceHeight, z: 0.04d))
        {
            AddEllipse(preview, "mouth_outer", "Outer lip reference", "mouth", 0d, mouthY, mouthHalfWidth, mouthOuterHalfHeight, 0.04d, 14);
        }

        if (!TryAddAnchoredContourProfile(preview, model.InnerLipShape, "mouth_inner", "Mouth opening learned contour", "mouth-opening", 0d, mouthY, mouthHalfWidth * 0.70d, mouthInnerHalfHeight, faceWidth, faceHeight, z: 0.07d))
        {
            AddEllipse(preview, "mouth_inner", "Mouth opening", "mouth-opening", 0d, mouthY, mouthHalfWidth * 0.70d, mouthInnerHalfHeight, 0.07d, 14);
        }

        AddNose(preview, faceWidth, faceHeight, yaw, pitch);
        AddJawDroopMarker(preview, faceHeight, jawOffset);
    }

    private static void AddContourShapeProfiles(PersonalFaceModel model, MeasurementFacePreviewModel preview)
    {
        AddContourShapeProfile(preview, model.LeftEyeShape);
        AddContourShapeProfile(preview, model.RightEyeShape);
        AddContourShapeProfile(preview, model.OuterLipShape);
        AddContourShapeProfile(preview, model.InnerLipShape);
        AddContourShapeProfile(preview, model.JawShape);
    }

    private static void AddContourShapeProfile(MeasurementFacePreviewModel preview, PersonalFaceContourShapeProfile profile)
    {
        if (profile.HasProfile)
        {
            preview.ContourShapeProfiles[profile.FeatureId] = profile;
        }
    }

    private static bool TryAddAnchoredContourProfile(
        MeasurementFacePreviewModel preview,
        PersonalFaceContourShapeProfile profile,
        string prefix,
        string label,
        string role,
        double targetCenterX,
        double targetCenterY,
        double targetHalfWidth,
        double targetHalfHeight,
        double faceWidth,
        double faceHeight,
        double z)
    {
        if (!profile.HasProfile)
        {
            return false;
        }

        var usablePoints = profile.Points
            .OrderBy(static point => point.Index)
            .Select(point => new
            {
                point.Index,
                X = point.X.ExponentialMovingAverage ?? point.X.Average,
                Y = point.Y.ExponentialMovingAverage ?? point.Y.Average
            })
            .Where(static point => point.X.HasValue && point.Y.HasValue)
            .ToList();
        if (usablePoints.Count < 2)
        {
            return false;
        }

        var minX = usablePoints.Min(static point => point.X!.Value);
        var maxX = usablePoints.Max(static point => point.X!.Value);
        var minY = usablePoints.Min(static point => point.Y!.Value);
        var maxY = usablePoints.Max(static point => point.Y!.Value);
        var sourceHalfWidth = Math.Max(0.001d, (maxX - minX) / 2d);
        var sourceHalfHeight = Math.Max(0.001d, (maxY - minY) / 2d);
        var sourceCenterX = (minX + maxX) / 2d;
        var sourceCenterY = (minY + maxY) / 2d;
        var boundedTargetHalfWidth = Math.Clamp(targetHalfWidth, faceWidth * 0.015d, faceWidth * 0.48d);
        var boundedTargetHalfHeight = Math.Clamp(targetHalfHeight, faceHeight * 0.004d, faceHeight * 0.36d);
        var provenance = "pose-normalized personal aggregate contour profile";
        var ids = new List<string>(usablePoints.Count + 1);
        foreach (var point in usablePoints)
        {
            var localX = (point.X!.Value - sourceCenterX) / sourceHalfWidth;
            var localY = (point.Y!.Value - sourceCenterY) / sourceHalfHeight;
            var x = targetCenterX + localX * boundedTargetHalfWidth;
            var y = targetCenterY + localY * boundedTargetHalfHeight;
            x = Math.Clamp(x, -faceWidth * 0.49d, faceWidth * 0.49d);
            y = Math.Clamp(y, -faceHeight * 0.52d, faceHeight * 0.62d);
            ids.Add(AddPoint(
                preview,
                $"{prefix}_shape_{point.Index:00}",
                label,
                role,
                x,
                y,
                z,
                provenance: provenance));
        }

        if (profile.Closed)
        {
            ids.Add(ids[0]);
        }

        AddPolyline(
            preview,
            $"{prefix}_shape_line",
            label,
            role,
            ids,
            provenance: provenance);
        return true;
    }

    private static void AddFaceOval(
        MeasurementFacePreviewModel preview,
        double halfWidth,
        double halfHeight,
        double jawOffset)
    {
        var ids = new List<string>();
        const int count = 24;
        for (var index = 0; index < count; index++)
        {
            var angle = -Math.PI / 2d + Math.PI * 2d * index / count;
            var x = Math.Cos(angle) * halfWidth;
            var y = Math.Sin(angle) * halfHeight;
            if (y > 0d)
            {
                y += jawOffset * Math.Pow(Math.Clamp(y / halfHeight, 0d, 1d), 1.8d);
            }

            var z = -Math.Abs(x) / Math.Max(0.01d, halfWidth) * 0.10d + (y > 0d ? 0.03d : -0.02d);
            ids.Add(AddPoint(preview, $"face_{index:00}", "Face oval", "face", x, y, z));
        }

        ids.Add(ids[0]);
        AddPolyline(preview, "face_oval", "Face oval", "face", ids);
    }

    private static void AddJawLine(
        MeasurementFacePreviewModel preview,
        double faceWidth,
        double faceHeight,
        double jawOffset)
    {
        var points = new[]
        {
            (-faceWidth * 0.34d, faceHeight * 0.14d, -0.06d),
            (-faceWidth * 0.29d, faceHeight * 0.30d, -0.03d),
            (-faceWidth * 0.18d, faceHeight * 0.43d, 0.01d),
            (0d, faceHeight * 0.50d + jawOffset, 0.05d),
            (faceWidth * 0.18d, faceHeight * 0.43d, 0.01d),
            (faceWidth * 0.29d, faceHeight * 0.30d, -0.03d),
            (faceWidth * 0.34d, faceHeight * 0.14d, -0.06d)
        };
        var ids = new List<string>();
        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            ids.Add(AddPoint(preview, $"jaw_{index:00}", "Jaw contour", "jaw", point.Item1, point.Item2, point.Item3));
        }

        AddPolyline(preview, "jaw_line", "Jaw contour", "jaw", ids);
    }

    private static void AddNose(
        MeasurementFacePreviewModel preview,
        double faceWidth,
        double faceHeight,
        double yaw,
        double pitch)
    {
        var yawOffset = yaw / 45d * faceWidth * 0.045d;
        var pitchOffset = pitch / 35d * faceHeight * 0.035d;
        var ids = new[]
        {
            AddPoint(preview, "nose_bridge", "Nose bridge", "nose", yawOffset * 0.35d, -faceHeight * 0.065d + pitchOffset, 0.11d),
            AddPoint(preview, "nose_tip", "Nose tip", "nose", yawOffset, faceHeight * 0.060d + pitchOffset, 0.20d),
            AddPoint(preview, "nose_base", "Nose base", "nose", yawOffset * 0.45d, faceHeight * 0.130d + pitchOffset, 0.10d)
        };
        AddPolyline(preview, "nose_line", "Nose pose hint", "nose", ids);
    }

    private static void AddJawDroopMarker(
        MeasurementFacePreviewModel preview,
        double faceHeight,
        double jawOffset)
    {
        var ids = new[]
        {
            AddPoint(preview, "jaw_droop_reference", "Neutral jaw reference", "jaw-droop", 0d, faceHeight * 0.50d, -0.02d),
            AddPoint(preview, "jaw_droop_current", "Current jaw droop estimate", "jaw-droop", 0d, faceHeight * 0.50d + jawOffset, 0.08d)
        };
        AddPolyline(preview, "jaw_droop_marker", "Jaw droop marker", "jaw-droop", ids);
    }

    private static void AddEllipse(
        MeasurementFacePreviewModel preview,
        string prefix,
        string label,
        string role,
        double centerX,
        double centerY,
        double halfWidth,
        double halfHeight,
        double z,
        int count)
    {
        var ids = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var angle = Math.PI * 2d * index / count;
            ids.Add(AddPoint(
                preview,
                $"{prefix}_{index:00}",
                label,
                role,
                centerX + Math.Cos(angle) * halfWidth,
                centerY + Math.Sin(angle) * halfHeight,
                z + Math.Sin(angle) * 0.015d));
        }

        ids.Add(ids[0]);
        AddPolyline(preview, $"{prefix}_line", label, role, ids);
    }

    private static string AddPoint(
        MeasurementFacePreviewModel preview,
        string id,
        string label,
        string role,
        double x,
        double y,
        double z,
        string provenance = "",
        double? confidencePercent = null)
    {
        preview.Points.Add(new MeasurementFacePreviewPoint
        {
            Id = id,
            Label = label,
            Role = role,
            Provenance = string.IsNullOrWhiteSpace(provenance) ? preview.GeometryProvenance : provenance,
            ConfidencePercent = Round(confidencePercent ?? preview.ConfidencePercent),
            X = Round(x),
            Y = Round(y),
            Z = Round(z)
        });
        return id;
    }

    private static void AddPolyline(
        MeasurementFacePreviewModel preview,
        string id,
        string label,
        string role,
        IEnumerable<string> pointIds,
        string provenance = "",
        double? confidencePercent = null)
    {
        preview.Polylines.Add(new MeasurementFacePreviewPolyline
        {
            Id = id,
            Label = label,
            Role = role,
            Provenance = string.IsNullOrWhiteSpace(provenance) ? preview.GeometryProvenance : provenance,
            ConfidencePercent = Round(confidencePercent ?? preview.ConfidencePercent),
            PointIds = pointIds.ToList()
        });
    }

    private static void ConfigureProvenance(PersonalFaceModel model, MeasurementFacePreviewModel preview)
    {
        var measurementContribution = CalculateMeasurementContribution(model);
        preview.MeasurementContributionPercent = measurementContribution;
        preview.TemplatePriorContributionPercent = Round(100d - measurementContribution);
        preview.TemplatePriorUsed = preview.TemplatePriorContributionPercent > 0.01d;
        preview.GeometryProvenance = model.AcceptedSamples <= 0
            ? "canonical template prior only"
            : preview.TemplatePriorUsed
                ? "personal measurements with canonical template fallbacks for missing or low-confidence fields"
                : "personal measurements";
    }

    private static double EyeHalfHeight(double eyeHalfWidth, double openingRatio, double faceHeight)
    {
        return Math.Clamp(eyeHalfWidth * openingRatio * 0.85d, faceHeight * 0.006d, faceHeight * 0.055d);
    }

    private static double CalculateConfidence(PersonalFaceModel model)
    {
        var sampleConfidence = Math.Clamp(model.AcceptedSamples / (double)StrongSampleCount * 100d, 0d, 100d);
        var weightConfidence = Math.Clamp(model.AcceptedSampleWeight / StrongSampleWeight * 100d, 0d, 100d);
        var reliabilityValues = new[]
        {
            model.AverageFaceReliabilityPercent,
            model.AverageFaceContinuityPercent,
            model.AverageEyeReliabilityPercent,
            model.AverageMouthReliabilityPercent
        }.Where(static value => value > 0d).ToList();
        var reliabilityConfidence = reliabilityValues.Count == 0 ? sampleConfidence : reliabilityValues.Average();
        return Round(sampleConfidence * 0.50d + weightConfidence * 0.20d + reliabilityConfidence * 0.30d);
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

    private static double CalculateShapeMaturity(PersonalFaceModel model)
    {
        if (model.AcceptedSamples <= 0 || model.AcceptedSampleWeight <= 0d)
        {
            return 0d;
        }

        var sampleMaturity = Math.Clamp(model.AcceptedSamples / 120d, 0d, 1d);
        var weightMaturity = Math.Clamp(model.AcceptedSampleWeight / StrongSampleWeight, 0d, 1d);
        return Math.Clamp(sampleMaturity * 0.45d + weightMaturity * 0.55d, 0d, 1d);
    }

    private static double Blend(double fallback, double measured, double measurementWeight)
    {
        var weight = Math.Clamp(measurementWeight, 0d, 1d);
        return fallback + (measured - fallback) * weight;
    }

    private static PersonalFacePoseBucketProfile? GetNeutralPoseProfile(PersonalFaceModel model)
    {
        return model.PoseBuckets.FirstOrDefault(static bucket =>
            bucket.PrimaryNeutralReference
            && bucket.SampleCount > 0
            && string.Equals(bucket.BucketId, PersonalFacePoseBuckets.FrontNeutral, StringComparison.OrdinalIgnoreCase));
    }

    private static PersonalMetricDistribution PreferNeutral(
        PersonalFacePoseBucketProfile? neutralPose,
        Func<PersonalFacePoseBucketProfile, PersonalMetricDistribution> selector,
        PersonalMetricDistribution fallback)
    {
        if (neutralPose is null)
        {
            return fallback;
        }

        var candidate = selector(neutralPose);
        return candidate.SampleCount > 0 ? candidate : fallback;
    }

    private static double Metric(PersonalMetricDistribution distribution, double fallback, double minimum, double maximum)
    {
        var value = distribution.ExponentialMovingAverage ?? distribution.Average ?? fallback;
        return Round(Bound(value, fallback, minimum, maximum));
    }

    private static double? NullableMetric(PersonalMetricDistribution distribution)
    {
        var value = distribution.ExponentialMovingAverage ?? distribution.Average;
        if (value is not double number || double.IsNaN(number) || double.IsInfinity(number))
        {
            return null;
        }

        return Round(number);
    }

    private static double Bound(double value, double fallback, double minimum, double maximum)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return Math.Clamp(fallback, minimum, maximum);
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private static double Round(double value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }
}
