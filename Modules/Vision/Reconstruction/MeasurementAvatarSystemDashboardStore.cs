using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using EpisodeMonitor.Modules.Infrastructure;
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

        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"5\"><title>Avatar System</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{margin:0;background:#080d12;color:#f5f8fb;font-family:Segoe UI,Arial,sans-serif;line-height:1.45}main{max-width:1180px;margin:0 auto;padding:28px}section{border:1px solid #243545;background:#101820;margin:16px 0;padding:18px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px}.metric{background:#0c1218;border:1px solid #1d2c38;padding:12px}.label{color:#b9d7ef;font-size:12px;text-transform:uppercase;letter-spacing:.04em}.value{font-size:22px;font-weight:700}.good{color:#80e0a4}.warn{color:#ffd27a}.bad{color:#ff9a9a}.muted{color:#b9d7ef}a{color:#8fc7ff}.bar{height:8px;background:#1d2c38;margin-top:8px}.fill{height:8px;background:#4fa3d1}ul{padding-left:20px}li{margin:5px 0}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #243545;padding:8px;text-align:left}th{color:#b9d7ef;font-weight:600}.links a{display:inline-block;margin:0 12px 8px 0}</style>");
        html.AppendLine("</head><body><main>");
        html.AppendLine($"<h1>Avatar System</h1><p class=\"muted\">Live report auto-refreshes every 5 seconds. Last updated {H(dashboard.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))}.</p><p class=\"muted\">{H(dashboard.StoragePolicy)}</p>");
        html.AppendLine("<section>");
        html.AppendLine($"<h2 class=\"{learningClass}\">{H(dashboard.AvatarLearningStatus)}</h2>");
        html.AppendLine($"<p>{H(dashboard.AvatarLearningCorrection)}</p>");
        html.AppendLine("<div class=\"grid\">");
        html.AppendLine(Metric("Subject confirmed", dashboard.SubjectConfirmed ? "Yes" : "No"));
        html.AppendLine(Metric("Learning switch", dashboard.AvatarLearningRequested ? "Started" : "Stopped"));
        html.AppendLine(Metric("Currently collecting", dashboard.AvatarLearningActive ? "Yes" : "No"));
        html.AppendLine(Metric("Current quality", $"{quality.Label} {quality.ScorePercent:0}%"));
        html.AppendLine(Metric("Measured geometry", $"{package.MeasurementContributionPercent:0.#}%"));
        html.AppendLine(Metric("Template scaffold", $"{package.TemplatePriorContributionPercent:0.#}%"));
        html.AppendLine("</div></section>");

        html.AppendLine("<section><h2>Face Learning Data</h2><div class=\"grid\">");
        html.AppendLine(Metric("Accepted face samples", model.AcceptedSamples.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine(Metric("Reviewed samples", model.ObservedSamples.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine(Metric("Learning anchor", $"{model.LearningStability.AnchorPercent:0.#}% {model.LearningStability.AnchorStatus}"));
        html.AppendLine(Metric("Next sample influence", $"{model.LearningStability.MaximumNextSampleInfluencePercent:0.##}% max"));
        html.AppendLine(Metric("Readiness", $"{readiness.OverallReadinessPercent:0.#}%"));
        html.AppendLine(Metric("Identity samples", model.IdentitySignatureSamples.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine(Metric("Pose buckets", $"{readiness.PoseBucketCoveredCount.ToString(CultureInfo.InvariantCulture)} / {readiness.PoseBucketRequiredCount.ToString(CultureInfo.InvariantCulture)}"));
        html.AppendLine(Metric("Motion observations", motion.UsableObservationCount.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine(Metric("Motion pairs", motion.MotionPairCount.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine("</div>");
        html.AppendLine($"<p class=\"muted\">{H(model.LearningStability.Guidance)}</p>");
        html.AppendLine(ScoreGrid(readiness));
        html.AppendLine("</section>");

        html.AppendLine("<section><h2>Why Frames Are Or Are Not Learned</h2><div class=\"grid\">");
        html.AppendLine(Metric("Frames reviewed", audit.TotalFramesReviewed.ToString(CultureInfo.InvariantCulture)));
        html.AppendLine(Metric("Face lock rate", FormatRate(audit.FaceDetectionRate)));
        html.AppendLine(Metric("Accepted rate", FormatRate(audit.PersonalModelAcceptedRate)));
        html.AppendLine(Metric("Avatar-grade rate", FormatRate(audit.CaptureQualityAvatarGradeRate)));
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
            + Score("Contour shape", readiness.ContourShapeCoveragePercent)
            + Score("Eye glasses trust", readiness.EyeBehindGlassesTrustPercent)
            + Score("Mouth jaw trust", readiness.MouthJawTrustPercent)
            + Score("Direct feature trust", readiness.DirectFeatureMeasurementTrustPercent)
            + Score("Quality", readiness.QualityCoveragePercent)
            + Score("Capture quality", readiness.CaptureQualityCoveragePercent)
            + "</div>";
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
