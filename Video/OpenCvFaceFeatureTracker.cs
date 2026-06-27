using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using CvRect = OpenCvSharp.Rect;
using WpfRect = System.Windows.Rect;

namespace EpisodeMonitor.Video;

public sealed class OpenCvFaceFeatureTracker : IDisposable
{
    private readonly CascadeClassifier? _faceCascade;
    private readonly CascadeClassifier? _eyeCascade;
    private readonly CascadeClassifier? _mouthCascade;

    public OpenCvFaceFeatureTracker()
    {
        var cascadeRoot = Path.Combine(AppContext.BaseDirectory, "dependencies", "vision", "opencv", "haarcascades");
        _faceCascade = LoadCascade(Path.Combine(cascadeRoot, "haarcascade_frontalface_alt2.xml"));
        _eyeCascade = LoadCascade(Path.Combine(cascadeRoot, "haarcascade_eye_tree_eyeglasses.xml"));
        _mouthCascade = LoadCascade(Path.Combine(cascadeRoot, "haarcascade_smile.xml"));
    }

    public bool IsAvailable => _faceCascade is not null && _eyeCascade is not null && _mouthCascade is not null;

    public FaceFeatureDetection Detect(BitmapSource bitmap)
    {
        if (!IsAvailable || _faceCascade is null || _eyeCascade is null || _mouthCascade is null)
        {
            return FaceFeatureDetection.None;
        }

        using var gray = CreateGrayMat(bitmap);
        using var small = new Mat();
        var scale = Math.Min(1d, 320d / Math.Max(gray.Width, gray.Height));
        if (scale < 1d)
        {
            Cv2.Resize(gray, small, new OpenCvSharp.Size(Math.Max(1, (int)(gray.Width * scale)), Math.Max(1, (int)(gray.Height * scale))));
        }
        else
        {
            gray.CopyTo(small);
        }

        Cv2.EqualizeHist(small, small);
        var faces = _faceCascade.DetectMultiScale(
            small,
            1.08,
            4,
            HaarDetectionTypes.ScaleImage,
            new OpenCvSharp.Size(Math.Max(40, small.Width / 12), Math.Max(40, small.Height / 12)));

        var face = faces
            .OrderByDescending(rect => rect.Width * rect.Height)
            .FirstOrDefault();
        if (face.Width <= 0 || face.Height <= 0)
        {
            return FaceFeatureDetection.None;
        }

        var leftEye = DetectEye(small, face, _eyeCascade, leftSide: true);
        var rightEye = DetectEye(small, face, _eyeCascade, leftSide: false);
        var mouth = DetectMouth(small, face, _mouthCascade);

        return new FaceFeatureDetection
        {
            HasFace = true,
            Source = "OpenCV Haar dynamic face tracker",
            FaceBox = ToNormalizedRect(face, small.Width, small.Height),
            LeftEyeBox = leftEye is CvRect left ? ToNormalizedRect(left, small.Width, small.Height) : null,
            RightEyeBox = rightEye is CvRect right ? ToNormalizedRect(right, small.Width, small.Height) : null,
            MouthBox = mouth is CvRect mouthRect ? ToNormalizedRect(mouthRect, small.Width, small.Height) : null
        };
    }

    public void Dispose()
    {
        _faceCascade?.Dispose();
        _eyeCascade?.Dispose();
        _mouthCascade?.Dispose();
    }

    private static CascadeClassifier? LoadCascade(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var cascade = new CascadeClassifier(path);
        return cascade.Empty() ? null : cascade;
    }

    private static Mat CreateGrayMat(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        using var bgra = Mat.FromPixelData(height, width, MatType.CV_8UC4, pixels);
        var gray = new Mat();
        Cv2.CvtColor(bgra, gray, ColorConversionCodes.BGRA2GRAY);
        return gray;
    }

    private static CvRect? DetectEye(Mat gray, CvRect face, CascadeClassifier cascade, bool leftSide)
    {
        var top = face.Y + (int)(face.Height * 0.18d);
        var height = Math.Max(1, (int)(face.Height * 0.34d));
        var x = leftSide ? face.X : face.X + face.Width / 2;
        var width = Math.Max(1, face.Width / 2);
        var roi = ClampRect(new CvRect(x, top, width, height), gray.Width, gray.Height);
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return null;
        }

        using var view = new Mat(gray, roi);
        var eyes = cascade.DetectMultiScale(
            view,
            1.08,
            3,
            HaarDetectionTypes.ScaleImage,
            new OpenCvSharp.Size(Math.Max(12, roi.Width / 8), Math.Max(8, roi.Height / 8)));

        var eye = eyes
            .Where(rect => rect.Y + rect.Height / 2d < roi.Height * 0.76d)
            .OrderByDescending(rect => rect.Width * rect.Height)
            .FirstOrDefault();
        if (eye.Width <= 0 || eye.Height <= 0)
        {
            return null;
        }

        return new CvRect(roi.X + eye.X, roi.Y + eye.Y, eye.Width, eye.Height);
    }

    private static CvRect? DetectMouth(Mat gray, CvRect face, CascadeClassifier cascade)
    {
        var roi = ClampRect(
            new CvRect(
                face.X + (int)(face.Width * 0.16d),
                face.Y + (int)(face.Height * 0.48d),
                (int)(face.Width * 0.68d),
                (int)(face.Height * 0.42d)),
            gray.Width,
            gray.Height);
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return null;
        }

        using var view = new Mat(gray, roi);
        var mouths = cascade.DetectMultiScale(
            view,
            1.12,
            8,
            HaarDetectionTypes.ScaleImage,
            new OpenCvSharp.Size(Math.Max(18, roi.Width / 5), Math.Max(10, roi.Height / 8)));

        var mouth = mouths
            .Where(rect => rect.Y + rect.Height / 2d > roi.Height * 0.35d)
            .OrderByDescending(rect => rect.Width * rect.Height)
            .FirstOrDefault();
        if (mouth.Width <= 0 || mouth.Height <= 0)
        {
            return null;
        }

        return new CvRect(roi.X + mouth.X, roi.Y + mouth.Y, mouth.Width, mouth.Height);
    }

    private static CvRect ClampRect(CvRect rect, int width, int height)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(0, width - 1));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, height - 1));
        var right = Math.Clamp(rect.Right, x + 1, width);
        var bottom = Math.Clamp(rect.Bottom, y + 1, height);
        return new CvRect(x, y, right - x, bottom - y);
    }

    private static WpfRect ToNormalizedRect(CvRect rect, int width, int height)
    {
        return new WpfRect(
            rect.X / (double)Math.Max(1, width),
            rect.Y / (double)Math.Max(1, height),
            rect.Width / (double)Math.Max(1, width),
            rect.Height / (double)Math.Max(1, height));
    }
}
