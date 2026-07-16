using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using EpisodeMonitor.Modules.Infrastructure;

namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class MeasurementFacePreviewStore
{
    public const string JsonFileName = "measurement_face_preview.json";
    public const string HtmlFileName = "measurement_face_preview.html";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public MeasurementFacePreviewFiles Write(string folder, MeasurementFacePreviewModel preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        Directory.CreateDirectory(folder);
        var jsonPath = Path.Combine(folder, JsonFileName);
        var htmlPath = Path.Combine(folder, HtmlFileName);
        AtomicTextFileWriter.WriteAllText(jsonPath, JsonSerializer.Serialize(preview, JsonOptions), Encoding.UTF8);
        AtomicTextFileWriter.WriteAllText(htmlPath, BuildHtml(preview), Encoding.UTF8);
        return new MeasurementFacePreviewFiles(jsonPath, htmlPath);
    }

    private static string BuildHtml(MeasurementFacePreviewModel preview)
    {
        var title = $"{preview.SubjectDisplayName} measurement face preview";
        var warnings = preview.Warnings.Count == 0
            ? "<li>No preview warnings.</li>"
            : string.Concat(preview.Warnings.Select(warning => $"<li>{Escape(warning)}</li>"));
        var metrics = string.Concat(preview.Metrics.Select(metric =>
            $"<tr><th>{Escape(metric.Key)}</th><td>{Format(metric.Value)}</td></tr>"));
        var previewStage = preview.CanRender
            ? BuildPreviewStage(preview)
            : "<div class=\"disabled\">Preview paused until the subject gate is accepted and measurements are available.</div>";

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
  --face: #65c8ff;
  --eye: #8ff2c5;
  --mouth: #ff9fbd;
  --jaw: #ffd166;
  --nose: #d9e8ff;
}
* { box-sizing: border-box; }
body {
  margin: 0;
  background: var(--bg);
  color: var(--text);
  font: 14px/1.45 "Segoe UI", Arial, sans-serif;
}
main {
  display: grid;
  grid-template-columns: minmax(360px, 1fr) minmax(320px, 420px);
  gap: 16px;
  padding: 16px;
}
h1 {
  margin: 0 0 4px;
  font-size: 22px;
}
.subtle { color: var(--muted); }
.stage, .panel {
  border: 1px solid var(--line);
  background: var(--panel);
  border-radius: 6px;
}
.stage {
  min-height: 560px;
  display: grid;
  grid-template-rows: minmax(420px, 1fr) auto;
  gap: 12px;
  overflow: hidden;
  padding: 12px;
}
.viewer {
  position: relative;
  min-height: 420px;
}
canvas {
  display: block;
  width: 100%;
  height: 100%;
  min-height: 420px;
  border: 1px solid #193149;
  background: #061019;
  cursor: grab;
  touch-action: none;
}
canvas[data-dragging="true"] {
  cursor: grabbing;
}
.viewer-overlay {
  position: absolute;
  left: 12px;
  top: 12px;
  color: var(--muted);
  background: rgba(5, 11, 16, 0.76);
  border: 1px solid #193149;
  padding: 8px 10px;
  max-width: min(520px, calc(100% - 24px));
  pointer-events: none;
}
.legend {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 12px;
  color: var(--muted);
  font-size: 12px;
}
.swatch {
  display: inline-block;
  width: 18px;
  height: 3px;
  margin-right: 6px;
  vertical-align: middle;
}
svg {
  width: min(92vw, 780px);
  height: auto;
  justify-self: center;
}
polyline {
  fill: none;
  stroke-width: 3;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.face { stroke: var(--face); }
.eye { stroke: var(--eye); }
.mouth, .mouth-opening { stroke: var(--mouth); }
.jaw, .jaw-droop { stroke: var(--jaw); }
.nose { stroke: var(--nose); }
.point { fill: #dcefff; opacity: 0.72; }
.panel { padding: 14px; }
table {
  width: 100%;
  border-collapse: collapse;
}
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
ul {
  margin: 8px 0 0;
  padding-left: 18px;
}
.disabled {
  max-width: 480px;
  color: var(--muted);
  text-align: center;
  padding: 24px;
}
@media (max-width: 900px) {
  main { grid-template-columns: 1fr; }
}
</style>
</head>
<body>
<main>
  <section class="stage" aria-label="Measurement face wireframe">
    {{previewStage}}
  </section>
  <aside class="panel">
    <h1>{{Escape(title)}}</h1>
    <div class="subtle">{{Escape(preview.RenderDecision)}} | confidence {{Format(preview.ConfidencePercent)}}%</div>
    <div class="subtle">Live report auto-refreshes every 5 seconds from the latest saved measurement snapshot.</div>
    <p>{{Escape(preview.StoragePolicy)}}</p>
    <p>{{Escape(preview.SafetyBoundary)}}</p>
    <table>
      <tr><th>Subject</th><td>{{Escape(preview.SubjectDisplayName)}} ({{Escape(preview.SubjectId)}})</td></tr>
      <tr><th>Accepted samples</th><td>{{preview.AcceptedSamples.ToString(CultureInfo.InvariantCulture)}} / {{preview.ObservedSamples.ToString(CultureInfo.InvariantCulture)}}</td></tr>
      <tr><th>Accepted weight</th><td>{{Format(preview.AcceptedSampleWeight)}}</td></tr>
      <tr><th>Geometry provenance</th><td>{{Escape(preview.GeometryProvenance)}}</td></tr>
      <tr><th>Measured contribution</th><td>{{Format(preview.MeasurementContributionPercent)}}%</td></tr>
      <tr><th>Template prior contribution</th><td>{{Format(preview.TemplatePriorContributionPercent)}}%</td></tr>
      <tr><th>Subject gate</th><td>{{Escape(preview.SubjectGate.GateDecision)}} - {{Escape(preview.SubjectGate.Reason)}}</td></tr>
      {{metrics}}
    </table>
    <h2>Warnings</h2>
    <ul>{{warnings}}</ul>
  </aside>
</main>
</body>
</html>
""";
    }

    private static string BuildPreviewStage(MeasurementFacePreviewModel preview)
    {
        var sceneJson = JsonSerializer.Serialize(new PreviewScene(
            preview.Points,
            preview.Polylines,
            preview.MeasurementContributionPercent,
            preview.TemplatePriorContributionPercent,
            preview.GeometryProvenance));
        return $$"""
<div class="viewer">
  <canvas id="face3d" aria-label="Interactive 3D measurement face construction" title="Drag to rotate. Use the mouse wheel to zoom. Double-click to reset."></canvas>
  <div class="viewer-overlay">
    <strong>{{Escape(preview.GeometryProvenance)}}</strong><br>
    Measured {{Format(preview.MeasurementContributionPercent)}}% | scaffold {{Format(preview.TemplatePriorContributionPercent)}}%
  </div>
</div>
<div class="legend">
  <span><span class="swatch" style="background:var(--face)"></span>face</span>
  <span><span class="swatch" style="background:var(--eye)"></span>eyes</span>
  <span><span class="swatch" style="background:var(--mouth)"></span>lips</span>
  <span><span class="swatch" style="background:var(--jaw)"></span>jaw</span>
  <span><span class="swatch" style="background:var(--nose)"></span>nose</span>
</div>
<details>
  <summary>2D outline fallback</summary>
  {{BuildSvg(preview)}}
</details>
<script type="application/json" id="face-preview-scene">{{sceneJson}}</script>
<script>
(() => {
  const canvas = document.getElementById('face3d');
  const dataNode = document.getElementById('face-preview-scene');
  if (!canvas || !dataNode) return;
  const scene = JSON.parse(dataNode.textContent || '{}');
  const ctx = canvas.getContext('2d');
  if (!ctx) return;

  const colors = {
    face: '#65c8ff',
    eye: '#8ff2c5',
    mouth: '#ff9fbd',
    'mouth-opening': '#ff9fbd',
    jaw: '#ffd166',
    'jaw-droop': '#ffd166',
    nose: '#d9e8ff'
  };
  const points = new Map((scene.Points || []).map(point => [point.Id, point]));
  const storageKey = `EpisodeMonitor.FacePreview3D.${scene.GeometryProvenance || 'default'}`;
  const defaultView = { yaw: -0.34, pitch: -0.08, zoom: 1 };
  let rotation = { ...defaultView };
  try {
    const stored = JSON.parse(localStorage.getItem(storageKey) || 'null');
    if (stored && Number.isFinite(stored.yaw) && Number.isFinite(stored.pitch)) {
      rotation = {
        yaw: stored.yaw,
        pitch: stored.pitch,
        zoom: Number.isFinite(stored.zoom) ? Math.max(0.55, Math.min(2.2, stored.zoom)) : 1
      };
    }
  } catch {}
  let dragging = false;
  let last = null;

  canvas.addEventListener('pointerdown', event => {
    dragging = true;
    last = { x: event.clientX, y: event.clientY };
    canvas.dataset.dragging = 'true';
    canvas.setPointerCapture(event.pointerId);
  });
  canvas.addEventListener('pointermove', event => {
    if (!dragging || !last) return;
    rotation.yaw += (event.clientX - last.x) * 0.008;
    rotation.pitch = Math.max(-0.9, Math.min(0.9, rotation.pitch + (event.clientY - last.y) * 0.006));
    last = { x: event.clientX, y: event.clientY };
    saveView();
    draw();
  });
  canvas.addEventListener('pointerup', releaseDrag);
  canvas.addEventListener('pointercancel', releaseDrag);
  canvas.addEventListener('wheel', event => {
    event.preventDefault();
    rotation.zoom = Math.max(0.55, Math.min(2.2, rotation.zoom * (event.deltaY < 0 ? 1.08 : 0.92)));
    saveView();
    draw();
  }, { passive: false });
  canvas.addEventListener('dblclick', () => {
    rotation = { ...defaultView };
    saveView();
    draw();
  });

  const resize = () => {
    const rect = canvas.getBoundingClientRect();
    const width = Math.max(320, Math.round(rect.width));
    const height = Math.max(360, Math.round(rect.height));
    const scale = window.devicePixelRatio || 1;
    canvas.width = Math.round(width * scale);
    canvas.height = Math.round(height * scale);
    ctx.setTransform(scale, 0, 0, scale, 0, 0);
    draw();
  };
  new ResizeObserver(resize).observe(canvas);
  resize();

  function project(point) {
    const yaw = rotation.yaw;
    const pitch = rotation.pitch;
    const cosY = Math.cos(yaw);
    const sinY = Math.sin(yaw);
    const cosP = Math.cos(pitch);
    const sinP = Math.sin(pitch);
    const x1 = point.X * cosY + point.Z * sinY;
    const z1 = -point.X * sinY + point.Z * cosY;
    const y1 = point.Y * cosP - z1 * sinP;
    const z2 = point.Y * sinP + z1 * cosP;
    const rect = canvas.getBoundingClientRect();
    const depth = 1.45 + z2;
    const zoom = Math.min(rect.width, rect.height) * 0.82 * rotation.zoom / Math.max(0.35, depth);
    return {
      x: rect.width * 0.5 + x1 * zoom,
      y: rect.height * 0.52 + y1 * zoom,
      z: z2,
      scale: Math.max(0.35, Math.min(1.8, 1 / Math.max(0.35, depth)))
    };
  }

  function releaseDrag() {
    dragging = false;
    last = null;
    delete canvas.dataset.dragging;
  }

  function saveView() {
    try { localStorage.setItem(storageKey, JSON.stringify(rotation)); } catch {}
  }

  function draw() {
    const rect = canvas.getBoundingClientRect();
    ctx.clearRect(0, 0, rect.width, rect.height);
    const background = ctx.createLinearGradient(0, 0, 0, rect.height);
    background.addColorStop(0, '#061019');
    background.addColorStop(1, '#02070b');
    ctx.fillStyle = background;
    ctx.fillRect(0, 0, rect.width, rect.height);
    drawGrid(rect);

    const lines = (scene.Polylines || []).map(line => {
      const projected = (line.PointIds || [])
        .map(id => points.get(id))
        .filter(Boolean)
        .map(project);
      const depth = projected.length ? projected.reduce((sum, point) => sum + point.z, 0) / projected.length : 0;
      return { line, projected, depth };
    }).filter(item => item.projected.length >= 2)
      .sort((a, b) => a.depth - b.depth);

    for (const item of lines) {
      const color = colors[item.line.Role] || '#dcefff';
      const confidence = Number.isFinite(item.line.ConfidencePercent) ? item.line.ConfidencePercent : 50;
      const templateHeavy = (item.line.Provenance || '').toLowerCase().includes('template');
      ctx.save();
      ctx.globalAlpha = Math.max(0.26, Math.min(0.95, confidence / 100));
      ctx.strokeStyle = color;
      ctx.lineWidth = templateHeavy ? 1.4 : 2.6;
      if (templateHeavy) ctx.setLineDash([7, 6]);
      ctx.lineCap = 'round';
      ctx.lineJoin = 'round';
      ctx.beginPath();
      ctx.moveTo(item.projected[0].x, item.projected[0].y);
      for (let index = 1; index < item.projected.length; index++) {
        ctx.lineTo(item.projected[index].x, item.projected[index].y);
      }
      ctx.stroke();
      ctx.restore();
    }

    const projectedPoints = Array.from(points.values())
      .map(point => ({ source: point, projected: project(point) }))
      .sort((a, b) => a.projected.z - b.projected.z);
    for (const item of projectedPoints) {
      const confidence = Number.isFinite(item.source.ConfidencePercent) ? item.source.ConfidencePercent : 50;
      ctx.globalAlpha = Math.max(0.28, Math.min(0.8, confidence / 120));
      ctx.fillStyle = colors[item.source.Role] || '#dcefff';
      const radius = Math.max(1.2, 2.4 * item.projected.scale);
      ctx.beginPath();
      ctx.arc(item.projected.x, item.projected.y, radius, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.globalAlpha = 1;
  }

  function drawGrid(rect) {
    ctx.save();
    ctx.strokeStyle = '#132638';
    ctx.lineWidth = 1;
    for (let x = rect.width * 0.16; x <= rect.width * 0.84; x += rect.width * 0.085) {
      ctx.beginPath();
      ctx.moveTo(x, rect.height * 0.12);
      ctx.lineTo(x, rect.height * 0.88);
      ctx.stroke();
    }
    for (let y = rect.height * 0.14; y <= rect.height * 0.88; y += rect.height * 0.09) {
      ctx.beginPath();
      ctx.moveTo(rect.width * 0.12, y);
      ctx.lineTo(rect.width * 0.88, y);
      ctx.stroke();
    }
    ctx.restore();
  }
})();
</script>
""";
    }

    private static string BuildSvg(MeasurementFacePreviewModel preview)
    {
        var pointsById = preview.Points.ToDictionary(static point => point.Id, StringComparer.OrdinalIgnoreCase);
        var polylines = new StringBuilder();
        foreach (var line in preview.Polylines)
        {
            var coordinates = line.PointIds
                .Where(pointsById.ContainsKey)
                .Select(id => Project(pointsById[id]))
                .ToList();
            if (coordinates.Count < 2)
            {
                continue;
            }

            polylines.AppendLine(
                $"<polyline class=\"{EscapeAttribute(line.Role)}\" points=\"{string.Join(" ", coordinates)}\"><title>{Escape(line.Label)} | {Escape(line.Provenance)} | confidence {Format(line.ConfidencePercent)}%</title></polyline>");
        }

        var points = string.Concat(preview.Points.Select(point =>
        {
            var (x, y) = ProjectTuple(point);
            return $"<circle class=\"point\" cx=\"{Format(x)}\" cy=\"{Format(y)}\" r=\"2.4\"><title>{Escape(point.Label)}: {Escape(point.Id)} | {Escape(point.Provenance)} | confidence {Format(point.ConfidencePercent)}%</title></circle>";
        }));

        return $$"""
<svg viewBox="0 0 900 900" role="img" aria-label="Measurement-only face wireframe">
  <rect x="0" y="0" width="900" height="900" fill="#061019"/>
  <line x1="450" y1="120" x2="450" y2="780" stroke="#193149" stroke-width="1"/>
  <line x1="170" y1="450" x2="730" y2="450" stroke="#193149" stroke-width="1"/>
  {{polylines}}
  {{points}}
</svg>
""";
    }

    private static string Project(MeasurementFacePreviewPoint point)
    {
        var (x, y) = ProjectTuple(point);
        return $"{Format(x)},{Format(y)}";
    }

    private static (double X, double Y) ProjectTuple(MeasurementFacePreviewPoint point)
    {
        const double center = 450d;
        const double scale = 780d;
        var depthLift = Math.Clamp(point.Z, -0.25d, 0.25d) * 46d;
        return (center + point.X * scale, center + point.Y * scale - depthLift);
    }

    private static string Escape(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static string EscapeAttribute(string value)
    {
        var cleaned = new string(value
            .Where(static character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "line" : cleaned;
    }

    private static string Format(double? value)
    {
        return value is double number
            ? Format(number)
            : "";
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

internal sealed record PreviewScene(
    IReadOnlyList<MeasurementFacePreviewPoint> Points,
    IReadOnlyList<MeasurementFacePreviewPolyline> Polylines,
    double MeasurementContributionPercent,
    double TemplatePriorContributionPercent,
    string GeometryProvenance);

public sealed record MeasurementFacePreviewFiles(string JsonPath, string HtmlPath);
