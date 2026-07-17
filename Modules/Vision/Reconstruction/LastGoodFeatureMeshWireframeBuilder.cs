using EpisodeMonitor.Modules.Vision.Common;

namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public static class LastGoodFeatureMeshWireframeBuilder
{
    private const int MinimumSurfacePointCount = 24;
    private const double MaximumScaffoldEdgeLengthPercent = 42d;

    private static readonly (int From, int To)[] CuratedSurfaceScaffoldEdges =
    [
        (109, 70), (67, 63), (103, 105), (54, 107), (10, 168),
        (284, 336), (332, 334), (297, 293), (338, 300),

        (70, 33), (63, 246), (105, 161), (66, 160), (107, 159),
        (55, 158), (65, 157), (52, 173), (53, 133), (46, 133),
        (336, 362), (296, 398), (334, 384), (293, 385), (300, 386),
        (285, 387), (295, 388), (282, 466), (283, 263), (276, 263),

        (33, 234), (7, 93), (163, 132), (145, 58), (133, 168), (133, 6),
        (362, 168), (362, 6), (263, 454), (249, 323), (390, 361), (374, 288),

        (168, 107), (168, 336), (6, 197), (197, 5), (195, 4), (5, 1),
        (4, 19), (19, 94), (94, 2),
        (98, 61), (97, 0), (2, 13), (326, 267), (327, 291),
        (98, 234), (97, 132), (326, 361), (327, 454),

        (234, 33), (93, 7), (132, 163), (58, 145), (172, 61), (136, 91),
        (454, 263), (323, 249), (361, 390), (288, 374), (397, 291), (365, 321),

        (61, 172), (146, 172), (91, 150), (84, 176), (17, 152),
        (314, 400), (321, 397), (291, 397),

        (127, 70), (162, 63), (21, 105), (251, 336), (389, 296), (356, 334),
        (234, 127), (454, 356), (152, 17), (148, 84), (377, 314)
    ];

    public static List<LastGoodFeatureMeshWireframeEdge> Build(
        IReadOnlyList<FaceMeshLandmarkPoint> points,
        IReadOnlyList<LastGoodFeatureMeshFeatureGroup> featureGroups,
        double trackingConfidencePercent)
    {
        var edges = new Dictionary<EdgeKey, LastGoodFeatureMeshWireframeEdge>();
        foreach (var edge in BuildSurfaceEdges(points, trackingConfidencePercent))
        {
            AddOrPromote(edges, edge);
        }

        foreach (var edge in BuildFeatureEdges(featureGroups))
        {
            AddOrPromote(edges, edge);
        }

        return edges.Values
            .OrderBy(static edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(static edge => edge.Role, StringComparer.Ordinal)
            .ThenBy(static edge => edge.FromIndex)
            .ThenBy(static edge => edge.ToIndex)
            .ToList();
    }

    private static IEnumerable<LastGoodFeatureMeshWireframeEdge> BuildSurfaceEdges(
        IReadOnlyList<FaceMeshLandmarkPoint> sourcePoints,
        double trackingConfidencePercent)
    {
        var points = sourcePoints
            .Where(static point => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z))
            .OrderBy(static point => point.Index)
            .Select(static point => new WirePoint(point.Index, point.X, point.Y, point.Z))
            .ToList();
        if (points.Count < MinimumSurfacePointCount)
        {
            yield break;
        }

        var bounds = Bounds.From(points);
        var scale = Math.Max(bounds.Width, bounds.Height);
        if (scale <= 0d)
        {
            yield break;
        }

        var lookup = points.ToDictionary(static point => point.Index);
        foreach (var (fromIndex, toIndex) in CuratedSurfaceScaffoldEdges)
        {
            if (!lookup.TryGetValue(fromIndex, out var a) || !lookup.TryGetValue(toIndex, out var b))
            {
                continue;
            }

            var threeDimensionalLength = Distance3d(a, b);
            var lengthPercent = threeDimensionalLength / scale * 100d;
            if (lengthPercent > MaximumScaffoldEdgeLengthPercent)
            {
                continue;
            }

            yield return new LastGoodFeatureMeshWireframeEdge
            {
                FromIndex = Math.Min(a.Index, b.Index),
                ToIndex = Math.Max(a.Index, b.Index),
                Role = "surface",
                Source = "curated-facial-scaffold",
                LengthPercent = Round(lengthPercent),
                ConfidencePercent = Round(Math.Clamp(trackingConfidencePercent, 0d, 100d))
            };
        }
    }

    private static IEnumerable<LastGoodFeatureMeshWireframeEdge> BuildFeatureEdges(
        IReadOnlyList<LastGoodFeatureMeshFeatureGroup> featureGroups)
    {
        foreach (var group in featureGroups)
        {
            if (group.LandmarkIndices.Count < 2)
            {
                continue;
            }

            for (var i = 0; i < group.LandmarkIndices.Count - 1; i++)
            {
                yield return CreateFeatureEdge(group, group.LandmarkIndices[i], group.LandmarkIndices[i + 1]);
            }

            if (group.Closed)
            {
                yield return CreateFeatureEdge(group, group.LandmarkIndices[^1], group.LandmarkIndices[0]);
            }
        }
    }

    private static LastGoodFeatureMeshWireframeEdge CreateFeatureEdge(
        LastGoodFeatureMeshFeatureGroup group,
        int from,
        int to)
    {
        return new LastGoodFeatureMeshWireframeEdge
        {
            FromIndex = Math.Min(from, to),
            ToIndex = Math.Max(from, to),
            Role = string.IsNullOrWhiteSpace(group.Role) ? "feature" : group.Role,
            Source = $"feature:{group.Id}",
            ConfidencePercent = Round(group.ConfidencePercent)
        };
    }

    private static void AddOrPromote(
        Dictionary<EdgeKey, LastGoodFeatureMeshWireframeEdge> edges,
        LastGoodFeatureMeshWireframeEdge edge)
    {
        var key = new EdgeKey(edge.FromIndex, edge.ToIndex);
        if (!edges.TryGetValue(key, out var existing)
            || IsFeatureEdge(edge)
            || edge.ConfidencePercent > existing.ConfidencePercent)
        {
            edges[key] = edge;
        }
    }

    private static bool IsFeatureEdge(LastGoodFeatureMeshWireframeEdge edge)
    {
        return edge.Source.StartsWith("feature:", StringComparison.OrdinalIgnoreCase);
    }

    private static double Distance3d(WirePoint a, WirePoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double Round(double value)
    {
        return double.IsFinite(value) ? Math.Round(value, 6) : 0d;
    }

    private readonly record struct WirePoint(int Index, double X, double Y, double Z);

    private readonly record struct EdgeKey
    {
        public EdgeKey(int first, int second)
        {
            A = Math.Min(first, second);
            B = Math.Max(first, second);
        }

        public int A { get; }

        public int B { get; }
    }

    private readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => MaxX - MinX;

        public double Height => MaxY - MinY;

        public double CenterX => (MinX + MaxX) / 2d;

        public double CenterY => (MinY + MaxY) / 2d;

        public static Bounds From(IReadOnlyList<WirePoint> points)
        {
            return new Bounds(
                points.Min(static point => point.X),
                points.Min(static point => point.Y),
                points.Max(static point => point.X),
                points.Max(static point => point.Y));
        }
    }

}
