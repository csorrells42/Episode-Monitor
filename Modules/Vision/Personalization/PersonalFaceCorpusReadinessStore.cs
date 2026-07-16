using System.IO;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using EpisodeMonitor.Modules.Infrastructure;

namespace EpisodeMonitor.Modules.Vision.Personalization;

public sealed class PersonalFaceCorpusReadinessStore
{
    public const string DefaultJsonFileName = "personal_face_corpus_readiness.json";
    public const string DefaultHtmlFileName = "personal_face_corpus_readiness.html";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string FileName { get; }

    public PersonalFaceCorpusReadinessStore(string fileName = DefaultJsonFileName)
    {
        FileName = fileName;
    }

    public string Write(string folder, PersonalFaceCorpusReadiness model)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName);
        AtomicTextFileWriter.WriteAllText(path, JsonSerializer.Serialize(model, JsonOptions), Encoding.UTF8);
        AtomicTextFileWriter.WriteAllText(GetHtmlPath(path), BuildHtml(model), Encoding.UTF8);
        return path;
    }

    public static string WriteFile(string path, PersonalFaceCorpusReadiness model)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
        AtomicTextFileWriter.WriteAllText(path, JsonSerializer.Serialize(model, JsonOptions), Encoding.UTF8);
        AtomicTextFileWriter.WriteAllText(GetHtmlPath(path), BuildHtml(model), Encoding.UTF8);
        return path;
    }

    public static string GetHtmlPath(string jsonPath)
    {
        return Path.ChangeExtension(jsonPath, ".html");
    }

    private static string BuildHtml(PersonalFaceCorpusReadiness readiness)
    {
        var title = $"{readiness.SubjectDisplayName} learning data health";
        var warnings = BuildList(readiness.Warnings, "No readiness warnings.");
        var suggestions = BuildList(readiness.NextCaptureSuggestions, "No next capture suggestions.");
        var strengths = BuildList(readiness.Strengths, "No strong learning-data areas yet.");
        var rows = string.Concat(new[]
        {
            MetricRow("Accepted baseline samples", readiness.AcceptedBaselineSamples.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Learning anchor", $"{Format(readiness.LearningAnchorPercent)}% ({readiness.LearningAnchorStatus})"),
            MetricRow("Max next-sample influence", $"{Format(readiness.MaximumNextSampleInfluencePercent)}%"),
            MetricRow("Max event-like influence", $"{Format(readiness.MaximumEventLikeNextSampleInfluencePercent)}%"),
            MetricRow("Recent samples reviewed", readiness.RecentMeasurementSamplesReviewed.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Motion observations", readiness.MotionUsableObservations.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Motion pairs", readiness.MotionPairs.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Pose buckets covered", $"{readiness.PoseBucketCoveredCount.ToString(CultureInfo.InvariantCulture)} / {readiness.PoseBucketRequiredCount.ToString(CultureInfo.InvariantCulture)}"),
            MetricRow("Identity signature samples", readiness.IdentitySignatureSamples.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Left eye shape samples", readiness.LeftEyeShapeSamples.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Right eye shape samples", readiness.RightEyeShapeSamples.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Outer lip shape samples", readiness.OuterLipShapeSamples.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Inner lip shape samples", readiness.InnerLipShapeSamples.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Jaw shape samples", readiness.JawShapeSamples.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Eye behind glasses trust", $"{Format(readiness.EyeBehindGlassesTrustPercent)}%"),
            MetricRow("Mouth/jaw trust", $"{Format(readiness.MouthJawTrustPercent)}%"),
            MetricRow("Direct feature trust", $"{Format(readiness.DirectFeatureMeasurementTrustPercent)}%"),
            MetricRow("Measurement storage", $"{FormatBytes(readiness.MeasurementJournalBytes)} / {FormatBytes(readiness.MeasurementBudgetBytes)} ({Format(readiness.MeasurementBudgetUsedPercent)}%)"),
            MetricRow("Capture quality samples", readiness.CaptureQualitySamples.ToString(CultureInfo.InvariantCulture)),
            MetricRow("Capture collectable rate", FormatRate(readiness.CaptureQualityCanCollectRate)),
            MetricRow("Capture avatar-grade rate", FormatRate(readiness.CaptureQualityAvatarGradeRate)),
            MetricRow("Average capture quality", $"{Format(readiness.AverageCaptureQualityScorePercent)}%"),
            MetricRow("Minimum capture quality", $"{Format(readiness.MinimumCaptureQualityScorePercent)}%"),
            MetricRow("Average camera mode score", $"{Format(readiness.AverageCaptureQualityCameraModeScorePercent)}%"),
            MetricRow("Average face scale score", $"{Format(readiness.AverageCaptureQualityFaceScaleScorePercent)}%"),
            MetricRow("Average eye evidence score", $"{Format(readiness.AverageCaptureQualityEyeScorePercent)}%"),
            MetricRow("Average mouth evidence score", $"{Format(readiness.AverageCaptureQualityMouthScorePercent)}%"),
            MetricRow("Average glasses score", $"{Format(readiness.AverageCaptureQualityGlassesScorePercent)}%"),
            MetricRow("Face width range", Format(readiness.FaceWidthRange)),
            MetricRow("Face height range", Format(readiness.FaceHeightRange)),
            MetricRow("Head yaw range", $"{Format(readiness.HeadYawRangeDegrees)} deg"),
            MetricRow("Head pitch range", $"{Format(readiness.HeadPitchRangeDegrees)} deg"),
            MetricRow("Head roll range", $"{Format(readiness.HeadRollRangeDegrees)} deg"),
            MetricRow("Eye opening range", Format(readiness.EyeOpeningRange)),
            MetricRow("Mouth opening range", Format(readiness.MouthOpeningRange)),
            MetricRow("Jaw droop range", Format(readiness.JawDroopRange)),
            MetricRow("MediaPipe blink range", $"{Format(readiness.MediaPipeBlinkRangePercent)}%"),
            MetricRow("MediaPipe jaw-open range", $"{Format(readiness.MediaPipeJawOpenRangePercent)}%"),
            MetricRow("Face aspect range", Format(readiness.FaceAspectRatioRange)),
            MetricRow("Eye spacing range", Format(readiness.InterEyeDistanceToFaceWidthRange)),
            MetricRow("Mouth width range", Format(readiness.MouthWidthToFaceWidthRange)),
            MetricRow("Eye artifact suppressed rate", FormatRate(readiness.EyeArtifactSuppressedRate)),
            MetricRow("Eye reconstructed rate", FormatRate(readiness.EyeReconstructedRate)),
            MetricRow("Mouth reconstructed rate", FormatRate(readiness.MouthReconstructedRate))
        });
        var bars = string.Concat(new[]
        {
            ScoreBar("Overall", readiness.OverallReadinessPercent),
            ScoreBar("Baseline", readiness.BaselineCoveragePercent),
            ScoreBar("Learning Stability", readiness.LearningStabilityCoveragePercent),
            ScoreBar("Motion", readiness.MotionCoveragePercent),
            ScoreBar("Pose", readiness.PoseCoveragePercent),
            ScoreBar("Pose Buckets", readiness.PoseBucketCoveragePercent),
            ScoreBar("Distance", readiness.DistanceCoveragePercent),
            ScoreBar("Expression", readiness.ExpressionCoveragePercent),
            ScoreBar("Identity", readiness.IdentityCoveragePercent),
            ScoreBar("Contour Shape", readiness.ContourShapeCoveragePercent),
            ScoreBar("Eye Glasses Trust", readiness.EyeBehindGlassesTrustPercent),
            ScoreBar("Mouth Jaw Trust", readiness.MouthJawTrustPercent),
            ScoreBar("Direct Feature Trust", readiness.DirectFeatureMeasurementTrustPercent),
            ScoreBar("Quality", readiness.QualityCoveragePercent),
            ScoreBar("Capture Quality", readiness.CaptureQualityCoveragePercent),
            ScoreBar("Storage", readiness.StorageHealthPercent)
        });
        var poseBuckets = BuildPoseBucketTable(readiness.PoseBuckets);

        return $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="refresh" content="5">
<title>{{Escape(title)}}</title>
<style>
:root {
  color-scheme: dark;
  --bg: #050b10;
  --panel: #0b141c;
  --line: #28435b;
  --text: #e7f6ff;
  --muted: #9db7c9;
  --accent: #65c8ff;
  --good: #8ff2c5;
  --warn: #ffd166;
  --bad: #ff9fbd;
}
* { box-sizing: border-box; }
body {
  margin: 0;
  background: var(--bg);
  color: var(--text);
  font: 14px/1.45 "Segoe UI", Arial, sans-serif;
}
main {
  max-width: 1180px;
  margin: 0 auto;
  padding: 18px;
}
h1 { margin: 0 0 4px; font-size: 24px; }
h2 { margin: 0 0 10px; font-size: 17px; }
.subtle { color: var(--muted); }
.grid {
  display: grid;
  grid-template-columns: minmax(320px, 1.2fr) minmax(300px, 0.8fr);
  gap: 14px;
  margin-top: 14px;
}
.panel {
  border: 1px solid var(--line);
  background: var(--panel);
  border-radius: 6px;
  padding: 14px;
}
.score {
  display: grid;
  grid-template-columns: 96px 1fr 58px;
  gap: 10px;
  align-items: center;
  margin: 10px 0;
}
.track {
  height: 12px;
  border: 1px solid #274359;
  background: #07141c;
  border-radius: 999px;
  overflow: hidden;
}
.fill {
  height: 100%;
  width: var(--value);
  background: linear-gradient(90deg, var(--bad), var(--warn), var(--good));
}
table { width: 100%; border-collapse: collapse; }
th, td {
  border-bottom: 1px solid #1c3042;
  padding: 6px 0;
  vertical-align: top;
}
th {
  text-align: left;
  color: var(--muted);
  font-weight: 600;
}
ul { margin: 8px 0 0; padding-left: 18px; }
li { margin: 5px 0; }
.hero {
  display: flex;
  justify-content: space-between;
  gap: 14px;
  align-items: end;
}
.big {
  font-size: 42px;
  line-height: 1;
  color: var(--accent);
}
@media (max-width: 860px) {
  .grid { grid-template-columns: 1fr; }
  .hero { display: block; }
}
</style>
</head>
<body>
<main>
  <section class="panel hero">
    <div>
      <h1>{{Escape(title)}}</h1>
      <div class="subtle">{{Escape(readiness.Status)}} | {{Escape(readiness.StoragePolicy)}}</div>
      <div class="subtle">Live report auto-refreshes every 5 seconds from the latest saved measurement snapshot.</div>
      <div class="subtle">Subject: {{Escape(readiness.SubjectDisplayName)}} ({{Escape(readiness.SubjectId)}}) | Unknown subject policy: {{Escape(readiness.UnknownSubjectPolicy)}}</div>
    </div>
    <div class="big">{{Format(readiness.OverallReadinessPercent)}}%</div>
  </section>
  <section class="grid">
    <div class="panel">
      <h2>Coverage Scores</h2>
      {{bars}}
    </div>
    <div class="panel">
      <h2>Next Capture Suggestions</h2>
      <ul>{{suggestions}}</ul>
    </div>
    <div class="panel">
      <h2>Learning Data Measurements</h2>
      <table>{{rows}}</table>
    </div>
    <div class="panel">
      <h2>Pose Buckets</h2>
      {{poseBuckets}}
    </div>
    <div class="panel">
      <h2>Warnings</h2>
      <ul>{{warnings}}</ul>
      <h2 style="margin-top:16px">Strengths</h2>
      <ul>{{strengths}}</ul>
    </div>
  </section>
</main>
</body>
</html>
""";
    }

    private static string ScoreBar(string label, double value)
    {
        var clamped = Math.Clamp(value, 0d, 100d);
        return $"<div class=\"score\"><div>{Escape(label)}</div><div class=\"track\"><div class=\"fill\" style=\"--value:{Format(clamped)}%\"></div></div><div>{Format(clamped)}%</div></div>";
    }

    private static string MetricRow(string label, string value)
    {
        return $"<tr><th>{Escape(label)}</th><td>{Escape(value)}</td></tr>";
    }

    private static string BuildPoseBucketTable(IReadOnlyList<PersonalFacePoseBucketProfile> buckets)
    {
        if (buckets.Count == 0)
        {
            return "<p class=\"subtle\">No pose buckets available yet.</p>";
        }

        var rows = string.Concat(buckets.Select(bucket =>
            $"<tr><th>{Escape(bucket.Label)}</th><td>{bucket.SampleCount.ToString(CultureInfo.InvariantCulture)}</td><td>{Format(bucket.TotalWeight)}</td><td>{Format(bucket.HeadYawDegrees.Average)}</td><td>{Format(bucket.HeadPitchDegrees.Average)}</td><td>{Format(bucket.HeadRollDegrees.Average)}</td></tr>"));
        return $"<table><tr><th>Pose</th><th>Samples</th><th>Weight</th><th>Yaw</th><th>Pitch</th><th>Roll</th></tr>{rows}</table>";
    }

    private static string BuildList(IReadOnlyCollection<string> values, string emptyText)
    {
        return values.Count == 0
            ? $"<li>{Escape(emptyText)}</li>"
            : string.Concat(values.Select(value => $"<li>{Escape(value)}</li>"));
    }

    private static string FormatRate(double? rate)
    {
        return rate is double value ? $"{Format(value * 100d)}%" : "";
    }

    private static string Format(double? value)
    {
        return value is double number ? Format(number) : "";
    }

    private static string Format(double value)
    {
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024L)
        {
            return $"{Math.Max(0L, bytes).ToString(CultureInfo.InvariantCulture)} B";
        }

        var size = (double)bytes;
        string[] units = ["KB", "MB", "GB"];
        var unitIndex = -1;
        do
        {
            size /= 1024d;
            unitIndex++;
        }
        while (size >= 1024d && unitIndex < units.Length - 1);

        return $"{size.ToString("0.#", CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }

    private static string Escape(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
