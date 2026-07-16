using System.Windows;
using EpisodeMonitor.Modules.Vision.Common;

namespace EpisodeMonitor.Modules.Vision.MediaPipe;

internal static class MediaPipeFaceLandmarkerMapper
{
    private static readonly int[] FaceOval =
    [
        10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288,
        397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136,
        172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109
    ];

    private static readonly int[] EyeA =
    [
        33, 246, 161, 160, 159, 158, 157, 173, 133, 155, 154, 153, 145, 144, 163, 7
    ];

    private static readonly int[] EyeB =
    [
        362, 398, 384, 385, 386, 387, 388, 466, 263, 249, 390, 373, 374, 380, 381, 382
    ];

    private static readonly int[] OuterLip =
    [
        61, 185, 40, 39, 37, 0, 267, 269, 270, 409,
        291, 375, 321, 405, 314, 17, 84, 181, 91, 146
    ];

    private static readonly int[] InnerLip =
    [
        78, 191, 80, 81, 82, 13, 312, 311, 310, 415,
        308, 324, 318, 402, 317, 14, 87, 178, 88, 95
    ];

    private static readonly int[] Jaw =
    [
        234, 93, 132, 58, 172, 136, 150, 149, 176, 148, 152,
        377, 400, 378, 379, 365, 397, 288, 361, 323, 454
    ];

    public static FaceLandmarkTrackingResult ToTrackingResult(
        MediaPipeSidecarResponse response,
        DateTime capturedAtUtc,
        string backendName)
    {
        if (!response.Ok)
        {
            return new FaceLandmarkTrackingResult
            {
                BackendName = backendName,
                BackendStatus = response.Status
            };
        }

        if (!response.HasFace || response.Landmarks.Count < 468)
        {
            return new FaceLandmarkTrackingResult
            {
                BackendName = backendName,
                BackendStatus = string.IsNullOrWhiteSpace(response.Status)
                    ? "MediaPipe sidecar searching"
                    : response.Status
            };
        }

        var faceContour = Select(response.Landmarks, FaceOval);
        var firstEye = Select(response.Landmarks, EyeA);
        var secondEye = Select(response.Landmarks, EyeB);
        var (leftEye, rightEye) = SortEyesByFramePosition(firstEye, secondEye);
        var outerLip = Select(response.Landmarks, OuterLip);
        var innerLip = Select(response.Landmarks, InnerLip);
        var jaw = Select(response.Landmarks, Jaw);
        var blendshapes = CreateBlendshapeDictionary(response.Blendshapes);
        var faceBox = BoundingRect(faceContour);
        var frame = new FaceLandmarkFrame
        {
            HasFace = true,
            Source = "MediaPipe Face Landmarker sidecar",
            CapturedAtUtc = capturedAtUtc,
            TrackingConfidence = 0.94d,
            EyeConfidence = 0.90d,
            MouthConfidence = 0.90d,
            HeadRollDegrees = EstimateRollDegrees(leftEye, rightEye),
            BlendshapeScores = blendshapes,
            FaceContour = faceContour,
            LeftEyeContour = leftEye,
            RightEyeContour = rightEye,
            OuterLipContour = outerLip,
            InnerLipContour = innerLip,
            JawContour = jaw
        };

        var feature = new FaceFeatureDetection
        {
            HasFace = true,
            Source = frame.Source,
            FaceBox = faceBox ?? new Rect(0, 0, 0, 0),
            LeftEyeBox = BoundingRect(leftEye),
            RightEyeBox = BoundingRect(rightEye),
            MouthBox = BoundingRect(outerLip.Count > 0 ? outerLip : innerLip),
            TrackingConfidence = frame.TrackingConfidence,
            EyeConfidence = frame.EyeConfidence,
            MouthConfidence = frame.MouthConfidence,
            FaceContour = frame.FaceContour,
            LeftEyeContour = frame.LeftEyeContour,
            RightEyeContour = frame.RightEyeContour,
            OuterLipContour = frame.OuterLipContour,
            InnerLipContour = frame.InnerLipContour,
            JawContour = frame.JawContour
        };

        return new FaceLandmarkTrackingResult
        {
            BackendName = backendName,
            BackendStatus = string.IsNullOrWhiteSpace(response.Status)
                ? "MediaPipe dense landmark lock"
                : response.Status,
            FeatureDetection = feature,
            LandmarkFrame = frame
        };
    }

    private static IReadOnlyList<Point> Select(IReadOnlyList<MediaPipeSidecarLandmark> landmarks, IReadOnlyList<int> indices)
    {
        var points = new List<Point>(indices.Count);
        foreach (var index in indices)
        {
            if (index >= 0 && index < landmarks.Count)
            {
                var landmark = landmarks[index];
                points.Add(new Point(
                    Math.Clamp(landmark.X, 0d, 1d),
                    Math.Clamp(landmark.Y, 0d, 1d)));
            }
        }

        return points;
    }

    private static (IReadOnlyList<Point> Left, IReadOnlyList<Point> Right) SortEyesByFramePosition(
        IReadOnlyList<Point> first,
        IReadOnlyList<Point> second)
    {
        var firstCenter = first.Count == 0 ? 0d : first.Average(static point => point.X);
        var secondCenter = second.Count == 0 ? 1d : second.Average(static point => point.X);
        return firstCenter <= secondCenter ? (first, second) : (second, first);
    }

    private static Rect? BoundingRect(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
        {
            return null;
        }

        var minX = points.Min(static point => point.X);
        var maxX = points.Max(static point => point.X);
        var minY = points.Min(static point => point.Y);
        var maxY = points.Max(static point => point.Y);
        return maxX <= minX || maxY <= minY
            ? null
            : new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static double EstimateRollDegrees(IReadOnlyList<Point> leftEye, IReadOnlyList<Point> rightEye)
    {
        if (leftEye.Count == 0 || rightEye.Count == 0)
        {
            return 0d;
        }

        var leftCenter = new Point(leftEye.Average(static point => point.X), leftEye.Average(static point => point.Y));
        var rightCenter = new Point(rightEye.Average(static point => point.X), rightEye.Average(static point => point.Y));
        return Math.Atan2(rightCenter.Y - leftCenter.Y, rightCenter.X - leftCenter.X) * 180d / Math.PI;
    }

    private static IReadOnlyDictionary<string, double> CreateBlendshapeDictionary(IReadOnlyList<MediaPipeSidecarBlendshape> blendshapes)
    {
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var blendshape in blendshapes)
        {
            if (!string.IsNullOrWhiteSpace(blendshape.CategoryName))
            {
                scores[blendshape.CategoryName] = Math.Clamp(blendshape.Score, 0d, 1d);
            }
        }

        return scores;
    }
}
