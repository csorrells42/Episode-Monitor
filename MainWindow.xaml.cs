using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EpisodeMonitor.Video;
using Microsoft.Win32;
using Line = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace EpisodeMonitor;

public partial class MainWindow : Window
{
    private const double EventVideoFramesPerSecond = 10d;
    private const int PreEventVideoSeconds = 60;
    private static readonly TimeSpan CalibrationSymptomFreeWindow = TimeSpan.FromHours(1);

    private readonly FfmpegCameraModeService _cameraModeService = new();
    private readonly FfmpegCameraPreviewService _previewService = new();
    private readonly FfmpegEventRecorderService _eventRecorder = new();
    private readonly IFaceCueAnalyzer _faceCueAnalyzer = new FaceCueAnalyzer();
    private readonly OpenCvFaceFeatureTracker _faceFeatureTracker = new();
    private readonly object _faceFeatureTrackerLock = new();
    private readonly ObservableCollection<EpisodeMonitorEvent> _events = [];
    private readonly string _defaultOutputFolder = Path.Combine(AppContext.BaseDirectory, "EpisodeMonitorSessions");
    private readonly object _frameLock = new();
    private readonly Queue<BufferedVideoFrame> _preEventVideoFrames = new();
    private readonly DispatcherTimer _calibrationGuardTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private IReadOnlyList<CameraDevice> _cameras = [];
    private CancellationTokenSource? _modeLoadCancellation;
    private string _outputFolder;
    private byte[]? _previousSample;
    private BitmapSource? _latestFrame;
    private FaceCueAnalysis? _currentFaceAnalysis;
    private FaceFeatureDetection _currentFaceFeatureDetection = FaceFeatureDetection.None;
    private DateTime? _lowMotionStartedAt;
    private DateTime? _eyeCueStartedAt;
    private DateTime? _jawCueStartedAt;
    private DateTime? _lastSymptomAt;
    private DateTime? _calibrationHoldUntil;
    private DateTime? _activeEpisodeStartedAt;
    private DateTime? _activeEpisodeEarliestAutoEndAt;
    private string _episodeStartSnapshot = "";
    private string _activeEventFolder = "";
    private string _activeEventVideo = "";
    private List<string> _activeTriggerReasons = [];
    private double _episodeMotionSum;
    private int _episodeMotionSamples;
    private DateTime _lastBufferedVideoFrameAt = DateTime.MinValue;
    private DateTime _lastRecordedVideoFrameAt = DateTime.MinValue;
    private DateTime _lastPreviewFrameAcceptedAt = DateTime.MinValue;
    private int _uiFramePending;
    private int _faceFeatureDetectionPending;
    private bool _manualCaptureActive;
    private bool _isCameraEnabled;
    private bool _isUpdatingCameraToggle;
    private bool _isRefreshingCameras;
    private bool _isSnappingSlider;
    private bool _isClosing;
    private FaceCueGuideLayout? _activeFaceCueLayout;
    private DateTime _lastFaceAutoFollowAt = DateTime.MinValue;
    private DateTime _lastFaceFeatureDetectionAt = DateTime.MinValue;
    private DateTime _lastFaceFeatureLockAt = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _outputFolder = _defaultOutputFolder;
        _previewService.FrameAvailable += PreviewFrameAvailable;
        _previewService.StatusChanged += PreviewStatusChanged;
        _eventRecorder.StatusChanged += PreviewStatusChanged;
        _calibrationGuardTimer.Tick += CalibrationGuardTick;
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        EnableDarkWindowFrame();
        EventGrid.ItemsSource = _events;
        UpdateOutputFolderText();
        UpdateSettingLabels();
        UpdateCalibrationGuard();
        _calibrationGuardTimer.Start();
        Dispatcher.InvokeAsync(async () => await RefreshCamerasAsync(), DispatcherPriority.ApplicationIdle);
    }

    private void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        _modeLoadCancellation?.Cancel();
        _modeLoadCancellation?.Dispose();
        EndActiveEpisode(DateTime.Now, null, "App closing");
        _calibrationGuardTimer.Stop();
        lock (_faceFeatureTrackerLock)
        {
            _faceFeatureTracker.Dispose();
        }
        _eventRecorder.Dispose();
        _previewService.Dispose();
    }

    private async void RefreshCamerasClicked(object sender, RoutedEventArgs e)
    {
        await RefreshCamerasAsync();
    }

    private async Task RefreshCamerasAsync()
    {
        if (_isRefreshingCameras)
        {
            return;
        }

        _isRefreshingCameras = true;
        SetStatus("Scanning cameras...");
        IReadOnlyList<CameraDevice> cameras;

        try
        {
            var cameraTask = GetVideoInputDevicesAsync();
            cameras = await cameraTask.WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch (TimeoutException)
        {
            SetStatus("Camera scan is taking longer than expected. The window is ready; try Refresh in a moment.");
            _isRefreshingCameras = false;
            return;
        }
        catch (Exception ex)
        {
            SetStatus($"Could not scan cameras: {ex.Message}");
            _isRefreshingCameras = false;
            return;
        }
        finally
        {
            _isRefreshingCameras = false;
        }

        _cameras = cameras;
        CameraComboBox.ItemsSource = _cameras;
        CameraComboBox.DisplayMemberPath = nameof(CameraDevice.Name);

        if (_cameras.Count > 0)
        {
            CameraComboBox.SelectedIndex = 0;
            SetStatus($"Found {_cameras.Count} camera{(_cameras.Count == 1 ? "" : "s")}.");
        }
        else
        {
            CameraModeComboBox.ItemsSource = new[] { CameraVideoMode.Auto };
            CameraModeComboBox.SelectedIndex = 0;
            SetStatus("No cameras found.");
            SetPreviewState("No camera source found", null);
        }
    }

    private static Task<IReadOnlyList<CameraDevice>> GetVideoInputDevicesAsync()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<CameraDevice>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(DirectShowCameraEnumerator.GetVideoInputDevices());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Episode Monitor Camera Enumerator"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private async void CameraSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CameraComboBox.SelectedItem is not CameraDevice camera)
        {
            return;
        }

        await LoadCameraModesAsync(camera);
        if (_isCameraEnabled)
        {
            RestartPreview();
        }
    }

    private async Task LoadCameraModesAsync(CameraDevice camera)
    {
        _modeLoadCancellation?.Cancel();
        _modeLoadCancellation?.Dispose();
        _modeLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _modeLoadCancellation.Token;

        CameraModeComboBox.ItemsSource = new[] { CameraVideoMode.Auto };
        CameraModeComboBox.SelectedIndex = 0;
        SetStatus($"Loading modes for {camera.Name}...");

        try
        {
            var modes = await _cameraModeService.GetModesAsync(camera.Name, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            CameraModeComboBox.ItemsSource = modes;
            CameraModeComboBox.SelectedIndex = 0;
            SetStatus($"Loaded {modes.Count} mode{(modes.Count == 1 ? "" : "s")} for {camera.Name}.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load camera modes: {ex.Message}");
        }
    }

    private void CameraModeSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isCameraEnabled)
        {
            RestartPreview();
        }
    }

    private void CameraToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingCameraToggle)
        {
            return;
        }

        if (CameraToggle.IsChecked == true)
        {
            StartPreview();
        }
        else
        {
            StopPreview();
        }
    }

    private async void StartPreview()
    {
        if (CameraComboBox.SelectedItem is not CameraDevice camera)
        {
            SetCameraToggle(false);
            SetStatus("Choose a camera first.");
            return;
        }

        var mode = CameraModeComboBox.SelectedItem as CameraVideoMode ?? CameraVideoMode.Auto;
        _previousSample = null;
        SetPreviewState($"Starting {camera.Name} ({mode.Label})", null);
        SetStatus($"Opening camera: {camera.Name} ({mode.Label})");
        _isCameraEnabled = await _previewService.StartAsync(camera.Name, mode);

        if (!_isCameraEnabled && !mode.IsAuto)
        {
            SetStatus("Selected camera mode failed. Retrying with Auto safe mode...");
            SetPreviewState("Retrying camera with Auto safe mode", null);
            CameraModeComboBox.SelectedItem = CameraVideoMode.Auto;
            _previousSample = null;
            _isCameraEnabled = await _previewService.StartAsync(camera.Name, CameraVideoMode.Auto);
        }

        SetCameraToggle(_isCameraEnabled);

        if (_isCameraEnabled)
        {
            SetStatus($"Camera active: {camera.Name}");
        }
        else
        {
            SetPreviewState("Camera failed to start", null);
            SetStatus("Camera failed to open. Close other webcam/AI apps and try Auto or a lower mode.");
        }
    }

    private void RestartPreview()
    {
        if (!_isCameraEnabled)
        {
            return;
        }

        StopPreview(keepToggleChecked: true);
        StartPreview();
    }

    private void StopPreview(bool keepToggleChecked = false)
    {
        _previewService.Stop();
        _isCameraEnabled = false;
        if (!keepToggleChecked)
        {
            SetCameraToggle(false);
            SetPreviewState("Camera disabled", null);
        }
    }

    private void SetCameraToggle(bool enabled)
    {
        _isUpdatingCameraToggle = true;
        CameraToggle.IsChecked = enabled;
        CameraToggle.Content = enabled ? "Camera On" : "Camera Off";
        _isUpdatingCameraToggle = false;
    }

    private void PreviewFrameAvailable(object? sender, BitmapSource frame)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastPreviewFrameAcceptedAt).TotalMilliseconds < 66d)
        {
            return;
        }

        if (Interlocked.Exchange(ref _uiFramePending, 1) == 1)
        {
            return;
        }

        _lastPreviewFrameAcceptedAt = now;
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                _latestFrame = frame;
                SetPreviewState("Camera active", frame);
                ProcessFrame(frame);
                UpdateFaceCueGuideOverlay(frame);
            }
            finally
            {
                Interlocked.Exchange(ref _uiFramePending, 0);
            }
        }, DispatcherPriority.Background);
    }

    private void PreviewStatusChanged(object? sender, string status)
    {
        Dispatcher.InvokeAsync(() => SetStatus(status));
    }

    private void SetPreviewState(string status, ImageSource? frame)
    {
        PreviewStateText.Text = status;
        if (frame is null)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            UpdateFaceCueGuideOverlay(null);
            return;
        }

        PreviewImage.Source = frame;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        UpdateFaceCueGuideOverlay(frame as BitmapSource);
    }

    private void PreviewHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFaceCueGuideOverlay(_latestFrame);
    }

    private void WatchEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (WatchEnabledCheckBox.IsChecked == true)
        {
            Directory.CreateDirectory(_outputFolder);
            ResetEpisodeState();
            AddEpisodeEvent(DateTime.Now, null, "Episode watch started", "", "", "");
            MonitorStatusText.Text = "Episode monitor watching for sustained low motion.";
            UpdateTrackingOverlay("Tracking armed", $"Motion -- | Threshold {GetMotionThreshold():0.0}%", "Waiting for a motion baseline.", "#37506a");
        }
        else
        {
            EndActiveEpisode(DateTime.Now, null, "Monitoring stopped");
            ResetEpisodeState();
            AddEpisodeEvent(DateTime.Now, null, "Episode watch stopped", "", "", "");
            MonitorStatusText.Text = "Episode monitor idle.";
            UpdateTrackingOverlay("Tracking idle", "Motion -- | Threshold --", "Enable episode watch to arm tracking.", "#37506a");
        }
    }

    private void SettingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (!_isSnappingSlider && sender is Slider slider)
        {
            SnapSliderToDefault(slider);
            if (IsFaceFieldSlider(slider))
            {
                _faceCueAnalyzer.Reset();
                _eyeCueStartedAt = null;
                _jawCueStartedAt = null;
                _currentFaceAnalysis = null;
                _currentFaceFeatureDetection = FaceFeatureDetection.None;
                _activeFaceCueLayout = null;
                _lastFaceFeatureLockAt = DateTime.MinValue;
                UpdateFaceCueGuideOverlay(_latestFrame);
            }
        }

        UpdateSettingLabels();
    }

    private static bool IsFaceFieldSlider(Slider slider)
    {
        return slider.Name is nameof(FaceFieldXSlider)
            or nameof(FaceFieldYSlider)
            or nameof(FaceFieldSizeSlider);
    }

    private void SnapSliderToDefault(Slider slider)
    {
        var (defaultValue, snapDistance) = slider.Name switch
        {
            nameof(ThresholdSlider) => (1.5d, 0.15d),
            nameof(StillnessSlider) => (120d, 10d),
            nameof(EyeCueSlider) => (35d, 2.5d),
            nameof(JawCueSlider) => (35d, 2.5d),
            nameof(FaceCueTimeSlider) => (3d, 0.5d),
            nameof(CompositeCueSlider) => (55d, 2.5d),
            nameof(FaceFieldXSlider) => (50d, 2d),
            nameof(FaceFieldYSlider) => (48d, 2d),
            nameof(FaceFieldSizeSlider) => (60d, 2d),
            _ => (double.NaN, 0d)
        };

        if (double.IsNaN(defaultValue)
            || Math.Abs(slider.Value - defaultValue) < double.Epsilon
            || Math.Abs(slider.Value - defaultValue) > snapDistance)
        {
            return;
        }

        _isSnappingSlider = true;
        slider.Value = defaultValue;
        _isSnappingSlider = false;
    }

    private void FaceCueChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _faceCueAnalyzer.Reset();
        _eyeCueStartedAt = null;
        _jawCueStartedAt = null;
        _currentFaceAnalysis = null;
        _currentFaceFeatureDetection = FaceFeatureDetection.None;
        _activeFaceCueLayout = null;
        _lastFaceFeatureLockAt = DateTime.MinValue;
        MonitorStatusText.Text = FaceCueCheckBox.IsChecked == true
            ? "Face cue tracking enabled. Sit awake and centered while it calibrates."
            : "Face cue tracking disabled.";
        UpdateCalibrationGuard();
    }

    private void CalibrateFaceCuesClicked(object sender, RoutedEventArgs e)
    {
        if (!IsCalibrationAllowed())
        {
            MonitorStatusText.Text = "Calibration blocked until you have been symptom-free for one hour.";
            UpdateCalibrationGuard();
            return;
        }

        _faceCueAnalyzer.Reset();
        _eyeCueStartedAt = null;
        _jawCueStartedAt = null;
        _currentFaceAnalysis = null;
        _currentFaceFeatureDetection = FaceFeatureDetection.None;
        _activeFaceCueLayout = null;
        _lastFaceFeatureLockAt = DateTime.MinValue;
        MonitorStatusText.Text = "Face cues recalibrating. Sit awake, centered, and naturally alert for a few seconds.";
    }

    private void MarkSymptomsNowClicked(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        AddEpisodeEvent(now, null, "Symptom marker", "", "", "User marked symptoms; calibration delayed for one symptom-free hour");
        MarkSymptomActivity(now, "Symptoms marked by user; calibration delayed for one symptom-free hour.");
    }

    private void ManualCaptureClicked(object sender, RoutedEventArgs e)
    {
        if (_manualCaptureActive && _activeEpisodeStartedAt is not null)
        {
            EndActiveEpisode(DateTime.Now, _latestFrame, "Manual capture stopped");
            ResetEpisodeState();
            MonitorStatusText.Text = "Manual capture stopped.";
            return;
        }

        if (_activeEpisodeStartedAt is not null)
        {
            MonitorStatusText.Text = "An event capture is already active.";
            return;
        }

        if (_latestFrame is null)
        {
            MonitorStatusText.Text = "Turn the camera on before starting manual capture.";
            return;
        }

        Directory.CreateDirectory(_outputFolder);
        _manualCaptureActive = true;
        ManualCaptureButton.Content = "Stop Capture";
        var now = DateTime.Now;
        StartActiveEpisode(
            _latestFrame,
            now,
            GetMotionThreshold(),
            GetStillnessSeconds(),
            "Manual capture",
            ["Manual capture started by user before or during a possible episode"]);
        MonitorStatusText.Text = "Manual capture started.";
    }

    private void ProcessFrame(BitmapSource bitmap)
    {
        if (WatchEnabledCheckBox.IsChecked != true && _activeEpisodeStartedAt is null)
        {
            var idleMetrics = CreateOverlayMetrics(null);
            var idleTrigger = "Preview only. Enable episode watch for automatic cues.";
            UpdateTrackingOverlay("Tracking idle", idleMetrics, idleTrigger, "#37506a");
            return;
        }

        byte[] sample;
        lock (_frameLock)
        {
            sample = CreateFrameSample(bitmap);
        }

        if (_previousSample is null)
        {
            _previousSample = sample;
            MonitorStatusText.Text = "Episode monitor armed. Building a motion baseline.";
            _currentFaceAnalysis = AnalyzeFaceCues(bitmap);
            var metrics = CreateOverlayMetrics(null);
            var trigger = "Building a motion baseline from the current camera view.";
            UpdateTrackingOverlay("Tracking armed", metrics, trigger, "#37506a");
            if (_activeEpisodeStartedAt is null)
            {
                BufferAnnotatedFrame(bitmap, DateTime.Now, "Tracking armed", metrics, trigger, "#37506a");
            }
            else
            {
                WriteAnnotatedVideoFrame(bitmap, DateTime.Now, "Event recording", metrics, trigger, "#dc5b5b");
            }
            return;
        }

        var now = DateTime.Now;
        var motion = CalculateFrameMotionPercent(_previousSample, sample);
        _previousSample = sample;
        _currentFaceAnalysis = AnalyzeFaceCues(bitmap);
        ProcessEpisodeMotion(bitmap, now, motion);
    }

    private void ProcessEpisodeMotion(BitmapSource bitmap, DateTime now, double motion)
    {
        var threshold = GetMotionThreshold();
        var stillnessSeconds = GetStillnessSeconds();
        var faceCueSeconds = GetFaceCueSeconds();
        var faceCueReasons = ProcessFaceCues(now);

        if (_activeEpisodeStartedAt is null && faceCueReasons.Count > 0)
        {
            StartActiveEpisode(bitmap, now, threshold, stillnessSeconds, "Face cue event", faceCueReasons);
        }

        if (motion <= threshold)
        {
            _lowMotionStartedAt ??= now;
            _episodeMotionSum += motion;
            _episodeMotionSamples++;

            var lowMotionFor = now - _lowMotionStartedAt.Value;
            if (_activeEpisodeStartedAt is null && lowMotionFor.TotalSeconds >= stillnessSeconds)
            {
                StartActiveEpisode(
                    bitmap,
                    _lowMotionStartedAt.Value,
                    threshold,
                    stillnessSeconds,
                    "Possible sleep onset",
                    [$"Low motion persisted for {stillnessSeconds:0}s at or below {threshold:0.0}%"]);
            }

            MonitorStatusText.Text = _activeEpisodeStartedAt is null
                ? $"Very still: {lowMotionFor.TotalSeconds:0}s of {stillnessSeconds:0}s needed. Motion: {motion:0.0}%"
                : $"Possible episode active. Duration: {(now - _activeEpisodeStartedAt.Value).TotalMinutes:0.0} min. Motion: {motion:0.0}%";
            var state = _activeEpisodeStartedAt is null ? "Possible event cue" : "Event recording";
            var metrics = CreateOverlayMetrics(motion);
            var trigger = _activeEpisodeStartedAt is null
                ? $"Low motion for {lowMotionFor.TotalSeconds:0}s of {stillnessSeconds:0}s required. Face cue time {faceCueSeconds:0}s."
                : $"Event duration {(now - _activeEpisodeStartedAt.Value).TotalSeconds:0}s. Trigger: {string.Join("; ", _activeTriggerReasons)}";
            var accent = _activeEpisodeStartedAt is null ? "#d7a53a" : "#dc5b5b";
            UpdateTrackingOverlay(state, metrics, trigger, accent);
            WriteAnnotatedVideoFrame(bitmap, now, state, metrics, trigger, accent);
            return;
        }

        if (_activeEpisodeStartedAt is not null)
        {
            if (_activeEpisodeEarliestAutoEndAt is DateTime earliestEnd && now < earliestEnd)
            {
                var holdState = _manualCaptureActive ? "Manual capture" : "Event recording";
                var holdMetrics = CreateOverlayMetrics(motion);
                var holdTrigger = $"Capture active. Auto-close available in {(earliestEnd - now).TotalSeconds:0}s. Trigger: {string.Join("; ", _activeTriggerReasons)}";
                UpdateTrackingOverlay(holdState, holdMetrics, holdTrigger, "#dc5b5b");
                WriteAnnotatedVideoFrame(bitmap, now, holdState, holdMetrics, holdTrigger, "#dc5b5b");
                return;
            }

            var endingState = "Event ending cue";
            var endingMetrics = CreateOverlayMetrics(motion);
            var endingTrigger = $"Motion returned above threshold. Event will close as: Motion returned.";
            UpdateTrackingOverlay(endingState, endingMetrics, endingTrigger, "#4aa36b");
            WriteAnnotatedVideoFrame(bitmap, now, endingState, endingMetrics, endingTrigger, "#4aa36b");
        }

        EndActiveEpisode(now, bitmap, "Motion returned");
        ResetEpisodeState();
        MonitorStatusText.Text = $"Awake/moving baseline. Motion: {motion:0.0}%";
        var baselineMetrics = CreateOverlayMetrics(motion);
        UpdateTrackingOverlay("Acceptable baseline", baselineMetrics, "Motion is above the event threshold.", "#4aa36b");
        BufferAnnotatedFrame(bitmap, now, "Acceptable baseline", baselineMetrics, "Motion is above the event threshold.", "#4aa36b");
    }

    private static byte[] CreateFrameSample(BitmapSource bitmap)
    {
        var scale = Math.Min(1d, 96d / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        var width = Math.Max(1, (int)(bitmap.PixelWidth * scale));
        var height = Math.Max(1, (int)(bitmap.PixelHeight * scale));
        var scaled = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Gray8, null, 0);
        var stride = width;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static double CalculateFrameMotionPercent(byte[] previous, byte[] current)
    {
        var length = Math.Min(previous.Length, current.Length);
        if (length == 0)
        {
            return 0d;
        }

        long total = 0;
        for (var i = 0; i < length; i++)
        {
            total += Math.Abs(previous[i] - current[i]);
        }

        return total / (double)(length * 255) * 100d;
    }

    private FaceCueAnalysis? AnalyzeFaceCues(BitmapSource bitmap)
    {
        if (FaceCueCheckBox.IsChecked != true)
        {
            return null;
        }

        try
        {
            var layout = GetManualFaceCueLayout();
            var now = DateTime.UtcNow;
            QueueFaceFeatureDetection(bitmap, now);

            if (HasUsableFaceFeatureLock(now) && FaceAutoFollowCheckBox.IsChecked == true)
            {
                var detectedLayout = _currentFaceFeatureDetection.ToGuideLayout(layout);
                var current = _activeFaceCueLayout ?? detectedLayout;
                _activeFaceCueLayout = new FaceCueGuideLayout(
                    current.CenterXPercent + (detectedLayout.CenterXPercent - current.CenterXPercent) * 0.45d,
                    current.CenterYPercent + (detectedLayout.CenterYPercent - current.CenterYPercent) * 0.45d,
                    current.HeightPercent + (detectedLayout.HeightPercent - current.HeightPercent) * 0.30d);
                layout = _activeFaceCueLayout;
            }
            else if (FaceAutoFollowCheckBox.IsChecked == true)
            {
                var current = _activeFaceCueLayout ?? layout;
                if ((now - _lastFaceAutoFollowAt).TotalMilliseconds >= 500d)
                {
                    _activeFaceCueLayout = FaceCueAutoLayoutEstimator.Estimate(bitmap, current);
                    _lastFaceAutoFollowAt = now;
                }

                layout = _activeFaceCueLayout ?? current;
            }

            return _faceCueAnalyzer.Analyze(bitmap, layout);
        }
        catch (Exception ex)
        {
            MonitorStatusText.Text = $"Face cue tracking paused: {ex.Message}";
            return null;
        }
    }

    private void QueueFaceFeatureDetection(BitmapSource bitmap, DateTime now)
    {
        if (_isClosing
            || !_faceFeatureTracker.IsAvailable
            || FaceAutoFollowCheckBox.IsChecked != true
            || (now - _lastFaceFeatureDetectionAt).TotalMilliseconds < 500d)
        {
            return;
        }

        if (Interlocked.Exchange(ref _faceFeatureDetectionPending, 1) == 1)
        {
            return;
        }

        _lastFaceFeatureDetectionAt = now;
        _ = DetectFaceFeaturesAsync(bitmap);
    }

    private async Task DetectFaceFeaturesAsync(BitmapSource bitmap)
    {
        var detection = FaceFeatureDetection.None;
        try
        {
            detection = await Task.Run(() =>
            {
                lock (_faceFeatureTrackerLock)
                {
                    return _isClosing ? FaceFeatureDetection.None : _faceFeatureTracker.Detect(bitmap);
                }
            });

            await Dispatcher.InvokeAsync(() =>
            {
                if (_isClosing)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                if (detection.HasFace)
                {
                    _currentFaceFeatureDetection = detection;
                    _lastFaceFeatureLockAt = now;
                }
                else if (!HasUsableFaceFeatureLock(now))
                {
                    _currentFaceFeatureDetection = FaceFeatureDetection.None;
                }

                UpdateFaceCueGuideOverlay(_latestFrame);
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => SetStatus($"Dynamic face tracker paused: {ex.Message}"), DispatcherPriority.Background);
        }
        finally
        {
            Interlocked.Exchange(ref _faceFeatureDetectionPending, 0);
        }
    }

    private bool HasUsableFaceFeatureLock(DateTime now)
    {
        return _currentFaceFeatureDetection.HasFace
            && (now - _lastFaceFeatureLockAt).TotalSeconds <= 4d;
    }

    private bool HasFreshFaceFeatureLock(DateTime now)
    {
        return _currentFaceFeatureDetection.HasFace
            && (now - _lastFaceFeatureLockAt).TotalSeconds <= 1.5d;
    }

    private List<string> ProcessFaceCues(DateTime now)
    {
        var reasons = new List<string>();
        if (FaceCueCheckBox.IsChecked != true || _currentFaceAnalysis is not { BaselineReady: true } analysis)
        {
            _eyeCueStartedAt = null;
            _jawCueStartedAt = null;
            return reasons;
        }

        var cueSeconds = GetFaceCueSeconds();
        var eyeCueActive = false;
        if (analysis.EyeDropPercent >= GetEyeCueThreshold())
        {
            _eyeCueStartedAt ??= now;
            if ((now - _eyeCueStartedAt.Value).TotalSeconds >= cueSeconds)
            {
                eyeCueActive = true;
                reasons.Add($"Primary eye cue persisted for {cueSeconds:0}s: openness dropped {analysis.EyeDropPercent:0}% from awake baseline");
            }
        }
        else
        {
            _eyeCueStartedAt = null;
        }

        if (analysis.JawChangePercent >= GetJawCueThreshold())
        {
            _jawCueStartedAt ??= now;
            if ((now - _jawCueStartedAt.Value).TotalSeconds >= cueSeconds)
            {
                if (eyeCueActive || analysis.EyeDropPercent >= GetEyeCueThreshold() * 0.55d)
                {
                    reasons.Add($"Supporting jaw/lower-face cue persisted for {cueSeconds:0}s: change measured {analysis.JawChangePercent:0}% from awake baseline");
                }
            }
        }
        else
        {
            _jawCueStartedAt = null;
        }

        if (reasons.Count == 0
            && analysis.QualityPercent >= 50d
            && analysis.CompositeCuePercent >= GetCompositeCueThreshold()
            && analysis.EyeDropPercent >= GetEyeCueThreshold() * 0.70d)
        {
            reasons.Add($"Primary AI eye-led cue score reached {analysis.CompositeCuePercent:0}% with eye drop {analysis.EyeDropPercent:0}%");
        }

        return reasons;
    }

    private void StartActiveEpisode(
        BitmapSource bitmap,
        DateTime startedAt,
        double threshold,
        double stillnessSeconds,
        string eventName,
        IReadOnlyList<string>? triggerReasons = null)
    {
        _activeEpisodeStartedAt = startedAt;
        _activeEpisodeEarliestAutoEndAt = DateTime.Now.AddSeconds(8);
        MarkSymptomActivity(startedAt, "Event capture started; calibration delayed for one symptom-free hour.");
        _activeEventFolder = CreateEventFolder(startedAt);
        _activeTriggerReasons = triggerReasons?.ToList() ??
        [
            $"Low motion persisted for {stillnessSeconds:0}s at or below {threshold:0.0}%"
        ];

        _episodeStartSnapshot = SnapshotCheckBox.IsChecked == true
            ? SaveSnapshot(bitmap, startedAt, "start", _activeEventFolder)
            : "";

        _activeEventVideo = "";
        if (EventVideoCheckBox.IsChecked == true)
        {
            var videoPath = Path.Combine(_activeEventFolder, "event_video.mp4");
            if (_eventRecorder.Start(videoPath, _preEventVideoFrames.Select(frame => frame.JpegBytes).ToArray()))
            {
                _activeEventVideo = videoPath;
            }
        }

        AddEpisodeEvent(startedAt, null, eventName, GetAverageMotionLabel(), _activeEventFolder, string.Join("; ", _activeTriggerReasons));
    }

    private string CreateEventFolder(DateTime timestamp)
    {
        Directory.CreateDirectory(_outputFolder);
        var folder = Path.Combine(_outputFolder, $"Episode_{timestamp:yyyy-MM-dd_HH-mm-ss}");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            return folder;
        }

        folder = Path.Combine(_outputFolder, $"Episode_{timestamp:yyyy-MM-dd_HH-mm-ss-fff}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private string SaveSnapshot(BitmapSource bitmap, DateTime timestamp, string kind, string? folder = null)
    {
        try
        {
            var targetFolder = folder ?? Path.Combine(_outputFolder, $"EpisodeMonitor_{timestamp:yyyy-MM-dd}");
            Directory.CreateDirectory(targetFolder);
            var path = Path.Combine(targetFolder, $"episode_{kind}_{timestamp:HH-mm-ss-fff}.jpg");
            var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }
        catch (Exception ex)
        {
            MonitorStatusText.Text = $"Episode event logged, but snapshot failed: {ex.Message}";
            return "";
        }
    }

    private void EndActiveEpisode(DateTime endedAt, BitmapSource? bitmap, string reason)
    {
        if (_activeEpisodeStartedAt is null)
        {
            return;
        }

        var endSnapshot = bitmap is not null && SnapshotCheckBox.IsChecked == true
            ? SaveSnapshot(bitmap, endedAt, "end", string.IsNullOrWhiteSpace(_activeEventFolder) ? null : _activeEventFolder)
            : "";
        _eventRecorder.Stop();

        var summaryFiles = WriteEventSummary(_activeEpisodeStartedAt.Value, endedAt, reason, endSnapshot);
        var files = string.Join(" | ", new[] { _activeEventVideo, _episodeStartSnapshot, endSnapshot, summaryFiles.JsonPath, summaryFiles.CsvPath }
            .Where(static path => !string.IsNullOrWhiteSpace(path)));
        var notes = $"Triggers: {string.Join("; ", _activeTriggerReasons)}. Ended: {reason}.";
        AddEpisodeEvent(_activeEpisodeStartedAt.Value, endedAt, reason, GetAverageMotionLabel(), files, notes);
        MarkSymptomActivity(endedAt, "Event capture ended; calibration delayed for one symptom-free hour.");
    }

    private void ResetEpisodeState()
    {
        _lowMotionStartedAt = null;
        _eyeCueStartedAt = null;
        _jawCueStartedAt = null;
        _activeEpisodeStartedAt = null;
        _activeEpisodeEarliestAutoEndAt = null;
        _episodeStartSnapshot = "";
        _activeEventFolder = "";
        _activeEventVideo = "";
        _activeTriggerReasons = [];
        _episodeMotionSum = 0d;
        _episodeMotionSamples = 0;
        _previousSample = null;
        _lastRecordedVideoFrameAt = DateTime.MinValue;
        _manualCaptureActive = false;
        ManualCaptureButton.Content = "Start Capture";
    }

    private void MarkNowClicked(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        AddEpisodeEvent(now, null, "Manual marker", "", "", "User marked an event");
        MonitorStatusText.Text = $"Manual marker added at {now:g}.";
    }

    private void ExportClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_outputFolder);
            var folder = Path.Combine(_outputFolder, "EpisodeLogs");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"episode_log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv");
            var builder = new StringBuilder();
            builder.AppendLine("Started,Ended,Duration,Event,AverageMotion,Notes,Files");
            foreach (var item in _events.Reverse())
            {
                builder.AppendLine(string.Join(",", [
                    Csv(item.StartLabel),
                    Csv(item.EndLabel),
                    Csv(item.Duration),
                    Csv(item.Event),
                    Csv(item.AvgMotion),
                    Csv(item.Notes),
                    Csv(item.File)
                ]));
            }

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            MonitorStatusText.Text = $"Episode log exported: {path}";
        }
        catch (Exception ex)
        {
            MonitorStatusText.Text = $"Could not export episode log: {ex.Message}";
        }
    }

    private void ClearClicked(object sender, RoutedEventArgs e)
    {
        _events.Clear();
        ResetEpisodeState();
        MonitorStatusText.Text = "Episode log cleared.";
    }

    private void BrowseOutputClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose episode output folder",
            InitialDirectory = Directory.Exists(_outputFolder) ? _outputFolder : AppContext.BaseDirectory,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _outputFolder = dialog.FolderName;
        UpdateOutputFolderText();
        MonitorStatusText.Text = $"Output folder set: {_outputFolder}";
    }

    private void CloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AddEpisodeEvent(DateTime startedAt, DateTime? endedAt, string eventName, string averageMotion, string file, string notes)
    {
        _events.Insert(0, new EpisodeMonitorEvent
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
            Event = eventName,
            AvgMotion = averageMotion,
            File = file,
            Notes = notes
        });
    }

    private double GetMotionThreshold()
    {
        return Math.Clamp(ThresholdSlider.Value, 0.2d, 8d);
    }

    private double GetStillnessSeconds()
    {
        return Math.Clamp(StillnessSlider.Value, 15d, 900d);
    }

    private double GetEyeCueThreshold()
    {
        return Math.Clamp(EyeCueSlider.Value, 15d, 80d);
    }

    private double GetJawCueThreshold()
    {
        return Math.Clamp(JawCueSlider.Value, 15d, 120d);
    }

    private double GetFaceCueSeconds()
    {
        return Math.Clamp(FaceCueTimeSlider.Value, 1d, 20d);
    }

    private double GetCompositeCueThreshold()
    {
        return Math.Clamp(CompositeCueSlider.Value, 25d, 90d);
    }

    private FaceCueGuideLayout GetFaceCueLayout()
    {
        if (FaceAutoFollowCheckBox.IsChecked == true && _activeFaceCueLayout is not null)
        {
            return _activeFaceCueLayout;
        }

        return GetManualFaceCueLayout();
    }

    private FaceCueGuideLayout GetManualFaceCueLayout()
    {
        return new FaceCueGuideLayout(
            Math.Clamp(FaceFieldXSlider.Value, 20d, 80d),
            Math.Clamp(FaceFieldYSlider.Value, 20d, 80d),
            Math.Clamp(FaceFieldSizeSlider.Value, 25d, 90d));
    }

    private string GetAverageMotionLabel()
    {
        return _episodeMotionSamples <= 0 ? "" : $"{_episodeMotionSum / _episodeMotionSamples:0.0}%";
    }

    private void UpdateSettingLabels()
    {
        ThresholdValueText.Text = $"{GetMotionThreshold():0.0}%";
        StillnessValueText.Text = $"{GetStillnessSeconds():0}s";
        EyeCueValueText.Text = $"{GetEyeCueThreshold():0}%";
        JawCueValueText.Text = $"{GetJawCueThreshold():0}%";
        FaceCueTimeValueText.Text = $"{GetFaceCueSeconds():0}s";
        CompositeCueValueText.Text = $"{GetCompositeCueThreshold():0}%";
        FaceFieldXValueText.Text = $"{FaceFieldXSlider.Value:0}%";
        FaceFieldYValueText.Text = $"{FaceFieldYSlider.Value:0}%";
        FaceFieldSizeValueText.Text = $"{FaceFieldSizeSlider.Value:0}%";
    }

    private void UpdateOutputFolderText()
    {
        OutputFolderText.Text = $"{_outputFolder}{Environment.NewLine}{GetStorageLabel(_outputFolder)}";
    }

    private void SetStatus(string status)
    {
        TopStatusText.Text = status;
        FooterText.Text = status;
    }

    private void EnableDarkWindowFrame()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
            }
        }
        catch
        {
        }
    }

    private static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string FormatOptional(double? value)
    {
        return value is null ? "" : value.Value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private (string JsonPath, string CsvPath) WriteEventSummary(DateTime startedAt, DateTime endedAt, string endReason, string endSnapshot)
    {
        if (string.IsNullOrWhiteSpace(_activeEventFolder))
        {
            return ("", "");
        }

        try
        {
            Directory.CreateDirectory(_activeEventFolder);
            var duration = endedAt - startedAt;
            var summary = new EventSummary
            {
                StartedAt = startedAt,
                EndedAt = endedAt,
                DurationSeconds = duration.TotalSeconds,
                EndReason = endReason,
                TriggerReasons = _activeTriggerReasons,
                AverageMotion = GetAverageMotionLabel(),
                FaceCueStatus = _currentFaceAnalysis?.Status ?? "",
                FaceCueQuality = _currentFaceAnalysis?.QualityStatus ?? "",
                FaceCueScore = _currentFaceAnalysis?.CompositeCuePercent,
                EyeOpenness = _currentFaceAnalysis?.EyeOpennessPercent,
                EyeDrop = _currentFaceAnalysis?.EyeDropPercent,
                EyeAsymmetry = _currentFaceAnalysis?.EyeAsymmetryPercent,
                JawChange = _currentFaceAnalysis?.JawChangePercent,
                JawAsymmetry = _currentFaceAnalysis?.JawAsymmetryPercent,
                LowerFaceDrop = _currentFaceAnalysis?.LowerFaceDropPercent,
                HeadDrift = _currentFaceAnalysis?.HeadDriftPercent,
                VideoFile = _activeEventVideo,
                VideoOverlayBurnedIn = !string.IsNullOrWhiteSpace(_activeEventVideo),
                PreEventVideoSeconds = PreEventVideoSeconds,
                StartSnapshot = _episodeStartSnapshot,
                EndSnapshot = endSnapshot,
                OutputFolder = _activeEventFolder
            };

            var jsonPath = Path.Combine(_activeEventFolder, "event_summary.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

            var csvPath = Path.Combine(_activeEventFolder, "event_summary.csv");
            var builder = new StringBuilder();
            builder.AppendLine("Started,Ended,DurationSeconds,EndReason,TriggerReasons,AverageMotion,FaceCueStatus,FaceCueQuality,FaceCueScore,EyeOpenness,EyeDrop,EyeAsymmetry,JawChange,JawAsymmetry,LowerFaceDrop,HeadDrift,VideoFile,VideoOverlayBurnedIn,PreEventVideoSeconds,StartSnapshot,EndSnapshot,OutputFolder");
            builder.AppendLine(string.Join(",", [
                Csv(startedAt.ToString("O")),
                Csv(endedAt.ToString("O")),
                Csv(duration.TotalSeconds.ToString("0.0")),
                Csv(endReason),
                Csv(string.Join("; ", _activeTriggerReasons)),
                Csv(GetAverageMotionLabel()),
                Csv(_currentFaceAnalysis?.Status ?? ""),
                Csv(_currentFaceAnalysis?.QualityStatus ?? ""),
                Csv(FormatOptional(_currentFaceAnalysis?.CompositeCuePercent)),
                Csv(FormatOptional(_currentFaceAnalysis?.EyeOpennessPercent)),
                Csv(FormatOptional(_currentFaceAnalysis?.EyeDropPercent)),
                Csv(FormatOptional(_currentFaceAnalysis?.EyeAsymmetryPercent)),
                Csv(FormatOptional(_currentFaceAnalysis?.JawChangePercent)),
                Csv(FormatOptional(_currentFaceAnalysis?.JawAsymmetryPercent)),
                Csv(FormatOptional(_currentFaceAnalysis?.LowerFaceDropPercent)),
                Csv(FormatOptional(_currentFaceAnalysis?.HeadDriftPercent)),
                Csv(_activeEventVideo),
                Csv((!string.IsNullOrWhiteSpace(_activeEventVideo)).ToString()),
                Csv(PreEventVideoSeconds.ToString(CultureInfo.InvariantCulture)),
                Csv(_episodeStartSnapshot),
                Csv(endSnapshot),
                Csv(_activeEventFolder)
            ]));
            File.WriteAllText(csvPath, builder.ToString(), Encoding.UTF8);
            return (jsonPath, csvPath);
        }
        catch (Exception ex)
        {
            MonitorStatusText.Text = $"Episode ended, but summary failed: {ex.Message}";
            return ("", "");
        }
    }

    private string GetStorageLabel(string folder)
    {
        try
        {
            var fullPath = Path.GetFullPath(folder);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return "Storage: unknown drive.";
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return $"Storage: {root} is not ready.";
            }

            var freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            var windowsRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            var driveNote = root.Equals(windowsRoot, StringComparison.OrdinalIgnoreCase)
                ? "Windows drive"
                : "off-system drive";
            return $"Storage: {drive.Name} {freeGb:0.0} GB free ({driveNote}).";
        }
        catch (Exception ex)
        {
            return $"Storage: unavailable ({ex.Message}).";
        }
    }

    private void CalibrationGuardTick(object? sender, EventArgs e)
    {
        UpdateCalibrationGuard();
    }

    private void MarkSymptomActivity(DateTime timestamp, string message)
    {
        _lastSymptomAt = timestamp;
        _calibrationHoldUntil = timestamp + CalibrationSymptomFreeWindow;
        UpdateCalibrationGuard();
        MonitorStatusText.Text = message;
    }

    private bool IsCalibrationAllowed()
    {
        return _calibrationHoldUntil is null || DateTime.Now >= _calibrationHoldUntil.Value;
    }

    private void UpdateCalibrationGuard()
    {
        if (!IsLoaded)
        {
            return;
        }

        if (_calibrationHoldUntil is DateTime holdUntil && DateTime.Now < holdUntil)
        {
            var remaining = holdUntil - DateTime.Now;
            CalibrateFaceCuesButton.IsEnabled = false;
            CalibrationGuardText.Text = $"Calibration locked: wait {FormatRemaining(remaining)} symptom-free. Last symptom marker: {_lastSymptomAt:g}.";
            return;
        }

        CalibrateFaceCuesButton.IsEnabled = FaceCueCheckBox.IsChecked == true;
        CalibrationGuardText.Text = _lastSymptomAt is DateTime lastSymptom
            ? $"Calibration available. Last symptom marker: {lastSymptom:g}."
            : "Calibration available. Best baseline: after one symptom-free hour.";
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalMinutes >= 1d)
        {
            return $"{Math.Ceiling(remaining.TotalMinutes):0} min";
        }

        return $"{Math.Max(0, Math.Ceiling(remaining.TotalSeconds)):0}s";
    }

    private void UpdateTrackingOverlay(string state, string metrics, string trigger, string accentColor)
    {
        OverlayStateText.Text = state;
        OverlayMetricText.Text = metrics;
        OverlayTriggerText.Text = trigger;
        TrackingOverlay.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(accentColor)!;
    }

    private void UpdateFaceCueGuideOverlay(BitmapSource? bitmap)
    {
        FaceCueGuideCanvas.Children.Clear();
        if (bitmap is null || FaceCueCheckBox.IsChecked != true)
        {
            return;
        }

        var display = GetPreviewDisplayRect(bitmap);
        if (display.Width <= 0d || display.Height <= 0d)
        {
            return;
        }

        var accent = GetFaceCueGuideColor();
        var regionBrush = new SolidColorBrush(Color.FromArgb(34, accent.R, accent.G, accent.B));
        var lineBrush = new SolidColorBrush(Color.FromArgb(235, accent.R, accent.G, accent.B));
        var supportBrush = new SolidColorBrush(Color.FromArgb(175, 185, 215, 239));
        var regionPen = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B));
        var layout = GetFaceCueLayout();
        var face = layout.GetFaceBox();
        var leftEye = layout.ToFrameRect(layout.LeftEye);
        var rightEye = layout.ToFrameRect(layout.RightEye);
        var jaw = layout.ToFrameRect(layout.Jaw);

        AddGuideRegion(display, face, Brushes.Transparent, supportBrush, 1d);
        AddGuideRegion(display, leftEye, regionBrush, regionPen, 2d);
        AddGuideRegion(display, rightEye, regionBrush, regionPen, 2d);
        AddGuideRegion(display, jaw, regionBrush, regionPen, 2d);

        AddGuideLine(display, leftEye.Left, leftEye.Top + leftEye.Height * 0.50d, leftEye.Right, leftEye.Top + leftEye.Height * 0.50d, lineBrush, 3d);
        AddGuideLine(display, rightEye.Left, rightEye.Top + rightEye.Height * 0.50d, rightEye.Right, rightEye.Top + rightEye.Height * 0.50d, lineBrush, 3d);
        AddGuideLine(display, jaw.Left + jaw.Width * 0.16d, jaw.Top + jaw.Height * 0.38d, jaw.Right - jaw.Width * 0.16d, jaw.Top + jaw.Height * 0.38d, lineBrush, 3d);
        AddGuideLine(display, face.Left + face.Width * 0.50d, face.Top, face.Left + face.Width * 0.50d, face.Bottom, supportBrush, 1d);

        if (HasUsableFaceFeatureLock(DateTime.UtcNow))
        {
            var detectorBrush = new SolidColorBrush(Color.FromArgb(230, 244, 211, 94));
            AddGuideRegion(display, _currentFaceFeatureDetection.FaceBox, Brushes.Transparent, detectorBrush, 2d);
            if (_currentFaceFeatureDetection.LeftEyeBox is Rect leftEyeBox)
            {
                AddGuideRegion(display, leftEyeBox, Brushes.Transparent, detectorBrush, 2d);
            }

            if (_currentFaceFeatureDetection.RightEyeBox is Rect rightEyeBox)
            {
                AddGuideRegion(display, rightEyeBox, Brushes.Transparent, detectorBrush, 2d);
            }

            if (_currentFaceFeatureDetection.MouthBox is Rect mouthBox)
            {
                AddGuideRegion(display, mouthBox, Brushes.Transparent, detectorBrush, 2d);
            }
        }
    }

    private Rect GetPreviewDisplayRect(BitmapSource bitmap)
    {
        var hostWidth = PreviewHost.ActualWidth;
        var hostHeight = PreviewHost.ActualHeight;
        if (hostWidth <= 0d || hostHeight <= 0d || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return Rect.Empty;
        }

        var scale = Math.Min(hostWidth / bitmap.PixelWidth, hostHeight / bitmap.PixelHeight);
        var width = bitmap.PixelWidth * scale;
        var height = bitmap.PixelHeight * scale;
        return new Rect((hostWidth - width) / 2d, (hostHeight - height) / 2d, width, height);
    }

    private Color GetFaceCueGuideColor()
    {
        if (_currentFaceAnalysis is not { BaselineReady: true } analysis)
        {
            return Color.FromRgb(74, 147, 214);
        }

        if (analysis.EyeDropPercent >= GetEyeCueThreshold()
            || analysis.CompositeCuePercent >= GetCompositeCueThreshold())
        {
            return Color.FromRgb(220, 91, 91);
        }

        if (analysis.JawChangePercent >= GetJawCueThreshold())
        {
            return Color.FromRgb(215, 165, 58);
        }

        return Color.FromRgb(74, 163, 107);
    }

    private void AddGuideRegion(Rect display, Rect frameRegion, Brush fill, Brush stroke, double thickness)
    {
        var rect = ToDisplayRect(display, frameRegion);
        var shape = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            RadiusX = 3,
            RadiusY = 3,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = thickness
        };

        Canvas.SetLeft(shape, rect.X);
        Canvas.SetTop(shape, rect.Y);
        FaceCueGuideCanvas.Children.Add(shape);
    }

    private void AddGuideLine(Rect display, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        var line = new Line
        {
            X1 = display.X + display.Width * x1,
            Y1 = display.Y + display.Height * y1,
            X2 = display.X + display.Width * x2,
            Y2 = display.Y + display.Height * y2,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        FaceCueGuideCanvas.Children.Add(line);
    }

    private static Rect ToDisplayRect(Rect display, double left, double top, double right, double bottom)
    {
        return new Rect(
            display.X + display.Width * left,
            display.Y + display.Height * top,
            display.Width * (right - left),
            display.Height * (bottom - top));
    }

    private static Rect ToDisplayRect(Rect display, Rect frameRegion)
    {
        return ToDisplayRect(display, frameRegion.Left, frameRegion.Top, frameRegion.Right, frameRegion.Bottom);
    }

    private string CreateOverlayMetrics(double? motion)
    {
        var motionLabel = motion is double value
            ? $"Motion {value:0.0}% / {GetMotionThreshold():0.0}%"
            : $"Motion -- / {GetMotionThreshold():0.0}%";

        if (FaceCueCheckBox.IsChecked != true)
        {
            return $"{motionLabel} | Face cues off";
        }

        var faceLabel = _currentFaceAnalysis is null
            ? "face cues waiting"
            : _currentFaceAnalysis.Status;
        if (_faceFeatureTracker.IsAvailable && FaceAutoFollowCheckBox.IsChecked == true)
        {
            var now = DateTime.UtcNow;
            var detectionLabel = GetFaceFeatureTrackerStatus(now);
            return $"{motionLabel} | {detectionLabel} | {faceLabel}";
        }

        return $"{motionLabel} | {faceLabel}";
    }

    private string GetFaceFeatureTrackerStatus(DateTime now)
    {
        if (!_faceFeatureTracker.IsAvailable)
        {
            return "dynamic tracker unavailable";
        }

        if (HasFreshFaceFeatureLock(now))
        {
            return "dynamic face lock";
        }

        if (HasUsableFaceFeatureLock(now))
        {
            return "dynamic face hold";
        }

        return Interlocked.CompareExchange(ref _faceFeatureDetectionPending, 0, 0) == 1
            ? "dynamic search"
            : "dynamic waiting";
    }

    private void WriteAnnotatedVideoFrame(BitmapSource bitmap, DateTime timestamp, string state, string metrics, string trigger, string accentColor)
    {
        if (_activeEpisodeStartedAt is null)
        {
            if ((timestamp - _lastBufferedVideoFrameAt).TotalSeconds < 1d / EventVideoFramesPerSecond)
            {
                return;
            }

            var bufferedJpeg = CreateAnnotatedJpeg(bitmap, timestamp, state, metrics, trigger, accentColor, _currentFaceAnalysis, _currentFaceFeatureDetection, GetFaceCueLayout(), GetEyeCueThreshold(), GetJawCueThreshold(), GetCompositeCueThreshold());
            AddPreEventVideoFrame(timestamp, bufferedJpeg);
            return;
        }

        if ((timestamp - _lastRecordedVideoFrameAt).TotalSeconds < 1d / EventVideoFramesPerSecond)
        {
            return;
        }

        _lastRecordedVideoFrameAt = timestamp;
        var jpeg = CreateAnnotatedJpeg(bitmap, timestamp, state, metrics, trigger, accentColor, _currentFaceAnalysis, _currentFaceFeatureDetection, GetFaceCueLayout(), GetEyeCueThreshold(), GetJawCueThreshold(), GetCompositeCueThreshold());
        _eventRecorder.AddFrame(jpeg);
    }

    private void BufferAnnotatedFrame(BitmapSource bitmap, DateTime timestamp, string state, string metrics, string trigger, string accentColor)
    {
        if ((timestamp - _lastBufferedVideoFrameAt).TotalSeconds < 1d / EventVideoFramesPerSecond)
        {
            return;
        }

        var jpeg = CreateAnnotatedJpeg(bitmap, timestamp, state, metrics, trigger, accentColor, _currentFaceAnalysis, _currentFaceFeatureDetection, GetFaceCueLayout(), GetEyeCueThreshold(), GetJawCueThreshold(), GetCompositeCueThreshold());
        AddPreEventVideoFrame(timestamp, jpeg);
    }

    private void AddPreEventVideoFrame(DateTime timestamp, byte[] jpeg)
    {
        _lastBufferedVideoFrameAt = timestamp;
        _preEventVideoFrames.Enqueue(new BufferedVideoFrame(timestamp, jpeg));
        var oldestAllowed = timestamp.AddSeconds(-PreEventVideoSeconds);
        var maximumFrames = (int)(PreEventVideoSeconds * EventVideoFramesPerSecond);
        while (_preEventVideoFrames.Count > 0
            && (_preEventVideoFrames.Peek().Timestamp < oldestAllowed || _preEventVideoFrames.Count > maximumFrames))
        {
            _preEventVideoFrames.Dequeue();
        }
    }

    private static byte[] CreateAnnotatedJpeg(
        BitmapSource bitmap,
        DateTime timestamp,
        string state,
        string metrics,
        string trigger,
        string accentColor,
        FaceCueAnalysis? faceAnalysis,
        FaceFeatureDetection featureDetection,
        FaceCueGuideLayout layout,
        double eyeCueThreshold,
        double jawCueThreshold,
        double compositeCueThreshold)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawImage(bitmap, new Rect(0, 0, width, height));
            DrawFaceCueGuides(context, new Rect(0, 0, width, height), faceAnalysis, featureDetection, layout, eyeCueThreshold, jawCueThreshold, compositeCueThreshold);

            var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
            var pixelsPerDip = dpi.PixelsPerDip;
            var font = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var smallFont = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var margin = Math.Max(16d, width * 0.018d);
            var boxWidth = Math.Min(width - margin * 2d, Math.Max(420d, width * 0.42d));
            var lineHeight = Math.Max(22d, height * 0.036d);
            var boxHeight = lineHeight * 4.6d;
            var box = new Rect(margin, margin, boxWidth, boxHeight);
            var accent = (Color)ColorConverter.ConvertFromString(accentColor);

            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(218, 8, 13, 18)), new Pen(new SolidColorBrush(accent), 3), box, 4, 4);

            DrawText(context, state, font, Math.Max(18d, height * 0.028d), Brushes.White, box.X + 14, box.Y + 10, box.Width - 28, pixelsPerDip);
            DrawText(context, timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture), smallFont, Math.Max(14d, height * 0.021d), Brushes.WhiteSmoke, box.X + 14, box.Y + lineHeight + 12, box.Width - 28, pixelsPerDip);
            DrawText(context, metrics, smallFont, Math.Max(14d, height * 0.021d), new SolidColorBrush(Color.FromRgb(185, 215, 239)), box.X + 14, box.Y + lineHeight * 2d + 12, box.Width - 28, pixelsPerDip);
            DrawText(context, trigger, smallFont, Math.Max(13d, height * 0.019d), new SolidColorBrush(Color.FromRgb(220, 231, 239)), box.X + 14, box.Y + lineHeight * 3d + 12, box.Width - 28, pixelsPerDip);
        }

        var render = new RenderTargetBitmap(width, height, bitmap.DpiX, bitmap.DpiY, PixelFormats.Pbgra32);
        render.Render(visual);
        render.Freeze();

        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(render));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void DrawFaceCueGuides(
        DrawingContext context,
        Rect display,
        FaceCueAnalysis? analysis,
        FaceFeatureDetection featureDetection,
        FaceCueGuideLayout layout,
        double eyeCueThreshold,
        double jawCueThreshold,
        double compositeCueThreshold)
    {
        var accent = GetFaceCueGuideColor(analysis, eyeCueThreshold, jawCueThreshold, compositeCueThreshold);
        var regionBrush = new SolidColorBrush(Color.FromArgb(34, accent.R, accent.G, accent.B));
        var lineBrush = new SolidColorBrush(Color.FromArgb(235, accent.R, accent.G, accent.B));
        var supportBrush = new SolidColorBrush(Color.FromArgb(175, 185, 215, 239));
        var regionPen = new Pen(new SolidColorBrush(Color.FromArgb(170, accent.R, accent.G, accent.B)), Math.Max(2d, display.Height * 0.004d));
        var linePen = new Pen(lineBrush, Math.Max(3d, display.Height * 0.006d))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var supportPen = new Pen(supportBrush, Math.Max(1d, display.Height * 0.002d));

        var face = layout.GetFaceBox();
        var leftEye = layout.ToFrameRect(layout.LeftEye);
        var rightEye = layout.ToFrameRect(layout.RightEye);
        var jaw = layout.ToFrameRect(layout.Jaw);

        context.DrawRoundedRectangle(Brushes.Transparent, supportPen, ToDisplayRect(display, face), 3, 3);
        context.DrawRoundedRectangle(regionBrush, regionPen, ToDisplayRect(display, leftEye), 3, 3);
        context.DrawRoundedRectangle(regionBrush, regionPen, ToDisplayRect(display, rightEye), 3, 3);
        context.DrawRoundedRectangle(regionBrush, regionPen, ToDisplayRect(display, jaw), 3, 3);

        DrawRelativeLine(context, display, leftEye.Left, leftEye.Top + leftEye.Height * 0.50d, leftEye.Right, leftEye.Top + leftEye.Height * 0.50d, linePen);
        DrawRelativeLine(context, display, rightEye.Left, rightEye.Top + rightEye.Height * 0.50d, rightEye.Right, rightEye.Top + rightEye.Height * 0.50d, linePen);
        DrawRelativeLine(context, display, jaw.Left + jaw.Width * 0.16d, jaw.Top + jaw.Height * 0.38d, jaw.Right - jaw.Width * 0.16d, jaw.Top + jaw.Height * 0.38d, linePen);
        DrawRelativeLine(context, display, face.Left + face.Width * 0.50d, face.Top, face.Left + face.Width * 0.50d, face.Bottom, supportPen);

        if (featureDetection.HasFace)
        {
            var detectorPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 244, 211, 94)), Math.Max(2d, display.Height * 0.004d));
            context.DrawRoundedRectangle(Brushes.Transparent, detectorPen, ToDisplayRect(display, featureDetection.FaceBox), 3, 3);
            if (featureDetection.LeftEyeBox is Rect leftEyeBox)
            {
                context.DrawRoundedRectangle(Brushes.Transparent, detectorPen, ToDisplayRect(display, leftEyeBox), 3, 3);
            }

            if (featureDetection.RightEyeBox is Rect rightEyeBox)
            {
                context.DrawRoundedRectangle(Brushes.Transparent, detectorPen, ToDisplayRect(display, rightEyeBox), 3, 3);
            }

            if (featureDetection.MouthBox is Rect mouthBox)
            {
                context.DrawRoundedRectangle(Brushes.Transparent, detectorPen, ToDisplayRect(display, mouthBox), 3, 3);
            }
        }
    }

    private static Color GetFaceCueGuideColor(FaceCueAnalysis? analysis, double eyeCueThreshold, double jawCueThreshold, double compositeCueThreshold)
    {
        if (analysis is not { BaselineReady: true })
        {
            return Color.FromRgb(74, 147, 214);
        }

        if (analysis.EyeDropPercent >= eyeCueThreshold
            || analysis.CompositeCuePercent >= compositeCueThreshold)
        {
            return Color.FromRgb(220, 91, 91);
        }

        if (analysis.JawChangePercent >= jawCueThreshold)
        {
            return Color.FromRgb(215, 165, 58);
        }

        return Color.FromRgb(74, 163, 107);
    }

    private static void DrawRelativeLine(DrawingContext context, Rect display, double x1, double y1, double x2, double y2, Pen pen)
    {
        context.DrawLine(
            pen,
            new Point(display.X + display.Width * x1, display.Y + display.Height * y1),
            new Point(display.X + display.Width * x2, display.Y + display.Height * y2));
    }

    private static void DrawText(DrawingContext context, string text, Typeface font, double size, Brush brush, double x, double y, double maxWidth, double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            font,
            size,
            brush,
            pixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 2,
            Trimming = TextTrimming.CharacterEllipsis
        };

        context.DrawText(formatted, new Point(x, y));
    }

    private sealed class EventSummary
    {
        public DateTime StartedAt { get; init; }

        public DateTime EndedAt { get; init; }

        public double DurationSeconds { get; init; }

        public string EndReason { get; init; } = "";

        public IReadOnlyList<string> TriggerReasons { get; init; } = [];

        public string AverageMotion { get; init; } = "";

        public string FaceCueStatus { get; init; } = "";

        public string FaceCueQuality { get; init; } = "";

        public double? FaceCueScore { get; init; }

        public double? EyeOpenness { get; init; }

        public double? EyeDrop { get; init; }

        public double? EyeAsymmetry { get; init; }

        public double? JawChange { get; init; }

        public double? JawAsymmetry { get; init; }

        public double? LowerFaceDrop { get; init; }

        public double? HeadDrift { get; init; }

        public string VideoFile { get; init; } = "";

        public bool VideoOverlayBurnedIn { get; init; }

        public int PreEventVideoSeconds { get; init; }

        public string StartSnapshot { get; init; } = "";

        public string EndSnapshot { get; init; } = "";

        public string OutputFolder { get; init; } = "";
    }

    private sealed record BufferedVideoFrame(DateTime Timestamp, byte[] JpegBytes);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
