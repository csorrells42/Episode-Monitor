using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using EpisodeMonitor.Modules.Infrastructure;
using EpisodeMonitor.Modules.Vision.Analysis;
using EpisodeMonitor.Modules.Vision.Personalization;

namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class MeasurementAvatarSystemDashboardStore
{
    public const string DefaultJsonFileName = "measurement_avatar_system.json";
    public const string DefaultHtmlFileName = "measurement_avatar_system.html";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private const int TrackingSanityMinimumSamples = 60;

    public string Write(string folder, MeasurementAvatarSystemDashboard dashboard)
    {
        Directory.CreateDirectory(folder);
        var jsonPath = Path.Combine(folder, DefaultJsonFileName);
        AtomicTextFileWriter.WriteAllText(jsonPath, JsonSerializer.Serialize(dashboard, JsonOptions), Encoding.UTF8);
        AtomicTextFileWriter.WriteAllText(GetHtmlPath(jsonPath), BuildHtml(dashboard), Encoding.UTF8);
        return jsonPath;
    }

    public static string GetHtmlPath(string jsonPath)
    {
        return Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", DefaultHtmlFileName);
    }

    private static string BuildHtml(MeasurementAvatarSystemDashboard dashboard)
    {
        var learningClass = dashboard.AvatarLearningActive
            ? "good"
            : dashboard.AvatarLearningRequested ? "warn" : "muted";
        var model = dashboard.FaceModel;
        var motion = dashboard.MotionModel;
        var readiness = dashboard.LearningDataReadiness;
        var audit = dashboard.CollectionAudit;
        var package = dashboard.AvatarPackage;
        var plan = dashboard.CapturePlan;
        var quality = dashboard.CurrentCaptureQuality;
        var headPose = dashboard.CurrentHeadPose;
        var featureStability = dashboard.LastGoodFeatureStability;
        var trackingSanity = FormatTrackingSanity(readiness);

        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"10\"><title>Avatar System</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{margin:0;background:#080d12;color:#f5f8fb;font-family:Segoe UI,Arial,sans-serif;line-height:1.45}main{max-width:1180px;margin:0 auto;padding:28px}section{border:1px solid #243545;background:#101820;margin:16px 0;padding:18px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px}.metric{background:#0c1218;border:1px solid #1d2c38;padding:12px}.label{color:#b9d7ef;font-size:12px;text-transform:uppercase;letter-spacing:.04em}.value{font-size:22px;font-weight:700}.good{color:#80e0a4}.warn{color:#ffd27a}.bad{color:#ff9a9a}.muted{color:#b9d7ef}a{color:#8fc7ff}.bar{height:8px;background:#1d2c38;margin-top:8px}.fill{height:8px;background:#4fa3d1}ul{padding-left:20px}li{margin:5px 0}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #243545;padding:8px;text-align:left}th{color:#b9d7ef;font-weight:600}.links a{display:inline-block;margin:0 12px 8px 0}</style>");
        html.AppendLine("</head><body><main>");
        html.AppendLine($"<h1>Avatar System</h1><p class=\"muted\">Live report auto-refreshes every 10 seconds. Last updated {H(dashboard.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))}.</p><p class=\"muted\">{H(dashboard.StoragePolicy)}</p>");
        html.AppendLine("<section>");
        html.AppendLine($"<h2 class=\"{learningClass}\">{H(dashboard.AvatarLearningStatus)}</h2>");
        html.AppendLine($"<p>{H(dashboard.AvatarLearningCorrection)}</p>");
        html.AppendLine("<div class=\"grid\">");
        html.AppendLine(Metric("Subject confirmed", dashboard.SubjectConfirmed ? "Yes" : "No"));
        html.AppendLine(Metric("Learning switch", dashboard.AvatarLearningRequested ? "Started" : "Stopped"));
        html.AppendLine(Metric("Currently collecting", dashboard.AvatarLearningActive ? "Yes" : "No"));
        html.AppendLine(Metric("Current quality", $"{quality.Label} {quality.ScorePercent:0}%"));
        html.AppendLine(Metric("Tracking sanity", trackingSanity));
        html.AppendLine(Metric("Recent mesh stability", FormatRecentMeshStability(featureStability)));
        html.AppendLine(Metric("B head-turn lock", FormatYawHeadTurnLock(featureStability)));
        html.AppendLine(Metric("A tilt lock", FormatAxisLock(featureStability.AHealthPercent, featureStability.ARangeDegrees, "deg")));
        html.AppendLine(Metric("C tilt lock", FormatAxisLock(featureStability.CHealthPercent, featureStability.CRangeDegrees, "deg")));
        html.AppendLine(Metric("Z distance lock", FormatAxisLock(featureStability.ZHealthPercent, featureStability.ZFaceScaleRangePercent, "% scale")));
        html.AppendLine(Metric("Z apparent distance", FormatApparentDistance(headPose)));
        html.AppendLine(Metric("Z vs reference", FormatRelativeScale(headPose)));
        html.AppendLine(Metric("Z estimate quality", FormatZQuality(headPose)));
        html.AppendLine(Metric("Face fill", FormatFaceFill(headPose)));
        html.AppendLine(Metric("Measured geometry", $"{package.MeasurementContributionPercent:0.#}%"));
        html.AppendLine(Metric("Template scaffold", $"{package.TemplatePriorContributionPercent:0.#}%"));
        html.AppendLine("</div></section>");

        html.AppendLine("<section><h2>Face Learning Data</h2><div class=\"grid\">");
        html.AppendLine(Metric("Accepted face samples", model.AcceptedSamples.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine(Metric("Reviewed samples", model.ObservedSamples.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine(Metric("Learning anchor", $"{model.LearningStability.AnchorPercent:0.#}% {model.LearningStability.AnchorStatus}"));
        html.AppendLine(Metric("Weakest tracked weight", $"{model.LearningStability.MinimumTrackedDistributionWeight:0.###}"));
        html.AppendLine(Metric("Next sample influence", $"{model.LearningStability.MaximumNextSampleInfluencePercent:0.##}% max"));
        html.AppendLine(Metric("Readiness", $"{readiness.OverallReadinessPercent:0.#}%"));
        html.AppendLine(Metric("Data audit", $"{readiness.DataAuditHealthPercent:0.#}%"));
        html.AppendLine(Metric("Pose audit", $"{readiness.PoseEstimationHealthPercent:0.#}%"));
        html.AppendLine(Metric("XYZABC coverage", $"{readiness.XYZABCCoveragePercent:0.#}%"));
        html.AppendLine(Metric("Z distance coverage", $"{readiness.ZDistanceCoveragePercent:0.#}%"));
        html.AppendLine(Metric("Z evidence health", $"{readiness.ZDistanceEvidenceHealthPercent:0.#}%"));
        html.AppendLine(Metric("A rotate X coverage", $"{readiness.ARotationAroundXCoveragePercent:0.#}%"));
        html.AppendLine(Metric("B rotate Y coverage", $"{readiness.BRotationAroundYCoveragePercent:0.#}%"));
        html.AppendLine(Metric("C rotate Z coverage", $"{readiness.CRotationAroundZCoveragePercent:0.#}%"));
        html.AppendLine(Metric("Feature anchoring", $"{readiness.FeatureAnchoringHealthPercent:0.#}%"));
        html.AppendLine(Metric("Pose-explained features", $"{readiness.PoseExplainedFeatureMotionHealthPercent:0.#}%"));
        html.AppendLine(Metric("Mouth anchor", $"{readiness.MouthVerticalAnchorHealthPercent:0.#}%"));
        html.AppendLine(Metric("Pose consistency", $"{readiness.PoseBucketConsistencyHealthPercent:0.#}%"));
        html.AppendLine(Metric("Aperture consistency", $"{readiness.ApertureConsistencyHealthPercent:0.#}%"));
        html.AppendLine(Metric("Contour Z profile", $"{readiness.ContourDepthProfileHealthPercent:0.#}%"));
        html.AppendLine(Metric("Surface Z profile", $"{readiness.SurfaceDepthProfileHealthPercent:0.#}%"));
        html.AppendLine(Metric("Surface geometry", $"{readiness.SurfaceGeometryHealthPercent:0.#}%"));
        html.AppendLine(Metric("Surface geometry status", readiness.SurfaceGeometryStatus));
        html.AppendLine(Metric("Identity samples", model.IdentitySignatureSamples.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine(Metric("Identity session", $"{readiness.IdentitySessionHealthPercent:0.#}% {readiness.IdentitySessionAuditStage}"));
        html.AppendLine(Metric("Pose buckets", $"{readiness.PoseBucketCoveredCount.ToString(CultureInfo.InvariantCulture)} / {readiness.PoseBucketRequiredCount.ToString(CultureInfo.InvariantCulture)}"));
        html.AppendLine(Metric("Motion observations", motion.UsableObservationCount.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine(Metric("Motion pairs", motion.MotionPairCount.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine("</div>");
        html.AppendLine($"<p class=\"muted\">{H(model.LearningStability.Guidance)}</p>");
        html.AppendLine($"<p class=\"muted\">{H(readiness.IdentitySessionAuditStatus)}</p>");
        html.AppendLine(ScoreGrid(readiness));
        html.AppendLine("<h3>Data audit findings</h3>");
        html.AppendLine(List(readiness.DataAuditFindings, "No data-audit findings."));
        html.AppendLine("<h3>Current XYZABC pose and apparent scale</h3>");
        html.AppendLine("<p class=\"muted\">X is horizontal, Y is vertical, Z points toward the camera. A rotates around X, B rotates around Y, and C rotates around Z.</p>");
        html.AppendLine("<p class=\"muted\">Dense landmark and learned surface points carry X/Y/Z positions. A/B/C orientation belongs to the frame, pose bucket, or future local surface patch used to interpret those points.</p>");
        html.AppendLine("<div class=\"grid\">");
        html.AppendLine(Metric("X horizontal", headPose.HasFace ? $"{headPose.XHorizontalPercent:0.#}% frame" : "waiting"));
        html.AppendLine(Metric("Y vertical", headPose.HasFace ? $"{headPose.YVerticalPercent:0.#}% frame" : "waiting"));
        html.AppendLine(Metric("Z apparent distance", FormatApparentDistance(headPose)));
        html.AppendLine(Metric("Z vs reference", FormatRelativeScale(headPose)));
        html.AppendLine(Metric("Z confidence", headPose.HasFace ? $"{headPose.ZConfidencePercent:0.#}% {H(headPose.ZQualityLabel)}" : "waiting"));
        html.AppendLine(Metric("Z source", string.IsNullOrWhiteSpace(headPose.ZEstimateKind) ? "waiting" : headPose.ZEstimateKind));
        html.AppendLine(Metric("Face fill", FormatFaceFill(headPose)));
        html.AppendLine(Metric("Eye span", headPose.InterEyeFrameWidthPercent is double eyeSpan ? $"{eyeSpan:0.#}% frame" : "waiting"));
        html.AppendLine(Metric("A rotate around X", $"{headPose.ARotationAroundXDegrees:0.#} deg"));
        html.AppendLine(Metric("B rotate around Y", $"{headPose.BRotationAroundYDegrees:0.#} deg"));
        html.AppendLine(Metric("C rotate around Z", $"{headPose.CRotationAroundZDegrees:0.#} deg"));
        html.AppendLine(Metric("Pose confidence", $"{headPose.ConfidencePercent:0.#}%"));
        html.AppendLine(Metric("Rotation source", string.IsNullOrWhiteSpace(headPose.RotationSource) ? "waiting" : headPose.RotationSource));
        html.AppendLine(Metric("Distance source", string.IsNullOrWhiteSpace(headPose.DistanceSource) ? "waiting" : headPose.DistanceSource));
        html.AppendLine(Metric("Reference source", string.IsNullOrWhiteSpace(headPose.ReferenceScaleSource) ? "waiting" : headPose.ReferenceScaleSource));
        html.AppendLine("</div>");
        html.AppendLine($"<p class=\"muted\">{H(string.IsNullOrWhiteSpace(headPose.ScaleCaveat) ? "Apparent distance uses face fill and eye span in the current camera image. Calibrated physical distance can be added later." : headPose.ScaleCaveat)}</p>");
        html.AppendLine("<h3>Recent dense mesh stability</h3>");
        html.AppendLine("<div class=\"grid\">");
        html.AppendLine(Metric("Head-lock health", $"{featureStability.HealthPercent:0.#}%"));
        html.AppendLine(Metric("Head-lock samples", $"{featureStability.HeadLockedSampleCount.ToString(CultureInfo.InvariantCulture)} / {featureStability.SampleCount.ToString(CultureInfo.InvariantCulture)}"));
        html.AppendLine(Metric("Worst feature drift", $"{featureStability.WorstFeatureDriftPercent:0.#}%"));
        html.AppendLine(Metric("Stability status", featureStability.Status));
        html.AppendLine(Metric("B lock health", featureStability.YawHealthPercent <= 0d ? "waiting" : $"{featureStability.YawHealthPercent:0.#}%"));
        html.AppendLine(Metric("B range", $"{featureStability.YawRangeDegrees:0.#} deg"));
        html.AppendLine(Metric("Left/right turns", $"{featureStability.YawLeftSampleCount.ToString(CultureInfo.InvariantCulture)} / {featureStability.YawRightSampleCount.ToString(CultureInfo.InvariantCulture)}"));
        html.AppendLine(Metric("B status", featureStability.YawStatus));
        html.AppendLine(Metric("A lock health", featureStability.AHealthPercent <= 0d ? "waiting" : $"{featureStability.AHealthPercent:0.#}%"));
        html.AppendLine(Metric("A range", $"{featureStability.ARangeDegrees:0.#} deg"));
        html.AppendLine(Metric("A negative/positive", $"{featureStability.ANegativeSampleCount.ToString(CultureInfo.InvariantCulture)} / {featureStability.APositiveSampleCount.ToString(CultureInfo.InvariantCulture)}"));
        html.AppendLine(Metric("A status", featureStability.AStatus));
        html.AppendLine(Metric("C lock health", featureStability.CHealthPercent <= 0d ? "waiting" : $"{featureStability.CHealthPercent:0.#}%"));
        html.AppendLine(Metric("C range", $"{featureStability.CRangeDegrees:0.#} deg"));
        html.AppendLine(Metric("C negative/positive", $"{featureStability.CNegativeSampleCount.ToString(CultureInfo.InvariantCulture)} / {featureStability.CPositiveSampleCount.ToString(CultureInfo.InvariantCulture)}"));
        html.AppendLine(Metric("C status", featureStability.CStatus));
        html.AppendLine(Metric("Z lock health", featureStability.ZHealthPercent <= 0d ? "waiting" : $"{featureStability.ZHealthPercent:0.#}%"));
        html.AppendLine(Metric("Z face-scale range", $"{featureStability.ZFaceScaleRangePercent:0.#}%"));
        html.AppendLine(Metric("Z close/far", $"{featureStability.ZCloseSampleCount.ToString(CultureInfo.InvariantCulture)} / {featureStability.ZFarSampleCount.ToString(CultureInfo.InvariantCulture)}"));
        html.AppendLine(Metric("Z status", featureStability.ZStatus));
        html.AppendLine("</div>");
        html.AppendLine(List(featureStability.Findings, "No recent dense mesh stability findings."));
        html.AppendLine("<h3>B head-turn findings</h3>");
        html.AppendLine(List(featureStability.YawFindings, "No B-axis head-turn findings."));
        html.AppendLine("<h3>A tilt findings</h3>");
        html.AppendLine(List(featureStability.AFindings, "No A-axis tilt findings."));
        html.AppendLine("<h3>C tilt findings</h3>");
        html.AppendLine(List(featureStability.CFindings, "No C-axis tilt findings."));
        html.AppendLine("<h3>Z distance findings</h3>");
        html.AppendLine(List(featureStability.ZFindings, "No Z distance findings."));
        html.AppendLine("</section>");

        html.AppendLine("<section><h2>Why Frames Are Or Are Not Learned</h2><div class=\"grid\">");
        html.AppendLine(Metric("Frames reviewed", audit.TotalFramesReviewed.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine(Metric("Face lock rate", FormatRate(audit.FaceDetectionRate)));
        html.AppendLine(Metric("Accepted rate", FormatRate(audit.PersonalModelAcceptedRate)));
        html.AppendLine(Metric("Avatar-grade rate", FormatRate(audit.CaptureQualityAvatarGradeRate)));
        html.AppendLine(Metric("Identity confidence", audit.AverageIdentityConfidencePercent is double identityConfidence ? $"{identityConfidence:0.#}% avg" : "warming"));
        html.AppendLine(Metric("Identity outliers", $"{audit.IdentityOutlierFrames.ToString(CultureInfo.InvariantCulture)} frame(s)"));
        html.AppendLine(Metric("Tracking holds", audit.TrackingAuditHoldFrames.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine("</div>");
        html.AppendLine("<h3>Top issues</h3>");
        html.AppendLine(List(audit.TopCaptureQualityIssues, "No recurring capture-quality issues yet."));
        html.AppendLine("<h3>Next fixes</h3>");
        html.AppendLine(List(audit.TopCaptureQualitySuggestions.Concat(audit.NextActions).Distinct(StringComparer.OrdinalIgnoreCase).Take(8), "No next fixes yet."));
        html.AppendLine("</section>");

        html.AppendLine("<section><h2>Avatar Package</h2>");
        html.AppendLine($"<p>{H(package.TrainingDecision)}</p>");
        html.AppendLine($"<p class=\"muted\">{H(package.ProvenancePolicy)} {H(package.TemplatePriorPolicy)}</p>");
        html.AppendLine("<h3>Warnings</h3>");
        html.AppendLine(List(package.Warnings, "No avatar package warnings."));
        html.AppendLine("<h3>Source files</h3>");
        html.AppendLine("<table><tr><th>Name</th><th>File</th><th>Purpose</th></tr>");
        foreach (var artifact in package.SourceArtifacts)
        {
            html.AppendLine($"<tr><td>{H(artifact.Name)}</td><td>{H(artifact.FileName)}</td><td>{H(artifact.Description)}</td></tr>");
        }
        html.AppendLine("</table></section>");

        html.AppendLine("<section><h2>Next Capture Plan</h2>");
        html.AppendLine($"<p>{H(plan.CollectionDecision)}</p>");
        html.AppendLine("<table><tr><th>Priority</th><th>Task</th><th>How</th><th>Target</th></tr>");
        foreach (var item in plan.Items.Take(8))
        {
            html.AppendLine($"<tr><td>{item.Priority}</td><td>{H(item.Title)}</td><td>{H(item.Instructions)}</td><td>{item.TargetMinutes} min</td></tr>");
        }
        html.AppendLine("</table></section>");

        html.AppendLine("<section><h2>Detailed Pages</h2><div class=\"links\">");
        html.AppendLine(Link(dashboard.FacePreviewHtmlPath, "Face preview"));
        html.AppendLine(Link(dashboard.LearningDataReportHtmlPath, "Learning data health"));
        html.AppendLine(Link(dashboard.CollectionAuditHtmlPath, "Collection audit"));
        html.AppendLine(Link(dashboard.AvatarPackageHtmlPath, "Avatar package"));
        html.AppendLine(Link(dashboard.CapturePlanHtmlPath, "Capture plan"));
        html.AppendLine(Link(dashboard.LastGoodFeaturesHtmlPath, "Last 10 good features"));
        html.AppendLine("</div></section>");
        html.AppendLine("</main></body></html>");
        return html.ToString();
    }

    private static string ScoreGrid(PersonalFaceCorpusReadiness readiness)
    {
        return "<h3>Coverage</h3><div class=\"grid\">"
            + Score("Baseline", readiness.BaselineCoveragePercent)
            + Score("Learning Stability", readiness.LearningStabilityCoveragePercent)
            + Score("Motion", readiness.MotionCoveragePercent)
            + Score("Pose", readiness.PoseCoveragePercent)
            + Score("Pose buckets", readiness.PoseBucketCoveragePercent)
            + Score("Distance", readiness.DistanceCoveragePercent)
            + Score("Expression", readiness.ExpressionCoveragePercent)
            + Score("Identity", readiness.IdentityCoveragePercent)
            + Score("Identity session", readiness.IdentitySessionHealthPercent)
            + Score("Contour shape", readiness.ContourShapeCoveragePercent)
            + Score("Contour Z profile", readiness.ContourDepthProfileHealthPercent)
            + Score("Surface shape", readiness.SurfaceShapeCoveragePercent)
            + Score("Surface Z profile", readiness.SurfaceDepthProfileHealthPercent)
            + Score("Surface geometry", readiness.SurfaceGeometryHealthPercent)
            + Score("Eye glasses trust", readiness.EyeBehindGlassesTrustPercent)
            + Score("Mouth jaw trust", readiness.MouthJawTrustPercent)
            + Score("Direct feature trust", readiness.DirectFeatureMeasurementTrustPercent)
            + Score("Aperture consistency", readiness.ApertureConsistencyHealthPercent)
            + Score("Eye aperture reliability", readiness.EyeApertureReliabilityHealthPercent)
            + Score("Quality", readiness.QualityCoveragePercent)
            + Score("Capture quality", readiness.CaptureQualityCoveragePercent)
            + Score("Data audit", readiness.DataAuditHealthPercent)
            + Score("Pose estimation", readiness.PoseEstimationHealthPercent)
            + Score("Feature anchoring", readiness.FeatureAnchoringHealthPercent)
            + Score("Pose-explained features", readiness.PoseExplainedFeatureMotionHealthPercent)
            + Score("Mouth anchor", readiness.MouthVerticalAnchorHealthPercent)
            + Score("Pose bucket consistency", readiness.PoseBucketConsistencyHealthPercent)
            + Score("Jaw scale", readiness.JawDroopScaleHealthPercent)
            + Score("Journal coverage", readiness.MeasurementJournalCoveragePercent)
            + "</div>";
    }

    private static string FormatTrackingSanity(PersonalFaceCorpusReadiness readiness)
    {
        if (readiness.AcceptedBaselineSamples <= 0)
        {
            return "waiting for accepted measurements";
        }

        if (readiness.AcceptedBaselineSamples < TrackingSanityMinimumSamples)
        {
            return $"warming {readiness.AcceptedBaselineSamples.ToString(CultureInfo.InvariantCulture)}/{TrackingSanityMinimumSamples.ToString(CultureInfo.InvariantCulture)} samples";
        }

        if (readiness.DataAuditHealthPercent <= 0d)
        {
            return "waiting for data audit";
        }

        if (readiness.PoseEstimationHealthPercent is > 0d and < 60d)
        {
            return $"review pose {readiness.PoseEstimationHealthPercent:0.#}%";
        }

        if (readiness.FeatureAnchoringHealthPercent is > 0d and < 60d)
        {
            return $"review feature anchoring {readiness.FeatureAnchoringHealthPercent:0.#}%";
        }

        if (readiness.PoseExplainedFeatureMotionHealthPercent is > 0d and < 70d)
        {
            return $"review pose-explained features {readiness.PoseExplainedFeatureMotionHealthPercent:0.#}%";
        }

        if (readiness.MouthVerticalAnchorHealthPercent is > 0d and < 70d)
        {
            return $"review mouth anchor {readiness.MouthVerticalAnchorHealthPercent:0.#}%";
        }

        if (readiness.PoseBucketConsistency.ComparedPoseBucketCount > 0
            && readiness.PoseBucketConsistencyHealthPercent is > 0d and < 70d)
        {
            return $"review pose consistency {readiness.PoseBucketConsistencyHealthPercent:0.#}%";
        }

        if (readiness.ApertureConsistencyHealthPercent is > 0d and < 70d)
        {
            return $"review aperture consistency {readiness.ApertureConsistencyHealthPercent:0.#}%";
        }

        if (readiness.DataAuditHealthPercent is > 0d and < 75d)
        {
            return $"warming audit {readiness.DataAuditHealthPercent:0.#}%";
        }

        return $"healthy: pose {readiness.PoseEstimationHealthPercent:0.#}%, anchoring {readiness.FeatureAnchoringHealthPercent:0.#}%";
    }

    private static string FormatRecentMeshStability(LastGoodFeatureMeshStabilityReport stability)
    {
        if (stability.SampleCount <= 0)
        {
            return "waiting for dense mesh samples";
        }

        if (stability.HeadLockedSampleCount < 3)
        {
            return $"warming {stability.HeadLockedSampleCount.ToString(CultureInfo.InvariantCulture)}/3 samples";
        }

        return $"{stability.HealthPercent:0.#}% | worst drift {stability.WorstFeatureDriftPercent:0.#}%";
    }

    private static string FormatYawHeadTurnLock(LastGoodFeatureMeshStabilityReport stability)
    {
        if (stability.SampleCount <= 0)
        {
            return "waiting for dense mesh samples";
        }

        if (stability.HeadLockedSampleCount < 3)
        {
            return $"warming {stability.HeadLockedSampleCount.ToString(CultureInfo.InvariantCulture)}/3 samples";
        }

        if (stability.YawHealthPercent <= 0d)
        {
            return $"waiting | range {stability.YawRangeDegrees:0.#} deg";
        }

        return $"{stability.YawHealthPercent:0.#}% | range {stability.YawRangeDegrees:0.#} deg";
    }

    private static string FormatAxisLock(double healthPercent, double range, string unit)
    {
        if (healthPercent <= 0d)
        {
            return $"waiting | range {range:0.#}{unit}";
        }

        return $"{healthPercent:0.#}% | range {range:0.#}{unit}";
    }

    private static string FormatApparentDistance(HeadPoseEstimate pose)
    {
        if (!pose.HasFace || pose.ApparentDistanceUnits is not double units)
        {
            return "waiting";
        }

        return $"{units:0.##} apparent units";
    }

    private static string FormatZQuality(HeadPoseEstimate pose)
    {
        if (!pose.HasFace || pose.ApparentDistanceUnits is not double)
        {
            return "waiting";
        }

        var label = string.IsNullOrWhiteSpace(pose.ZQualityLabel) ? "Z estimate" : pose.ZQualityLabel;
        return $"{pose.ZConfidencePercent:0.#}% | {label}";
    }

    private static string FormatRelativeScale(HeadPoseEstimate pose)
    {
        if (!pose.HasFace || pose.ZRelativeToReference is not double scale)
        {
            return "waiting for learned reference";
        }

        return $"{scale:0.##}x learned ref";
    }

    private static string FormatFaceFill(HeadPoseEstimate pose)
    {
        return pose.FaceFillWidthPercent is double width && pose.FaceFillHeightPercent is double height
            ? $"{width:0.#}% x {height:0.#}%"
            : "waiting";
    }

    private static string Score(string label, double value)
    {
        var width = Math.Clamp(value, 0d, 100d).ToString("0.#", CultureInfo.InvariantCulture);
        return $"<div class=\"metric\"><div class=\"label\">{H(label)}</div><div class=\"value\">{width}%</div><div class=\"bar\"><div class=\"fill\" style=\"width:{width}%\"></div></div></div>";
    }

    private static string Metric(string label, string value)
    {
        return $"<div class=\"metric\"><div class=\"label\">{H(label)}</div><div class=\"value\">{H(value)}</div></div>";
    }

    private static string List(IEnumerable<string> values, string empty)
    {
        var items = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        if (items.Count == 0)
        {
            return $"<p class=\"muted\">{H(empty)}</p>";
        }

        return "<ul>" + string.Concat(items.Select(static item => $"<li>{H(item)}</li>")) + "</ul>";
    }

    private static string Link(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return $"<a href=\"{H(Path.GetFileName(path))}\">{H(label)}</a>";
    }

    private static string FormatRate(double value)
    {
        return value.ToString("P0", CultureInfo.InvariantCulture);
    }

    private static string H(string value)
    {
        return WebUtility.HtmlEncode(value ?? "");
    }
}
