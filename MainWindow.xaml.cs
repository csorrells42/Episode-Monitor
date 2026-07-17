using System.Collections.ObjectModel;
using System.Diagnostics;
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
using EpisodeMonitor.Modules.Episodes;
using EpisodeMonitor.Modules.Infrastructure;
using EpisodeMonitor.Modules.Recording;
using EpisodeMonitor.Modules.Vision.Analysis;
using EpisodeMonitor.Modules.Vision.Common;
using EpisodeMonitor.Modules.Vision.Personalization;
using EpisodeMonitor.Modules.Vision.Pipeline;
using EpisodeMonitor.Modules.Vision.Reconstruction;
using EpisodeMonitor.Modules.Webcam;
using EpisodeMonitor.Modules.Webcam.Common;
using EpisodeMonitor.Modules.Webcam.DirectShow;
using EpisodeMonitor.Modules.Webcam.DirectX12;
using EpisodeMonitor.Modules.Webcam.Ffmpeg;
using EpisodeMonitor.Modules.Webcam.MediaFoundation;
using EpisodeMonitor.Modules.Webcam.Pipeline;
using Microsoft.Win32;
using Ellipse = System.Windows.Shapes.Ellipse;
using Line = System.Windows.Shapes.Line;
using Polyline = System.Windows.Shapes.Polyline;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace EpisodeMonitor;

public partial class MainWindow : Window
{
    private const double EventVideoFramesPerSecond = 10d;
    private const int PreEventVideoSeconds = 60;
    private const string PersonalFaceSubjectId = "chris";
    private const string PersonalFaceSubjectDisplayName = "Chris";
    private const string PreferredExternalOutputFolder = @"D:\Episode Monitor Output";
    private const string SettingsFileName = "EpisodeMonitorSettings.json";
    private const int PersonalFaceMotionModelMaxSamples = PersonalFaceMeasurementJournal.DefaultRecentSampleReadLimit;
    private const int PersonalFaceCollectionAuditMaxObservations = 6000;
    private const string AlertBaselineFolderName = "AlertBaseline";
    private const string AlertBaselineFileName = "alert_baseline.json";
    private const string AlertBaselineStartButtonText = "Calibrate Alert Baseline";
    private const string AlertBaselineInProgressButtonText = "Calibrating...";
    private const string SleepEventWatchStartButtonText = "Start Sleep Event Watch";
    private const string SleepEventWatchStopButtonText = "Stop Sleep Event Watch";
    private const string SymptomCaptureStartButtonText = "Capture Symptoms";
    private const string SymptomCaptureStopButtonText = "Stop Capture";
    private const string AvatarLearningStartButtonText = "Start Avatar Learning";
    private const string AvatarLearningStopButtonText = "Stop Avatar Learning";
    private const double PersonalFaceLiveReportSaveIntervalSeconds = 10d;
    private const double PersonalFaceDataAuditBlockThresholdPercent = 50d;
    private const int PersonalFaceDataAuditMinimumSamplesToBlock = 60;
    private const int PersonalFacePoseReferenceMinimumSamples = 12;
    private const int LastGoodFeatureMeshStabilityMinimumSamplesToHold = 3;
    private const double LastGoodFeatureMeshStabilityHoldThresholdPercent = 62d;
    private const double LastGoodFeatureMeshBRotationMinimumRangeDegrees = 14d;
    private const double Insta360Link2ProHorizontalFovDegrees = 71.4d;
    private const string PersonalFaceModelFolderName = "PersonalFaceModel";
    private const string PersonalFaceArchiveFolderName = "PersonalFaceModelArchive";
    private static readonly TimeSpan CalibrationSymptomFreeWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan TrackingOverlayRefreshInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FaceFeatureDetectionTargetInterval = TimeSpan.FromMilliseconds(120);
    private static readonly SolidColorBrush StartActionButtonBackground = CreateFrozenBrush(0x1f, 0x7a, 0x43);
    private static readonly SolidColorBrush StartActionButtonBorder = CreateFrozenBrush(0x52, 0xc4, 0x7b);
    private static readonly SolidColorBrush StopActionButtonBackground = CreateFrozenBrush(0x9d, 0x2f, 0x2f);
    private static readonly SolidColorBrush StopActionButtonBorder = CreateFrozenBrush(0xe0, 0x69, 0x69);
    private static readonly int[] DenseMeshEyeA =
    [
        33, 246, 161, 160, 159, 158, 157, 173, 133, 155, 154, 153, 145, 144, 163, 7
    ];
    private static readonly int[] DenseMeshEyeB =
    [
        362, 398, 384, 385, 386, 387, 388, 466, 263, 249, 390, 373, 374, 380, 381, 382
    ];
    private static readonly int[] DenseMeshJawCenter =
    [
        152, 148, 176, 149, 150, 377, 400, 378, 379
    ];

    private readonly FfmpegCameraModeService _cameraModeService = new();
    private readonly DirectShowCameraControlService _cameraControlService = new();
    private readonly CameraPreviewService _previewService = new();
    private readonly FfmpegEventRecorderService _eventRecorder = new();
    private readonly IFaceCueAnalyzer _faceCueAnalyzer = new FaceCueAnalyzer();
    private readonly CompositeFaceLandmarkTracker _faceLandmarkTracker = new();
    private readonly FaceLandmarkTemporalReconstructor _faceLandmarkReconstructor = new();
    private readonly FaceLandmarkMetricCalculator _faceLandmarkMetricCalculator = new();
    private readonly FaceLandmarkCueAnalyzer _faceLandmarkCueAnalyzer = new();
    private readonly FaceLandmarkTrendAnalyzer _faceLandmarkTrendAnalyzer = new();
    private readonly FaceLockStabilityAnalyzer _faceLockStabilityAnalyzer = new();
    private readonly HeadPoseEstimator _headPoseEstimator = new();
    private readonly EpisodeMonitorStartupOptions _startupOptions;
    private readonly PersonalFaceModelBuilder _personalFaceModelBuilder = new(
        PersonalFaceSubjectId,
        PersonalFaceSubjectDisplayName,
        PersonalFaceSubject.ManualConfirmationMode);
    private readonly PersonalFaceModelStore _personalFaceModelStore = new();
    private readonly PersonalFaceMeasurementJournal _personalFaceMeasurementJournal = new();
    private readonly PersonalFaceCaptureQualityAnalyzer _personalFaceCaptureQualityAnalyzer = new();
    private readonly PersonalFaceMotionModelStore _personalFaceMotionModelStore = new();
    private readonly PersonalFaceCollectionAuditBuilder _personalFaceCollectionAuditBuilder = new();
    private readonly LandmarkEventAggregate _landmarkEventAggregate = new();
    private readonly LandmarkEventTimeline _landmarkEventTimeline = new();
    private readonly EpisodeEventDatabase _eventDatabase = new();
    private readonly LastGoodFeatureMeshStore _lastGoodFeatureMeshStore = new();
    private readonly object _faceLandmarkTrackerLock = new();
    private readonly ObservableCollection<EpisodeMonitorEvent> _events = [];
    private readonly object _frameLock = new();
    private readonly object _previewFramePumpLock = new();
    private readonly object _directX12PreviewLock = new();
    private readonly object _directX12AnalysisFrameLock = new();
    private readonly object _faceFeatureDetectionFrameLock = new();
    private readonly object _personalFaceReportWriterLock = new();
    private readonly Queue<BufferedVideoFrame> _preEventVideoFrames = new();
    private readonly List<PersonalFaceCollectionAuditObservation> _personalFaceCollectionAuditObservations = [];
    private readonly List<LastGoodFeatureMeshSample> _lastGoodFeatureMeshSamples = [];
    private readonly DispatcherTimer _calibrationGuardTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private IReadOnlyList<CameraDevice> _cameras = [];
    private CancellationTokenSource? _modeLoadCancellation;
    private string _outputFolder;
    private byte[]? _previousSample;
    private BitmapSource? _latestFrame;
    private FaceCueAnalysis? _currentFaceAnalysis;
    private FaceFeatureDetection _currentFaceFeatureDetection = FaceFeatureDetection.None;
    private FaceLandmarkFrame _currentFaceLandmarkFrame = FaceLandmarkFrame.None;
    private FaceLandmarkMetrics _currentFaceLandmarkMetrics = FaceLandmarkMetrics.None;
    private FaceLandmarkCueAnalysis? _currentFaceLandmarkCueAnalysis;
    private FaceLandmarkTrendAnalysis _currentFaceLandmarkTrendAnalysis = FaceLandmarkTrendAnalysis.Waiting;
    private FaceLockStabilityAnalysis _currentFaceLockStabilityAnalysis = FaceLockStabilityAnalysis.Waiting;
    private HeadPoseEstimate _currentHeadPoseEstimate = HeadPoseEstimate.None;
    private PersonalFaceModelUpdate _currentPersonalFaceModelUpdate;
    private PersonalFaceCaptureQualityAssessment _currentPersonalFaceCaptureQuality = PersonalFaceCaptureQualityAssessment.Waiting;
    private PersonalFaceMotionModel _currentPersonalFaceMotionModel = new();
    private PersonalFaceCorpusReadiness _currentPersonalFaceCorpusReadiness = new();
    private PersonalFaceCollectionAudit _currentPersonalFaceCollectionAudit = new();
    private MeasurementAvatarCapturePlan? _currentMeasurementAvatarCapturePlan;
    private DateTime? _lowMotionStartedAt;
    private DateTime? _eyeCueStartedAt;
    private DateTime? _jawCueStartedAt;
    private DateTime? _eyeTrendCueStartedAt;
    private DateTime? _lastSymptomAt;
    private DateTime? _calibrationHoldUntil;
    private DateTime? _activeEpisodeStartedAt;
    private DateTime? _activeEpisodeEarliestAutoEndAt;
    private string _episodeStartSnapshot = "";
    private string _activeEventFolder = "";
    private string _activeEventVideo = "";
    private string _personalFaceModelPath = "";
    private string _personalFaceMotionModelPath = "";
    private string _personalFaceCorpusReadinessPath = "";
    private string _personalFaceCollectionAuditPath = "";
    private string _measurementFacePreviewPath = "";
    private string _measurementAvatarTrainingPackagePath = "";
    private string _measurementAvatarCapturePlanPath = "";
    private string _measurementAvatarSystemDashboardPath = "";
    private string _lastGoodFeatureMeshJsonPath = "";
    private string _lastGoodFeatureMeshHtmlPath = "";
    private string _personalFaceMeasurementJournalPath = "";
    private DateTime? _alertBaselineSavedAtUtc;
    private string _alertBaselineCameraName = "";
    private string _alertBaselineModeLabel = "";
    private long _personalFaceMeasurementJournalSizeBytes;
    private List<string> _activeTriggerReasons = [];
    private BitmapSource? _pendingPreviewFrame;
    private BitmapSource? _pendingFaceFeatureDetectionFrame;
    private TextureNativeFrameLease? _pendingDirectX12AnalysisFrame;
    private PersonalFaceReportSnapshot? _pendingPersonalFaceReportSnapshot;
    private Task? _personalFaceReportWriterTask;
    private Direct3D12PreviewHost? _directX12PreviewHost;
    private Dx12Camera? _directX12NativeCamera;
    private double _episodeMotionSum;
    private int _episodeMotionSamples;
    private DateTime _lastBufferedVideoFrameAt = DateTime.MinValue;
    private DateTime _lastRecordedVideoFrameAt = DateTime.MinValue;
    private DateTime _lastPreviewFrameAcceptedAt = DateTime.MinValue;
    private DateTime _lastDirectX12DiagnosticsAtUtc = DateTime.MinValue;
    private DateTime _lastDirectX12AnalysisFrameAtUtc = DateTime.MinValue;
    private DateTime _lastPersonalFaceModelSavedAtUtc = DateTime.MinValue;
    private DateTime _lastTrackingOverlayUpdateAtUtc = DateTime.MinValue;
    private DateTime _previewReplacementWindowStartedAtUtc = DateTime.MinValue;
    private string _lastTrackingOverlayState = "";
    private string _lastTrackingOverlayMetrics = "";
    private string _lastTrackingOverlayTrigger = "";
    private string _lastTrackingOverlayAccentColor = "";
    private string _avatarHistoricalDataAuditSummary = "";
    private string _avatarTrackingAuditHoldSummary = "";
    private string _avatarRecentMeshStabilitySummary = "";
    private string _trackingFidelityConfigurationStatus = "";
    private string _lastGoodFeatureMeshStatus = "last good feature mesh waiting";
    private LastGoodFeatureMeshStabilityReport _lastGoodFeatureMeshStability = new();
    private SolidColorBrush? _lastTrackingOverlayAccentBrush;
    private double _directX12PreviewMaxRenderFramesPerSecond;
    private double _cameraDisplayFramesPerSecond;
    private double _featureOverlayFramesPerSecond;
    private double _directX12RenderFramesPerSecond;
    private TimeSpan _directX12AnalysisFrameInterval = TimeSpan.FromSeconds(1d / 5d);
    private int _directX12AnalysisMaxOutputWidth = 3840;
    private int _previewFramesReplacedSinceWarning;
    private int _cameraDisplayFramesInHealthWindow;
    private int _featureOverlayFramesInHealthWindow;
    private int _uiFramePending;
    private int _previewWarningPending;
    private int _faceFeatureDetectionPending;
    private bool _avatarHistoricalDataSuspect;
    private bool _avatarTrackingAuditHold;
    private bool _avatarRecentMeshStabilityHold;
    private long _directX12FrameNumber;
    private int _directX12AnalysisWorkerQueued;
    private bool _alertBaselineCalibrationActive;
    private bool _sleepEventWatchActive;
    private bool _symptomCaptureActive;
    private bool _avatarLearningRequested;
    private bool _showLiveWireframePreview;
    private bool _isCameraEnabled;
    private bool _isUpdatingCameraToggle;
    private bool _isChoosingCameraModeForFidelity;
    private bool _isRefreshingCameras;
    private bool _isLoadingCameraControls;
    private bool _isUpdatingCameraControlUi;
    private bool _isSnappingSlider;
    private bool _isClosing;
    private bool _startupOptionsApplied;
    private FaceCueGuideLayout? _activeFaceCueLayout;
    private DateTime _lastFaceAutoFollowAt = DateTime.MinValue;
    private DateTime _lastFaceFeatureDetectionAt = DateTime.MinValue;
    private DateTime _pendingFaceFeatureDetectionCapturedAtUtc = DateTime.MinValue;
    private DateTime _lastFaceFeatureLockAt = DateTime.MinValue;
    private DateTime _cameraHealthWindowStartedAtUtc = DateTime.MinValue;
    private DateTime _featureOverlayHealthWindowStartedAtUtc = DateTime.MinValue;
    private DateTime _lastGoodFeatureMeshCapturedAtUtc = DateTime.MinValue;

    private static readonly IReadOnlyList<TrackingFidelityOption> TrackingFidelityOptions =
    [
        new("Safe preview - 960px / 15 fps", 960, 15d),
        new("HD camera - 1920px / 18 fps", 1920, 18d),
        new("4K camera - 1920px analysis / 15 fps", 3840, 15d)
    ];

    private static SolidColorBrush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateFrozenBrush(byte red, byte green, byte blue, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    public MainWindow()
        : this(EpisodeMonitorStartupOptions.Default)
    {
    }

    public MainWindow(EpisodeMonitorStartupOptions startupOptions)
    {
        _startupOptions = startupOptions ?? EpisodeMonitorStartupOptions.Default;
        InitializeComponent();
        _outputFolder = ResolveInitialOutputFolder(_startupOptions.OutputFolder);
        SaveOutputFolderSetting(_outputFolder);
        _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
            false,
            PersonalFaceModelRejectionKind.NoFace,
            "waiting for face landmarks",
            0d,
            _personalFaceModelBuilder.CurrentModel);
        _previewService.FrameAvailable += PreviewFrameAvailable;
        _previewService.CameraFrameAvailable += PreviewCameraFrameAvailable;
        _previewService.StatusChanged += PreviewStatusChanged;
        _eventRecorder.StatusChanged += PreviewStatusChanged;
        _calibrationGuardTimer.Tick += CalibrationGuardTick;
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        EnableDarkWindowFrame();
        EventGrid.ItemsSource = _events;
        PruneOldEventData();
        LoadTodaysEventListFromOutputFolder();
        TrackingFidelityComboBox.ItemsSource = TrackingFidelityOptions;
        TrackingFidelityComboBox.DisplayMemberPath = nameof(TrackingFidelityOption.Label);
        TrackingFidelityComboBox.SelectedIndex = 2;
        ApplyTrackingFidelity();
        UpdateOutputFolderText();
        UpdateSettingLabels();
        UpdateSleepEventWatchButtonState();
        LoadAlertBaselineFromOutputFolder(showStatus: false);
        LoadPersonalFaceModelFromOutputFolder(showStatus: false);
        UpdateCalibrationGuard();
        UpdateAvatarLearningStatusUi();
        _calibrationGuardTimer.Start();
        Dispatcher.InvokeAsync(async () => await RefreshCamerasAsync(), DispatcherPriority.ApplicationIdle);
        Dispatcher.InvokeAsync(ApplyStartupOptionsAfterLoad, DispatcherPriority.ApplicationIdle);
    }

    private void ApplyStartupOptionsAfterLoad()
    {
        if (_startupOptionsApplied)
        {
            return;
        }

        _startupOptionsApplied = true;
        if (_startupOptions.StartAvatarLearning)
        {
            _avatarLearningRequested = true;
        }

        if (_startupOptions.OpenAvatarSystem)
        {
            OpenAvatarSystemClicked(this, new RoutedEventArgs());
        }

        UpdateAvatarLearningStatusUi();
        if (_startupOptions.EasyAvatarMode)
        {
            var state = GetAvatarEasyModeState();
            var status = $"Easy Avatar Mode launch: {state.Title}. {state.Detail}";
            SetStatus(status);
            MonitorStatusText.Text = status;
        }
    }

    private void PruneOldEventData()
    {
        try
        {
            var cutoff = DateTime.Today.AddDays(-30);
            var deleted = _eventDatabase.DeleteEventsOlderThan(_outputFolder, cutoff);
            DeleteEventArtifacts(deleted);
            if (deleted.Count > 0)
            {
                SetStatus($"Pruned {deleted.Count} event{(deleted.Count == 1 ? "" : "s")} older than 30 days.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Could not prune old event data: {ex.Message}");
        }
    }

    private void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        _modeLoadCancellation?.Cancel();
        _modeLoadCancellation?.Dispose();
        EndActiveEpisode(DateTime.Now, null, "App closing");
        _calibrationGuardTimer.Stop();
        _previewService.CameraFrameAvailable -= PreviewCameraFrameAvailable;
        SaveAlertBaselineToOutputFolder();
        SavePersonalFaceModelOnClose();
        lock (_faceLandmarkTrackerLock)
        {
            _faceLandmarkTracker.Dispose();
        }
        DisposeDirectX12PreviewHost();
        _eventRecorder.Dispose();
        _previewService.Dispose();
    }

    private void SavePersonalFaceModelOnClose()
    {
        try
        {
            if (_currentPersonalFaceModelUpdate.Model.AcceptedSamples > 0)
            {
                var folder = GetPersonalFaceModelFolder();
                _personalFaceModelPath = _personalFaceModelStore.Write(folder, _currentPersonalFaceModelUpdate.Model);
                _personalFaceMeasurementJournalSizeBytes = PersonalFaceMeasurementJournal.GetMeasurementsSizeBytes(folder);
            }
        }
        catch
        {
            // Closing should not be blocked by a final best-effort model save.
        }
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
        CameraComboBox.DisplayMemberPath = nameof(CameraDevice.DisplayName);

        if (_cameras.Count > 0)
        {
            CameraComboBox.SelectedIndex = 0;
            SetStatus($"Found {_cameras.Count} camera{(_cameras.Count == 1 ? "" : "s")}.");
        }
        else
        {
            CameraModeComboBox.ItemsSource = new[] { CameraVideoMode.Auto };
            CameraModeComboBox.SelectedIndex = 0;
            CameraControlsPanel.Children.Clear();
            CameraControlsStatusText.Text = CameraControlText.FormatChooseCameraControlsStatus();
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
                IReadOnlyList<CameraDevice> mediaFoundationDevices;
                try
                {
                    mediaFoundationDevices = MediaFoundationCameraEnumerator.GetVideoInputDevices();
                }
                catch
                {
                    mediaFoundationDevices = [];
                }

                IReadOnlyList<CameraDevice> directShowDevices;
                try
                {
                    directShowDevices = DirectShowCameraEnumerator.GetVideoInputDevices();
                }
                catch
                {
                    directShowDevices = [];
                }

                completion.SetResult(CameraDeviceCatalog.MergeDevices(mediaFoundationDevices, directShowDevices));
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
        await LoadCameraControlsAsync(camera);
        UpdateCalibrationGuard();
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
            var modes = await _cameraModeService.GetModesAsync(camera, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            CameraModeComboBox.ItemsSource = modes;
            SelectRecommendedCameraModeForFidelity(replaceAutoOnly: false);
            var selectedMode = CameraModeComboBox.SelectedItem as CameraVideoMode ?? CameraVideoMode.Auto;
            SetStatus($"Loaded {modes.Count} mode{(modes.Count == 1 ? "" : "s")} for {camera.Name}. Selected {selectedMode.Label}.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load camera modes: {ex.Message}");
        }
    }

    private async void RefreshCameraControlsClicked(object sender, RoutedEventArgs e)
    {
        if (CameraComboBox.SelectedItem is not CameraDevice camera)
        {
            CameraControlsPanel.Children.Clear();
            CameraControlsStatusText.Text = CameraControlText.FormatChooseCameraControlsStatus();
            return;
        }

        await LoadCameraControlsAsync(camera);
    }

    private async Task LoadCameraControlsAsync(CameraDevice camera)
    {
        if (_isLoadingCameraControls)
        {
            return;
        }

        _isLoadingCameraControls = true;
        CameraControlsPanel.Children.Clear();
        CameraControlsStatusText.Text = $"Loading controls for {camera.Name}...";

        try
        {
            var controls = await GetCameraControlsAsync(camera).WaitAsync(TimeSpan.FromSeconds(5));
            if (!ReferenceEquals(CameraComboBox.SelectedItem, camera))
            {
                return;
            }

            BuildCameraControlRows(camera, controls);
            CameraControlsStatusText.Text = controls.Count == 0
                ? CameraControlText.FormatNoCameraControlsStatus()
                : CameraControlText.FormatCameraControlsLoadedStatus(camera, controls.Count);
        }
        catch (TimeoutException)
        {
            CameraControlsStatusText.Text = "Camera controls are taking longer than expected. Try Refresh after the camera is idle.";
        }
        catch (Exception ex)
        {
            CameraControlsStatusText.Text = $"Could not load camera controls: {ex.Message}";
        }
        finally
        {
            _isLoadingCameraControls = false;
        }
    }

    private Task<IReadOnlyList<CameraControlItem>> GetCameraControlsAsync(CameraDevice camera)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<CameraControlItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(_cameraControlService.GetControls(camera));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Episode Monitor Camera Controls"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void BuildCameraControlRows(CameraDevice camera, IReadOnlyList<CameraControlItem> controls)
    {
        CameraControlsPanel.Children.Clear();
        foreach (var control in controls.OrderBy(static control => control.Kind).ThenBy(static control => control.Name))
        {
            CameraControlsPanel.Children.Add(CreateCameraControlRow(camera, control));
        }
    }

    private UIElement CreateCameraControlRow(CameraDevice camera, CameraControlItem control)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new TextBlock
        {
            Text = control.Name,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(nameText);

        var valueText = new TextBlock
        {
            Text = CameraControlText.FormatCameraControlValue(control.Value),
            Foreground = new SolidColorBrush(Color.FromRgb(185, 215, 239)),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueText, 1);
        header.Children.Add(valueText);

        var autoCheckBox = new CheckBox
        {
            Content = "Auto",
            IsChecked = control.IsAuto,
            IsEnabled = control.SupportsAuto,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(autoCheckBox, 2);
        header.Children.Add(autoCheckBox);

        var slider = new Slider
        {
            Minimum = control.Minimum,
            Maximum = control.Maximum,
            Value = Math.Clamp(control.Value, control.Minimum, control.Maximum),
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            IsSnapToTickEnabled = false,
            Ticks = new DoubleCollection { control.DefaultValue },
            ToolTip = $"Default: {CameraControlText.FormatCameraControlValue(control.DefaultValue)}",
            IsEnabled = !control.IsAuto || !control.SupportsAuto
        };

        var binding = new CameraControlBinding(camera, control, valueText, slider, autoCheckBox);
        slider.Tag = binding;
        autoCheckBox.Tag = binding;
        slider.ValueChanged += CameraControlSliderChanged;
        autoCheckBox.Checked += CameraControlAutoChanged;
        autoCheckBox.Unchecked += CameraControlAutoChanged;

        panel.Children.Add(header);
        panel.Children.Add(slider);
        return panel;
    }

    private void CameraControlSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingCameraControlUi
            || sender is not Slider slider
            || slider.Tag is not CameraControlBinding binding)
        {
            return;
        }

        var value = CameraControlText.RoundCameraControlToStep(slider.Value, binding.Control);
        value = CameraControlText.ApplyCameraControlDefaultMagnet(value, binding.Control);
        _isUpdatingCameraControlUi = true;
        try
        {
            if (Math.Abs(slider.Value - value) > 0.001d)
            {
                slider.Value = value;
            }

            if (binding.AutoCheckBox is not null)
            {
                binding.AutoCheckBox.IsChecked = false;
            }
        }
        finally
        {
            _isUpdatingCameraControlUi = false;
        }

        ApplyCameraControl(binding, value, isAuto: false);
    }

    private void CameraControlAutoChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingCameraControlUi
            || sender is not CheckBox checkBox
            || checkBox.Tag is not CameraControlBinding binding)
        {
            return;
        }

        var isAuto = checkBox.IsChecked == true;
        var value = CameraControlText.RoundCameraControlToStep(binding.Slider.Value, binding.Control);
        binding.Slider.IsEnabled = !isAuto;
        ApplyCameraControl(binding, value, isAuto);
    }

    private void ApplyCameraControl(CameraControlBinding binding, int value, bool isAuto)
    {
        if (CameraComboBox.SelectedItem is not CameraDevice selectedCamera
            || !string.Equals(selectedCamera.DevicePath, binding.Camera.DevicePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        binding.Control.Value = value;
        binding.Control.IsAuto = isAuto;
        binding.ValueText.Text = isAuto
            ? "Auto"
            : CameraControlText.FormatCameraControlValue(value);

        var success = _cameraControlService.SetControl(binding.Camera, binding.Control, value, isAuto);
        CameraControlsStatusText.Text = CameraControlText.FormatCameraControlSetStatus(binding.Control, value, isAuto, success);
    }

    private void CameraModeSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isChoosingCameraModeForFidelity)
        {
            return;
        }

        if (_isCameraEnabled)
        {
            RestartPreview();
        }
        UpdateCalibrationGuard();
    }

    private void TrackingFidelitySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ApplyTrackingFidelity();
        SelectRecommendedCameraModeForFidelity(replaceAutoOnly: true);
        if (_isCameraEnabled)
        {
            RestartPreview();
        }
    }

    private void ApplyTrackingFidelity()
    {
        var option = GetSelectedTrackingFidelityOption();
        var analysisOutputWidth = GetTrackingAnalysisOutputWidth(option);

        _previewService.MaxOutputWidth = analysisOutputWidth;
        _previewService.MaxOutputFramesPerSecond = option.MaxFramesPerSecond;
        _directX12AnalysisMaxOutputWidth = analysisOutputWidth;
        _directX12AnalysisFrameInterval = TimeSpan.FromSeconds(1d / Math.Clamp(option.MaxFramesPerSecond, 1d, 60d));
        _faceLandmarkTracker.MaxDetectionDimension = option.MaxOutputWidth >= 3840 ? 1920 : Math.Clamp(option.MaxOutputWidth, 640, 960);
        TrackingFidelityValueText.Text = option.ShortLabel;
        _trackingFidelityConfigurationStatus = $"Camera mode target: {option.MaxOutputWidth}px. Analysis frames: {analysisOutputWidth}px max at {option.MaxFramesPerSecond:0.###} fps; landmark detector max {_faceLandmarkTracker.MaxDetectionDimension}px.";
        UpdateTrackingFidelityHealthText(force: true);
    }

    private void TrackCameraDisplayFrame(DateTime utcNow)
    {
        if (_cameraHealthWindowStartedAtUtc == DateTime.MinValue)
        {
            _cameraHealthWindowStartedAtUtc = utcNow;
        }

        _cameraDisplayFramesInHealthWindow++;
        var elapsed = utcNow - _cameraHealthWindowStartedAtUtc;
        if (elapsed.TotalSeconds < 2d)
        {
            return;
        }

        _cameraDisplayFramesPerSecond = _cameraDisplayFramesInHealthWindow / Math.Max(0.001d, elapsed.TotalSeconds);
        _cameraDisplayFramesInHealthWindow = 0;
        _cameraHealthWindowStartedAtUtc = utcNow;
        UpdateTrackingFidelityHealthText();
    }

    private void TrackFeatureOverlayFrame(DateTime utcNow)
    {
        if (_featureOverlayHealthWindowStartedAtUtc == DateTime.MinValue)
        {
            _featureOverlayHealthWindowStartedAtUtc = utcNow;
        }

        _featureOverlayFramesInHealthWindow++;
        var elapsed = utcNow - _featureOverlayHealthWindowStartedAtUtc;
        if (elapsed.TotalSeconds < 2d)
        {
            return;
        }

        _featureOverlayFramesPerSecond = _featureOverlayFramesInHealthWindow / Math.Max(0.001d, elapsed.TotalSeconds);
        _featureOverlayFramesInHealthWindow = 0;
        _featureOverlayHealthWindowStartedAtUtc = utcNow;
        UpdateTrackingFidelityHealthText();
    }

    private void UpdateTrackingFidelityHealthText(bool force = false)
    {
        if (TrackingFidelityStatusText is null)
        {
            return;
        }

        var option = GetSelectedTrackingFidelityOption();
        var cameraRate = _directX12RenderFramesPerSecond > 0d
            ? _directX12RenderFramesPerSecond
            : _cameraDisplayFramesPerSecond;
        var cameraLabel = cameraRate > 0d
            ? $"{cameraRate:0.#} fps"
            : "warming";
        var featureLabel = _featureOverlayFramesPerSecond > 0d
            ? $"{_featureOverlayFramesPerSecond:0.#} fps"
            : "warming";
        var featureTarget = Math.Min(
            option.MaxFramesPerSecond,
            1d / Math.Max(0.001d, FaceFeatureDetectionTargetInterval.TotalSeconds));
        var health = "health ok";
        if (cameraRate > 0d && cameraRate < option.MaxFramesPerSecond * 0.55d)
        {
            health = "camera/render rate low";
        }
        else if (_featureOverlayFramesPerSecond > 0d && _featureOverlayFramesPerSecond < featureTarget * 0.45d)
        {
            health = _personalFaceReportWriterTask is { IsCompleted: false }
                ? "feature overlay low while report writer is active"
                : "feature overlay rate low";
        }

        var text = $"{_trackingFidelityConfigurationStatus} Live health: camera/render {cameraLabel}; feature overlay {featureLabel} target {featureTarget:0.#} fps; {health}. {_lastGoodFeatureMeshStatus}.";
        if (force || !string.Equals(TrackingFidelityStatusText.Text, text, StringComparison.Ordinal))
        {
            TrackingFidelityStatusText.Text = text;
        }
    }

    private static int GetTrackingAnalysisOutputWidth(TrackingFidelityOption option)
    {
        if (option.MaxOutputWidth >= 3840)
        {
            return 1920;
        }

        return option.MaxOutputWidth;
    }

    private TrackingFidelityOption GetSelectedTrackingFidelityOption()
    {
        return TrackingFidelityComboBox.SelectedItem as TrackingFidelityOption
            ?? TrackingFidelityOptions.ElementAtOrDefault(1)
            ?? TrackingFidelityOptions[0];
    }

    private void SelectRecommendedCameraModeForFidelity(bool replaceAutoOnly)
    {
        var currentMode = CameraModeComboBox.SelectedItem as CameraVideoMode;
        if (replaceAutoOnly && currentMode is { IsAuto: false })
        {
            return;
        }

        var modes = CameraModeComboBox.Items.OfType<CameraVideoMode>().ToList();
        var recommended = FindRecommendedCameraMode(modes, GetSelectedTrackingFidelityOption());
        if (recommended is null || IsSameCameraMode(currentMode, recommended))
        {
            if (CameraModeComboBox.SelectedIndex < 0 && CameraModeComboBox.Items.Count > 0)
            {
                CameraModeComboBox.SelectedIndex = 0;
            }

            return;
        }

        _isChoosingCameraModeForFidelity = true;
        try
        {
            CameraModeComboBox.SelectedItem = recommended;
        }
        finally
        {
            _isChoosingCameraModeForFidelity = false;
        }
    }

    private static CameraVideoMode? FindRecommendedCameraMode(IReadOnlyList<CameraVideoMode> modes, TrackingFidelityOption option)
    {
        return CameraModeRecommendation.FindRecommendedMode(
            modes,
            option.MaxOutputWidth,
            option.MaxFramesPerSecond);
    }

    private static bool IsSameCameraMode(CameraVideoMode? left, CameraVideoMode right)
    {
        return left is not null
            && left.IsAuto == right.IsAuto
            && left.Width == right.Width
            && left.Height == right.Height
            && Nullable.Equals(left.FramesPerSecond, right.FramesPerSecond)
            && string.Equals(left.InputFormat, right.InputFormat, StringComparison.OrdinalIgnoreCase);
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

    private void DirectX12PreviewChanged(object sender, RoutedEventArgs e)
    {
        if (_isCameraEnabled)
        {
            RestartPreview();
            return;
        }

        UpdateDirectX12PreviewMode();
        if (_latestFrame is not null)
        {
            SetPreviewState("Camera active", _latestFrame);
        }
    }

    private void LiveWireframePreviewChanged(object sender, RoutedEventArgs e)
    {
        _showLiveWireframePreview = LiveWireframePreviewToggle.IsChecked == true;
        LiveWireframePreviewToggle.Content = _showLiveWireframePreview
            ? "Show Webcam Preview"
            : "Show Live Wireframe";
        SetPreviewState(_showLiveWireframePreview ? "Live wireframe preview" : "Camera active", _latestFrame);
    }

    private void LiveWireframeHeadLockChanged(object sender, RoutedEventArgs e)
    {
        if (_showLiveWireframePreview)
        {
            DrawLiveWireframePreview();
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
        ApplyTrackingFidelity();
        _previousSample = null;
        SetPreviewState($"Starting {camera.Name} ({mode.Label})", null);
        SetStatus($"Opening camera: {camera.Name} ({mode.Label})");

        if (IsDirectX12PreviewEnabled() && TryStartDirectX12NativeCamera(camera, mode))
        {
            _isCameraEnabled = true;
            SetCameraToggle(true);
            SetStatus($"Camera active through native DX12 texture path: {camera.Name} ({mode.Label})");
            return;
        }

        _directX12PreviewMaxRenderFramesPerSecond = IsDirectX12PreviewEnabled()
            ? GetDirectX12PreviewRenderFramesPerSecond(mode, nativeTexturePath: false)
            : 0d;
        UpdateDirectX12PreviewMode();
        _isCameraEnabled = await _previewService.StartAsync(camera, mode);

        if (!_isCameraEnabled && !mode.IsAuto)
        {
            SetStatus("Selected camera mode failed. Retrying with Auto safe mode...");
            SetPreviewState("Retrying camera with Auto safe mode", null);
            CameraModeComboBox.SelectedItem = CameraVideoMode.Auto;
            _previousSample = null;
            _isCameraEnabled = await _previewService.StartAsync(camera, CameraVideoMode.Auto);
        }

        SetCameraToggle(_isCameraEnabled);

        if (_isCameraEnabled)
        {
            SetStatus($"Camera active: {camera.Name} ({mode.Label})");
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
        DisposeDirectX12NativeCamera();
        _previewService.Stop();
        DisposeDirectX12PreviewHost();
        ResetDirectX12AnalysisFramePump();
        ResetPreviewFramePump();
        _isCameraEnabled = false;
        _currentFaceFeatureDetection = FaceFeatureDetection.None;
        ResetLandmarkTracking();
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
        lock (_previewFramePumpLock)
        {
            if (_pendingPreviewFrame is not null)
            {
                TrackPreviewFrameReplacement();
            }

            _pendingPreviewFrame = frame;
        }

        QueuePreviewFrameProcessing();
    }

    private void PreviewCameraFrameAvailable(object? sender, CameraFrame frame)
    {
        if (!IsDirectX12PreviewEnabled())
        {
            return;
        }

        Direct3D12PreviewHost? host;
        lock (_directX12PreviewLock)
        {
            host = _directX12PreviewHost;
        }

        if (host is null)
        {
            return;
        }

        try
        {
            host.RenderBgraFrame(frame, Interlocked.Increment(ref _directX12FrameNumber));
        }
        catch (Exception ex)
        {
            Dispatcher.InvokeAsync(() => SetStatus($"DX12 preview paused: {ex.Message}"), DispatcherPriority.Background);
        }
    }

    private bool TryStartDirectX12NativeCamera(CameraDevice camera, CameraVideoMode mode)
    {
        if (TextureNativePreviewPolicy.TryGetPreviewFailure(camera, mode, out var cachedFailure))
        {
            SetStatus($"Native DX12 camera path cooling down after a previous failure: {cachedFailure}. Falling back to standard camera path.");
            return false;
        }

        DisposeDirectX12NativeCamera();
        DisposeDirectX12PreviewHost();
        DirectX12PreviewLayer.Children.Clear();
        DirectX12PreviewLayer.Visibility = Visibility.Visible;

        try
        {
            var target = new Dx12Camera.PreviewTarget(
                DirectX12PreviewLayer,
                PreviewImage,
                PreviewPlaceholder,
                PreviewStateText,
                hostInsertIndex: 0,
                name: "Episode Monitor");
            _directX12PreviewMaxRenderFramesPerSecond = GetDirectX12PreviewRenderFramesPerSecond(mode, nativeTexturePath: true);
            var options = new Dx12CameraOptions
            {
                Camera = camera,
                Mode = mode,
                MaxPreviewRenderFramesPerSecond = _directX12PreviewMaxRenderFramesPerSecond,
                FrameAvailable = DirectX12NativeFrameAvailable,
                TextureFrameAvailable = DirectX12NativeTextureFrameAvailable,
                DiagnosticsChanged = DirectX12NativeDiagnosticsChanged,
                StatusChanged = DirectX12NativeStatusChanged
            };

            _directX12NativeCamera = WebcamModule.StartDx12Camera(target, options);
            TextureNativePreviewPolicy.ForgetPreviewFailure(camera, mode);
            _lastDirectX12AnalysisFrameAtUtc = DateTime.MinValue;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (Exception ex)
        {
            TextureNativePreviewPolicy.RememberPreviewFailure(camera, mode, ex.Message);
            DisposeDirectX12NativeCamera();
            DirectX12PreviewLayer.Children.Clear();
            SetStatus($"Native DX12 camera path unavailable: {ex.Message}. Falling back to standard camera path.");
            return false;
        }
    }

    private void DirectX12NativeStatusChanged(object? sender, string status)
    {
        Dispatcher.InvokeAsync(() => SetStatus(status), DispatcherPriority.Background);
    }

    private void DirectX12NativeDiagnosticsChanged(object? sender, Direct3D12PreviewDiagnostics diagnostics)
    {
        DirectX12PreviewDiagnosticsChanged(sender, diagnostics);
    }

    private void DirectX12NativeFrameAvailable(object? sender, TextureNativeFrameInfo frame)
    {
        if (frame.FrameNumber % 120 != 0)
        {
            return;
        }

        if ((DateTime.UtcNow - _lastDirectX12DiagnosticsAtUtc).TotalSeconds < 6d)
        {
            return;
        }

        Dispatcher.InvokeAsync(
            () => SetStatus($"Native DX12 camera: {frame.Width}x{frame.Height}@{frame.FramesPerSecond:0.###} {frame.MediaSubtype} via {frame.DeviceMode}; preview cap {FormatPreviewRenderLimit()}."),
            DispatcherPriority.Background);
    }

    private void DirectX12NativeTextureFrameAvailable(object? sender, TextureNativeFrameLease frame)
    {
        if (!ShouldAcceptDirectX12AnalysisFrame())
        {
            return;
        }

        var analysisFrame = frame.Duplicate();
        if (analysisFrame is null)
        {
            return;
        }

        QueueDirectX12AnalysisFrame(analysisFrame);
    }

    private void QueueDirectX12AnalysisFrame(TextureNativeFrameLease frame)
    {
        TextureNativeFrameLease? replacedFrame;
        lock (_directX12AnalysisFrameLock)
        {
            replacedFrame = _pendingDirectX12AnalysisFrame;
            _pendingDirectX12AnalysisFrame = frame;
        }

        replacedFrame?.Dispose();
        if (Interlocked.Exchange(ref _directX12AnalysisWorkerQueued, 1) == 0)
        {
            _ = Task.Run(ProcessPendingDirectX12AnalysisFrames);
        }
    }

    private void ProcessPendingDirectX12AnalysisFrames()
    {
        while (!_isClosing)
        {
            TextureNativeFrameLease? frame;
            lock (_directX12AnalysisFrameLock)
            {
                frame = _pendingDirectX12AnalysisFrame;
                _pendingDirectX12AnalysisFrame = null;
            }

            if (frame is null)
            {
                break;
            }

            try
            {
                if (TryCreateBitmapFromDirectX12TextureFrame(frame, out var bitmap))
                {
                    PreviewFrameAvailable(this, bitmap);
                }
            }
            finally
            {
                frame.Dispose();
            }
        }

        Interlocked.Exchange(ref _directX12AnalysisWorkerQueued, 0);
        lock (_directX12AnalysisFrameLock)
        {
            if (_pendingDirectX12AnalysisFrame is not null
                && Interlocked.Exchange(ref _directX12AnalysisWorkerQueued, 1) == 0)
            {
                _ = Task.Run(ProcessPendingDirectX12AnalysisFrames);
            }
        }
    }

    private bool ShouldAcceptDirectX12AnalysisFrame()
    {
        var now = DateTime.UtcNow;
        if (now - _lastDirectX12AnalysisFrameAtUtc < _directX12AnalysisFrameInterval)
        {
            return false;
        }

        _lastDirectX12AnalysisFrameAtUtc = now;
        return true;
    }

    private bool TryCreateBitmapFromDirectX12TextureFrame(TextureNativeFrameLease frame, out BitmapSource bitmap)
    {
        bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);

        var maximumWidth = Math.Clamp(_directX12AnalysisMaxOutputWidth, 320, 3840);
        var bgraBytes = frame.BgraPreviewBytes;
        var bgraStride = frame.BgraPreviewStride;
        var bitmapWidth = frame.Width;
        var bitmapHeight = frame.Height;
        if ((bgraBytes is null || bgraBytes.Length == 0 || bgraStride <= 0)
            && frame.Nv12PreviewBytes is { Length: > 0 } nv12Bytes
            && frame.Nv12PreviewStride > 0)
        {
            bgraBytes = Nv12FrameConverter.ConvertToBgra(
                nv12Bytes,
                frame.Nv12PreviewStride,
                frame.Width,
                frame.Height,
                maximumWidth,
                out bitmapWidth,
                out bitmapHeight,
                out bgraStride);
        }

        if (bgraBytes is null || bgraBytes.Length == 0 || bgraStride <= 0)
        {
            return false;
        }

        var cameraFrame = new CameraFrame(
            bgraBytes,
            bitmapWidth,
            bitmapHeight,
            bgraStride,
            null,
            0,
            $"{frame.MediaSubtype}-analysis");
        return TryCreateBitmapFromBgraCameraFrame(cameraFrame, out bitmap);
    }

    private bool TryCreateBitmapFromBgraCameraFrame(CameraFrame frame, out BitmapSource bitmap)
    {
        bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
        if (!frame.HasBgra || frame.Width <= 0 || frame.Height <= 0)
        {
            return false;
        }

        var source = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            frame.BgraBytes,
            frame.Stride);
        source.Freeze();

        var maximumWidth = Math.Clamp(_directX12AnalysisMaxOutputWidth, 320, 3840);
        if (frame.Width <= maximumWidth)
        {
            bitmap = source;
            return true;
        }

        var scale = maximumWidth / (double)frame.Width;
        var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        transformed.Freeze();
        bitmap = transformed;
        return true;
    }

    private bool IsDirectX12PreviewEnabled()
    {
        return DirectX12PreviewCheckBox.IsChecked == true;
    }

    private static double GetDirectX12PreviewRenderFramesPerSecond(CameraVideoMode mode, bool nativeTexturePath)
    {
        return 0d;
    }

    private string FormatPreviewRenderLimit()
    {
        return _directX12PreviewMaxRenderFramesPerSecond > 0d
            ? $"{_directX12PreviewMaxRenderFramesPerSecond:0.#} fps"
            : "source fps";
    }

    private void UpdateDirectX12PreviewMode()
    {
        if (IsDirectX12PreviewEnabled())
        {
            if (_directX12NativeCamera is not null)
            {
                DirectX12PreviewLayer.Visibility = Visibility.Visible;
                return;
            }

            if (TryEnsureDirectX12PreviewHost())
            {
                DirectX12PreviewLayer.Visibility = Visibility.Visible;
            }

            return;
        }

        DisposeDirectX12NativeCamera();
        DirectX12PreviewLayer.Visibility = Visibility.Collapsed;
        DisposeDirectX12PreviewHost();
    }

    private bool TryEnsureDirectX12PreviewHost()
    {
        lock (_directX12PreviewLock)
        {
            if (_directX12PreviewHost is not null)
            {
                _directX12PreviewHost.LimitRenderRate(_directX12PreviewMaxRenderFramesPerSecond);
                return true;
            }

            try
            {
                var host = WebcamModule.CreateDirect3D12PreviewHost();
                host.LimitRenderRate(_directX12PreviewMaxRenderFramesPerSecond);
                host.HorizontalAlignment = HorizontalAlignment.Stretch;
                host.VerticalAlignment = VerticalAlignment.Stretch;
                host.StatusChanged += DirectX12PreviewStatusChanged;
                host.DiagnosticsChanged += DirectX12PreviewDiagnosticsChanged;
                DirectX12PreviewLayer.Children.Clear();
                DirectX12PreviewLayer.Children.Add(host);
                _directX12PreviewHost = host;
                Interlocked.Exchange(ref _directX12FrameNumber, 0);
                return true;
            }
            catch (Exception ex)
            {
                DirectX12PreviewLayer.Children.Clear();
                DirectX12PreviewLayer.Visibility = Visibility.Collapsed;
                SetStatus($"DX12 preview unavailable: {ex.Message}");
                Dispatcher.InvokeAsync(() => DirectX12PreviewCheckBox.IsChecked = false, DispatcherPriority.Background);
                return false;
            }
        }
    }

    private void DisposeDirectX12PreviewHost()
    {
        Direct3D12PreviewHost? host;
        lock (_directX12PreviewLock)
        {
            host = _directX12PreviewHost;
            _directX12PreviewHost = null;
            DirectX12PreviewLayer.Children.Clear();
        }

        if (host is null)
        {
            return;
        }

        host.StatusChanged -= DirectX12PreviewStatusChanged;
        host.DiagnosticsChanged -= DirectX12PreviewDiagnosticsChanged;
        host.Dispose();
    }

    private void DisposeDirectX12NativeCamera()
    {
        var camera = _directX12NativeCamera;
        if (camera is null)
        {
            return;
        }

        _directX12NativeCamera = null;
        camera.FrameAvailable -= DirectX12NativeFrameAvailable;
        camera.TextureFrameAvailable -= DirectX12NativeTextureFrameAvailable;
        camera.DiagnosticsChanged -= DirectX12NativeDiagnosticsChanged;
        camera.StatusChanged -= DirectX12NativeStatusChanged;
        camera.Dispose();
        ResetDirectX12AnalysisFramePump();
        DirectX12PreviewLayer.Children.Clear();
        _directX12PreviewMaxRenderFramesPerSecond = 0d;
    }

    private void DirectX12PreviewStatusChanged(object? sender, string status)
    {
        Dispatcher.InvokeAsync(() => SetStatus(status), DispatcherPriority.Background);
    }

    private void DirectX12PreviewDiagnosticsChanged(object? sender, Direct3D12PreviewDiagnostics diagnostics)
    {
        var now = DateTime.UtcNow;
        _directX12RenderFramesPerSecond = diagnostics.RenderFramesPerSecond;
        if ((now - _lastDirectX12DiagnosticsAtUtc).TotalSeconds < 2d)
        {
            return;
        }

        _lastDirectX12DiagnosticsAtUtc = now;
        UpdateTrackingFidelityHealthText();
        Dispatcher.InvokeAsync(
            () => SetStatus(FormatDirectX12DiagnosticsStatus(diagnostics)),
            DispatcherPriority.Background);
    }

    private string FormatDirectX12DiagnosticsStatus(Direct3D12PreviewDiagnostics diagnostics)
    {
        var status = diagnostics.FormatStatusLine();
        if (_directX12PreviewMaxRenderFramesPerSecond <= 0d)
        {
            return status;
        }

        return $"{status}; preview cap {FormatPreviewRenderLimit()}";
    }

    private void QueuePreviewFrameProcessing()
    {
        if (Interlocked.Exchange(ref _uiFramePending, 1) != 0)
        {
            return;
        }

        Dispatcher.InvokeAsync(ProcessPendingPreviewFrame, DispatcherPriority.Background);
    }

    private void ProcessPendingPreviewFrame()
    {
        BitmapSource? frame;
        lock (_previewFramePumpLock)
        {
            frame = _pendingPreviewFrame;
            _pendingPreviewFrame = null;
        }

        try
        {
            if (frame is not null)
            {
                _lastPreviewFrameAcceptedAt = DateTime.UtcNow;
                TrackCameraDisplayFrame(_lastPreviewFrameAcceptedAt);
                _latestFrame = frame;
                SetPreviewState("Camera active", frame);
                ProcessFrame(frame);
                UpdateFaceCueGuideOverlay(frame);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _uiFramePending, 0);

            lock (_previewFramePumpLock)
            {
                frame = _pendingPreviewFrame;
            }

            if (frame is not null)
            {
                QueuePreviewFrameProcessing();
            }
        }
    }

    private void ResetPreviewFramePump()
    {
        lock (_previewFramePumpLock)
        {
            _pendingPreviewFrame = null;
            _previewFramesReplacedSinceWarning = 0;
            _previewReplacementWindowStartedAtUtc = DateTime.MinValue;
        }

        Interlocked.Exchange(ref _uiFramePending, 0);
        Interlocked.Exchange(ref _previewWarningPending, 0);
        ResetFaceFeatureDetectionFramePump();
    }

    private void ResetDirectX12AnalysisFramePump()
    {
        TextureNativeFrameLease? frame;
        lock (_directX12AnalysisFrameLock)
        {
            frame = _pendingDirectX12AnalysisFrame;
            _pendingDirectX12AnalysisFrame = null;
        }

        frame?.Dispose();
    }

    private void ResetFaceFeatureDetectionFramePump()
    {
        lock (_faceFeatureDetectionFrameLock)
        {
            _pendingFaceFeatureDetectionFrame = null;
            _pendingFaceFeatureDetectionCapturedAtUtc = DateTime.MinValue;
        }

        _lastFaceFeatureDetectionAt = DateTime.MinValue;
    }

    private void TrackPreviewFrameReplacement()
    {
        var now = DateTime.UtcNow;
        if (_previewReplacementWindowStartedAtUtc == DateTime.MinValue)
        {
            _previewReplacementWindowStartedAtUtc = now;
        }

        _previewFramesReplacedSinceWarning++;
        if (_previewFramesReplacedSinceWarning < 50)
        {
            return;
        }

        var elapsed = now - _previewReplacementWindowStartedAtUtc;
        _previewFramesReplacedSinceWarning = 0;
        _previewReplacementWindowStartedAtUtc = now;

        if (elapsed <= TimeSpan.FromSeconds(2))
        {
            QueuePreviewPumpWarning($"Camera preview kept the latest frame and skipped 50 stale frames in {elapsed.TotalSeconds:0.0}s.");
        }
    }

    private void QueuePreviewPumpWarning(string warning)
    {
        if (Interlocked.Exchange(ref _previewWarningPending, 1) != 0)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref _previewWarningPending, 0);
            SetStatus(warning);
        }, DispatcherPriority.Background);
    }

    private void PreviewStatusChanged(object? sender, string status)
    {
        Dispatcher.InvokeAsync(() => SetStatus(status));
    }

    private void SetPreviewState(string status, ImageSource? frame)
    {
        PreviewStateText.Text = status;
        if (_showLiveWireframePreview)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            DirectX12PreviewLayer.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            FaceCueGuideCanvas.Children.Clear();
            FaceCueGuideCanvas.Visibility = Visibility.Collapsed;
            LiveWireframeCanvas.Visibility = Visibility.Visible;
            DrawLiveWireframePreview();
            return;
        }

        LiveWireframeCanvas.Visibility = Visibility.Collapsed;
        FaceCueGuideCanvas.Visibility = Visibility.Visible;
        var directX12Enabled = IsDirectX12PreviewEnabled();
        if (frame is null)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            DirectX12PreviewLayer.Visibility = directX12Enabled
                && (_directX12PreviewHost is not null || _directX12NativeCamera is not null)
                ? Visibility.Visible
                : Visibility.Collapsed;
            PreviewPlaceholder.Visibility = DirectX12PreviewLayer.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdateFaceCueGuideOverlay(null);
            return;
        }

        if (directX12Enabled && _directX12NativeCamera is not null)
        {
            if (ShouldUseWpfTrackingPreview(frame))
            {
                DirectX12PreviewLayer.Visibility = Visibility.Collapsed;
                PreviewImage.Source = frame;
                PreviewImage.Visibility = Visibility.Visible;
            }
            else
            {
                DirectX12PreviewLayer.Visibility = Visibility.Visible;
                PreviewImage.Source = null;
                PreviewImage.Visibility = Visibility.Collapsed;
            }

            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            UpdateFaceCueGuideOverlay(frame as BitmapSource);
            return;
        }

        if (directX12Enabled && TryEnsureDirectX12PreviewHost())
        {
            DirectX12PreviewLayer.Visibility = Visibility.Visible;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            UpdateFaceCueGuideOverlay(frame as BitmapSource);
            return;
        }

        DirectX12PreviewLayer.Visibility = Visibility.Collapsed;
        PreviewImage.Source = frame;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        UpdateFaceCueGuideOverlay(frame as BitmapSource);
    }

    private bool ShouldUseWpfTrackingPreview(ImageSource frame)
    {
        return frame is BitmapSource
            && _directX12NativeCamera is not null;
    }

    private void PreviewHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_showLiveWireframePreview)
        {
            DrawLiveWireframePreview();
            return;
        }

        UpdateFaceCueGuideOverlay(_latestFrame);
    }

    private void SleepEventWatchClicked(object sender, RoutedEventArgs e)
    {
        _sleepEventWatchActive = !_sleepEventWatchActive;
        UpdateSleepEventWatchButtonState();
        if (_sleepEventWatchActive)
        {
            Directory.CreateDirectory(_outputFolder);
            ResetEpisodeState();
            MonitorStatusText.Text = "Episode monitor watching for sustained low motion.";
            UpdateTrackingOverlay("Tracking armed", $"Motion -- | Threshold {GetMotionThreshold():0.0}%", "Waiting for a motion baseline.", "#37506a");
        }
        else
        {
            EndActiveEpisode(DateTime.Now, null, "Monitoring stopped");
            ResetEpisodeState();
            MonitorStatusText.Text = "Episode monitor idle.";
            UpdateTrackingOverlay("Tracking idle", "Motion -- | Threshold --", "Enable episode watch to arm tracking.", "#37506a");
        }
    }

    private void UpdateSleepEventWatchButtonState()
    {
        ApplyStartStopButtonState(
            SleepEventWatchButton,
            _sleepEventWatchActive,
            SleepEventWatchStartButtonText,
            SleepEventWatchStopButtonText,
            "Starts automatic event watching from low motion, eye cues, mouth cues, and trends.",
            "Stops automatic event watching and closes any active sleep event capture.");
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
                _faceLandmarkCueAnalyzer.Reset();
                _eyeCueStartedAt = null;
                _jawCueStartedAt = null;
                _currentFaceAnalysis = null;
                _currentFaceFeatureDetection = FaceFeatureDetection.None;
                ResetLandmarkTracking();
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
        _faceLandmarkCueAnalyzer.Reset();
        _eyeCueStartedAt = null;
        _jawCueStartedAt = null;
        _currentFaceAnalysis = null;
        _currentFaceFeatureDetection = FaceFeatureDetection.None;
        ResetLandmarkTracking();
        _activeFaceCueLayout = null;
        _lastFaceFeatureLockAt = DateTime.MinValue;
        MonitorStatusText.Text = "Face tracking reset. Use Calibrate Alert Baseline when you are alert and symptom-free.";
        UpdateCalibrationGuard();
    }

    private void PersonalModelSubjectChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (PersonalModelSubjectCheckBox.IsChecked == true)
        {
            _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
                false,
                PersonalFaceModelRejectionKind.NoFace,
                "subject confirmed; waiting for high-confidence face measurements",
                0d,
                _personalFaceModelBuilder.CurrentModel);
            SetStatus("Chris confirmed for Avatar System learning.");
        }
        else
        {
            _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
                false,
                PersonalFaceModelRejectionKind.SubjectNotConfirmed,
                "subject not confirmed; personal model collection paused",
                0d,
                _personalFaceModelBuilder.CurrentModel);
            SetStatus("Avatar System learning paused until Chris is confirmed.");
        }

        UpdateAvatarLearningStatusUi();
    }

    private void AvatarLearningToggleClicked(object sender, RoutedEventArgs e)
    {
        _avatarLearningRequested = !_avatarLearningRequested;
        UpdateAvatarLearningStatusUi();
        SetStatus(_avatarLearningRequested
            ? "Avatar System learning started. It can learn usual face range/motion during episodes, but alert-baseline calibration remains protected."
            : "Avatar System learning stopped.");
    }

    private void UpdateAvatarLearningStatusUi()
    {
        if (!IsLoaded)
        {
            return;
        }

        var state = GetAvatarLearningState();
        ApplyStartStopButtonState(
            AvatarLearningToggleButton,
            _avatarLearningRequested,
            AvatarLearningStartButtonText,
            AvatarLearningStopButtonText,
            "Starts measurement-only learning for the Avatar System. Event recording is separate.",
            "Stops measurement-only learning for the Avatar System. Event recording is separate.");
        AvatarLearningStateText.Text = state.Title;
        AvatarLearningStatusText.Text = state.Detail;
        AvatarLearningIndicator.Background = new SolidColorBrush(state.Accent);

        var trackingSanity = GetAvatarTrackingSanityState();
        AvatarTrackingSanityText.Text = trackingSanity.Detail;
        AvatarTrackingSanityText.Foreground = new SolidColorBrush(trackingSanity.Accent);
        UpdateAvatarEasyModeUi();
    }

    private static void ApplyStartStopButtonState(
        Button button,
        bool isActive,
        string startText,
        string stopText,
        string startToolTip,
        string stopToolTip)
    {
        button.Content = isActive ? stopText : startText;
        button.Background = isActive ? StopActionButtonBackground : StartActionButtonBackground;
        button.BorderBrush = isActive ? StopActionButtonBorder : StartActionButtonBorder;
        button.Foreground = Brushes.White;
        button.ToolTip = isActive ? stopToolTip : startToolTip;
    }

    private AvatarLearningState GetAvatarLearningState()
    {
        var subjectConfirmed = PersonalModelSubjectCheckBox.IsChecked == true;
        if (!subjectConfirmed)
        {
            return new AvatarLearningState(
                false,
                "Avatar learning stopped",
                "Not learning: check 'This is Chris' only when you are in front of the camera.",
                Color.FromRgb(89, 97, 107));
        }

        if (!_avatarLearningRequested)
        {
            return new AvatarLearningState(
                false,
                "Avatar learning stopped",
                "Not learning: click Start Avatar Learning when Chris is present and you want measurement-only face learning.",
                Color.FromRgb(89, 97, 107));
        }

        if (_avatarHistoricalDataSuspect)
        {
            return new AvatarLearningState(
                false,
                "Avatar data needs rebuild",
                $"{_avatarHistoricalDataAuditSummary} Click Rebuild Avatar Data before collecting more measurements.",
                Color.FromRgb(215, 165, 58));
        }

        if (_avatarTrackingAuditHold)
        {
            return new AvatarLearningState(
                false,
                "Avatar learning paused for tracking review",
                $"{_avatarTrackingAuditHoldSummary} Review the overlay and Face Preview, then Rebuild Avatar Data if the face features were sliding instead of rotating with the head.",
                Color.FromRgb(215, 165, 58));
        }

        if (_avatarRecentMeshStabilityHold)
        {
            return new AvatarLearningState(
                false,
                "Avatar learning paused for head-turn review",
                $"{_avatarRecentMeshStabilitySummary} Review the Head-lock wireframe while slowly looking left and right.",
                Color.FromRgb(215, 165, 58));
        }

        if (!_isCameraEnabled || _latestFrame is null)
        {
            return new AvatarLearningState(
                false,
                "Avatar learning waiting",
                "Not learning yet: turn the camera on and wait for the face tracker to lock.",
                Color.FromRgb(215, 165, 58));
        }

        if (!_currentFaceLandmarkFrame.HasFace || !_currentFaceLandmarkMetrics.HasFace)
        {
            return new AvatarLearningState(
                false,
                "Avatar learning waiting",
                "Not learning yet: keep your full face visible until the eye and mouth overlay locks on.",
                Color.FromRgb(215, 165, 58));
        }

        if (_currentPersonalFaceModelUpdate.Accepted && _currentPersonalFaceCaptureQuality.CanCollectMeasurements)
        {
            var eventNote = _activeEpisodeStartedAt is not null
                ? " Event is active, so this is face range/motion data, not the alert baseline."
                : "";
            var title = _activeEpisodeStartedAt is not null
                || _currentPersonalFaceModelUpdate.Reason.Contains("event-like", StringComparison.OrdinalIgnoreCase)
                    ? "Learning face range/motion"
                    : "Learning face measurements";
            return new AvatarLearningState(
                true,
                title,
                $"Learning: {_currentPersonalFaceModelUpdate.Reason}; accepted {_currentPersonalFaceModelUpdate.Model.AcceptedSamples} samples; quality {_currentPersonalFaceCaptureQuality.ScorePercent:0}%.{eventNote}",
                Color.FromRgb(74, 163, 107));
        }

        var correction = _currentPersonalFaceCaptureQuality.Suggestions.FirstOrDefault()
            ?? _currentPersonalFaceModelUpdate.Reason
            ?? _currentPersonalFaceCaptureQuality.PrimaryReason;
        if (string.IsNullOrWhiteSpace(correction))
        {
            correction = "Improve face lock, eye visibility, mouth visibility, lighting, or camera mode.";
        }

        return new AvatarLearningState(
            false,
            "Avatar learning waiting",
            $"Not learning: {_currentPersonalFaceCaptureQuality.PrimaryReason}. Fix: {correction}",
            Color.FromRgb(215, 165, 58));
    }

    private AvatarTrackingSanityState GetAvatarTrackingSanityState()
    {
        var readiness = _currentPersonalFaceCorpusReadiness;
        var model = _currentPersonalFaceModelUpdate.Model;
        if (_avatarRecentMeshStabilityHold)
        {
            return new AvatarTrackingSanityState(
                $"Head-turn sanity hold: {_avatarRecentMeshStabilitySummary}",
                Color.FromRgb(255, 210, 122));
        }

        if (_lastGoodFeatureMeshStability.HeadLockedSampleCount >= LastGoodFeatureMeshStabilityMinimumSamplesToHold
            && _lastGoodFeatureMeshStability.YawHealthPercent is > 0d and < 82d)
        {
            return new AvatarTrackingSanityState(
                $"Recent B head-turn lock warming: {_lastGoodFeatureMeshStability.YawStatus}; B range {_lastGoodFeatureMeshStability.YawRangeDegrees:0.#} deg; worst drift {_lastGoodFeatureMeshStability.YawWorstFeatureDriftPercent:0.#}%.",
                Color.FromRgb(255, 210, 122));
        }

        if (readiness.AcceptedBaselineSamples <= 0 && model.AcceptedSamples <= 0)
        {
            return new AvatarTrackingSanityState(
                "Tracking sanity: waiting for enough accepted measurements to audit head pose and feature anchoring.",
                Color.FromRgb(185, 215, 239));
        }

        if (readiness.AcceptedBaselineSamples < PersonalFaceDataAuditMinimumSamplesToBlock)
        {
            return new AvatarTrackingSanityState(
                $"Tracking sanity: warming ({readiness.AcceptedBaselineSamples}/{PersonalFaceDataAuditMinimumSamplesToBlock} audit samples). Keep collecting varied face positions.",
                Color.FromRgb(185, 215, 239));
        }

        if (readiness.DataAuditHealthPercent <= 0d)
        {
            return new AvatarTrackingSanityState(
                "Tracking sanity: waiting for the data audit to calculate pose and feature anchoring health.",
                Color.FromRgb(185, 215, 239));
        }

        var auditGate = PersonalFaceLearningAuditGate.Evaluate(readiness);
        if (auditGate.HoldLearning)
        {
            return new AvatarTrackingSanityState(
                $"Tracking sanity hold: {auditGate.Reason}",
                Color.FromRgb(255, 210, 122));
        }

        var firstFinding = readiness.DataAuditFindings.FirstOrDefault();
        if (readiness.PoseEstimationHealthPercent is > 0d and < 60d)
        {
            return new AvatarTrackingSanityState(
                $"Tracking sanity needs review: pose {readiness.PoseEstimationHealthPercent:0.#}%. {firstFinding ?? "Turned-head data may not be separating head motion from feature motion."}",
                Color.FromRgb(255, 210, 122));
        }

        if (readiness.FeatureAnchoringHealthPercent is > 0d and < 60d)
        {
            return new AvatarTrackingSanityState(
                $"Tracking sanity needs review: feature anchoring {readiness.FeatureAnchoringHealthPercent:0.#}%. {firstFinding ?? "Eyes or mouth may be sliding relative to the head."}",
                Color.FromRgb(255, 210, 122));
        }

        if (readiness.PoseBucketConsistency.ComparedPoseBucketCount > 0
            && readiness.PoseBucketConsistencyHealthPercent is > 0d and < 70d)
        {
            return new AvatarTrackingSanityState(
                $"Tracking sanity needs review: pose consistency {readiness.PoseBucketConsistencyHealthPercent:0.#}%. {firstFinding ?? "Turned-head buckets should not rewrite eye, mouth, or face proportions."}",
                Color.FromRgb(255, 210, 122));
        }

        if (readiness.ApertureConsistencyHealthPercent is > 0d and < 70d)
        {
            return new AvatarTrackingSanityState(
                $"Tracking sanity needs review: aperture consistency {readiness.ApertureConsistencyHealthPercent:0.#}%. {firstFinding ?? "Eye, mouth, or jaw opening should agree with dense blink and mouth evidence."}",
                Color.FromRgb(255, 210, 122));
        }

        if (readiness.DataAuditHealthPercent is > 0d and < 75d)
        {
            return new AvatarTrackingSanityState(
                $"Tracking sanity warming: data audit {readiness.DataAuditHealthPercent:0.#}%. {firstFinding ?? "Keep collecting varied pose, distance, and expression data."}",
                Color.FromRgb(255, 210, 122));
        }

        return new AvatarTrackingSanityState(
            $"Tracking sanity healthy: pose {readiness.PoseEstimationHealthPercent:0.#}%, anchoring {readiness.FeatureAnchoringHealthPercent:0.#}%, pose consistency {readiness.PoseBucketConsistencyHealthPercent:0.#}%, aperture {readiness.ApertureConsistencyHealthPercent:0.#}%, jaw scale {readiness.JawDroopScaleHealthPercent:0.#}%.",
            Color.FromRgb(128, 224, 164));
    }

    private void AvatarEasyModeClicked(object sender, RoutedEventArgs e)
    {
        var state = GetAvatarEasyModeState();
        if (!state.CanStartLearning)
        {
            if (_avatarHistoricalDataSuspect || _avatarTrackingAuditHold)
            {
                OpenAvatarSystemClicked(sender, e);
            }
            else
            {
                SetStatus($"{state.Title}: {state.Detail}");
                MonitorStatusText.Text = state.Detail;
            }

            UpdateAvatarEasyModeUi();
            return;
        }

        _avatarLearningRequested = true;
        try
        {
            var folder = GetPersonalFaceModelFolder();
            Directory.CreateDirectory(folder);
            _measurementAvatarCapturePlanPath = Path.Combine(folder, MeasurementAvatarCapturePlanStore.HtmlFileName);
            _measurementAvatarSystemDashboardPath = GetMeasurementAvatarSystemDashboardHtmlPath(folder);
            QueuePersonalFaceReportSave(CreatePersonalFaceReportSnapshot(folder, _currentPersonalFaceModelUpdate.Model));
        }
        catch (Exception ex)
        {
            SetStatus($"Easy Avatar Mode report refresh paused: {ex.Message}");
        }

        UpdateAvatarLearningStatusUi();
        state = GetAvatarEasyModeState();
        var status = $"{state.Title}: {state.Detail}";
        SetStatus(status);
        MonitorStatusText.Text = status;
    }

    private void UpdateAvatarEasyModeUi()
    {
        if (!IsLoaded)
        {
            return;
        }

        var state = GetAvatarEasyModeState();
        AvatarEasyModeTitleText.Text = state.Title;
        AvatarEasyModeDetailText.Text = state.Detail;
        AvatarEasyModeTitleText.Foreground = new SolidColorBrush(ColorForEasyModeSeverity(state.Severity));
        AvatarEasyModeButton.Content = state.ActionText;
        AvatarEasyModeButton.ToolTip = state.Detail;
        AvatarEasyModeButton.Background = state.CanStartLearning ? StartActionButtonBackground : CreateFrozenBrush(0x20, 0x32, 0x43);
        AvatarEasyModeButton.BorderBrush = state.CanStartLearning ? StartActionButtonBorder : CreateFrozenBrush(0x4a, 0x63, 0x7a);
        AvatarEasyModeButton.Foreground = Brushes.White;
    }

    private MeasurementAvatarEasyModeState GetAvatarEasyModeState()
    {
        var state = MeasurementAvatarEasyModeAdvisor.Create(new MeasurementAvatarEasyModeInput
        {
            SubjectConfirmed = PersonalModelSubjectCheckBox.IsChecked == true,
            AvatarLearningRequested = _avatarLearningRequested,
            CameraActive = _isCameraEnabled && _latestFrame is not null,
            FaceLocked = _currentFaceLandmarkFrame.HasFace && _currentFaceLandmarkMetrics.HasFace,
            HistoricalDataSuspect = _avatarHistoricalDataSuspect,
            HistoricalDataAuditSummary = _avatarHistoricalDataAuditSummary,
            TrackingAuditHold = _avatarTrackingAuditHold || _avatarRecentMeshStabilityHold,
            TrackingAuditHoldSummary = _avatarRecentMeshStabilityHold
                ? _avatarRecentMeshStabilitySummary
                : _avatarTrackingAuditHoldSummary,
            CaptureQuality = _currentPersonalFaceCaptureQuality,
            CapturePlan = _currentMeasurementAvatarCapturePlan,
            CapturePlanHtmlPath = _measurementAvatarCapturePlanPath
        });
        state.CapturePlanHtmlPath = _measurementAvatarCapturePlanPath;
        return state;
    }

    private static Color ColorForEasyModeSeverity(string severity)
    {
        return severity switch
        {
            MeasurementAvatarEasyModeSeverity.Good => Color.FromRgb(128, 224, 164),
            MeasurementAvatarEasyModeSeverity.Warning => Color.FromRgb(255, 210, 122),
            MeasurementAvatarEasyModeSeverity.Blocked => Color.FromRgb(255, 154, 154),
            _ => Color.FromRgb(185, 215, 239)
        };
    }

    private void CalibrateAlertBaselineClicked(object sender, RoutedEventArgs e)
    {
        if (!IsCalibrationAllowed())
        {
            MonitorStatusText.Text = "Alert baseline calibration is blocked until you have been symptom-free for one hour.";
            UpdateCalibrationGuard();
            return;
        }

        if (_latestFrame is null)
        {
            MonitorStatusText.Text = "Turn the camera on before calibrating the alert baseline.";
            CalibrationGuardText.Text = "Alert baseline needs a live camera view. Turn the camera on, sit alert, then calibrate.";
            return;
        }

        _faceCueAnalyzer.Reset();
        _faceLandmarkCueAnalyzer.Reset();
        _eyeCueStartedAt = null;
        _jawCueStartedAt = null;
        _currentFaceAnalysis = null;
        _currentFaceFeatureDetection = FaceFeatureDetection.None;
        _alertBaselineSavedAtUtc = null;
        _alertBaselineCameraName = "";
        _alertBaselineModeLabel = "";
        _alertBaselineCalibrationActive = true;
        ResetLandmarkTracking();
        _activeFaceCueLayout = null;
        _lastFaceFeatureLockAt = DateTime.MinValue;
        CalibrateAlertBaselineButton.Content = AlertBaselineInProgressButtonText;
        CalibrateAlertBaselineButton.IsEnabled = false;
        CalibrationGuardText.Text = "Calibrating alert baseline: stay awake, alert, symptom-free, and naturally relaxed.";
        MonitorStatusText.Text = "Calibrating alert baseline. Keep eyes naturally open and mouth relaxed.";
    }

    private void OpenAvatarSystemClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = GetPersonalFaceModelFolder();
            Directory.CreateDirectory(folder);
            var snapshot = CreatePersonalFaceReportSnapshot(folder, _currentPersonalFaceModelUpdate.Model);
            QueuePersonalFaceReportSave(snapshot);
            _personalFaceModelPath = Path.Combine(folder, _personalFaceModelStore.FileName);
            _measurementFacePreviewPath = Path.Combine(folder, MeasurementFacePreviewStore.HtmlFileName);
            _personalFaceMotionModelPath = Path.Combine(folder, _personalFaceMotionModelStore.FileName);
            _measurementAvatarTrainingPackagePath = Path.Combine(folder, MeasurementAvatarTrainingPackageStore.HtmlFileName);
            _measurementAvatarCapturePlanPath = Path.Combine(folder, MeasurementAvatarCapturePlanStore.HtmlFileName);
            _lastGoodFeatureMeshHtmlPath = LastGoodFeatureMeshStore.GetHtmlPath(folder);
            _measurementAvatarSystemDashboardPath = GetMeasurementAvatarSystemDashboardHtmlPath(folder);
            EnsureAvatarSystemPlaceholder(_measurementAvatarSystemDashboardPath);
            OpenLocalFile(_measurementAvatarSystemDashboardPath);
            var status = _currentPersonalFaceModelUpdate.Model.AcceptedSamples > 0
                ? $"Opened live Avatar System: {_measurementAvatarSystemDashboardPath}"
                : "Opened live waiting Avatar System. Confirm Chris and start avatar learning to collect measurements.";
            MonitorStatusText.Text = status;
            SetStatus(status);
        }
        catch (Exception ex)
        {
            var status = $"Could not open Avatar System: {ex.Message}";
            MonitorStatusText.Text = status;
            SetStatus(status);
        }
    }

    private void OpenLastGoodFeaturesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = GetPersonalFaceModelFolder();
            Directory.CreateDirectory(folder);
            var files = _lastGoodFeatureMeshStore.Write(
                folder,
                new LastGoodFeatureMeshReport
                {
                    SubjectId = PersonalFaceSubjectId,
                    SubjectDisplayName = PersonalFaceSubjectDisplayName,
                    Samples = _lastGoodFeatureMeshSamples.ToList()
                });
            _lastGoodFeatureMeshJsonPath = files.JsonPath;
            _lastGoodFeatureMeshHtmlPath = files.HtmlPath;
            OpenLocalFile(files.HtmlPath);
            var status = _lastGoodFeatureMeshSamples.Count > 0
                ? $"Opened Last 10 Good Features: {files.HtmlPath}"
                : "Opened Last 10 Good Features. Confirm Chris and let the dense tracker get a good eye/mouth lock to populate it.";
            SetStatus(status);
            MonitorStatusText.Text = status;
        }
        catch (Exception ex)
        {
            var status = $"Could not open Last 10 Good Features: {ex.Message}";
            SetStatus(status);
            MonitorStatusText.Text = status;
        }
    }

    private void RebuildAvatarDataClicked(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Archive the current Avatar measurements and start a fresh model? The old files will be moved to an archive folder, not deleted.",
            "Rebuild Avatar Data?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (_personalFaceReportWriterTask is { IsCompleted: false })
        {
            SetStatus("Avatar report save is still finishing. Try Rebuild Avatar Data again in a moment.");
            return;
        }

        try
        {
            var folder = GetPersonalFaceModelFolder();
            string archivePath = "";
            if (Directory.Exists(folder))
            {
                var archiveRoot = Path.Combine(_outputFolder, PersonalFaceArchiveFolderName);
                Directory.CreateDirectory(archiveRoot);
                archivePath = CreateUniqueArchivePath(archiveRoot, DateTime.UtcNow);
                Directory.Move(folder, archivePath);
            }

            Directory.CreateDirectory(folder);
            _personalFaceModelBuilder.Reset();
            _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
                false,
                PersonalFaceModelRejectionKind.LearningStopped,
                "avatar data rebuilt; learning resumes when Start Avatar Learning is active",
                0d,
                _personalFaceModelBuilder.CurrentModel);
            _currentPersonalFaceCaptureQuality = PersonalFaceCaptureQualityAssessment.Waiting;
            _currentPersonalFaceMotionModel = new PersonalFaceMotionModel();
            _currentPersonalFaceCorpusReadiness = new PersonalFaceCorpusReadiness();
            _currentPersonalFaceCollectionAudit = new PersonalFaceCollectionAudit();
            _personalFaceCollectionAuditObservations.Clear();
            _personalFaceMeasurementJournalSizeBytes = 0L;
            _personalFaceModelPath = "";
            _personalFaceMotionModelPath = "";
            _personalFaceCorpusReadinessPath = "";
            _personalFaceCollectionAuditPath = "";
            _measurementFacePreviewPath = "";
            _measurementAvatarTrainingPackagePath = "";
            _measurementAvatarCapturePlanPath = "";
            _measurementAvatarSystemDashboardPath = "";
            _currentMeasurementAvatarCapturePlan = null;
            _lastGoodFeatureMeshJsonPath = "";
            _lastGoodFeatureMeshHtmlPath = "";
            _lastGoodFeatureMeshSamples.Clear();
            RefreshLastGoodFeatureMeshStabilityAudit();
            _lastGoodFeatureMeshStatus = "last good feature mesh waiting";
            _avatarHistoricalDataSuspect = false;
            _avatarHistoricalDataAuditSummary = "";
            _avatarRecentMeshStabilityHold = false;
            _avatarRecentMeshStabilitySummary = "";
            _avatarTrackingAuditHold = false;
            _avatarTrackingAuditHoldSummary = "";
            _avatarLearningRequested = false;
            UpdateAvatarLearningStatusUi();
            SetStatus(string.IsNullOrWhiteSpace(archivePath)
                ? "Avatar data reset. Start Avatar Learning to collect fresh measurements."
                : $"Avatar data archived to {archivePath}. Start Avatar Learning to collect fresh measurements.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not rebuild Avatar data: {ex.Message}");
        }
    }

    private void SymptomCaptureClicked(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        if (_symptomCaptureActive && _activeEpisodeStartedAt is not null)
        {
            EndActiveEpisode(now, _latestFrame, "Symptom capture stopped");
            ResetEpisodeState();
            MonitorStatusText.Text = "Symptom capture stopped.";
            return;
        }

        MarkSymptomActivity(now, "Symptoms marked; alert-baseline calibration delayed for one symptom-free hour.");

        if (_activeEpisodeStartedAt is not null)
        {
            const string symptomConfirmation = "User marked symptoms during active event capture";
            if (!_activeTriggerReasons.Any(existing => string.Equals(existing, symptomConfirmation, StringComparison.OrdinalIgnoreCase)))
            {
                _activeTriggerReasons.Add(symptomConfirmation);
            }

            MonitorStatusText.Text = "Symptoms marked during active capture. Alert baseline is held and event evidence is recording.";
            return;
        }

        if (_latestFrame is null)
        {
            MonitorStatusText.Text = "Symptoms marked. Turn the camera on to record event video.";
            return;
        }

        Directory.CreateDirectory(_outputFolder);
        _symptomCaptureActive = true;
        SymptomCaptureButton.Content = SymptomCaptureStopButtonText;
        var started = StartActiveEpisode(
            _latestFrame,
            now,
            GetMotionThreshold(),
            GetStillnessSeconds(),
            "Symptom capture",
            [
                "User started symptom capture before or during a possible episode",
                "Alert-baseline calibration held so this state is not learned as the alert reference"
            ]);
        if (!started)
        {
            _symptomCaptureActive = false;
            SymptomCaptureButton.Content = SymptomCaptureStartButtonText;
            return;
        }

        MonitorStatusText.Text = "Symptom capture started. Alert baseline is held and sleepy-state evidence is recording.";
    }

    private void ProcessFrame(BitmapSource bitmap)
    {
        if (!_sleepEventWatchActive && _activeEpisodeStartedAt is null)
        {
            _currentFaceAnalysis = AnalyzeFaceCues(bitmap);
            var idleMetrics = CreateOverlayMetrics(null);
            var idleTrigger = "Preview only. Face tracking overlay is active; start Sleep Event Watch to log events.";
            UpdateTrackingOverlay("Tracking idle", idleMetrics, idleTrigger, "#37506a");
            UpdateAlertBaselineCalibrationStatus();
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
            UpdateAlertBaselineCalibrationStatus();
            return;
        }

        var now = DateTime.Now;
        var motion = CalculateFrameMotionPercent(_previousSample, sample);
        _previousSample = sample;
        _currentFaceAnalysis = AnalyzeFaceCues(bitmap);
        if (_activeEpisodeStartedAt is not null)
        {
            UpdateLandmarkEventEvidence(motion);
        }

        ProcessEpisodeMotion(bitmap, now, motion);
        UpdateAlertBaselineCalibrationStatus();
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
                var holdState = _symptomCaptureActive ? "Symptom capture" : "Event recording";
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
        var scaled = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Gray8, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = Math.Max(1, (width * converted.Format.BitsPerPixel + 7) / 8);
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
        catch
        {
            MonitorStatusText.Text = "Face tracking is resyncing with the latest camera frame.";
            return null;
        }
    }

    private void QueueFaceFeatureDetection(BitmapSource bitmap, DateTime now)
    {
        if (_isClosing
            || !_faceLandmarkTracker.IsAvailable
            || FaceAutoFollowCheckBox.IsChecked != true
            || now - _lastFaceFeatureDetectionAt < FaceFeatureDetectionTargetInterval)
        {
            return;
        }

        _lastFaceFeatureDetectionAt = now;
        lock (_faceFeatureDetectionFrameLock)
        {
            _pendingFaceFeatureDetectionFrame = bitmap;
            _pendingFaceFeatureDetectionCapturedAtUtc = now;
        }

        if (Interlocked.Exchange(ref _faceFeatureDetectionPending, 1) == 0)
        {
            _ = Task.Run(ProcessPendingFaceFeatureDetectionFramesAsync);
        }
    }

    private async Task ProcessPendingFaceFeatureDetectionFramesAsync()
    {
        while (!_isClosing)
        {
            BitmapSource? bitmap;
            DateTime capturedAtUtc;
            lock (_faceFeatureDetectionFrameLock)
            {
                bitmap = _pendingFaceFeatureDetectionFrame;
                capturedAtUtc = _pendingFaceFeatureDetectionCapturedAtUtc;
                _pendingFaceFeatureDetectionFrame = null;
                _pendingFaceFeatureDetectionCapturedAtUtc = DateTime.MinValue;
            }

            if (bitmap is null)
            {
                break;
            }

            var result = FaceLandmarkTrackingResult.None;
            try
            {
                lock (_faceLandmarkTrackerLock)
                {
                    result = _isClosing
                        ? FaceLandmarkTrackingResult.None
                        : _faceLandmarkTracker.Detect(bitmap, capturedAtUtc == DateTime.MinValue ? DateTime.UtcNow : capturedAtUtc);
                }

                await ApplyFaceFeatureDetectionResultAsync(result);
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => SetStatus($"Landmark tracker paused: {ex.Message}"), DispatcherPriority.Background);
            }
        }

        Interlocked.Exchange(ref _faceFeatureDetectionPending, 0);
        lock (_faceFeatureDetectionFrameLock)
        {
            if (_pendingFaceFeatureDetectionFrame is not null
                && Interlocked.Exchange(ref _faceFeatureDetectionPending, 1) == 0)
            {
                _ = Task.Run(ProcessPendingFaceFeatureDetectionFramesAsync);
            }
        }
    }

    private Task ApplyFaceFeatureDetectionResultAsync(FaceLandmarkTrackingResult result)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            if (_isClosing)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var detection = result.FeatureDetection;
            if (detection.HasFace)
            {
                _currentFaceFeatureDetection = detection;
                _lastFaceFeatureLockAt = now;
                var rawLandmarkFrame = result.LandmarkFrame.HasFace
                    ? result.LandmarkFrame
                    : detection.ToLandmarkFrame(now);
                _currentFaceLandmarkFrame = _faceLandmarkReconstructor.Update(rawLandmarkFrame);
                _currentFaceLandmarkMetrics = _faceLandmarkMetricCalculator.Update(_currentFaceLandmarkFrame);
                _currentHeadPoseEstimate = _headPoseEstimator.Estimate(new HeadPoseEstimatorInput
                {
                    Frame = _currentFaceLandmarkFrame,
                    FrameWidthPixels = _latestFrame?.PixelWidth,
                    FrameHeightPixels = _latestFrame?.PixelHeight,
                    Calibration = GetCurrentHeadPoseCalibration()
                });
                _currentFaceLandmarkCueAnalysis = _faceLandmarkCueAnalyzer.Analyze(_currentFaceLandmarkMetrics);
                _currentFaceLandmarkTrendAnalysis = _faceLandmarkTrendAnalyzer.Update(_currentFaceLandmarkMetrics);
                _currentFaceLockStabilityAnalysis = _faceLockStabilityAnalyzer.Update(
                    _currentFaceFeatureDetection,
                    _currentFaceLandmarkFrame,
                    _currentFaceLandmarkMetrics);
                TrackFeatureOverlayFrame(now);
                TrackLastGoodFeatureMeshSample(now);
                UpdatePersonalFaceModel(now);
                UpdateAlertBaselineCalibrationStatus();
            }
            else if (!HasUsableFaceFeatureLock(now))
            {
                _currentFaceFeatureDetection = FaceFeatureDetection.None;
                ResetLandmarkTracking();
            }

            UpdateFaceCueGuideOverlay(_latestFrame);
        }, DispatcherPriority.Background).Task;
    }

    private void TrackLastGoodFeatureMeshSample(DateTime utcNow)
    {
        if (PersonalModelSubjectCheckBox.IsChecked != true)
        {
            _lastGoodFeatureMeshStatus = "Last good mesh waiting: confirm Chris";
            RefreshLastGoodFeatureMeshStabilityAudit();
            UpdateTrackingFidelityHealthText();
            return;
        }

        if ((utcNow - _lastGoodFeatureMeshCapturedAtUtc).TotalMilliseconds < 100d)
        {
            return;
        }

        if (!LastGoodFeatureMeshSampleFactory.TryCreate(
                _currentFaceLandmarkFrame,
                _currentFaceLandmarkMetrics,
                _currentFaceLockStabilityAnalysis,
                _currentPersonalFaceCaptureQuality,
                out var sample,
                out var reason,
                headPose: _currentHeadPoseEstimate))
        {
            _lastGoodFeatureMeshStatus = $"Last good mesh waiting: {reason}";
            RefreshLastGoodFeatureMeshStabilityAudit();
            UpdateTrackingFidelityHealthText();
            return;
        }

        if (_lastGoodFeatureMeshSamples.Count > 0
            && string.Equals(_lastGoodFeatureMeshSamples[^1].SampleId, sample.SampleId, StringComparison.Ordinal))
        {
            return;
        }

        _lastGoodFeatureMeshSamples.Add(sample);
        if (_lastGoodFeatureMeshSamples.Count > 10)
        {
            _lastGoodFeatureMeshSamples.RemoveRange(0, _lastGoodFeatureMeshSamples.Count - 10);
        }

        _lastGoodFeatureMeshCapturedAtUtc = utcNow;
        _lastGoodFeatureMeshStatus = $"Last good mesh captured: {_lastGoodFeatureMeshSamples.Count}/10";
        RefreshLastGoodFeatureMeshStabilityAudit();
        if (_showLiveWireframePreview)
        {
            DrawLiveWireframePreview();
        }

        UpdateTrackingFidelityHealthText();
    }

    private void RefreshLastGoodFeatureMeshStabilityAudit()
    {
        _lastGoodFeatureMeshStability = LastGoodFeatureMeshStabilityAnalyzer.Analyze(_lastGoodFeatureMeshSamples);
        _avatarRecentMeshStabilityHold =
            _lastGoodFeatureMeshStability.HeadLockedSampleCount >= LastGoodFeatureMeshStabilityMinimumSamplesToHold
            && _lastGoodFeatureMeshStability.YawRangeDegrees >= LastGoodFeatureMeshBRotationMinimumRangeDegrees
            && _lastGoodFeatureMeshStability.YawHealthPercent > 0d
            && _lastGoodFeatureMeshStability.YawHealthPercent < LastGoodFeatureMeshStabilityHoldThresholdPercent;
        _avatarRecentMeshStabilitySummary = _avatarRecentMeshStabilityHold
            ? FormatLastGoodFeatureMeshStabilitySummary(_lastGoodFeatureMeshStability)
            : "";
    }

    private static string FormatLastGoodFeatureMeshStabilitySummary(LastGoodFeatureMeshStabilityReport stability)
    {
        var finding = stability.YawFindings.FirstOrDefault() ?? stability.Findings.FirstOrDefault();
        var findingText = string.IsNullOrWhiteSpace(finding) ? "" : $" Finding: {finding}";
        return $"Recent B head-turn lock failed ({stability.YawHealthPercent:0.#}% health; B range {stability.YawRangeDegrees:0.#} deg; worst B-axis feature drift {stability.YawWorstFeatureDriftPercent:0.#}%; left/right samples {stability.YawLeftSampleCount}/{stability.YawRightSampleCount}).{findingText}";
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

    private void ResetLandmarkTracking()
    {
        _currentFaceLandmarkFrame = FaceLandmarkFrame.None;
        _currentFaceLandmarkMetrics = FaceLandmarkMetrics.None;
        _currentFaceLandmarkCueAnalysis = null;
        _currentFaceLandmarkTrendAnalysis = FaceLandmarkTrendAnalysis.Waiting;
        _currentFaceLockStabilityAnalysis = FaceLockStabilityAnalysis.Waiting;
        _currentHeadPoseEstimate = HeadPoseEstimate.None;
        _currentPersonalFaceCaptureQuality = PersonalFaceCaptureQualityAssessment.Waiting;
        _faceLandmarkTracker.Reset();
        _faceLandmarkReconstructor.Reset();
        _faceLandmarkMetricCalculator.Reset();
        _faceLandmarkTrendAnalyzer.Reset();
        _faceLockStabilityAnalyzer.Reset();
    }

    private void UpdatePersonalFaceModel(DateTime utcNow)
    {
        if (PersonalModelSubjectCheckBox.IsChecked != true)
        {
            _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
                false,
                PersonalFaceModelRejectionKind.SubjectNotConfirmed,
                "subject not confirmed; personal model collection paused",
                0d,
                _personalFaceModelBuilder.CurrentModel);
            UpdatePersonalFaceCaptureQuality();
            TrackPersonalFaceCollectionAudit(utcNow, subjectConfirmed: false, pausedForEventOrCalibration: false);
            UpdateAvatarLearningStatusUi();
            return;
        }

        if (!_avatarLearningRequested)
        {
            _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
                false,
                PersonalFaceModelRejectionKind.LearningStopped,
                "avatar learning stopped by user",
                0d,
                _personalFaceModelBuilder.CurrentModel);
            UpdatePersonalFaceCaptureQuality();
            TrackPersonalFaceCollectionAudit(utcNow, subjectConfirmed: true, pausedForEventOrCalibration: false);
            UpdateAvatarLearningStatusUi();
            return;
        }

        if (_avatarHistoricalDataSuspect)
        {
            _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
                false,
                PersonalFaceModelRejectionKind.LearningStopped,
                "saved avatar data failed audit; rebuild before learning",
                0d,
                _personalFaceModelBuilder.CurrentModel);
            UpdatePersonalFaceCaptureQuality();
            TrackPersonalFaceCollectionAudit(utcNow, subjectConfirmed: true, pausedForEventOrCalibration: false);
            UpdateAvatarLearningStatusUi();
            return;
        }

        if (_avatarTrackingAuditHold)
        {
            _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
                false,
                PersonalFaceModelRejectionKind.TrackingAuditHold,
                _avatarTrackingAuditHoldSummary,
                0d,
                _personalFaceModelBuilder.CurrentModel);
            UpdatePersonalFaceCaptureQuality();
            TrackPersonalFaceCollectionAudit(utcNow, subjectConfirmed: true, pausedForEventOrCalibration: false);
            UpdateAvatarLearningStatusUi();
            return;
        }

        if (_avatarRecentMeshStabilityHold)
        {
            _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
                false,
                PersonalFaceModelRejectionKind.TrackingAuditHold,
                _avatarRecentMeshStabilitySummary,
                0d,
                _personalFaceModelBuilder.CurrentModel);
            UpdatePersonalFaceCaptureQuality();
            TrackPersonalFaceCollectionAudit(utcNow, subjectConfirmed: true, pausedForEventOrCalibration: false);
            UpdateAvatarLearningStatusUi();
            return;
        }

        var preflightModelUpdate = new PersonalFaceModelUpdate(
            true,
            PersonalFaceModelRejectionKind.None,
            "capture-quality preflight",
            1d,
            _personalFaceModelBuilder.CurrentModel);
        var preflightCaptureQuality = AnalyzePersonalFaceCaptureQuality(preflightModelUpdate);
        if (!preflightCaptureQuality.CanCollectMeasurements)
        {
            _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
                false,
                CaptureQualityRejectionKind(preflightCaptureQuality),
                $"capture quality gate: {preflightCaptureQuality.PrimaryReason}",
                0d,
                _personalFaceModelBuilder.CurrentModel);
            _currentPersonalFaceCaptureQuality = preflightCaptureQuality;
            TrackPersonalFaceCollectionAudit(utcNow, subjectConfirmed: true, pausedForEventOrCalibration: false);
            UpdateAvatarLearningStatusUi();
            return;
        }

        _currentPersonalFaceModelUpdate = _personalFaceModelBuilder.Update(
            _currentFaceLandmarkFrame,
            _currentFaceLandmarkMetrics,
            _currentFaceLockStabilityAnalysis,
            _currentFaceLandmarkCueAnalysis,
            _currentFaceLandmarkTrendAnalysis,
            _currentHeadPoseEstimate,
            allowEventLikeMeasurements: true);
        _currentPersonalFaceCaptureQuality = AnalyzePersonalFaceCaptureQuality(_currentPersonalFaceModelUpdate);
        TrackPersonalFaceCollectionAudit(utcNow, subjectConfirmed: true, pausedForEventOrCalibration: false);
        SavePersonalMeasurementIfDue();
        SavePersonalFaceModelIfDue(utcNow);
        UpdateAvatarLearningStatusUi();
    }

    private void UpdatePersonalFaceCaptureQuality()
    {
        _currentPersonalFaceCaptureQuality = AnalyzePersonalFaceCaptureQuality(_currentPersonalFaceModelUpdate);
    }

    private PersonalFaceCaptureQualityAssessment AnalyzePersonalFaceCaptureQuality(PersonalFaceModelUpdate modelUpdate)
    {
        var mode = CameraModeComboBox.SelectedItem as CameraVideoMode;
        return _personalFaceCaptureQualityAnalyzer.Analyze(new PersonalFaceCaptureQualityInput
        {
            VideoWidth = mode?.Width,
            VideoHeight = mode?.Height,
            FramesPerSecond = mode?.FramesPerSecond,
            InputFormat = mode?.InputFormat,
            IsAutoCameraMode = mode?.IsAuto != false,
            LandmarkFrame = _currentFaceLandmarkFrame,
            Metrics = _currentFaceLandmarkMetrics,
            Stability = _currentFaceLockStabilityAnalysis,
            PersonalModelUpdate = modelUpdate,
            MeasurementJournalBytes = _personalFaceMeasurementJournalSizeBytes
        });
    }

    private static PersonalFaceModelRejectionKind CaptureQualityRejectionKind(PersonalFaceCaptureQualityAssessment captureQuality)
    {
        return captureQuality.Label.Equals("no-face", StringComparison.OrdinalIgnoreCase)
            ? PersonalFaceModelRejectionKind.NoFace
            : PersonalFaceModelRejectionKind.LowQuality;
    }

    private void SavePersonalMeasurementIfDue()
    {
        if (!_currentPersonalFaceModelUpdate.Accepted || !_currentPersonalFaceCaptureQuality.CanCollectMeasurements)
        {
            return;
        }

        try
        {
            var folder = GetPersonalFaceModelFolder();
            var path = _personalFaceMeasurementJournal.WriteAcceptedSampleIfDue(
                folder,
                _currentPersonalFaceModelUpdate,
                _currentFaceLandmarkFrame,
                _currentFaceLandmarkMetrics,
                _currentFaceLockStabilityAnalysis,
                _currentPersonalFaceCaptureQuality,
                _currentHeadPoseEstimate);
            if (!string.IsNullOrWhiteSpace(path))
            {
                _personalFaceMeasurementJournalPath = path;
                _personalFaceMeasurementJournalSizeBytes = PersonalFaceMeasurementJournal.GetMeasurementsSizeBytes(folder);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Personal measurement journal paused: {ex.Message}");
        }
    }

    private void SavePersonalFaceModelIfDue(DateTime utcNow)
    {
        if (_currentPersonalFaceModelUpdate.Model.AcceptedSamples <= 0
            || (utcNow - _lastPersonalFaceModelSavedAtUtc).TotalSeconds < PersonalFaceLiveReportSaveIntervalSeconds)
        {
            return;
        }

        try
        {
            var folder = GetPersonalFaceModelFolder();
            QueuePersonalFaceReportSave(CreatePersonalFaceReportSnapshot(folder, _currentPersonalFaceModelUpdate.Model));
            _lastPersonalFaceModelSavedAtUtc = utcNow;
        }
        catch (Exception ex)
        {
            SetStatus($"Personal face model save paused: {ex.Message}");
        }
    }

    private PersonalFaceReportSnapshot CreatePersonalFaceReportSnapshot(string folder, PersonalFaceModel model)
    {
        var modelSnapshot = CloneForBackgroundSave(model);
        var state = GetAvatarLearningState();
        var subjectConfirmed = PersonalModelSubjectCheckBox.IsChecked == true
            && string.Equals(modelSnapshot.SubjectId, PersonalFaceSubjectId, StringComparison.OrdinalIgnoreCase);

        return new PersonalFaceReportSnapshot(
            folder,
            modelSnapshot,
            _personalFaceCollectionAuditObservations.ToList(),
            CloneCaptureQuality(_currentPersonalFaceCaptureQuality),
            subjectConfirmed,
            _avatarLearningRequested,
            state.Active,
            state.Title,
            state.Detail,
            CloneForBackgroundSave(_currentHeadPoseEstimate),
            _lastGoodFeatureMeshStability,
            _lastGoodFeatureMeshSamples.ToList());
    }

    private static T CloneForBackgroundSave<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<T>(json) ?? value;
    }

    private static PersonalFaceCaptureQualityAssessment CloneCaptureQuality(PersonalFaceCaptureQualityAssessment value)
    {
        return new PersonalFaceCaptureQualityAssessment
        {
            Label = value.Label,
            ScorePercent = value.ScorePercent,
            CanCollectMeasurements = value.CanCollectMeasurements,
            StrongEnoughForAvatarLearning = value.StrongEnoughForAvatarLearning,
            PrimaryReason = value.PrimaryReason,
            StatusLine = value.StatusLine,
            CameraModeScorePercent = value.CameraModeScorePercent,
            FaceScaleScorePercent = value.FaceScaleScorePercent,
            EyeEvidenceScorePercent = value.EyeEvidenceScorePercent,
            MouthEvidenceScorePercent = value.MouthEvidenceScorePercent,
            StabilityScorePercent = value.StabilityScorePercent,
            GlassesRiskScorePercent = value.GlassesRiskScorePercent,
            StorageScorePercent = value.StorageScorePercent,
            FaceWidthPercent = value.FaceWidthPercent,
            FaceHeightPercent = value.FaceHeightPercent,
            Issues = value.Issues.ToList(),
            Suggestions = value.Suggestions.ToList()
        };
    }

    private void QueuePersonalFaceReportSave(PersonalFaceReportSnapshot snapshot)
    {
        lock (_personalFaceReportWriterLock)
        {
            _pendingPersonalFaceReportSnapshot = snapshot;
            if (_personalFaceReportWriterTask is { IsCompleted: false })
            {
                return;
            }

            _personalFaceReportWriterTask = Task.Run(ProcessPersonalFaceReportWriterQueue);
        }
    }

    private void ProcessPersonalFaceReportWriterQueue()
    {
        while (true)
        {
            PersonalFaceReportSnapshot? snapshot;
            lock (_personalFaceReportWriterLock)
            {
                snapshot = _pendingPersonalFaceReportSnapshot;
                _pendingPersonalFaceReportSnapshot = null;
                if (snapshot is null)
                {
                    _personalFaceReportWriterTask = null;
                    return;
                }
            }

            try
            {
                var result = WritePersonalFaceReports(snapshot);
                Dispatcher.BeginInvoke(() => ApplyPersonalFaceReportSaveResult(result), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_isClosing)
                    {
                        SetStatus($"Live Avatar System save paused: {ex.Message}");
                    }
                }, DispatcherPriority.Background);
            }
        }
    }

    private static PersonalFaceReportSaveResult WritePersonalFaceReports(PersonalFaceReportSnapshot snapshot)
    {
        Directory.CreateDirectory(snapshot.Folder);

        var personalFaceModelStore = new PersonalFaceModelStore();
        var personalFaceMotionModelStore = new PersonalFaceMotionModelStore();
        var personalFaceCorpusReadinessStore = new PersonalFaceCorpusReadinessStore();
        var personalFaceCollectionAuditStore = new PersonalFaceCollectionAuditStore();
        var measurementFacePreviewBuilder = new MeasurementFacePreviewBuilder();
        var measurementFacePreviewStore = new MeasurementFacePreviewStore();
        var measurementAvatarTrainingPackageBuilder = new MeasurementAvatarTrainingPackageBuilder();
        var measurementAvatarTrainingPackageStore = new MeasurementAvatarTrainingPackageStore();
        var measurementAvatarCapturePlanBuilder = new MeasurementAvatarCapturePlanBuilder();
        var measurementAvatarCapturePlanStore = new MeasurementAvatarCapturePlanStore();
        var measurementAvatarSystemDashboardStore = new MeasurementAvatarSystemDashboardStore();
        var lastGoodFeatureMeshStore = new LastGoodFeatureMeshStore();

        var model = snapshot.Model;
        var samples = PersonalFaceMeasurementJournal
            .ReadRecentSamples(snapshot.Folder, PersonalFaceMotionModelMaxSamples)
            .Where(sample => string.Equals(sample.SubjectId, model.SubjectId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var observations = samples
            .Select(PersonalFaceMotionObservation.FromMeasurementSample)
            .ToList();
        var motionModel = new PersonalFaceMotionModelBuilder().Build(observations);
        if (motionModel.ObservationCount == 0)
        {
            motionModel.SubjectId = model.SubjectId;
            motionModel.SubjectDisplayName = model.SubjectDisplayName;
            motionModel.SubjectCollectionMode = model.SubjectCollectionMode;
            motionModel.CreatedAtUtc = DateTime.UtcNow;
            motionModel.UpdatedAtUtc = DateTime.UtcNow;
        }

        var measurementBytes = PersonalFaceMeasurementJournal.GetMeasurementsSizeBytes(snapshot.Folder);
        var readiness = new PersonalFaceCorpusReadinessBuilder().Build(model, motionModel, samples, measurementBytes);
        var audit = new PersonalFaceCollectionAuditBuilder().Build(model, snapshot.CollectionAuditObservations);

        var personalFaceModelPath = personalFaceModelStore.Write(snapshot.Folder, model);
        var previewGate = BuildSubjectGateForSnapshot(
            model,
            snapshot.SubjectConfirmed,
            "manual subject confirmation is active",
            "subject not confirmed for preview output");
        var previewFiles = measurementFacePreviewStore.Write(
            snapshot.Folder,
            measurementFacePreviewBuilder.Build(model, previewGate));

        var motionModelPath = personalFaceMotionModelStore.Write(snapshot.Folder, motionModel);
        var readinessJsonPath = personalFaceCorpusReadinessStore.Write(snapshot.Folder, readiness);
        var auditJsonPath = personalFaceCollectionAuditStore.Write(snapshot.Folder, audit);

        var packageGate = BuildSubjectGateForSnapshot(
            model,
            snapshot.SubjectConfirmed,
            "manual subject confirmation is active for avatar package",
            "subject not confirmed for avatar package output");
        var package = measurementAvatarTrainingPackageBuilder.Build(
            model,
            motionModel,
            readiness,
            packageGate,
            measurementBytes,
            collectionAudit: audit);
        var packageFiles = measurementAvatarTrainingPackageStore.Write(snapshot.Folder, package);

        var planGate = BuildSubjectGateForSnapshot(
            model,
            snapshot.SubjectConfirmed,
            "manual subject confirmation is active for capture planning",
            "subject not confirmed for capture planning");
        var plan = measurementAvatarCapturePlanBuilder.Build(
            model,
            motionModel,
            readiness,
            planGate,
            measurementBytes);
        var planFiles = measurementAvatarCapturePlanStore.Write(snapshot.Folder, plan);
        var lastGoodFeatureMeshFiles = lastGoodFeatureMeshStore.Write(
            snapshot.Folder,
            new LastGoodFeatureMeshReport
            {
                SubjectId = model.SubjectId,
                SubjectDisplayName = model.SubjectDisplayName,
                Samples = snapshot.LastGoodFeatureMeshSamples
            });

        var dashboardGate = BuildSubjectGateForSnapshot(
            model,
            snapshot.SubjectConfirmed,
            "manual subject confirmation is active for avatar system",
            "subject not confirmed for avatar system");
        var dashboard = new MeasurementAvatarSystemDashboard
        {
            SubjectId = model.SubjectId,
            SubjectDisplayName = model.SubjectDisplayName,
            SubjectConfirmed = snapshot.SubjectConfirmed,
            AvatarLearningRequested = snapshot.AvatarLearningRequested,
            AvatarLearningActive = snapshot.AvatarLearningActive,
            AvatarLearningStatus = snapshot.AvatarLearningStatus,
            AvatarLearningCorrection = snapshot.AvatarLearningCorrection,
            FaceModel = model,
            MotionModel = motionModel,
            LearningDataReadiness = readiness,
            CollectionAudit = audit,
            AvatarPackage = measurementAvatarTrainingPackageBuilder.Build(
                model,
                motionModel,
                readiness,
                dashboardGate,
                measurementBytes,
                collectionAudit: audit),
            CapturePlan = measurementAvatarCapturePlanBuilder.Build(
                model,
                motionModel,
                readiness,
                dashboardGate,
                measurementBytes),
            CurrentCaptureQuality = snapshot.CaptureQuality,
            CurrentHeadPose = snapshot.HeadPose,
            LastGoodFeatureStability = snapshot.LastGoodFeatureMeshStability,
            FacePreviewHtmlPath = previewFiles.HtmlPath,
            LearningDataReportHtmlPath = PersonalFaceCorpusReadinessStore.GetHtmlPath(readinessJsonPath),
            CollectionAuditHtmlPath = PersonalFaceCollectionAuditStore.GetHtmlPath(auditJsonPath),
            AvatarPackageHtmlPath = packageFiles.HtmlPath,
            CapturePlanHtmlPath = planFiles.HtmlPath,
            LastGoodFeaturesHtmlPath = lastGoodFeatureMeshFiles.HtmlPath
        };

        var dashboardJsonPath = measurementAvatarSystemDashboardStore.Write(snapshot.Folder, dashboard);
        return new PersonalFaceReportSaveResult(
            personalFaceModelPath,
            motionModelPath,
            PersonalFaceCorpusReadinessStore.GetHtmlPath(readinessJsonPath),
            PersonalFaceCollectionAuditStore.GetHtmlPath(auditJsonPath),
            previewFiles.HtmlPath,
            packageFiles.HtmlPath,
            planFiles.HtmlPath,
            lastGoodFeatureMeshFiles.JsonPath,
            lastGoodFeatureMeshFiles.HtmlPath,
            MeasurementAvatarSystemDashboardStore.GetHtmlPath(dashboardJsonPath),
            measurementBytes,
            motionModel,
            readiness,
            audit,
            plan);
    }

    private static FaceReconstructionSubjectGate BuildSubjectGateForSnapshot(
        PersonalFaceModel model,
        bool subjectConfirmed,
        string acceptedReason,
        string pausedReason)
    {
        return FaceReconstructionSubjectGate.FromPersonalModel(
            model,
            subjectConfirmed,
            reason: subjectConfirmed ? acceptedReason : pausedReason);
    }

    private void ApplyPersonalFaceReportSaveResult(PersonalFaceReportSaveResult result)
    {
        if (_isClosing)
        {
            return;
        }

        _personalFaceModelPath = result.PersonalFaceModelPath;
        _personalFaceMotionModelPath = result.PersonalFaceMotionModelPath;
        _personalFaceCorpusReadinessPath = result.PersonalFaceCorpusReadinessPath;
        _personalFaceCollectionAuditPath = result.PersonalFaceCollectionAuditPath;
        _measurementFacePreviewPath = result.MeasurementFacePreviewPath;
        _measurementAvatarTrainingPackagePath = result.MeasurementAvatarTrainingPackagePath;
        _measurementAvatarCapturePlanPath = result.MeasurementAvatarCapturePlanPath;
        _lastGoodFeatureMeshJsonPath = result.LastGoodFeatureMeshJsonPath;
        _lastGoodFeatureMeshHtmlPath = result.LastGoodFeatureMeshHtmlPath;
        _measurementAvatarSystemDashboardPath = result.MeasurementAvatarSystemDashboardPath;
        _personalFaceMeasurementJournalSizeBytes = result.MeasurementJournalBytes;
        _currentPersonalFaceMotionModel = result.MotionModel;
        _currentPersonalFaceCorpusReadiness = result.CorpusReadiness;
        ApplyAvatarHistoricalDataAudit(result.CorpusReadiness);
        _currentPersonalFaceCollectionAudit = result.CollectionAudit;
        _currentMeasurementAvatarCapturePlan = result.CapturePlan;
        UpdateAvatarLearningStatusUi();
    }

    private void TrackPersonalFaceCollectionAudit(
        DateTime utcNow,
        bool subjectConfirmed,
        bool pausedForEventOrCalibration)
    {
        var reviewedAtUtc = _currentFaceLandmarkFrame.CapturedAtUtc != default
            ? _currentFaceLandmarkFrame.CapturedAtUtc
            : utcNow;
        var hasFace = _currentFaceLandmarkFrame.HasFace || _currentFaceLandmarkMetrics.HasFace || _currentFaceFeatureDetection.HasFace;
        _personalFaceCollectionAuditObservations.Add(PersonalFaceCollectionAuditObservation.Create(
            reviewedAtUtc,
            subjectConfirmed,
            pausedForEventOrCalibration,
            hasFace,
            _currentPersonalFaceModelUpdate,
            _currentPersonalFaceCaptureQuality));
        if (_personalFaceCollectionAuditObservations.Count > PersonalFaceCollectionAuditMaxObservations)
        {
            _personalFaceCollectionAuditObservations.RemoveRange(
                0,
                _personalFaceCollectionAuditObservations.Count - PersonalFaceCollectionAuditMaxObservations);
        }

        _currentPersonalFaceCollectionAudit = _personalFaceCollectionAuditBuilder.Build(
            _currentPersonalFaceModelUpdate.Model,
            _personalFaceCollectionAuditObservations);
    }

    private static void OpenLocalFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Preview file was not created.", path);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static string GetMeasurementAvatarSystemDashboardHtmlPath(string folder)
    {
        var dashboardJsonPath = Path.Combine(folder, MeasurementAvatarSystemDashboardStore.DefaultJsonFileName);
        return MeasurementAvatarSystemDashboardStore.GetHtmlPath(dashboardJsonPath);
    }

    private static void EnsureAvatarSystemPlaceholder(string path)
    {
        if (File.Exists(path))
        {
            return;
        }

        var html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="refresh" content="2">
<title>Avatar System</title>
<style>
:root{color-scheme:dark}body{margin:0;background:#080d12;color:#f5f8fb;font-family:Segoe UI,Arial,sans-serif}main{max-width:860px;margin:0 auto;padding:28px}section{border:1px solid #243545;background:#101820;padding:18px}.muted{color:#b9d7ef}
</style>
</head>
<body>
<main>
<section>
<h1>Avatar System</h1>
<p class="muted">Preparing the live report. This page refreshes automatically while the background writer saves the latest measurement snapshot.</p>
</section>
</main>
</body>
</html>
""";
        AtomicTextFileWriter.WriteAllText(path, html, Encoding.UTF8);
    }

    private List<string> ProcessFaceCues(DateTime now)
    {
        var reasons = new List<string>();
        var regionAnalysis = _currentFaceAnalysis is { BaselineReady: true } readyRegionAnalysis
            ? readyRegionAnalysis
            : null;
        var landmarkAnalysis = _currentFaceLandmarkCueAnalysis is { BaselineReady: true } readyLandmarkAnalysis
            ? readyLandmarkAnalysis
            : null;
        var trendAnalysis = _currentFaceLandmarkTrendAnalysis.HasUsableTrend
            ? _currentFaceLandmarkTrendAnalysis
            : null;

        if (regionAnalysis is null && landmarkAnalysis is null && trendAnalysis is null)
        {
            _eyeCueStartedAt = null;
            _jawCueStartedAt = null;
            _eyeTrendCueStartedAt = null;
            return reasons;
        }

        var cueSeconds = GetFaceCueSeconds();
        var eyeDropPercent = MaxSignal(regionAnalysis?.EyeDropPercent, landmarkAnalysis?.EyeClosurePercent);
        var jawChangePercent = MaxSignal(
            regionAnalysis?.JawChangePercent,
            landmarkAnalysis?.MouthOpeningChangePercent,
            landmarkAnalysis?.JawDroopChangePercent);
        var qualityPercent = MaxSignal(regionAnalysis?.QualityPercent, landmarkAnalysis?.QualityPercent);
        var compositeCuePercent = MaxSignal(regionAnalysis?.CompositeCuePercent, landmarkAnalysis?.CompositeCuePercent);
        var trendCuePercent = trendAnalysis?.TrendCuePercent ?? 0d;
        var eyeCueActive = false;
        if (eyeDropPercent >= GetEyeCueThreshold())
        {
            _eyeCueStartedAt ??= now;
            if ((now - _eyeCueStartedAt.Value).TotalSeconds >= cueSeconds)
            {
                eyeCueActive = true;
                reasons.Add($"Primary eye cue persisted for {cueSeconds:0}s: {DescribeEyeSignal(regionAnalysis, landmarkAnalysis, _currentFaceLandmarkMetrics, eyeDropPercent)}");
            }
        }
        else
        {
            _eyeCueStartedAt = null;
        }

        if (jawChangePercent >= GetJawCueThreshold())
        {
            _jawCueStartedAt ??= now;
            if ((now - _jawCueStartedAt.Value).TotalSeconds >= cueSeconds)
            {
                if (eyeCueActive || eyeDropPercent >= GetEyeCueThreshold() * 0.55d)
                {
                    reasons.Add($"Supporting mouth/jaw cue persisted for {cueSeconds:0}s: {DescribeMouthSignal(regionAnalysis, landmarkAnalysis, _currentFaceLandmarkMetrics, jawChangePercent)}");
                }
            }
        }
        else
        {
            _jawCueStartedAt = null;
        }

        if (trendAnalysis is not null
            && trendAnalysis.QualityPercent >= 50d
            && trendAnalysis.EyeClosingTrendPercent is double eyeTrend
            && eyeTrend >= GetEyeCueThreshold() * 0.45d
            && trendCuePercent >= GetCompositeCueThreshold() * 0.60d)
        {
            _eyeTrendCueStartedAt ??= now;
            if ((now - _eyeTrendCueStartedAt.Value).TotalSeconds >= Math.Max(1d, cueSeconds * 0.5d))
            {
                reasons.Add($"Primary eye trend persisted: eyelid aperture fell {eyeTrend:0}% over {trendAnalysis.WindowSeconds:0}s");
            }
        }
        else
        {
            _eyeTrendCueStartedAt = null;
        }

        if (reasons.Count == 0
            && qualityPercent >= 50d
            && compositeCuePercent >= GetCompositeCueThreshold()
            && eyeDropPercent >= GetEyeCueThreshold() * 0.70d)
        {
            reasons.Add($"Primary eye-led cue score reached {compositeCuePercent:0}% with eye closure signal {eyeDropPercent:0}%{DescribeMediaPipeEyeCorroboration(_currentFaceLandmarkCueAnalysis, _currentFaceLandmarkMetrics)}");
        }

        return reasons;
    }

    private static double MaxSignal(params double?[] values)
    {
        var maximum = 0d;
        foreach (var value in values)
        {
            if (value is double number)
            {
                maximum = Math.Max(maximum, number);
            }
        }

        return maximum;
    }

    private static string DescribeEyeSignal(
        FaceCueAnalysis? regionAnalysis,
        FaceLandmarkCueAnalysis? landmarkAnalysis,
        FaceLandmarkMetrics metrics,
        double selectedSignal)
    {
        if (landmarkAnalysis?.EyeClosurePercent is double landmarkEye
            && landmarkEye >= selectedSignal - 0.1d)
        {
            return $"landmark eyelid aperture closed {landmarkEye:0}% from awake baseline{DescribeMediaPipeEyeCorroboration(landmarkAnalysis, metrics)}";
        }

        if (regionAnalysis is not null)
        {
            return $"eye-region openness dropped {regionAnalysis.EyeDropPercent:0}% from awake baseline{DescribeMediaPipeEyeCorroboration(landmarkAnalysis, metrics)}";
        }

        return $"eye signal reached {selectedSignal:0}%{DescribeMediaPipeEyeCorroboration(landmarkAnalysis, metrics)}";
    }

    private static string DescribeMouthSignal(
        FaceCueAnalysis? regionAnalysis,
        FaceLandmarkCueAnalysis? landmarkAnalysis,
        FaceLandmarkMetrics metrics,
        double selectedSignal)
    {
        if (landmarkAnalysis?.MouthOpeningChangePercent is double mouthChange
            && mouthChange >= selectedSignal - 0.1d)
        {
            return $"landmark mouth opening increased {mouthChange:0}% from awake baseline{DescribeMediaPipeMouthCorroboration(landmarkAnalysis, metrics)}";
        }

        if (landmarkAnalysis?.JawDroopChangePercent is double jawDroopChange
            && jawDroopChange >= selectedSignal - 0.1d)
        {
            return $"landmark jaw contour dropped {jawDroopChange:0}% from awake baseline{DescribeMediaPipeMouthCorroboration(landmarkAnalysis, metrics)}";
        }

        if (regionAnalysis is not null)
        {
            return $"lower-face/jaw change measured {regionAnalysis.JawChangePercent:0}% from awake baseline{DescribeMediaPipeMouthCorroboration(landmarkAnalysis, metrics)}";
        }

        return $"mouth/jaw signal reached {selectedSignal:0}%{DescribeMediaPipeMouthCorroboration(landmarkAnalysis, metrics)}";
    }

    private static string DescribeMediaPipeEyeCorroboration(FaceLandmarkCueAnalysis? analysis, FaceLandmarkMetrics metrics)
    {
        if (analysis?.MediaPipeBlinkChangePercent is double blinkChange)
        {
            var raw = metrics.MediaPipeAverageEyeBlinkPercent is double rawBlink
                ? $", raw {rawBlink:0}%"
                : "";
            return $" (MediaPipe blink +{blinkChange:0}% from awake baseline{raw})";
        }

        return metrics.MediaPipeAverageEyeBlinkPercent is double blink
            ? $" (MediaPipe blink {blink:0}%)"
            : "";
    }

    private static string DescribeMediaPipeMouthCorroboration(FaceLandmarkCueAnalysis? analysis, FaceLandmarkMetrics metrics)
    {
        if (analysis?.MediaPipeMouthOpeningEvidencePercent is double mouthEvidence)
        {
            var jawChange = analysis.MediaPipeJawOpenChangePercent is double jaw
                ? $"jaw +{jaw:0}%"
                : "";
            var closeDrop = analysis.MediaPipeMouthCloseDropPercent is double close
                ? $"mouth-close drop {close:0}%"
                : "";
            var separator = jawChange.Length > 0 && closeDrop.Length > 0 ? ", " : "";
            return $" (MediaPipe mouth +{mouthEvidence:0}% from awake baseline: {jawChange}{separator}{closeDrop})";
        }

        if (metrics.MediaPipeJawOpenPercent is double jawOpen && metrics.MediaPipeMouthClosePercent is double mouthClose)
        {
            return $" (MediaPipe jaw open {jawOpen:0}%, mouth close {mouthClose:0}%)";
        }

        if (metrics.MediaPipeJawOpenPercent is double jawOnly)
        {
            return $" (MediaPipe jaw open {jawOnly:0}%)";
        }

        if (metrics.MediaPipeMouthClosePercent is double mouthOnly)
        {
            return $" (MediaPipe mouth close {mouthOnly:0}%)";
        }

        return "";
    }

    private bool StartActiveEpisode(
        BitmapSource bitmap,
        DateTime startedAt,
        double threshold,
        double stillnessSeconds,
        string eventName,
        IReadOnlyList<string>? triggerReasons = null)
    {
        var eventFolder = CreateEventFolder(startedAt);
        var videoPath = "";
        if (EventVideoCheckBox.IsChecked == true)
        {
            var requestedVideoPath = Path.Combine(eventFolder, "event_video.mp4");
            if (_eventRecorder.Start(requestedVideoPath, _preEventVideoFrames.Select(frame => frame.JpegBytes).ToArray()))
            {
                videoPath = requestedVideoPath;
            }
            else
            {
                MonitorStatusText.Text = "Event capture started without video. The event database and summaries will still be saved.";
            }
        }

        _activeEpisodeStartedAt = startedAt;
        _activeEpisodeEarliestAutoEndAt = DateTime.Now.AddSeconds(8);
        MarkSymptomActivity(startedAt, "Event capture started; alert-baseline calibration delayed for one symptom-free hour.");
        _activeEventFolder = eventFolder;
        _activeEventVideo = videoPath;
        _activeTriggerReasons = triggerReasons?.ToList() ??
        [
            $"Low motion persisted for {stillnessSeconds:0}s at or below {threshold:0.0}%"
        ];
        _landmarkEventAggregate.Reset();
        _landmarkEventTimeline.Reset();
        UpdateLandmarkEventEvidence(null);

        _episodeStartSnapshot = SnapshotCheckBox.IsChecked == true
            ? SaveSnapshot(bitmap, startedAt, "start", _activeEventFolder)
            : "";

        return true;
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

    private void TryDeleteEventFolder(string folder)
    {
        try
        {
            if (IsSafeEventFolderPath(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch
        {
        }
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
        var files = string.Join(" | ", new[] { _activeEventVideo, _episodeStartSnapshot, endSnapshot, summaryFiles.JsonPath, summaryFiles.CsvPath, summaryFiles.TimelineJsonPath, summaryFiles.TimelineCsvPath }
            .Where(static path => !string.IsNullOrWhiteSpace(path)));
        var notes = $"Triggers: {string.Join("; ", _activeTriggerReasons)}. Ended: {reason}.";
        AddEpisodeEvent(_activeEpisodeStartedAt.Value, endedAt, reason, GetAverageMotionLabel(), files, notes, _activeEventFolder, _activeEventVideo, _episodeStartSnapshot, endSnapshot);

        MarkSymptomActivity(endedAt, "Event capture ended; alert-baseline calibration delayed for one symptom-free hour.");
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
        _symptomCaptureActive = false;
        SymptomCaptureButton.Content = SymptomCaptureStartButtonText;
        _landmarkEventAggregate.Reset();
        _landmarkEventTimeline.Reset();
    }

    private void UpdateLandmarkEventEvidence(double? motionPercent)
    {
        _landmarkEventAggregate.Update(
            _currentFaceLandmarkMetrics,
            _currentFaceLandmarkCueAnalysis,
            _currentFaceLandmarkTrendAnalysis,
            _currentFaceLockStabilityAnalysis,
            _faceLandmarkTracker.LastBackendStatus,
            _currentPersonalFaceCaptureQuality);
        if (_activeEpisodeStartedAt is DateTime startedAt)
        {
            _landmarkEventTimeline.Add(
                startedAt,
                motionPercent,
                _currentFaceLandmarkMetrics,
                _currentFaceLandmarkCueAnalysis,
                _currentFaceLandmarkTrendAnalysis,
                _currentFaceLockStabilityAnalysis,
                _faceLandmarkTracker.LastBackendStatus,
                _currentPersonalFaceCaptureQuality);
        }
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

    private void OpenEventLogClicked(object sender, RoutedEventArgs e)
    {
        var cutoff = DateTime.Today.AddDays(-29);
        var logEvents = new ObservableCollection<EpisodeMonitorEvent>(
            _eventDatabase.LoadEventsSince(_outputFolder, cutoff)
                .Where(HasEventEvidenceFolder));
        var grid = CreateEventLogGrid(logEvents);
        var closeButton = new Button
        {
            Content = "Close",
            Width = 110,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var panel = new DockPanel { Margin = new Thickness(14) };
        var header = new TextBlock
        {
            Text = "Event Log - Last 30 Days",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        DockPanel.SetDock(closeButton, Dock.Bottom);
        panel.Children.Add(closeButton);
        panel.Children.Add(grid);

        var window = new Window
        {
            Owner = this,
            Title = "Episode Monitor Event Log",
            Width = 1100,
            Height = 620,
            MinWidth = 820,
            MinHeight = 460,
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = Foreground,
            Content = panel
        };
        closeButton.Click += (_, _) => window.Close();
        window.Show();
    }

    private DataGrid CreateEventLogGrid(ObservableCollection<EpisodeMonitorEvent> events)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = events
        };
        grid.PreviewMouseRightButtonDown += EventGridRightClick;
        grid.Columns.Add(new DataGridTextColumn { Header = "Started", Binding = new System.Windows.Data.Binding(nameof(EpisodeMonitorEvent.StartLabel)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Ended", Binding = new System.Windows.Data.Binding(nameof(EpisodeMonitorEvent.EndLabel)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Duration", Binding = new System.Windows.Data.Binding(nameof(EpisodeMonitorEvent.Duration)), Width = 95 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Event", Binding = new System.Windows.Data.Binding(nameof(EpisodeMonitorEvent.Event)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Avg Motion", Binding = new System.Windows.Data.Binding(nameof(EpisodeMonitorEvent.AvgMotion)), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Notes", Binding = new System.Windows.Data.Binding(nameof(EpisodeMonitorEvent.Notes)), Width = 280 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Files", Binding = new System.Windows.Data.Binding(nameof(EpisodeMonitorEvent.File)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var deleteItem = new MenuItem { Header = "Delete Event" };
        deleteItem.Click += (_, _) =>
        {
            if (grid.SelectedItem is EpisodeMonitorEvent item && DeleteEventWithConfirmation(item, grid))
            {
                events.Remove(item);
            }
        };
        grid.ContextMenu = new ContextMenu();
        grid.ContextMenu.Items.Add(deleteItem);
        return grid;
    }

    private void DeleteSelectedEventClicked(object sender, RoutedEventArgs e)
    {
        if (EventGrid.SelectedItem is EpisodeMonitorEvent item)
        {
            DeleteEventWithConfirmation(item, EventGrid);
        }
    }

    private void EventGridRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(source);
        if (row is null)
        {
            return;
        }

        row.IsSelected = true;
        grid.SelectedItem = row.Item;
    }

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private bool DeleteEventWithConfirmation(EpisodeMonitorEvent item, FrameworkElement owner)
    {
        var result = MessageBox.Show(
            Window.GetWindow(owner) ?? this,
            $"Delete the event from {item.StartLabel}? This removes the database row and deletes the event video/folder tied to it.",
            "Delete Event?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        try
        {
            var deleted = _eventDatabase.DeleteEvent(_outputFolder, item.EventId) ?? item;
            DeleteEventArtifacts([deleted]);
            RemoveEventFromMainGrid(item.EventId);
            MonitorStatusText.Text = $"Deleted event from {item.StartLabel}.";
            return true;
        }
        catch (Exception ex)
        {
            MonitorStatusText.Text = $"Could not delete event: {ex.Message}";
            return false;
        }
    }

    private void RemoveEventFromMainGrid(string eventId)
    {
        var match = _events.FirstOrDefault(item => string.Equals(item.EventId, eventId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            _events.Remove(match);
        }
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
        SaveOutputFolderSetting(_outputFolder);
        var savedCurrentBaseline = SaveAlertBaselineToOutputFolder();
        var loadedBaseline = savedCurrentBaseline || LoadAlertBaselineFromOutputFolder(showStatus: false);
        UpdateOutputFolderText();
        var eventListStatus = SyncTodaysEventListAfterOutputFolderChange();
        var personalModelStatus = LoadPersonalFaceModelFromOutputFolder(showStatus: false);
        UpdateCalibrationGuard();
        var baselineStatus = loadedBaseline
            ? $"Output folder set: {_outputFolder}. Alert baseline is saved there."
            : $"Output folder set: {_outputFolder}. Calibrate Alert Baseline once when you are alert and symptom-free.";
        MonitorStatusText.Text = $"{baselineStatus} {eventListStatus} {personalModelStatus}".Trim();
    }

    private void CloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AddEpisodeEvent(
        DateTime startedAt,
        DateTime? endedAt,
        string eventName,
        string averageMotion,
        string file,
        string notes,
        string eventFolder,
        string videoFile,
        string startSnapshot,
        string endSnapshot)
    {
        if (string.IsNullOrWhiteSpace(eventFolder) || !Directory.Exists(eventFolder))
        {
            return;
        }

        var item = new EpisodeMonitorEvent
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
            Event = eventName,
            AvgMotion = averageMotion,
            File = file,
            Notes = notes,
            EventFolder = eventFolder,
            VideoFile = videoFile,
            StartSnapshot = startSnapshot,
            EndSnapshot = endSnapshot
        };
        _events.Insert(0, item);
        SaveEventListItem(item);
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

    private static string ResolveInitialOutputFolder(string requestedOutputFolder = "")
    {
        if (TryResolveRequestedOutputFolder(requestedOutputFolder, out var requested))
        {
            return requested;
        }

        var saved = LoadOutputFolderSetting();
        if (!string.IsNullOrWhiteSpace(saved))
        {
            return saved;
        }

        var preferredRoot = Path.GetPathRoot(PreferredExternalOutputFolder);
        if (!string.IsNullOrWhiteSpace(preferredRoot) && Directory.Exists(preferredRoot))
        {
            return PreferredExternalOutputFolder;
        }

        return Path.Combine(AppContext.BaseDirectory, "EpisodeMonitorSessions");
    }

    private static bool TryResolveRequestedOutputFolder(string requestedOutputFolder, out string outputFolder)
    {
        outputFolder = "";
        if (string.IsNullOrWhiteSpace(requestedOutputFolder))
        {
            return false;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(requestedOutputFolder.Trim().Trim('"'));
            var fullPath = Path.GetFullPath(expanded);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return false;
            }

            outputFolder = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string LoadOutputFolderSetting()
    {
        var saved = GetSettingsPaths()
            .Select(ReadOutputFolderSetting)
            .Where(static settings => !string.IsNullOrWhiteSpace(settings?.OutputFolder))
            .OrderByDescending(static settings => settings!.UpdatedAtUtc)
            .FirstOrDefault();
        return saved?.OutputFolder.Trim() ?? "";
    }

    private static void SaveOutputFolderSetting(string outputFolder)
    {
        var settings = new EpisodeMonitorSettings
        {
            OutputFolder = outputFolder,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

        foreach (var path in GetSettingsPaths())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch
            {
                // Output folder persistence should never interrupt monitoring.
            }
        }
    }

    private static EpisodeMonitorSettings? ReadOutputFolderSetting(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<EpisodeMonitorSettings>(
                File.ReadAllText(path, Encoding.UTF8));
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetSettingsPaths()
    {
        var paths = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, SettingsFileName)
        };

        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                paths.Add(Path.Combine(localAppData, "EpisodeMonitor", SettingsFileName));
            }
        }
        catch
        {
            // A beside-exe settings file is still enough if the user profile path is unavailable.
        }

        return paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024L)
        {
            return $"{Math.Max(0L, bytes).ToString(CultureInfo.InvariantCulture)} B";
        }

        var size = (double)Math.Max(0L, bytes);
        var units = new[] { "KB", "MB", "GB", "TB" };
        var unitIndex = -1;
        do
        {
            size /= 1024d;
            unitIndex++;
        }
        while (size >= 1024d && unitIndex < units.Length - 1);

        return $"{size:0.##} {units[unitIndex]}";
    }

    private void SaveEventListItem(EpisodeMonitorEvent item)
    {
        if (!HasEventEvidenceFolder(item))
        {
            return;
        }

        try
        {
            _eventDatabase.SaveEvent(_outputFolder, item);
        }
        catch (Exception ex)
        {
            MonitorStatusText.Text = $"Event was captured, but the event database could not be updated: {ex.Message}";
        }
    }

    private int LoadTodaysEventListFromOutputFolder()
    {
        _events.Clear();
        var loaded = _eventDatabase.LoadEventsForDate(_outputFolder, DateTime.Today);
        foreach (var item in loaded)
        {
            if (HasEventEvidenceFolder(item))
            {
                _events.Add(item);
            }
        }

        return _events.Count;
    }

    private string SyncTodaysEventListAfterOutputFolderChange()
    {
        var databasePath = _eventDatabase.GetDatabasePath(_outputFolder);
        if (File.Exists(databasePath))
        {
            var loaded = LoadTodaysEventListFromOutputFolder();
            return loaded == 1
                ? "Loaded 1 database event for today."
                : $"Loaded {loaded} database events for today.";
        }

        _events.Clear();
        return "No event database found for today in the new output folder.";
    }

    private void DeleteEventArtifacts(IEnumerable<EpisodeMonitorEvent> events)
    {
        var folders = events
            .Select(static item => item.EventFolder)
            .Where(IsSafeEventFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var folder in folders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; the database row is already gone.
            }
        }
    }

    private static bool HasEventEvidenceFolder(EpisodeMonitorEvent item)
    {
        return !string.IsNullOrWhiteSpace(item.EventFolder)
            && Directory.Exists(item.EventFolder);
    }

    private bool IsSafeEventFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var outputRoot = Path.GetFullPath(_outputFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return fullPath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase)
            && name.StartsWith("Episode_", StringComparison.OrdinalIgnoreCase);
    }

    private bool SaveAlertBaselineToOutputFolder()
    {
        try
        {
            var regionBaseline = _faceCueAnalyzer.ExportBaseline();
            var landmarkBaseline = _faceLandmarkCueAnalyzer.ExportBaseline();
            if (!IsFaceCueBaselineReady(regionBaseline) && !IsLandmarkCueBaselineReady(landmarkBaseline))
            {
                return false;
            }

            var cameraName = GetSelectedCameraName();
            var modeLabel = GetSelectedCameraModeLabel();
            var savedAtUtc = DateTime.UtcNow;
            var file = new AlertBaselineFile
            {
                Version = 1,
                SavedAtUtc = savedAtUtc,
                CameraName = cameraName,
                CameraModeLabel = modeLabel,
                RegionBaseline = regionBaseline,
                LandmarkBaseline = landmarkBaseline
            };

            var path = GetAlertBaselinePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _outputFolder);
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, Encoding.UTF8);
            _alertBaselineSavedAtUtc = savedAtUtc;
            _alertBaselineCameraName = cameraName;
            _alertBaselineModeLabel = modeLabel;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool LoadAlertBaselineFromOutputFolder(bool showStatus)
    {
        try
        {
            var path = GetAlertBaselinePath();
            if (!File.Exists(path))
            {
                _alertBaselineSavedAtUtc = null;
                _alertBaselineCameraName = "";
                _alertBaselineModeLabel = "";
                if (showStatus)
                {
                    MonitorStatusText.Text = "No saved alert baseline found in this output folder.";
                }

                return false;
            }

            var file = JsonSerializer.Deserialize<AlertBaselineFile>(File.ReadAllText(path, Encoding.UTF8));
            if (file is null)
            {
                return false;
            }

            var loadedRegion = _faceCueAnalyzer.TryImportBaseline(file.RegionBaseline);
            var loadedLandmark = _faceLandmarkCueAnalyzer.TryImportBaseline(file.LandmarkBaseline);
            if ((!loadedRegion && !loadedLandmark)
                || (!IsFaceCueBaselineReady(file.RegionBaseline) && !IsLandmarkCueBaselineReady(file.LandmarkBaseline)))
            {
                return false;
            }

            _alertBaselineCalibrationActive = false;
            _alertBaselineSavedAtUtc = file.SavedAtUtc == default
                ? File.GetLastWriteTimeUtc(path)
                : file.SavedAtUtc;
            _alertBaselineCameraName = file.CameraName ?? "";
            _alertBaselineModeLabel = file.CameraModeLabel ?? "";
            CalibrateAlertBaselineButton.Content = AlertBaselineStartButtonText;
            if (showStatus)
            {
                MonitorStatusText.Text = $"Loaded saved alert baseline from {path}.";
            }

            return true;
        }
        catch (Exception ex)
        {
            if (showStatus)
            {
                MonitorStatusText.Text = $"Could not load saved alert baseline: {ex.Message}";
            }

            return false;
        }
    }

    private string LoadPersonalFaceModelFromOutputFolder(bool showStatus)
    {
        var folder = GetPersonalFaceModelFolder();
        try
        {
            var budgetReport = PersonalFaceMeasurementJournal.EnforceBudgetForModelFolder(folder);
            var model = _personalFaceModelStore.TryRead(folder);
            _personalFaceMeasurementJournalSizeBytes = budgetReport.BytesAfter;
            var budgetStatus = budgetReport.DeletedFileCount > 0
                ? $" Pruned {budgetReport.DeletedFileCount} old measurement file{(budgetReport.DeletedFileCount == 1 ? "" : "s")} to keep the journal under {FormatBytes(budgetReport.BudgetBytes)}."
                : "";
            if (model is null)
            {
                _avatarHistoricalDataSuspect = false;
                _avatarHistoricalDataAuditSummary = "";
                _avatarTrackingAuditHold = false;
                _avatarTrackingAuditHoldSummary = "";
                _currentMeasurementAvatarCapturePlan = null;
                var emptyStatus = _personalFaceModelBuilder.CurrentModel.AcceptedSamples > 0
                    ? "No saved Avatar model found in this output folder; current in-memory model will continue and save there."
                    : "No saved Avatar model found in this output folder yet.";
                emptyStatus += budgetStatus;
                if (showStatus)
                {
                    MonitorStatusText.Text = emptyStatus;
                }

                return emptyStatus;
            }

            var auditSnapshot = BuildPersonalFaceAuditSnapshot(folder, model);
            _currentPersonalFaceMotionModel = auditSnapshot.MotionModel;
            _currentPersonalFaceCorpusReadiness = auditSnapshot.Readiness;
            ApplyAvatarHistoricalDataAudit(auditSnapshot.Readiness);
            _personalFaceModelBuilder.LoadModel(model);
            _currentPersonalFaceModelUpdate = new PersonalFaceModelUpdate(
                false,
                PersonalFaceModelRejectionKind.LearningStopped,
                "loaded saved Avatar model; learning resumes when Start Avatar Learning is active",
                0d,
                _personalFaceModelBuilder.CurrentModel);
            _personalFaceModelPath = Path.Combine(folder, _personalFaceModelStore.FileName);
            _personalFaceMotionModelPath = Path.Combine(folder, _personalFaceMotionModelStore.FileName);
            _personalFaceCorpusReadinessPath = Path.Combine(folder, PersonalFaceCorpusReadinessStore.DefaultJsonFileName);
            _personalFaceCollectionAuditPath = Path.Combine(folder, PersonalFaceCollectionAuditStore.DefaultJsonFileName);
            _measurementFacePreviewPath = Path.Combine(folder, MeasurementFacePreviewStore.HtmlFileName);
            _measurementAvatarTrainingPackagePath = Path.Combine(folder, MeasurementAvatarTrainingPackageStore.HtmlFileName);
            _measurementAvatarCapturePlanPath = Path.Combine(folder, MeasurementAvatarCapturePlanStore.HtmlFileName);
            _measurementAvatarSystemDashboardPath = GetMeasurementAvatarSystemDashboardHtmlPath(folder);
            QueuePersonalFaceReportSave(CreatePersonalFaceReportSnapshot(folder, _currentPersonalFaceModelUpdate.Model));
            UpdatePersonalFaceCaptureQuality();
            UpdateAvatarLearningStatusUi();

            var auditStatus = _avatarHistoricalDataSuspect
                ? $" {_avatarHistoricalDataAuditSummary} Rebuild Avatar Data before collecting more."
                : _avatarTrackingAuditHold
                    ? $" {_avatarTrackingAuditHoldSummary} Review tracking before collecting more."
                : "";
            var status = $"Loaded Avatar model: {model.AcceptedSamples} accepted sample{(model.AcceptedSamples == 1 ? "" : "s")}; measurements {FormatBytes(_personalFaceMeasurementJournalSizeBytes)}.{budgetStatus}{auditStatus}";
            if (showStatus)
            {
                MonitorStatusText.Text = status;
            }

            return status;
        }
        catch (Exception ex)
        {
            var status = $"Could not load saved Avatar model: {ex.Message}";
            if (showStatus)
            {
                MonitorStatusText.Text = status;
            }

            return status;
        }
    }

    private string GetPersonalFaceModelFolder()
    {
        return Path.Combine(_outputFolder, PersonalFaceModelFolderName);
    }

    private static string CreateUniqueArchivePath(string archiveRoot, DateTime utcNow)
    {
        var basePath = Path.Combine(archiveRoot, $"avatar-data-{utcNow:yyyyMMddTHHmmssZ}");
        var path = basePath;
        for (var index = 2; Directory.Exists(path); index++)
        {
            path = $"{basePath}-{index}";
        }

        return path;
    }

    private static PersonalFaceAuditSnapshot BuildPersonalFaceAuditSnapshot(string folder, PersonalFaceModel model)
    {
        var samples = PersonalFaceMeasurementJournal
            .ReadRecentSamples(folder, PersonalFaceMotionModelMaxSamples)
            .Where(sample => string.Equals(sample.SubjectId, model.SubjectId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var motionModel = new PersonalFaceMotionModelBuilder().Build(samples.Select(PersonalFaceMotionObservation.FromMeasurementSample));
        if (motionModel.ObservationCount == 0)
        {
            motionModel.SubjectId = model.SubjectId;
            motionModel.SubjectDisplayName = model.SubjectDisplayName;
            motionModel.SubjectCollectionMode = model.SubjectCollectionMode;
            motionModel.CreatedAtUtc = DateTime.UtcNow;
            motionModel.UpdatedAtUtc = DateTime.UtcNow;
        }

        var readiness = new PersonalFaceCorpusReadinessBuilder().Build(
            model,
            motionModel,
            samples,
            PersonalFaceMeasurementJournal.GetMeasurementsSizeBytes(folder));
        return new PersonalFaceAuditSnapshot(motionModel, readiness);
    }

    private void ApplyAvatarHistoricalDataAudit(PersonalFaceCorpusReadiness readiness)
    {
        _avatarHistoricalDataSuspect = readiness.AcceptedBaselineSamples >= PersonalFaceDataAuditMinimumSamplesToBlock
            && readiness.DataAuditHealthPercent > 0d
            && readiness.DataAuditHealthPercent < PersonalFaceDataAuditBlockThresholdPercent;
        _avatarHistoricalDataAuditSummary = _avatarHistoricalDataSuspect
            ? FormatAvatarDataAuditSummary(readiness)
            : "";
        var auditGate = PersonalFaceLearningAuditGate.Evaluate(readiness);
        _avatarTrackingAuditHold = !_avatarHistoricalDataSuspect && auditGate.HoldLearning;
        _avatarTrackingAuditHoldSummary = _avatarTrackingAuditHold
            ? auditGate.Reason
            : "";
    }

    private static string FormatAvatarDataAuditSummary(PersonalFaceCorpusReadiness readiness)
    {
        var finding = readiness.DataAuditFindings.FirstOrDefault();
        var findingText = string.IsNullOrWhiteSpace(finding) ? "" : $" Finding: {finding}";
        return $"Saved Avatar data failed audit ({readiness.DataAuditHealthPercent:0.#}% health; pose {readiness.PoseEstimationHealthPercent:0.#}%, feature anchoring {readiness.FeatureAnchoringHealthPercent:0.#}%, jaw scale {readiness.JawDroopScaleHealthPercent:0.#}%).{findingText}";
    }

    private string GetAlertBaselinePath()
    {
        return Path.Combine(_outputFolder, AlertBaselineFolderName, AlertBaselineFileName);
    }

    private string GetSelectedCameraName()
    {
        return CameraComboBox.SelectedItem is CameraDevice camera ? camera.Name : "";
    }

    private string GetSelectedCameraModeLabel()
    {
        return CameraModeComboBox.SelectedItem is CameraVideoMode mode ? mode.Label : "";
    }

    private HeadPoseCalibration GetCurrentHeadPoseCalibration()
    {
        var model = _currentPersonalFaceModelUpdate.Model;
        var learnedReferenceEyeSpan = TryEstimateLearnedReferenceInterEyeFrameWidth(model, out var referenceSamples);
        var cameraName = GetSelectedCameraName();
        if (cameraName.Contains("Insta360 Link 2 Pro", StringComparison.OrdinalIgnoreCase))
        {
            return new HeadPoseCalibration
            {
                CameraHorizontalFovDegrees = Insta360Link2ProHorizontalFovDegrees,
                ReferenceInterEyeFrameWidth = learnedReferenceEyeSpan,
                ReferenceSampleCount = referenceSamples,
                ReferenceSource = learnedReferenceEyeSpan is > 0d
                    ? $"learned {PersonalFaceSubjectDisplayName} face scale ({referenceSamples} samples)"
                    : ""
            };
        }

        return learnedReferenceEyeSpan is > 0d
            ? new HeadPoseCalibration
            {
                ReferenceInterEyeFrameWidth = learnedReferenceEyeSpan,
                ReferenceSampleCount = referenceSamples,
                ReferenceSource = $"learned {PersonalFaceSubjectDisplayName} face scale ({referenceSamples} samples)"
            }
            : HeadPoseCalibration.None;
    }

    private static double? TryEstimateLearnedReferenceInterEyeFrameWidth(
        PersonalFaceModel model,
        out int referenceSamples)
    {
        ArgumentNullException.ThrowIfNull(model);

        referenceSamples = Math.Min(model.FaceWidth.SampleCount, model.InterEyeDistanceToFaceWidth.SampleCount);
        if (referenceSamples < PersonalFacePoseReferenceMinimumSamples)
        {
            return null;
        }

        var faceWidth = MetricValue(model.FaceWidth);
        var interEyeToFaceWidth = MetricValue(model.InterEyeDistanceToFaceWidth);
        if (faceWidth is not > 0.04d or > 0.95d
            || interEyeToFaceWidth is not > 0.08d or > 0.75d)
        {
            return null;
        }

        var interEyeFrameWidth = faceWidth.Value * interEyeToFaceWidth.Value;
        return interEyeFrameWidth is > 0.01d and < 0.75d
            ? interEyeFrameWidth
            : null;
    }

    private static double? MetricValue(PersonalMetricDistribution distribution)
    {
        return distribution.ExponentialMovingAverage ?? distribution.Average;
    }

    private bool IsAlertBaselineCameraSetupDifferent()
    {
        var cameraName = GetSelectedCameraName();
        var modeLabel = GetSelectedCameraModeLabel();
        var cameraChanged = !string.IsNullOrWhiteSpace(_alertBaselineCameraName)
            && !string.IsNullOrWhiteSpace(cameraName)
            && !string.Equals(_alertBaselineCameraName, cameraName, StringComparison.OrdinalIgnoreCase);
        var modeChanged = !string.IsNullOrWhiteSpace(_alertBaselineModeLabel)
            && !string.IsNullOrWhiteSpace(modeLabel)
            && !string.Equals(_alertBaselineModeLabel, modeLabel, StringComparison.OrdinalIgnoreCase);
        return cameraChanged || modeChanged;
    }

    private string GetAlertBaselineDurabilityLabel()
    {
        var regionBaseline = _faceCueAnalyzer.ExportBaseline();
        var landmarkBaseline = _faceLandmarkCueAnalyzer.ExportBaseline();
        var regionReady = IsFaceCueBaselineReady(regionBaseline);
        var landmarkReady = IsLandmarkCueBaselineReady(landmarkBaseline);
        var landmarkPartial = IsLandmarkCueBaselineUsable(landmarkBaseline);
        return (regionReady, landmarkReady, landmarkPartial) switch
        {
            (_, true, _) => "strong saved baseline",
            (true, false, true) => "usable saved baseline; landmark cues will keep strengthening",
            (true, false, false) => "usable saved fallback baseline",
            _ => "partial baseline"
        };
    }

    private string GetAlertBaselineStatusText()
    {
        if (_alertBaselineSavedAtUtc is DateTime savedAtUtc)
        {
            var savedText = savedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            var setupNote = IsAlertBaselineCameraSetupDifferent()
                ? " Current camera setup differs from the saved setup; recalibrate when convenient if tracking looks off."
                : " Recalibrate only if lighting, glasses, seating distance, or camera setup changed noticeably.";
            return $"Alert baseline ready ({GetAlertBaselineDurabilityLabel()}, saved {savedText}).{setupNote}";
        }

        return _lastSymptomAt is DateTime lastSymptom
            ? $"No saved alert baseline yet. Calibrate when alert and symptom-free. Last symptom marker: {lastSymptom:g}."
            : "No saved alert baseline yet. Calibrate once while alert and symptom-free; it will be reused from this output folder.";
    }

    private static bool IsFaceCueBaselineReady(FaceCueBaselineSnapshot? baseline)
    {
        return baseline is not null && baseline.BaselineSamples >= 30;
    }

    private static bool IsLandmarkCueBaselineReady(FaceLandmarkCueBaselineSnapshot? baseline)
    {
        return baseline is not null
            && (baseline.EyeBaselineSamples >= 20
                || baseline.MediaPipeBlinkBaselineSamples >= 20);
    }

    private static bool IsLandmarkCueBaselineUsable(FaceLandmarkCueBaselineSnapshot? baseline)
    {
        return baseline is not null
            && (baseline.EyeBaselineSamples > 0
            || baseline.MouthBaselineSamples > 0
            || baseline.JawDroopBaselineSamples > 0
            || baseline.MediaPipeBlinkBaselineSamples > 0
            || baseline.MediaPipeJawOpenBaselineSamples > 0
            || baseline.MediaPipeMouthCloseBaselineSamples > 0);
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

    private static string FormatOptional(bool? value)
    {
        return value?.ToString() ?? "";
    }

    private (string JsonPath, string CsvPath, string TimelineJsonPath, string TimelineCsvPath) WriteEventSummary(DateTime startedAt, DateTime endedAt, string endReason, string endSnapshot)
    {
        if (string.IsNullOrWhiteSpace(_activeEventFolder))
        {
            return ("", "", "", "");
        }

        try
        {
            Directory.CreateDirectory(_activeEventFolder);
            var duration = endedAt - startedAt;
            var timelineFiles = _landmarkEventTimeline.Write(_activeEventFolder);
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
                LandmarkSource = _currentFaceLandmarkMetrics.Source,
                LandmarkConfidence = _currentFaceLandmarkMetrics.ConfidenceLabel,
                LandmarkTrackingConfidence = _currentFaceLandmarkMetrics.TrackingConfidence,
                LandmarkEyeConfidence = _currentFaceLandmarkMetrics.EyeConfidence,
                LandmarkMouthConfidence = _currentFaceLandmarkMetrics.MouthConfidence,
                LandmarkEyeQuality = _currentFaceLandmarkMetrics.EyeMeasurementQualityPercent,
                LandmarkMouthQuality = _currentFaceLandmarkMetrics.MouthMeasurementQualityPercent,
                LandmarkOverallQuality = _currentFaceLandmarkMetrics.OverallMeasurementQualityPercent,
                CaptureQualityLabel = _currentPersonalFaceCaptureQuality.Label,
                CaptureQualityScore = _currentPersonalFaceCaptureQuality.ScorePercent,
                CaptureQualityCanCollect = _currentPersonalFaceCaptureQuality.CanCollectMeasurements,
                CaptureQualityAvatarGrade = _currentPersonalFaceCaptureQuality.StrongEnoughForAvatarLearning,
                CaptureQualityReason = _currentPersonalFaceCaptureQuality.PrimaryReason,
                CaptureQualityCameraModeScore = _currentPersonalFaceCaptureQuality.CameraModeScorePercent,
                CaptureQualityFaceScaleScore = _currentPersonalFaceCaptureQuality.FaceScaleScorePercent,
                CaptureQualityEyeScore = _currentPersonalFaceCaptureQuality.EyeEvidenceScorePercent,
                CaptureQualityMouthScore = _currentPersonalFaceCaptureQuality.MouthEvidenceScorePercent,
                CaptureQualityStabilityScore = _currentPersonalFaceCaptureQuality.StabilityScorePercent,
                CaptureQualityGlassesScore = _currentPersonalFaceCaptureQuality.GlassesRiskScorePercent,
                CaptureQualityStorageScore = _currentPersonalFaceCaptureQuality.StorageScorePercent,
                CaptureQualityFaceWidth = _currentPersonalFaceCaptureQuality.FaceWidthPercent,
                CaptureQualityFaceHeight = _currentPersonalFaceCaptureQuality.FaceHeightPercent,
                CaptureQualityIssues = _currentPersonalFaceCaptureQuality.Issues,
                CaptureQualitySuggestions = _currentPersonalFaceCaptureQuality.Suggestions,
                LandmarkFaceReliabilityStatus = _currentFaceLockStabilityAnalysis.Status,
                LandmarkFaceReliabilitySamples = _currentFaceLockStabilityAnalysis.SampleCount,
                LandmarkFaceReliability = _currentFaceLockStabilityAnalysis.CompositeReliabilityPercent,
                LandmarkFaceContinuity = _currentFaceLockStabilityAnalysis.FaceContinuityPercent,
                LandmarkEyeReliability = _currentFaceLockStabilityAnalysis.EyeReliabilityPercent,
                LandmarkMouthReliability = _currentFaceLockStabilityAnalysis.MouthReliabilityPercent,
                LandmarkFaceBoundsRate = _currentFaceLockStabilityAnalysis.FaceBoundsRatePercent,
                LandmarkEyeUsableRate = _currentFaceLockStabilityAnalysis.EyeUsableRatePercent,
                LandmarkMouthUsableRate = _currentFaceLockStabilityAnalysis.MouthUsableRatePercent,
                LandmarkEyeImageQualityAvailable = _currentFaceLandmarkMetrics.EyeImageQualityAvailable,
                LandmarkMouthImageQualityAvailable = _currentFaceLandmarkMetrics.MouthImageQualityAvailable,
                LandmarkEyeGlare = _currentFaceLandmarkMetrics.EyeGlarePercent,
                LandmarkMouthGlare = _currentFaceLandmarkMetrics.MouthGlarePercent,
                LandmarkEyeContrast = _currentFaceLandmarkMetrics.EyeContrastPercent,
                LandmarkMouthContrast = _currentFaceLandmarkMetrics.MouthContrastPercent,
                LandmarkEyeSharpness = _currentFaceLandmarkMetrics.EyeSharpnessPercent,
                LandmarkMouthSharpness = _currentFaceLandmarkMetrics.MouthSharpnessPercent,
                LandmarkEyeDarkCoverage = _currentFaceLandmarkMetrics.EyeDarkCoveragePercent,
                LandmarkMouthDarkCoverage = _currentFaceLandmarkMetrics.MouthDarkCoveragePercent,
                LandmarkRawEyeAsymmetry = _currentFaceLandmarkMetrics.RawEyeAsymmetryPercent,
                LandmarkEyeAsymmetry = _currentFaceLandmarkMetrics.EyeAsymmetryPercent,
                LandmarkEyeAgreement = _currentFaceLandmarkMetrics.EyeAgreementPercent,
                LandmarkPossibleOneEyeArtifact = _currentFaceLandmarkMetrics.PossibleOneEyeArtifact,
                LandmarkLeftEyeReconstructed = _currentFaceLandmarkMetrics.LeftEyeReconstructed,
                LandmarkRightEyeReconstructed = _currentFaceLandmarkMetrics.RightEyeReconstructed,
                LandmarkMouthReconstructed = _currentFaceLandmarkMetrics.MouthReconstructed,
                LandmarkEyeArtifactSuppressed = _currentFaceLandmarkMetrics.EyeArtifactSuppressed,
                LandmarkRawEyeOpening = _currentFaceLandmarkMetrics.RawAverageEyeOpeningRatio,
                LandmarkRawMouthOpening = _currentFaceLandmarkMetrics.RawMouthOpeningRatio,
                LandmarkRawJawDroop = _currentFaceLandmarkMetrics.RawJawDroopRatio,
                LandmarkEyeOpening = _currentFaceLandmarkMetrics.AverageEyeOpeningRatio,
                LandmarkMouthOpening = _currentFaceLandmarkMetrics.MouthOpeningRatio,
                LandmarkMouthOpeningVelocity = _currentFaceLandmarkMetrics.MouthOpeningVelocityPerSecond,
                LandmarkJawDroop = _currentFaceLandmarkMetrics.JawDroopRatio,
                LandmarkJawDroopVelocity = _currentFaceLandmarkMetrics.JawDroopVelocityPerSecond,
                LandmarkMediaPipeLeftEyeBlink = _currentFaceLandmarkMetrics.MediaPipeLeftEyeBlinkPercent,
                LandmarkMediaPipeRightEyeBlink = _currentFaceLandmarkMetrics.MediaPipeRightEyeBlinkPercent,
                LandmarkMediaPipeAverageEyeBlink = _currentFaceLandmarkMetrics.MediaPipeAverageEyeBlinkPercent,
                LandmarkMediaPipeJawOpen = _currentFaceLandmarkMetrics.MediaPipeJawOpenPercent,
                LandmarkMediaPipeMouthClose = _currentFaceLandmarkMetrics.MediaPipeMouthClosePercent,
                LandmarkMediaPipeEyeOpeningCorrection = _currentFaceLandmarkMetrics.MediaPipeEyeOpeningCorrectionRatio,
                LandmarkMediaPipeMouthOpeningCorrection = _currentFaceLandmarkMetrics.MediaPipeMouthOpeningCorrectionRatio,
                LandmarkMediaPipeEyeOpeningCorrected = _currentFaceLandmarkMetrics.MediaPipeEyeOpeningCorrected,
                LandmarkMediaPipeMouthOpeningCorrected = _currentFaceLandmarkMetrics.MediaPipeMouthOpeningCorrected,
                LandmarkCueStatus = _currentFaceLandmarkCueAnalysis?.Status ?? "",
                LandmarkCueScore = _currentFaceLandmarkCueAnalysis?.CompositeCuePercent,
                LandmarkEyeCueEligible = _currentFaceLandmarkCueAnalysis?.EyeCueEligible,
                LandmarkMouthCueEligible = _currentFaceLandmarkCueAnalysis?.MouthCueEligible,
                LandmarkEyeClosure = _currentFaceLandmarkCueAnalysis?.EyeClosurePercent,
                LandmarkMouthOpeningChange = _currentFaceLandmarkCueAnalysis?.MouthOpeningChangePercent,
                LandmarkJawDroopBaseline = _currentFaceLandmarkCueAnalysis?.JawDroopBaselineRatio,
                LandmarkJawDroopChange = _currentFaceLandmarkCueAnalysis?.JawDroopChangePercent,
                LandmarkMediaPipeBlinkBaselineReady = _currentFaceLandmarkCueAnalysis?.MediaPipeBlinkBaselineReady,
                LandmarkMediaPipeMouthBaselineReady = _currentFaceLandmarkCueAnalysis?.MediaPipeMouthBaselineReady,
                LandmarkMediaPipeBlinkBaseline = _currentFaceLandmarkCueAnalysis?.MediaPipeBlinkBaselinePercent,
                LandmarkMediaPipeJawOpenBaseline = _currentFaceLandmarkCueAnalysis?.MediaPipeJawOpenBaselinePercent,
                LandmarkMediaPipeMouthCloseBaseline = _currentFaceLandmarkCueAnalysis?.MediaPipeMouthCloseBaselinePercent,
                LandmarkMediaPipeBlinkChange = _currentFaceLandmarkCueAnalysis?.MediaPipeBlinkChangePercent,
                LandmarkMediaPipeJawOpenChange = _currentFaceLandmarkCueAnalysis?.MediaPipeJawOpenChangePercent,
                LandmarkMediaPipeMouthCloseDrop = _currentFaceLandmarkCueAnalysis?.MediaPipeMouthCloseDropPercent,
                LandmarkMediaPipeMouthOpeningEvidence = _currentFaceLandmarkCueAnalysis?.MediaPipeMouthOpeningEvidencePercent,
                LandmarkTrendStatus = _currentFaceLandmarkTrendAnalysis.Status,
                LandmarkTrendScore = _currentFaceLandmarkTrendAnalysis.TrendCuePercent,
                LandmarkTrendWindowSeconds = _currentFaceLandmarkTrendAnalysis.WindowSeconds,
                LandmarkEyeClosingTrend = _currentFaceLandmarkTrendAnalysis.EyeClosingTrendPercent,
                LandmarkMouthOpeningTrend = _currentFaceLandmarkTrendAnalysis.MouthOpeningTrendPercent,
                LandmarkEyeOpeningSlope = _currentFaceLandmarkTrendAnalysis.EyeOpeningSlopePerSecond,
                LandmarkMouthOpeningSlope = _currentFaceLandmarkTrendAnalysis.MouthOpeningSlopePerSecond,
                LandmarkEventSamples = _landmarkEventAggregate.SampleCount,
                LandmarkEventSources = _landmarkEventAggregate.Sources,
                LandmarkEventBackendStatuses = _landmarkEventAggregate.BackendStatuses,
                LandmarkEventMinimumEyeQuality = _landmarkEventAggregate.MinimumEyeQualityPercent,
                LandmarkEventMinimumMouthQuality = _landmarkEventAggregate.MinimumMouthQualityPercent,
                LandmarkEventMinimumOverallQuality = _landmarkEventAggregate.MinimumOverallQualityPercent,
                LandmarkEventAverageOverallQuality = _landmarkEventAggregate.AverageOverallQualityPercent,
                LandmarkEventCaptureQualitySamples = _landmarkEventAggregate.CaptureQualitySamples,
                LandmarkEventCaptureQualityCanCollectSamples = _landmarkEventAggregate.CaptureQualityCanCollectSamples,
                LandmarkEventCaptureQualityAvatarGradeSamples = _landmarkEventAggregate.CaptureQualityAvatarGradeSamples,
                LandmarkEventMinimumCaptureQualityScore = _landmarkEventAggregate.MinimumCaptureQualityScore,
                LandmarkEventMaximumCaptureQualityScore = _landmarkEventAggregate.MaximumCaptureQualityScore,
                LandmarkEventAverageCaptureQualityScore = _landmarkEventAggregate.AverageCaptureQualityScore,
                LandmarkEventCaptureQualityLabels = _landmarkEventAggregate.CaptureQualityLabels,
                LandmarkEventCaptureQualityIssues = _landmarkEventAggregate.CaptureQualityIssues,
                LandmarkEventFaceReliabilitySamples = _landmarkEventAggregate.FaceReliabilitySamples,
                LandmarkEventFaceReliabilityUsableSamples = _landmarkEventAggregate.FaceReliabilityUsableSamples,
                LandmarkEventMinimumFaceReliability = _landmarkEventAggregate.MinimumFaceReliabilityPercent,
                LandmarkEventAverageFaceReliability = _landmarkEventAggregate.AverageFaceReliabilityPercent,
                LandmarkEventMinimumFaceContinuity = _landmarkEventAggregate.MinimumFaceContinuityPercent,
                LandmarkEventAverageFaceContinuity = _landmarkEventAggregate.AverageFaceContinuityPercent,
                LandmarkEventMinimumEyeReliability = _landmarkEventAggregate.MinimumEyeReliabilityPercent,
                LandmarkEventAverageEyeReliability = _landmarkEventAggregate.AverageEyeReliabilityPercent,
                LandmarkEventMinimumMouthReliability = _landmarkEventAggregate.MinimumMouthReliabilityPercent,
                LandmarkEventAverageMouthReliability = _landmarkEventAggregate.AverageMouthReliabilityPercent,
                LandmarkEventMinimumEyeOpening = _landmarkEventAggregate.MinimumEyeOpeningRatio,
                LandmarkEventMaximumEyeClosure = _landmarkEventAggregate.MaximumEyeClosurePercent,
                LandmarkEventMaximumMouthOpening = _landmarkEventAggregate.MaximumMouthOpeningRatio,
                LandmarkEventMaximumMouthOpeningChange = _landmarkEventAggregate.MaximumMouthOpeningChangePercent,
                LandmarkEventMaximumMouthOpeningVelocity = _landmarkEventAggregate.MaximumMouthOpeningVelocityPerSecond,
                LandmarkEventMaximumJawDroop = _landmarkEventAggregate.MaximumJawDroopRatio,
                LandmarkEventMaximumJawDroopChange = _landmarkEventAggregate.MaximumJawDroopChangePercent,
                LandmarkEventMaximumJawDroopVelocity = _landmarkEventAggregate.MaximumJawDroopVelocityPerSecond,
                LandmarkEventMaximumMediaPipeAverageEyeBlink = _landmarkEventAggregate.MaximumMediaPipeAverageEyeBlinkPercent,
                LandmarkEventMaximumMediaPipeJawOpen = _landmarkEventAggregate.MaximumMediaPipeJawOpenPercent,
                LandmarkEventMinimumMediaPipeMouthClose = _landmarkEventAggregate.MinimumMediaPipeMouthClosePercent,
                LandmarkEventMaximumMediaPipeBlinkChange = _landmarkEventAggregate.MaximumMediaPipeBlinkChangePercent,
                LandmarkEventMaximumMediaPipeJawOpenChange = _landmarkEventAggregate.MaximumMediaPipeJawOpenChangePercent,
                LandmarkEventMaximumMediaPipeMouthCloseDrop = _landmarkEventAggregate.MaximumMediaPipeMouthCloseDropPercent,
                LandmarkEventMaximumMediaPipeMouthOpeningEvidence = _landmarkEventAggregate.MaximumMediaPipeMouthOpeningEvidencePercent,
                LandmarkEventMediaPipeEyeOpeningCorrectedSamples = _landmarkEventAggregate.MediaPipeEyeOpeningCorrectedSamples,
                LandmarkEventMediaPipeMouthOpeningCorrectedSamples = _landmarkEventAggregate.MediaPipeMouthOpeningCorrectedSamples,
                LandmarkEventMaximumAbsoluteMediaPipeEyeOpeningCorrection = _landmarkEventAggregate.MaximumAbsoluteMediaPipeEyeOpeningCorrection,
                LandmarkEventMaximumAbsoluteMediaPipeMouthOpeningCorrection = _landmarkEventAggregate.MaximumAbsoluteMediaPipeMouthOpeningCorrection,
                LandmarkEventMaximumCueScore = _landmarkEventAggregate.MaximumLandmarkCueScore,
                LandmarkEventMaximumEyeClosingTrend = _landmarkEventAggregate.MaximumEyeClosingTrendPercent,
                LandmarkEventMaximumMouthOpeningTrend = _landmarkEventAggregate.MaximumMouthOpeningTrendPercent,
                LandmarkEventMinimumEyeOpeningSlope = _landmarkEventAggregate.MinimumEyeOpeningSlopePerSecond,
                LandmarkEventMaximumMouthOpeningSlope = _landmarkEventAggregate.MaximumMouthOpeningSlopePerSecond,
                LandmarkEventMaximumTrendScore = _landmarkEventAggregate.MaximumLandmarkTrendScore,
                LandmarkEventMaximumEyeGlare = _landmarkEventAggregate.MaximumEyeGlarePercent,
                LandmarkEventMaximumMouthGlare = _landmarkEventAggregate.MaximumMouthGlarePercent,
                LandmarkEventMinimumEyeContrast = _landmarkEventAggregate.MinimumEyeContrastPercent,
                LandmarkEventMinimumMouthContrast = _landmarkEventAggregate.MinimumMouthContrastPercent,
                LandmarkEventMinimumEyeSharpness = _landmarkEventAggregate.MinimumEyeSharpnessPercent,
                LandmarkEventMinimumMouthSharpness = _landmarkEventAggregate.MinimumMouthSharpnessPercent,
                LandmarkEventMaximumRawEyeAsymmetry = _landmarkEventAggregate.MaximumRawEyeAsymmetryPercent,
                LandmarkEventMaximumEyeAsymmetry = _landmarkEventAggregate.MaximumEyeAsymmetryPercent,
                LandmarkEventPossibleOneEyeArtifactSamples = _landmarkEventAggregate.PossibleOneEyeArtifactSamples,
                LandmarkEventLeftEyeReconstructedSamples = _landmarkEventAggregate.LeftEyeReconstructedSamples,
                LandmarkEventRightEyeReconstructedSamples = _landmarkEventAggregate.RightEyeReconstructedSamples,
                LandmarkEventMouthReconstructedSamples = _landmarkEventAggregate.MouthReconstructedSamples,
                LandmarkEventEyeArtifactSuppressedSamples = _landmarkEventAggregate.EyeArtifactSuppressedSamples,
                VideoFile = _activeEventVideo,
                VideoOverlayBurnedIn = !string.IsNullOrWhiteSpace(_activeEventVideo),
                PreEventVideoSeconds = PreEventVideoSeconds,
                LandmarkTimelineSamples = _landmarkEventTimeline.Count,
                LandmarkTimelineJsonFile = timelineFiles.JsonPath,
                LandmarkTimelineCsvFile = timelineFiles.CsvPath,
                StartSnapshot = _episodeStartSnapshot,
                EndSnapshot = endSnapshot,
                OutputFolder = _activeEventFolder
            };

            var jsonPath = Path.Combine(_activeEventFolder, "event_summary.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

            var csvPath = Path.Combine(_activeEventFolder, "event_summary.csv");
            var builder = new StringBuilder();
            builder.AppendLine("Started,Ended,DurationSeconds,EndReason,TriggerReasons,AverageMotion,FaceCueStatus,FaceCueQuality,FaceCueScore,EyeOpenness,EyeDrop,EyeAsymmetry,JawChange,JawAsymmetry,LowerFaceDrop,HeadDrift,LandmarkSource,LandmarkConfidence,LandmarkTrackingConfidence,LandmarkEyeConfidence,LandmarkMouthConfidence,LandmarkEyeQuality,LandmarkMouthQuality,LandmarkOverallQuality,CaptureQualityLabel,CaptureQualityScore,CaptureQualityCanCollect,CaptureQualityAvatarGrade,CaptureQualityReason,CaptureQualityCameraModeScore,CaptureQualityFaceScaleScore,CaptureQualityEyeScore,CaptureQualityMouthScore,CaptureQualityStabilityScore,CaptureQualityGlassesScore,CaptureQualityStorageScore,CaptureQualityFaceWidth,CaptureQualityFaceHeight,CaptureQualityIssues,CaptureQualitySuggestions,LandmarkFaceReliabilityStatus,LandmarkFaceReliabilitySamples,LandmarkFaceReliability,LandmarkFaceContinuity,LandmarkEyeReliability,LandmarkMouthReliability,LandmarkFaceBoundsRate,LandmarkEyeUsableRate,LandmarkMouthUsableRate,LandmarkEyeImageQualityAvailable,LandmarkMouthImageQualityAvailable,LandmarkEyeGlare,LandmarkMouthGlare,LandmarkEyeContrast,LandmarkMouthContrast,LandmarkEyeSharpness,LandmarkMouthSharpness,LandmarkEyeDarkCoverage,LandmarkMouthDarkCoverage,LandmarkRawEyeAsymmetry,LandmarkEyeAsymmetry,LandmarkEyeAgreement,LandmarkPossibleOneEyeArtifact,LandmarkLeftEyeReconstructed,LandmarkRightEyeReconstructed,LandmarkMouthReconstructed,LandmarkEyeArtifactSuppressed,LandmarkRawEyeOpening,LandmarkRawMouthOpening,LandmarkRawJawDroop,LandmarkEyeOpening,LandmarkMouthOpening,LandmarkMouthOpeningVelocity,LandmarkJawDroop,LandmarkJawDroopVelocity,LandmarkMediaPipeLeftEyeBlink,LandmarkMediaPipeRightEyeBlink,LandmarkMediaPipeAverageEyeBlink,LandmarkMediaPipeJawOpen,LandmarkMediaPipeMouthClose,LandmarkMediaPipeEyeOpeningCorrection,LandmarkMediaPipeMouthOpeningCorrection,LandmarkMediaPipeEyeOpeningCorrected,LandmarkMediaPipeMouthOpeningCorrected,LandmarkMediaPipeBlinkBaselineReady,LandmarkMediaPipeMouthBaselineReady,LandmarkMediaPipeBlinkBaseline,LandmarkMediaPipeJawOpenBaseline,LandmarkMediaPipeMouthCloseBaseline,LandmarkMediaPipeBlinkChange,LandmarkMediaPipeJawOpenChange,LandmarkMediaPipeMouthCloseDrop,LandmarkMediaPipeMouthOpeningEvidence,LandmarkCueStatus,LandmarkCueScore,LandmarkEyeCueEligible,LandmarkMouthCueEligible,LandmarkEyeClosure,LandmarkMouthOpeningChange,LandmarkJawDroopBaseline,LandmarkJawDroopChange,LandmarkTrendStatus,LandmarkTrendScore,LandmarkTrendWindowSeconds,LandmarkEyeClosingTrend,LandmarkMouthOpeningTrend,LandmarkEyeOpeningSlope,LandmarkMouthOpeningSlope,LandmarkEventSamples,LandmarkEventSources,LandmarkEventBackendStatuses,LandmarkEventMinimumEyeQuality,LandmarkEventMinimumMouthQuality,LandmarkEventMinimumOverallQuality,LandmarkEventAverageOverallQuality,LandmarkEventCaptureQualitySamples,LandmarkEventCaptureQualityCanCollectSamples,LandmarkEventCaptureQualityAvatarGradeSamples,LandmarkEventMinimumCaptureQualityScore,LandmarkEventMaximumCaptureQualityScore,LandmarkEventAverageCaptureQualityScore,LandmarkEventCaptureQualityLabels,LandmarkEventCaptureQualityIssues,LandmarkEventFaceReliabilitySamples,LandmarkEventFaceReliabilityUsableSamples,LandmarkEventMinimumFaceReliability,LandmarkEventAverageFaceReliability,LandmarkEventMinimumFaceContinuity,LandmarkEventAverageFaceContinuity,LandmarkEventMinimumEyeReliability,LandmarkEventAverageEyeReliability,LandmarkEventMinimumMouthReliability,LandmarkEventAverageMouthReliability,LandmarkEventMinimumEyeOpening,LandmarkEventMaximumEyeClosure,LandmarkEventMaximumMouthOpening,LandmarkEventMaximumMouthOpeningChange,LandmarkEventMaximumMouthOpeningVelocity,LandmarkEventMaximumJawDroop,LandmarkEventMaximumJawDroopChange,LandmarkEventMaximumJawDroopVelocity,LandmarkEventMaximumMediaPipeAverageEyeBlink,LandmarkEventMaximumMediaPipeJawOpen,LandmarkEventMinimumMediaPipeMouthClose,LandmarkEventMaximumMediaPipeBlinkChange,LandmarkEventMaximumMediaPipeJawOpenChange,LandmarkEventMaximumMediaPipeMouthCloseDrop,LandmarkEventMaximumMediaPipeMouthOpeningEvidence,LandmarkEventMediaPipeEyeOpeningCorrectedSamples,LandmarkEventMediaPipeMouthOpeningCorrectedSamples,LandmarkEventMaximumAbsoluteMediaPipeEyeOpeningCorrection,LandmarkEventMaximumAbsoluteMediaPipeMouthOpeningCorrection,LandmarkEventMaximumCueScore,LandmarkEventMaximumEyeClosingTrend,LandmarkEventMaximumMouthOpeningTrend,LandmarkEventMinimumEyeOpeningSlope,LandmarkEventMaximumMouthOpeningSlope,LandmarkEventMaximumTrendScore,LandmarkEventMaximumEyeGlare,LandmarkEventMaximumMouthGlare,LandmarkEventMinimumEyeContrast,LandmarkEventMinimumMouthContrast,LandmarkEventMinimumEyeSharpness,LandmarkEventMinimumMouthSharpness,LandmarkEventMaximumRawEyeAsymmetry,LandmarkEventMaximumEyeAsymmetry,LandmarkEventPossibleOneEyeArtifactSamples,LandmarkEventLeftEyeReconstructedSamples,LandmarkEventRightEyeReconstructedSamples,LandmarkEventMouthReconstructedSamples,LandmarkEventEyeArtifactSuppressedSamples,VideoFile,VideoOverlayBurnedIn,PreEventVideoSeconds,LandmarkTimelineSamples,LandmarkTimelineJsonFile,LandmarkTimelineCsvFile,StartSnapshot,EndSnapshot,OutputFolder");
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
                Csv(_currentFaceLandmarkMetrics.Source),
                Csv(_currentFaceLandmarkMetrics.ConfidenceLabel),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.TrackingConfidence)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.EyeConfidence)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MouthConfidence)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.EyeMeasurementQualityPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MouthMeasurementQualityPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.OverallMeasurementQualityPercent)),
                Csv(_currentPersonalFaceCaptureQuality.Label),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.ScorePercent)),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.CanCollectMeasurements)),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.StrongEnoughForAvatarLearning)),
                Csv(_currentPersonalFaceCaptureQuality.PrimaryReason),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.CameraModeScorePercent)),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.FaceScaleScorePercent)),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.EyeEvidenceScorePercent)),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.MouthEvidenceScorePercent)),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.StabilityScorePercent)),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.GlassesRiskScorePercent)),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.StorageScorePercent)),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.FaceWidthPercent)),
                Csv(FormatOptional(_currentPersonalFaceCaptureQuality.FaceHeightPercent)),
                Csv(string.Join("; ", _currentPersonalFaceCaptureQuality.Issues)),
                Csv(string.Join("; ", _currentPersonalFaceCaptureQuality.Suggestions)),
                Csv(_currentFaceLockStabilityAnalysis.Status),
                Csv(_currentFaceLockStabilityAnalysis.SampleCount.ToString(CultureInfo.InvariantCulture)),
                Csv(FormatOptional(_currentFaceLockStabilityAnalysis.CompositeReliabilityPercent)),
                Csv(FormatOptional(_currentFaceLockStabilityAnalysis.FaceContinuityPercent)),
                Csv(FormatOptional(_currentFaceLockStabilityAnalysis.EyeReliabilityPercent)),
                Csv(FormatOptional(_currentFaceLockStabilityAnalysis.MouthReliabilityPercent)),
                Csv(FormatOptional(_currentFaceLockStabilityAnalysis.FaceBoundsRatePercent)),
                Csv(FormatOptional(_currentFaceLockStabilityAnalysis.EyeUsableRatePercent)),
                Csv(FormatOptional(_currentFaceLockStabilityAnalysis.MouthUsableRatePercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.EyeImageQualityAvailable)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MouthImageQualityAvailable)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.EyeGlarePercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MouthGlarePercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.EyeContrastPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MouthContrastPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.EyeSharpnessPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MouthSharpnessPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.EyeDarkCoveragePercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MouthDarkCoveragePercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.RawEyeAsymmetryPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.EyeAsymmetryPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.EyeAgreementPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.PossibleOneEyeArtifact)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.LeftEyeReconstructed)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.RightEyeReconstructed)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MouthReconstructed)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.EyeArtifactSuppressed)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.RawAverageEyeOpeningRatio)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.RawMouthOpeningRatio)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.RawJawDroopRatio)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.AverageEyeOpeningRatio)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MouthOpeningRatio)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MouthOpeningVelocityPerSecond)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.JawDroopRatio)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.JawDroopVelocityPerSecond)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MediaPipeLeftEyeBlinkPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MediaPipeRightEyeBlinkPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MediaPipeAverageEyeBlinkPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MediaPipeJawOpenPercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MediaPipeMouthClosePercent)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MediaPipeEyeOpeningCorrectionRatio)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MediaPipeMouthOpeningCorrectionRatio)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MediaPipeEyeOpeningCorrected)),
                Csv(FormatOptional(_currentFaceLandmarkMetrics.MediaPipeMouthOpeningCorrected)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MediaPipeBlinkBaselineReady)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MediaPipeMouthBaselineReady)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MediaPipeBlinkBaselinePercent)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MediaPipeJawOpenBaselinePercent)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MediaPipeMouthCloseBaselinePercent)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MediaPipeBlinkChangePercent)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MediaPipeJawOpenChangePercent)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MediaPipeMouthCloseDropPercent)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MediaPipeMouthOpeningEvidencePercent)),
                Csv(_currentFaceLandmarkCueAnalysis?.Status ?? ""),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.CompositeCuePercent)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.EyeCueEligible)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MouthCueEligible)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.EyeClosurePercent)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.MouthOpeningChangePercent)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.JawDroopBaselineRatio)),
                Csv(FormatOptional(_currentFaceLandmarkCueAnalysis?.JawDroopChangePercent)),
                Csv(_currentFaceLandmarkTrendAnalysis.Status),
                Csv(FormatOptional(_currentFaceLandmarkTrendAnalysis.TrendCuePercent)),
                Csv(FormatOptional(_currentFaceLandmarkTrendAnalysis.WindowSeconds)),
                Csv(FormatOptional(_currentFaceLandmarkTrendAnalysis.EyeClosingTrendPercent)),
                Csv(FormatOptional(_currentFaceLandmarkTrendAnalysis.MouthOpeningTrendPercent)),
                Csv(FormatOptional(_currentFaceLandmarkTrendAnalysis.EyeOpeningSlopePerSecond)),
                Csv(FormatOptional(_currentFaceLandmarkTrendAnalysis.MouthOpeningSlopePerSecond)),
                Csv(_landmarkEventAggregate.SampleCount.ToString(CultureInfo.InvariantCulture)),
                Csv(string.Join("; ", _landmarkEventAggregate.Sources)),
                Csv(string.Join("; ", _landmarkEventAggregate.BackendStatuses)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumEyeQualityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumMouthQualityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumOverallQualityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.AverageOverallQualityPercent)),
                Csv(_landmarkEventAggregate.CaptureQualitySamples.ToString(CultureInfo.InvariantCulture)),
                Csv(_landmarkEventAggregate.CaptureQualityCanCollectSamples.ToString(CultureInfo.InvariantCulture)),
                Csv(_landmarkEventAggregate.CaptureQualityAvatarGradeSamples.ToString(CultureInfo.InvariantCulture)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumCaptureQualityScore)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumCaptureQualityScore)),
                Csv(FormatOptional(_landmarkEventAggregate.AverageCaptureQualityScore)),
                Csv(string.Join("; ", _landmarkEventAggregate.CaptureQualityLabels)),
                Csv(string.Join("; ", _landmarkEventAggregate.CaptureQualityIssues)),
                Csv(_landmarkEventAggregate.FaceReliabilitySamples.ToString(CultureInfo.InvariantCulture)),
                Csv(_landmarkEventAggregate.FaceReliabilityUsableSamples.ToString(CultureInfo.InvariantCulture)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumFaceReliabilityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.AverageFaceReliabilityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumFaceContinuityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.AverageFaceContinuityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumEyeReliabilityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.AverageEyeReliabilityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumMouthReliabilityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.AverageMouthReliabilityPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumEyeOpeningRatio)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumEyeClosurePercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMouthOpeningRatio)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMouthOpeningChangePercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMouthOpeningVelocityPerSecond)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumJawDroopRatio)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumJawDroopChangePercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumJawDroopVelocityPerSecond)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMediaPipeAverageEyeBlinkPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMediaPipeJawOpenPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumMediaPipeMouthClosePercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMediaPipeBlinkChangePercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMediaPipeJawOpenChangePercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMediaPipeMouthCloseDropPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMediaPipeMouthOpeningEvidencePercent)),
                Csv(_landmarkEventAggregate.MediaPipeEyeOpeningCorrectedSamples.ToString(CultureInfo.InvariantCulture)),
                Csv(_landmarkEventAggregate.MediaPipeMouthOpeningCorrectedSamples.ToString(CultureInfo.InvariantCulture)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumAbsoluteMediaPipeEyeOpeningCorrection)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumAbsoluteMediaPipeMouthOpeningCorrection)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumLandmarkCueScore)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumEyeClosingTrendPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMouthOpeningTrendPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumEyeOpeningSlopePerSecond)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMouthOpeningSlopePerSecond)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumLandmarkTrendScore)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumEyeGlarePercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumMouthGlarePercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumEyeContrastPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumMouthContrastPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumEyeSharpnessPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MinimumMouthSharpnessPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumRawEyeAsymmetryPercent)),
                Csv(FormatOptional(_landmarkEventAggregate.MaximumEyeAsymmetryPercent)),
                Csv(_landmarkEventAggregate.PossibleOneEyeArtifactSamples.ToString(CultureInfo.InvariantCulture)),
                Csv(_landmarkEventAggregate.LeftEyeReconstructedSamples.ToString(CultureInfo.InvariantCulture)),
                Csv(_landmarkEventAggregate.RightEyeReconstructedSamples.ToString(CultureInfo.InvariantCulture)),
                Csv(_landmarkEventAggregate.MouthReconstructedSamples.ToString(CultureInfo.InvariantCulture)),
                Csv(_landmarkEventAggregate.EyeArtifactSuppressedSamples.ToString(CultureInfo.InvariantCulture)),
                Csv(_activeEventVideo),
                Csv((!string.IsNullOrWhiteSpace(_activeEventVideo)).ToString()),
                Csv(PreEventVideoSeconds.ToString(CultureInfo.InvariantCulture)),
                Csv(_landmarkEventTimeline.Count.ToString(CultureInfo.InvariantCulture)),
                Csv(timelineFiles.JsonPath),
                Csv(timelineFiles.CsvPath),
                Csv(_episodeStartSnapshot),
                Csv(endSnapshot),
                Csv(_activeEventFolder)
            ]));
            File.WriteAllText(csvPath, builder.ToString(), Encoding.UTF8);
            return (jsonPath, csvPath, timelineFiles.JsonPath, timelineFiles.CsvPath);
        }
        catch (Exception ex)
        {
            MonitorStatusText.Text = $"Episode ended, but summary failed: {ex.Message}";
            return ("", "", "", "");
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

    private void UpdateAlertBaselineCalibrationStatus()
    {
        if (!_alertBaselineCalibrationActive || !IsLoaded)
        {
            return;
        }

        var regionReady = _currentFaceAnalysis?.BaselineReady == true;
        var landmarkReady = _currentFaceLandmarkCueAnalysis?.BaselineReady == true;
        if (regionReady || landmarkReady)
        {
            _alertBaselineCalibrationActive = false;
            CalibrateAlertBaselineButton.Content = AlertBaselineStartButtonText;
            CalibrateAlertBaselineButton.IsEnabled = true;
            var saved = SaveAlertBaselineToOutputFolder();
            var saveText = saved
                ? $" Saved to {GetAlertBaselinePath()}."
                : " The baseline is active for this session, but could not be saved to the output folder.";
            CalibrationGuardText.Text = saved
                ? GetAlertBaselineStatusText()
                : "Alert baseline ready for this session. Check output folder permissions if you want it reused next time.";
            MonitorStatusText.Text = $"Alert baseline ready. Eye, mouth, and jaw cues now have an alert reference.{saveText}";
            return;
        }

        CalibrateAlertBaselineButton.Content = AlertBaselineInProgressButtonText;
        CalibrateAlertBaselineButton.IsEnabled = false;
        var progress = GetAlertBaselineProgressText();
        CalibrationGuardText.Text = $"Calibrating alert baseline: {progress}. Stay alert, symptom-free, and naturally relaxed.";
        MonitorStatusText.Text = $"Calibrating alert baseline: {progress}. Keep eyes naturally open and mouth relaxed.";
    }

    private string GetAlertBaselineProgressText()
    {
        var parts = new List<string>();
        if (_currentFaceAnalysis is FaceCueAnalysis regionAnalysis)
        {
            parts.Add($"fallback {Math.Min(regionAnalysis.BaselineSamples, 30)}/30");
        }
        else
        {
            parts.Add("fallback waiting");
        }

        if (_currentFaceLandmarkCueAnalysis is { HasUsableMeasurements: true } landmarkAnalysis)
        {
            parts.Add($"landmarks {Math.Min(landmarkAnalysis.BaselineSamples, 20)}/20");
        }
        else
        {
            parts.Add("landmarks waiting for face lock");
        }

        return string.Join(", ", parts);
    }

    private void MarkSymptomActivity(DateTime timestamp, string message)
    {
        CancelActiveAlertBaselineCalibration();
        _lastSymptomAt = timestamp;
        _calibrationHoldUntil = timestamp + CalibrationSymptomFreeWindow;
        UpdateCalibrationGuard();
        MonitorStatusText.Text = message;
    }

    private void CancelActiveAlertBaselineCalibration()
    {
        if (!_alertBaselineCalibrationActive)
        {
            return;
        }

        _alertBaselineCalibrationActive = false;
        _faceCueAnalyzer.Reset();
        _faceLandmarkCueAnalyzer.Reset();
        _currentFaceAnalysis = null;
        _currentFaceLandmarkCueAnalysis = null;
        CalibrateAlertBaselineButton.Content = AlertBaselineStartButtonText;
        LoadAlertBaselineFromOutputFolder(showStatus: false);
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
            CalibrateAlertBaselineButton.IsEnabled = false;
            CalibrateAlertBaselineButton.Content = AlertBaselineStartButtonText;
            CalibrationGuardText.Text = $"Alert baseline locked: wait {FormatRemaining(remaining)} symptom-free. Last symptom marker: {_lastSymptomAt:g}.";
            return;
        }

        if (_alertBaselineCalibrationActive)
        {
            CalibrateAlertBaselineButton.IsEnabled = false;
            CalibrateAlertBaselineButton.Content = AlertBaselineInProgressButtonText;
            return;
        }

        CalibrateAlertBaselineButton.IsEnabled = true;
        CalibrateAlertBaselineButton.Content = AlertBaselineStartButtonText;
        CalibrationGuardText.Text = GetAlertBaselineStatusText();
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
        var now = DateTime.UtcNow;
        var accentChanged = !string.Equals(accentColor, _lastTrackingOverlayAccentColor, StringComparison.OrdinalIgnoreCase);
        var stateChanged = !string.Equals(state, _lastTrackingOverlayState, StringComparison.Ordinal)
            || !string.Equals(trigger, _lastTrackingOverlayTrigger, StringComparison.Ordinal)
            || accentChanged;
        var metricsChanged = !string.Equals(metrics, _lastTrackingOverlayMetrics, StringComparison.Ordinal);
        if (!stateChanged
            && metricsChanged
            && now - _lastTrackingOverlayUpdateAtUtc < TrackingOverlayRefreshInterval)
        {
            return;
        }

        if (!stateChanged && !metricsChanged)
        {
            return;
        }

        _lastTrackingOverlayUpdateAtUtc = now;
        _lastTrackingOverlayState = state;
        _lastTrackingOverlayMetrics = metrics;
        _lastTrackingOverlayTrigger = trigger;
        _lastTrackingOverlayAccentColor = accentColor;

        var availableWidth = Math.Max(360d, PreviewHost.ActualWidth - 36d);
        TrackingOverlay.MaxWidth = Math.Min(860d, availableWidth);
        if (!string.Equals(OverlayStateText.Text, state, StringComparison.Ordinal))
        {
            OverlayStateText.Text = state;
        }

        if (!string.Equals(OverlayMetricText.Text, metrics, StringComparison.Ordinal))
        {
            OverlayMetricText.Text = metrics;
        }

        if (!string.Equals(OverlayTriggerText.Text, trigger, StringComparison.Ordinal))
        {
            OverlayTriggerText.Text = trigger;
        }

        if (_lastTrackingOverlayAccentBrush is null || accentChanged)
        {
            _lastTrackingOverlayAccentBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(accentColor)!;
            _lastTrackingOverlayAccentBrush.Freeze();
        }

        TrackingOverlay.BorderBrush = _lastTrackingOverlayAccentBrush;
    }

    private void DrawLiveWireframePreview()
    {
        if (LiveWireframeCanvas is null)
        {
            return;
        }

        LiveWireframeCanvas.Children.Clear();
        var width = Math.Max(1d, LiveWireframeCanvas.ActualWidth);
        var height = Math.Max(1d, LiveWireframeCanvas.ActualHeight);
        var sample = _lastGoodFeatureMeshSamples.Count > 0 ? _lastGoodFeatureMeshSamples[^1] : null;
        if (sample is null)
        {
            AddWireframeText(
                "Live wireframe waiting",
                "Confirm Chris and let the dense tracker capture a good eye, mouth, and brow lock.",
                18,
                18);
            return;
        }

        var projection = BuildLiveWireframeProjection(sample, width, height);
        var pointMap = projection.Points;
        var surfaceBrush = CreateFrozenBrush(0x2f, 0x6c, 0x8f, 0x92);
        foreach (var edge in sample.WireframeEdges.Where(static edge => edge.Role == "surface"))
        {
            DrawWireframeEdge(edge, pointMap, width, height, surfaceBrush, 0.72d);
        }

        foreach (var edge in sample.WireframeEdges.Where(static edge => edge.Role != "surface"))
        {
            DrawWireframeEdge(edge, pointMap, width, height, BrushForWireframeRole(edge.Role), 1.75d);
        }

        var featureIndexes = sample.FeatureGroups
            .SelectMany(static group => group.LandmarkIndices)
            .ToHashSet();
        var pointBrush = CreateFrozenBrush(0xdc, 0xef, 0xff, 0xb8);
        foreach (var point in sample.Points)
        {
            if (!pointMap.TryGetValue(point.Index, out var projectedPoint))
            {
                continue;
            }

            var dotSize = featureIndexes.Contains(point.Index) ? 3.2d : 2.0d;
            var ellipse = new Ellipse
            {
                Width = dotSize,
                Height = dotSize,
                Fill = pointBrush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(ellipse, projectedPoint.X - dotSize / 2d);
            Canvas.SetTop(ellipse, projectedPoint.Y - dotSize / 2d);
            LiveWireframeCanvas.Children.Add(ellipse);
        }

        if (projection.HeadLocked)
        {
            DrawLiveWireframeHeadAxes(projection);
        }

        AddWireframeText(
            $"Live wireframe: {sample.PointCount} points, {sample.WireframeEdges.Count} edges",
            $"{projection.Mode}. Quality {sample.OverallQualityPercent:0}% | eyes {sample.EyeQualityPercent:0}% | brows {sample.BrowQualityPercent:0}% ({FormatRatioPercent(sample.AverageBrowHeightRatio)}) | mouth {sample.MouthQualityPercent:0}% | A/B/C {sample.HeadPitchDegrees:0}/{sample.HeadYawDegrees:0}/{sample.HeadRollDegrees:0} | B lock {_lastGoodFeatureMeshStability.YawStatus} ({_lastGoodFeatureMeshStability.YawRangeDegrees:0.#} deg)",
            18,
            18);
    }

    private void DrawWireframeEdge(
        LastGoodFeatureMeshWireframeEdge edge,
        IReadOnlyDictionary<int, LiveWireframeProjectedPoint> points,
        double width,
        double height,
        Brush brush,
        double thickness)
    {
        if (!points.TryGetValue(edge.FromIndex, out var from)
            || !points.TryGetValue(edge.ToIndex, out var to))
        {
            return;
        }

        var line = new Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            Stroke = brush,
            StrokeThickness = thickness,
            IsHitTestVisible = false
        };
        LiveWireframeCanvas.Children.Add(line);
    }

    private LiveWireframeProjection BuildLiveWireframeProjection(
        LastGoodFeatureMeshSample sample,
        double width,
        double height)
    {
        if (LiveWireframeHeadLockCheckBox?.IsChecked == true
            && TryBuildHeadLockedLiveWireframeProjection(sample, width, height, out var headLockedProjection))
        {
            return headLockedProjection;
        }

        var points = sample.Points.ToDictionary(
            static point => point.Index,
            point => new LiveWireframeProjectedPoint(
                point.Index,
                Math.Clamp(point.X, 0d, 1d) * width,
                Math.Clamp(point.Y, 0d, 1d) * height,
                point.Z));
        var mode = LiveWireframeHeadLockCheckBox?.IsChecked == true
            ? "Camera-normalized live wireframe fallback"
            : "Camera-normalized live wireframe";
        return new LiveWireframeProjection(
            points,
            mode,
            HeadLocked: false,
            AxisOriginX: width * 0.5d,
            AxisOriginY: height * 0.5d,
            AxisUnitPixels: Math.Min(width, height) * 0.20d);
    }

    private bool TryBuildHeadLockedLiveWireframeProjection(
        LastGoodFeatureMeshSample sample,
        double width,
        double height,
        out LiveWireframeProjection projection)
    {
        projection = new LiveWireframeProjection(
            new Dictionary<int, LiveWireframeProjectedPoint>(),
            "Camera-normalized live wireframe fallback",
            HeadLocked: false,
            AxisOriginX: width * 0.5d,
            AxisOriginY: height * 0.5d,
            AxisUnitPixels: Math.Min(width, height) * 0.20d);

        var rawPoints = sample.Points.ToDictionary(static point => point.Index, ToMeshPoint);
        if (!TryGetFeatureCenter(sample, rawPoints, "left_eye", DenseMeshEyeA, out var leftEye)
            || !TryGetFeatureCenter(sample, rawPoints, "right_eye", DenseMeshEyeB, out var rightEye)
            || !TryGetFeatureCenter(sample, rawPoints, "jaw", DenseMeshJawCenter, out var chin))
        {
            return false;
        }

        var eyeMid = Multiply(Add(leftEye, rightEye), 0.5d);
        var xAxis = Subtract(rightEye, leftEye);
        var interEyeDistance = Length(xAxis);
        if (interEyeDistance < 0.0001d)
        {
            return false;
        }

        xAxis = Normalize(xAxis);
        var yAxis = Subtract(chin, eyeMid);
        yAxis = Subtract(yAxis, Multiply(xAxis, Dot(yAxis, xAxis)));
        if (Length(yAxis) < 0.0001d)
        {
            return false;
        }

        yAxis = Normalize(yAxis);
        var zAxis = Normalize(Cross(xAxis, yAxis));
        if (Length(zAxis) < 0.0001d)
        {
            return false;
        }

        yAxis = Normalize(Cross(zAxis, xAxis));
        var faceHeight = Distance(eyeMid, chin);
        var faceScale = Math.Max(0.0001d, Math.Max(interEyeDistance * 2.35d, faceHeight * 1.28d));
        var screenScale = Math.Min(width, height) * 1.08d;
        var originX = width * 0.5d;
        var originY = height * 0.38d;
        var projectedPoints = new Dictionary<int, LiveWireframeProjectedPoint>(rawPoints.Count);

        foreach (var (index, point) in rawPoints)
        {
            var relative = Subtract(point, eyeMid);
            var localX = Dot(relative, xAxis) / faceScale;
            var localY = Dot(relative, yAxis) / faceScale;
            var localZ = Dot(relative, zAxis) / faceScale;
            projectedPoints[index] = new LiveWireframeProjectedPoint(
                index,
                originX + localX * screenScale,
                originY + localY * screenScale,
                localZ);
        }

        projection = new LiveWireframeProjection(
            projectedPoints,
            $"Head-locked live wireframe | eye scale {interEyeDistance * 100d:0.#}%",
            HeadLocked: true,
            AxisOriginX: originX,
            AxisOriginY: originY,
            AxisUnitPixels: screenScale);
        return true;
    }

    private void DrawLiveWireframeHeadAxes(LiveWireframeProjection projection)
    {
        var xBrush = CreateFrozenBrush(0x65, 0xc8, 0xff, 0xe8);
        var yBrush = CreateFrozenBrush(0xff, 0xd1, 0x66, 0xe8);
        var axisLength = Math.Max(28d, projection.AxisUnitPixels * 0.16d);
        AddLiveWireframeAxis(projection.AxisOriginX, projection.AxisOriginY, projection.AxisOriginX + axisLength, projection.AxisOriginY, xBrush);
        AddLiveWireframeAxis(projection.AxisOriginX, projection.AxisOriginY, projection.AxisOriginX, projection.AxisOriginY + axisLength, yBrush);
    }

    private void AddLiveWireframeAxis(double x1, double y1, double x2, double y2, Brush brush)
    {
        LiveWireframeCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = 2d,
            IsHitTestVisible = false
        });
    }

    private static bool TryGetFeatureCenter(
        LastGoodFeatureMeshSample sample,
        IReadOnlyDictionary<int, MeshPoint3D> points,
        string featureGroupId,
        IReadOnlyList<int> fallbackIndices,
        out MeshPoint3D center)
    {
        var group = sample.FeatureGroups.FirstOrDefault(group =>
            string.Equals(group.Id, featureGroupId, StringComparison.OrdinalIgnoreCase));
        if (group is not null && TryGetCenter(points, group.LandmarkIndices, out center))
        {
            return true;
        }

        return TryGetCenter(points, fallbackIndices, out center);
    }

    private static bool TryGetCenter(
        IReadOnlyDictionary<int, MeshPoint3D> points,
        IReadOnlyList<int> indices,
        out MeshPoint3D center)
    {
        var values = indices
            .Where(points.ContainsKey)
            .Select(index => points[index])
            .ToList();
        if (values.Count == 0)
        {
            center = default;
            return false;
        }

        center = new MeshPoint3D(
            values.Average(static point => point.X),
            values.Average(static point => point.Y),
            values.Average(static point => point.Z));
        return true;
    }

    private static MeshPoint3D ToMeshPoint(FaceMeshLandmarkPoint point)
    {
        return new MeshPoint3D(point.X, point.Y, point.Z);
    }

    private static MeshPoint3D Add(MeshPoint3D first, MeshPoint3D second)
    {
        return new MeshPoint3D(first.X + second.X, first.Y + second.Y, first.Z + second.Z);
    }

    private static MeshPoint3D Subtract(MeshPoint3D first, MeshPoint3D second)
    {
        return new MeshPoint3D(first.X - second.X, first.Y - second.Y, first.Z - second.Z);
    }

    private static MeshPoint3D Multiply(MeshPoint3D point, double scale)
    {
        return new MeshPoint3D(point.X * scale, point.Y * scale, point.Z * scale);
    }

    private static double Dot(MeshPoint3D first, MeshPoint3D second)
    {
        return first.X * second.X + first.Y * second.Y + first.Z * second.Z;
    }

    private static MeshPoint3D Cross(MeshPoint3D first, MeshPoint3D second)
    {
        return new MeshPoint3D(
            first.Y * second.Z - first.Z * second.Y,
            first.Z * second.X - first.X * second.Z,
            first.X * second.Y - first.Y * second.X);
    }

    private static double Length(MeshPoint3D point)
    {
        return Math.Sqrt(Dot(point, point));
    }

    private static double Distance(MeshPoint3D first, MeshPoint3D second)
    {
        return Length(Subtract(first, second));
    }

    private static MeshPoint3D Normalize(MeshPoint3D point)
    {
        var length = Math.Max(0.000001d, Length(point));
        return Multiply(point, 1d / length);
    }

    private void AddWireframeText(string title, string detail, double left, double top)
    {
        var panel = new StackPanel
        {
            Background = CreateFrozenBrush(0x08, 0x0d, 0x12, 0xdc),
            IsHitTestVisible = false
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(10, 8, 10, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail,
            Foreground = new SolidColorBrush(Color.FromRgb(185, 215, 239)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 4, 10, 8),
            MaxWidth = 760
        });
        Canvas.SetLeft(panel, left);
        Canvas.SetTop(panel, top);
        LiveWireframeCanvas.Children.Add(panel);
    }

    private static SolidColorBrush BrushForWireframeRole(string role)
    {
        return role switch
        {
            "eye" => CreateFrozenBrush(0x8f, 0xf2, 0xc5, 0xf2),
            "brow" => CreateFrozenBrush(0xc9, 0xf7, 0xa3, 0xf2),
            "mouth" or "mouth-opening" => CreateFrozenBrush(0xff, 0x9f, 0xbd, 0xf2),
            "jaw" => CreateFrozenBrush(0xff, 0xd1, 0x66, 0xf2),
            "nose" => CreateFrozenBrush(0xd9, 0xe8, 0xff, 0xf2),
            "cheek" => CreateFrozenBrush(0xc7, 0xa6, 0xff, 0xf2),
            "forehead" => CreateFrozenBrush(0x9d, 0xb7, 0xc9, 0xf2),
            "face" => CreateFrozenBrush(0x65, 0xc8, 0xff, 0xf2),
            _ => CreateFrozenBrush(0xdc, 0xef, 0xff, 0xe0)
        };
    }

    private void UpdateFaceCueGuideOverlay(BitmapSource? bitmap)
    {
        FaceCueGuideCanvas.Children.Clear();
        if (_showLiveWireframePreview)
        {
            FaceCueGuideCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        FaceCueGuideCanvas.Visibility = Visibility.Visible;
        if (bitmap is null)
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

        if (_currentFaceLandmarkFrame.HasFace)
        {
            AddLandmarkContours(display, _currentFaceLandmarkFrame);
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

    private void AddLandmarkContours(Rect display, FaceLandmarkFrame frame)
    {
        var eyeBrush = new SolidColorBrush(Color.FromArgb(245, 122, 218, 255));
        var inferredEyeBrush = new SolidColorBrush(Color.FromArgb(245, 238, 174, 74));
        var lipBrush = new SolidColorBrush(Color.FromArgb(245, 255, 190, 110));
        var faceBrush = new SolidColorBrush(Color.FromArgb(135, 185, 215, 239));
        var leftEyeInferred = frame.LeftEyeReconstructed || frame.EyeArtifactSuppressed;
        var rightEyeInferred = frame.RightEyeReconstructed || frame.EyeArtifactSuppressed;
        var eyeInferenceBrush = frame.EyeArtifactSuppressed ? inferredEyeBrush : eyeBrush;

        AddGuidePolyline(display, frame.FaceContour, faceBrush, 1.4d, close: true);
        AddGuidePolyline(display, frame.JawContour, faceBrush, 1.8d, close: false);
        AddGuidePolyline(display, frame.LeftEyeContour, leftEyeInferred ? eyeInferenceBrush : eyeBrush, 2.4d, close: true, inferred: leftEyeInferred);
        AddGuidePolyline(display, frame.RightEyeContour, rightEyeInferred ? eyeInferenceBrush : eyeBrush, 2.4d, close: true, inferred: rightEyeInferred);
        AddGuidePolyline(display, frame.OuterLipContour, lipBrush, 2.2d, close: true, inferred: frame.MouthReconstructed);
        AddGuidePolyline(display, frame.InnerLipContour, lipBrush, 1.8d, close: true, inferred: frame.MouthReconstructed);
    }

    private void AddGuidePolyline(Rect display, IReadOnlyList<Point> points, Brush stroke, double thickness, bool close, bool inferred = false)
    {
        if (points.Count < 2)
        {
            return;
        }

        var shape = new Polyline
        {
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        if (inferred)
        {
            shape.StrokeDashArray = CreateInferenceDashArray();
        }

        foreach (var point in points)
        {
            shape.Points.Add(ToDisplayPoint(display, point));
        }

        if (close)
        {
            shape.Points.Add(ToDisplayPoint(display, points[0]));
        }

        FaceCueGuideCanvas.Children.Add(shape);
    }

    private static DoubleCollection CreateInferenceDashArray()
    {
        return new DoubleCollection { 5d, 3d };
    }

    private static Point ToDisplayPoint(Rect display, Point framePoint)
    {
        return new Point(
            display.X + display.Width * framePoint.X,
            display.Y + display.Height * framePoint.Y);
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
        return string.Join(Environment.NewLine, CreateOverlayMetricLines(motion));
    }

    private IReadOnlyList<string> CreateOverlayMetricLines(double? motion)
    {
        var motionLabel = motion is double value
            ? $"Motion {value:0.0}% / {GetMotionThreshold():0.0}%"
            : $"Motion -- / {GetMotionThreshold():0.0}%";

        var lines = new List<string>();
        if (_faceLandmarkTracker.IsAvailable && FaceAutoFollowCheckBox.IsChecked == true)
        {
            var now = DateTime.UtcNow;
            var detectionLabel = GetFaceFeatureTrackerStatus(now);
            lines.Add($"{motionLabel} | {detectionLabel}");
        }
        else
        {
            lines.Add(motionLabel);
        }

        lines.Add(FormatCompactFaceSignalLine(_currentFaceLandmarkMetrics));
        lines.Add(FormatCompactHeadPoseLine(_currentHeadPoseEstimate));
        lines.Add(FormatCueLine(_currentFaceLandmarkCueAnalysis, _currentFaceLandmarkTrendAnalysis, _currentFaceAnalysis));
        lines.Add(FormatCompactAvatarLine());
        return lines;
    }

    private string FormatCompactFaceSignalLine(FaceLandmarkMetrics metrics)
    {
        if (!metrics.HasFace)
        {
            return "Face: waiting for landmarks";
        }

        var eyeLock = metrics.IsEyeMeasurementUsable ? "ok" : "limited";
        var mouthLock = metrics.IsMouthMeasurementUsable ? "ok" : "limited";
        var browLock = metrics.IsBrowMeasurementUsable ? "ok" : "limited";
        var blink = metrics.MediaPipeAverageEyeBlinkPercent is double blinkPercent
            ? $" | blink {blinkPercent:0}%"
            : "";
        var jawOpen = metrics.MediaPipeJawOpenPercent is double jawOpenPercent
            ? $" | MP jaw {jawOpenPercent:0}%"
            : "";
        var artifact = metrics.PossibleOneEyeArtifact
            ? " | eye artifact"
            : metrics.AnyEyeReconstructed || metrics.MouthReconstructed || metrics.EyeArtifactSuppressed
                ? " | reconstructed"
                : "";
        return $"Eyes {FormatRatioPercent(metrics.AverageEyeOpeningRatio)} ({eyeLock} q{metrics.EyeMeasurementQualityPercent:0}%) | brow {FormatRatioPercent(metrics.AverageBrowHeightRatio)} ({browLock} q{metrics.BrowMeasurementQualityPercent:0}%) | mouth {FormatRatioPercent(metrics.MouthOpeningRatio)} ({mouthLock} q{metrics.MouthMeasurementQualityPercent:0}%) | jaw {FormatRatioPercent(metrics.JawDroopRatio)}{blink}{jawOpen}{artifact}";
    }

    private static string FormatCompactHeadPoseLine(HeadPoseEstimate pose)
    {
        if (!pose.HasFace)
        {
            return "Pose: waiting for face lock";
        }

        var distance = pose.ApparentDistanceUnits is double units
            ? $"Z {units:0.##}"
            : "Z --";
        if (pose.ZRelativeToReference is double relative)
        {
            distance += $" ({relative:0.##}x ref)";
        }

        var fill = pose.FaceFillWidthPercent is double width && pose.FaceFillHeightPercent is double height
            ? $" | fill {width:0.#}% x {height:0.#}%"
            : "";
        return $"Pose: X {pose.XHorizontalPercent:0.#}% | Y {pose.YVerticalPercent:0.#}% | {distance}{fill} | A/X {pose.ARotationAroundXDegrees:0.#} deg | B/Y {pose.BRotationAroundYDegrees:0.#} deg | C/Z {pose.CRotationAroundZDegrees:0.#} deg";
    }

    private string FormatCompactAvatarLine()
    {
        var model = _currentPersonalFaceModelUpdate.Model;
        var quality = _currentPersonalFaceCaptureQuality.ScorePercent > 0d
            ? $"quality {_currentPersonalFaceCaptureQuality.Label} {_currentPersonalFaceCaptureQuality.ScorePercent:0}%"
            : "quality waiting";
        if (PersonalModelSubjectCheckBox.IsChecked != true)
        {
            return $"Avatar: subject off | {quality}";
        }

        if (!_avatarLearningRequested)
        {
            return $"Avatar: stopped | accepted {model.AcceptedSamples}/{model.ObservedSamples} | {quality}";
        }

        var state = _currentPersonalFaceModelUpdate.Accepted
            ? "learning"
            : $"waiting ({_currentPersonalFaceModelUpdate.RejectionKind})";
        return $"Avatar: {state} | accepted {model.AcceptedSamples}/{model.ObservedSamples} | {quality}";
    }

    private static string FormatFaceReliabilityLine(FaceLockStabilityAnalysis stability)
    {
        if (stability.SampleCount <= 0)
        {
            return "Face reliability: waiting for temporal lock";
        }

        if (stability.SampleCount < 3)
        {
            return $"Face reliability: warming {stability.SampleCount} sample(s)";
        }

        return $"Face reliability: {stability.Label} {stability.CompositeReliabilityPercent:0}% | continuity {stability.FaceContinuityPercent:0}% | eye {stability.EyeReliabilityPercent:0}% | mouth {stability.MouthReliabilityPercent:0}%";
    }

    private static string FormatPersonalFaceModelLine(
        PersonalFaceModelUpdate update,
        PersonalFaceMotionModel motionModel,
        PersonalFaceCorpusReadiness readiness,
        long measurementJournalSizeBytes)
    {
        var model = update.Model;
        var subject = string.IsNullOrWhiteSpace(model.SubjectDisplayName)
            ? "subject"
            : model.SubjectDisplayName;
        var storage = $"journal {FormatStorageSize(measurementJournalSizeBytes)} / 10 GB";
        var motion = motionModel.ObservationCount > 0
            ? $"motion {motionModel.UsableObservationCount} obs/{motionModel.MotionPairCount} pairs"
            : "motion waiting";
        var readinessLabel = readiness.AcceptedBaselineSamples > 0 || readiness.MotionUsableObservations > 0
            ? $"readiness {readiness.OverallReadinessPercent:0}%"
            : "readiness waiting";
        var identity = update.IdentityAnalysis is { HasMeasurement: true } identityAnalysis
            ? $"identity {identityAnalysis.ConfidencePercent:0}%"
            : model.IdentitySignatureSamples > 0
                ? $"identity {model.IdentitySignatureSamples} sig"
                : "identity warming";
        if (update.RejectionKind == PersonalFaceModelRejectionKind.SubjectNotConfirmed)
        {
            return $"Personal model ({subject}): subject gate off | accepted {model.AcceptedSamples}/{model.ObservedSamples} | {motion} | {identity} | {readinessLabel} | {storage}";
        }

        if (model.ObservedSamples <= 0)
        {
            return $"Personal model ({subject}): waiting | {motion} | {identity} | {readinessLabel} | {storage}";
        }

        var action = update.Accepted
            ? "learning"
            : $"paused ({update.RejectionKind})";
        return $"Personal model ({subject}): {action} | accepted {model.AcceptedSamples}/{model.ObservedSamples} | weight {model.AcceptedSampleWeight:0.0} | {motion} | {identity} | {readinessLabel} | {storage}";
    }

    private static string FormatPersonalFaceCollectionAuditLine(PersonalFaceCollectionAudit audit)
    {
        if (audit.TotalFramesReviewed <= 0)
        {
            return "Collection audit: waiting for reviewed frames";
        }

        var issue = FormatCollectionAuditOverlayIssue(audit.TopCaptureQualityIssues.FirstOrDefault());
        var issueLabel = string.IsNullOrWhiteSpace(issue)
            ? ""
            : $" | top issue {issue}";
        return $"Collection audit: reviewed {audit.TotalFramesReviewed} | face {audit.FaceDetectionRate:P0} | collect {audit.CaptureQualityCollectableRate:P0} | avatar {audit.CaptureQualityAvatarGradeRate:P0}{issueLabel}";
    }

    private static string FormatCollectionAuditOverlayIssue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        var countIndex = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
        if (countIndex > 0)
        {
            trimmed = trimmed[..countIndex];
        }

        const int MaxLength = 84;
        return trimmed.Length <= MaxLength ? trimmed : $"{trimmed[..(MaxLength - 3)]}...";
    }

    private static string FormatStorageSize(long bytes)
    {
        if (bytes < 1024L)
        {
            return $"{Math.Max(0L, bytes)} B";
        }

        var size = (double)bytes;
        string[] units = ["KB", "MB", "GB"];
        var unitIndex = -1;
        while (size >= 1024d && unitIndex < units.Length - 1)
        {
            size /= 1024d;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }

    private static string FormatEyeLockLine(FaceLandmarkMetrics metrics)
    {
        if (!metrics.HasFace)
        {
            return "Eye lock: waiting for face landmarks";
        }

        var lockLabel = metrics.IsEyeMeasurementUsable ? "usable" : "limited";
        var artifact = metrics.PossibleOneEyeArtifact ? "; possible one-eye artifact" : "";
        var reconstructed = metrics.AnyEyeReconstructed || metrics.EyeArtifactSuppressed
            ? "; reconstruction used"
            : "";
        var mediaPipe = metrics.MediaPipeAverageEyeBlinkPercent is double blink
            ? $"; MP blink {blink:0}%"
            : "";
        return $"Eye lock: {lockLabel} q{metrics.EyeMeasurementQualityPercent:0}% | open {FormatRatioPercent(metrics.AverageEyeOpeningRatio)} | agree {metrics.EyeAgreementPercent:0}%{mediaPipe}{artifact}{reconstructed}";
    }

    private static string FormatMouthLockLine(FaceLandmarkMetrics metrics)
    {
        if (!metrics.HasFace)
        {
            return "Mouth lock: waiting for face landmarks";
        }

        var lockLabel = metrics.IsMouthMeasurementUsable ? "usable" : "limited";
        var reconstructed = metrics.MouthReconstructed ? "; reconstruction used" : "";
        var mediaPipe = CreateMediaPipeMouthLabel(metrics);
        return $"Mouth lock: {lockLabel} q{metrics.MouthMeasurementQualityPercent:0}% | open {FormatRatioPercent(metrics.MouthOpeningRatio)} | jaw drop {FormatRatioPercent(metrics.JawDroopRatio)}{mediaPipe}{reconstructed}";
    }

    private static string CreateMediaPipeMouthLabel(FaceLandmarkMetrics metrics)
    {
        var parts = new List<string>();
        if (metrics.MediaPipeJawOpenPercent is double jawOpen)
        {
            parts.Add($"jaw {jawOpen:0}%");
        }

        if (metrics.MediaPipeMouthClosePercent is double mouthClose)
        {
            parts.Add($"close {mouthClose:0}%");
        }

        if (metrics.MediaPipeMouthOpeningCorrectionRatio is double correction)
        {
            parts.Add($"corr {FormatSigned(correction)}");
        }

        return parts.Count == 0 ? "" : $"; MP {string.Join(", ", parts)}";
    }

    private static string FormatCueLine(
        FaceLandmarkCueAnalysis? landmarkCue,
        FaceLandmarkTrendAnalysis trend,
        FaceCueAnalysis? regionCue)
    {
        if (landmarkCue is null && regionCue is null)
        {
            return "Cue: waiting for baseline";
        }

        var cueScore = landmarkCue?.CompositeCuePercent ?? regionCue?.CompositeCuePercent;
        var eyeCue = landmarkCue?.EyeClosurePercent ?? regionCue?.EyeDropPercent;
        var mouthCue = MaxSignal(landmarkCue?.MouthOpeningChangePercent, landmarkCue?.JawDroopChangePercent, regionCue?.JawChangePercent);
        var trendLabel = trend.HasUsableTrend
            ? $" | trend eye {FormatPercent(trend.EyeClosingTrendPercent)}, mouth {FormatPercent(trend.MouthOpeningTrendPercent)}"
            : "";
        return $"Cue: score {FormatPercent(cueScore)} | eye close {FormatPercent(eyeCue)} | mouth/jaw {FormatPercent(mouthCue)}{trendLabel}";
    }

    private static string FormatRatioPercent(double? value)
    {
        return value is double number ? $"{number * 100d:0}%" : "--";
    }

    private static string FormatPercent(double? value)
    {
        return value is double number ? $"{number:0}%" : "--";
    }

    private static string FormatSigned(double value)
    {
        return value.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture);
    }

    private string GetFaceFeatureTrackerStatus(DateTime now)
    {
        if (!_faceLandmarkTracker.IsAvailable)
        {
            return "landmark tracker unavailable";
        }

        if (HasFreshFaceFeatureLock(now))
        {
            return "landmark face lock";
        }

        if (HasUsableFaceFeatureLock(now))
        {
            return "landmark face hold";
        }

        var trackerStatus = _faceLandmarkTracker.LastBackendStatus;
        if (!string.IsNullOrWhiteSpace(trackerStatus) && trackerStatus.Contains("dense", StringComparison.OrdinalIgnoreCase))
        {
            return Interlocked.CompareExchange(ref _faceFeatureDetectionPending, 0, 0) == 1
                ? $"landmark search ({trackerStatus})"
                : $"landmark waiting ({trackerStatus})";
        }

        return Interlocked.CompareExchange(ref _faceFeatureDetectionPending, 0, 0) == 1
            ? "landmark search"
            : "landmark waiting";
    }

    private void WriteAnnotatedVideoFrame(BitmapSource bitmap, DateTime timestamp, string state, string metrics, string trigger, string accentColor)
    {
        if (_activeEpisodeStartedAt is null)
        {
            if ((timestamp - _lastBufferedVideoFrameAt).TotalSeconds < 1d / EventVideoFramesPerSecond)
            {
                return;
            }

            var bufferedJpeg = CreateAnnotatedJpeg(bitmap, timestamp, state, metrics, trigger, accentColor, _currentFaceAnalysis, _currentFaceFeatureDetection, _currentFaceLandmarkFrame, GetFaceCueLayout(), GetEyeCueThreshold(), GetJawCueThreshold(), GetCompositeCueThreshold());
            AddPreEventVideoFrame(timestamp, bufferedJpeg);
            return;
        }

        if ((timestamp - _lastRecordedVideoFrameAt).TotalSeconds < 1d / EventVideoFramesPerSecond)
        {
            return;
        }

        _lastRecordedVideoFrameAt = timestamp;
        var jpeg = CreateAnnotatedJpeg(bitmap, timestamp, state, metrics, trigger, accentColor, _currentFaceAnalysis, _currentFaceFeatureDetection, _currentFaceLandmarkFrame, GetFaceCueLayout(), GetEyeCueThreshold(), GetJawCueThreshold(), GetCompositeCueThreshold());
        _eventRecorder.AddFrame(jpeg);
    }

    private void BufferAnnotatedFrame(BitmapSource bitmap, DateTime timestamp, string state, string metrics, string trigger, string accentColor)
    {
        if ((timestamp - _lastBufferedVideoFrameAt).TotalSeconds < 1d / EventVideoFramesPerSecond)
        {
            return;
        }

        var jpeg = CreateAnnotatedJpeg(bitmap, timestamp, state, metrics, trigger, accentColor, _currentFaceAnalysis, _currentFaceFeatureDetection, _currentFaceLandmarkFrame, GetFaceCueLayout(), GetEyeCueThreshold(), GetJawCueThreshold(), GetCompositeCueThreshold());
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
        FaceLandmarkFrame landmarkFrame,
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
            DrawFaceCueGuides(context, new Rect(0, 0, width, height), faceAnalysis, featureDetection, landmarkFrame, layout, eyeCueThreshold, jawCueThreshold, compositeCueThreshold);

            var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
            var pixelsPerDip = dpi.PixelsPerDip;
            var font = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var smallFont = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var margin = Math.Max(16d, width * 0.018d);
            var boxWidth = Math.Min(width - margin * 2d, Math.Max(560d, width * 0.72d));
            var lineHeight = Math.Max(22d, height * 0.032d);
            var metricLines = SplitOverlayLines(metrics, 4);
            var triggerLines = SplitOverlayLines(trigger, 2);
            var boxLineCount = 2 + metricLines.Count + triggerLines.Count;
            var boxHeight = Math.Min(height - margin * 2d, Math.Max(lineHeight * 4.6d, boxLineCount * lineHeight + 30d));
            var box = new Rect(margin, margin, boxWidth, boxHeight);
            var accent = (Color)ColorConverter.ConvertFromString(accentColor);

            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(218, 8, 13, 18)), new Pen(new SolidColorBrush(accent), 3), box, 4, 4);

            var cursorY = box.Y + 10d;
            DrawText(context, state, font, Math.Max(18d, height * 0.028d), Brushes.White, box.X + 14, cursorY, box.Width - 28, pixelsPerDip, maxLineCount: 1);
            cursorY += lineHeight;
            DrawText(context, timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture), smallFont, Math.Max(14d, height * 0.021d), Brushes.WhiteSmoke, box.X + 14, cursorY, box.Width - 28, pixelsPerDip, maxLineCount: 1);
            cursorY += lineHeight;

            var metricBrush = new SolidColorBrush(Color.FromRgb(185, 215, 239));
            foreach (var line in metricLines)
            {
                DrawText(context, line, smallFont, Math.Max(14d, height * 0.020d), metricBrush, box.X + 14, cursorY, box.Width - 28, pixelsPerDip, maxLineCount: 1);
                cursorY += lineHeight;
            }

            var triggerBrush = new SolidColorBrush(Color.FromRgb(220, 231, 239));
            foreach (var line in triggerLines)
            {
                DrawText(context, line, smallFont, Math.Max(13d, height * 0.019d), triggerBrush, box.X + 14, cursorY, box.Width - 28, pixelsPerDip, maxLineCount: 1);
                cursorY += lineHeight;
            }
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
        FaceLandmarkFrame landmarkFrame,
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

        if (landmarkFrame.HasFace)
        {
            DrawLandmarkContours(context, display, landmarkFrame);
        }
    }

    private static void DrawLandmarkContours(DrawingContext context, Rect display, FaceLandmarkFrame frame)
    {
        var eyePen = CreateLandmarkPen(Color.FromArgb(245, 122, 218, 255), Math.Max(2d, display.Height * 0.004d));
        var inferredEyePen = CreateLandmarkPen(Color.FromArgb(245, 238, 174, 74), Math.Max(2d, display.Height * 0.004d), inferred: true);
        var reconstructedEyePen = CreateLandmarkPen(Color.FromArgb(245, 122, 218, 255), Math.Max(2d, display.Height * 0.004d), inferred: true);
        var lipPen = CreateLandmarkPen(Color.FromArgb(245, 255, 190, 110), Math.Max(2d, display.Height * 0.0035d));
        var reconstructedLipPen = CreateLandmarkPen(Color.FromArgb(245, 255, 190, 110), Math.Max(2d, display.Height * 0.0035d), inferred: true);
        var browPen = CreateLandmarkPen(Color.FromArgb(245, 196, 247, 163), Math.Max(2d, display.Height * 0.0035d));
        var facePen = CreateLandmarkPen(Color.FromArgb(135, 185, 215, 239), Math.Max(1d, display.Height * 0.002d));
        var leftEyePen = frame.EyeArtifactSuppressed
            ? inferredEyePen
            : frame.LeftEyeReconstructed ? reconstructedEyePen : eyePen;
        var rightEyePen = frame.EyeArtifactSuppressed
            ? inferredEyePen
            : frame.RightEyeReconstructed ? reconstructedEyePen : eyePen;
        var mouthPen = frame.MouthReconstructed ? reconstructedLipPen : lipPen;

        DrawRelativePolyline(context, display, frame.FaceContour, facePen, close: true);
        DrawRelativePolyline(context, display, frame.JawContour, facePen, close: false);
        DrawRelativePolyline(context, display, frame.LeftEyeContour, leftEyePen, close: true);
        DrawRelativePolyline(context, display, frame.RightEyeContour, rightEyePen, close: true);
        DrawRelativePolyline(context, display, frame.LeftBrowContour, browPen, close: false);
        DrawRelativePolyline(context, display, frame.RightBrowContour, browPen, close: false);
        DrawRelativePolyline(context, display, frame.OuterLipContour, mouthPen, close: true);
        DrawRelativePolyline(context, display, frame.InnerLipContour, mouthPen, close: true);
    }

    private static Pen CreateLandmarkPen(Color color, double thickness, bool inferred = false)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        if (inferred)
        {
            pen.DashStyle = new DashStyle(new[] { 5d, 3d }, 0d);
        }

        return pen;
    }

    private static void DrawRelativePolyline(DrawingContext context, Rect display, IReadOnlyList<Point> points, Pen pen, bool close)
    {
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(ToDisplayPoint(display, points[0]), isFilled: false, isClosed: close);
            for (var index = 1; index < points.Count; index++)
            {
                geometryContext.LineTo(ToDisplayPoint(display, points[index]), isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
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

    private static IReadOnlyList<string> SplitOverlayLines(string text, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text) || maxLines <= 0)
        {
            return [];
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToList();
        if (lines.Count <= maxLines)
        {
            return lines;
        }

        var clipped = lines.Take(maxLines).ToList();
        clipped[^1] = clipped[^1].TrimEnd('.', ' ') + "...";
        return clipped;
    }

    private static void DrawText(DrawingContext context, string text, Typeface font, double size, Brush brush, double x, double y, double maxWidth, double pixelsPerDip, int maxLineCount = 2)
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
            MaxLineCount = Math.Max(1, maxLineCount),
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

        public string LandmarkSource { get; init; } = "";

        public string LandmarkConfidence { get; init; } = "";

        public double? LandmarkTrackingConfidence { get; init; }

        public double? LandmarkEyeConfidence { get; init; }

        public double? LandmarkMouthConfidence { get; init; }

        public double? LandmarkEyeQuality { get; init; }

        public double? LandmarkMouthQuality { get; init; }

        public double? LandmarkOverallQuality { get; init; }

        public string CaptureQualityLabel { get; init; } = "";

        public double? CaptureQualityScore { get; init; }

        public bool? CaptureQualityCanCollect { get; init; }

        public bool? CaptureQualityAvatarGrade { get; init; }

        public string CaptureQualityReason { get; init; } = "";

        public double? CaptureQualityCameraModeScore { get; init; }

        public double? CaptureQualityFaceScaleScore { get; init; }

        public double? CaptureQualityEyeScore { get; init; }

        public double? CaptureQualityMouthScore { get; init; }

        public double? CaptureQualityStabilityScore { get; init; }

        public double? CaptureQualityGlassesScore { get; init; }

        public double? CaptureQualityStorageScore { get; init; }

        public double? CaptureQualityFaceWidth { get; init; }

        public double? CaptureQualityFaceHeight { get; init; }

        public IReadOnlyList<string> CaptureQualityIssues { get; init; } = [];

        public IReadOnlyList<string> CaptureQualitySuggestions { get; init; } = [];

        public string LandmarkFaceReliabilityStatus { get; init; } = "";

        public int LandmarkFaceReliabilitySamples { get; init; }

        public double? LandmarkFaceReliability { get; init; }

        public double? LandmarkFaceContinuity { get; init; }

        public double? LandmarkEyeReliability { get; init; }

        public double? LandmarkMouthReliability { get; init; }

        public double? LandmarkFaceBoundsRate { get; init; }

        public double? LandmarkEyeUsableRate { get; init; }

        public double? LandmarkMouthUsableRate { get; init; }

        public bool? LandmarkEyeImageQualityAvailable { get; init; }

        public bool? LandmarkMouthImageQualityAvailable { get; init; }

        public double? LandmarkEyeGlare { get; init; }

        public double? LandmarkMouthGlare { get; init; }

        public double? LandmarkEyeContrast { get; init; }

        public double? LandmarkMouthContrast { get; init; }

        public double? LandmarkEyeSharpness { get; init; }

        public double? LandmarkMouthSharpness { get; init; }

        public double? LandmarkEyeDarkCoverage { get; init; }

        public double? LandmarkMouthDarkCoverage { get; init; }

        public double? LandmarkRawEyeAsymmetry { get; init; }

        public double? LandmarkEyeAsymmetry { get; init; }

        public double? LandmarkEyeAgreement { get; init; }

        public bool? LandmarkPossibleOneEyeArtifact { get; init; }

        public bool? LandmarkLeftEyeReconstructed { get; init; }

        public bool? LandmarkRightEyeReconstructed { get; init; }

        public bool? LandmarkMouthReconstructed { get; init; }

        public bool? LandmarkEyeArtifactSuppressed { get; init; }

        public double? LandmarkRawEyeOpening { get; init; }

        public double? LandmarkRawMouthOpening { get; init; }

        public double? LandmarkRawJawDroop { get; init; }

        public double? LandmarkEyeOpening { get; init; }

        public double? LandmarkMouthOpening { get; init; }

        public double? LandmarkMouthOpeningVelocity { get; init; }

        public double? LandmarkJawDroop { get; init; }

        public double? LandmarkJawDroopVelocity { get; init; }

        public double? LandmarkMediaPipeLeftEyeBlink { get; init; }

        public double? LandmarkMediaPipeRightEyeBlink { get; init; }

        public double? LandmarkMediaPipeAverageEyeBlink { get; init; }

        public double? LandmarkMediaPipeJawOpen { get; init; }

        public double? LandmarkMediaPipeMouthClose { get; init; }

        public double? LandmarkMediaPipeEyeOpeningCorrection { get; init; }

        public double? LandmarkMediaPipeMouthOpeningCorrection { get; init; }

        public bool? LandmarkMediaPipeEyeOpeningCorrected { get; init; }

        public bool? LandmarkMediaPipeMouthOpeningCorrected { get; init; }

        public bool? LandmarkMediaPipeBlinkBaselineReady { get; init; }

        public bool? LandmarkMediaPipeMouthBaselineReady { get; init; }

        public double? LandmarkMediaPipeBlinkBaseline { get; init; }

        public double? LandmarkMediaPipeJawOpenBaseline { get; init; }

        public double? LandmarkMediaPipeMouthCloseBaseline { get; init; }

        public double? LandmarkMediaPipeBlinkChange { get; init; }

        public double? LandmarkMediaPipeJawOpenChange { get; init; }

        public double? LandmarkMediaPipeMouthCloseDrop { get; init; }

        public double? LandmarkMediaPipeMouthOpeningEvidence { get; init; }

        public string LandmarkCueStatus { get; init; } = "";

        public double? LandmarkCueScore { get; init; }

        public bool? LandmarkEyeCueEligible { get; init; }

        public bool? LandmarkMouthCueEligible { get; init; }

        public double? LandmarkEyeClosure { get; init; }

        public double? LandmarkMouthOpeningChange { get; init; }

        public double? LandmarkJawDroopBaseline { get; init; }

        public double? LandmarkJawDroopChange { get; init; }

        public string LandmarkTrendStatus { get; init; } = "";

        public double? LandmarkTrendScore { get; init; }

        public double? LandmarkTrendWindowSeconds { get; init; }

        public double? LandmarkEyeClosingTrend { get; init; }

        public double? LandmarkMouthOpeningTrend { get; init; }

        public double? LandmarkEyeOpeningSlope { get; init; }

        public double? LandmarkMouthOpeningSlope { get; init; }

        public int LandmarkEventSamples { get; init; }

        public IReadOnlyList<string> LandmarkEventSources { get; init; } = [];

        public IReadOnlyList<string> LandmarkEventBackendStatuses { get; init; } = [];

        public double? LandmarkEventMinimumEyeQuality { get; init; }

        public double? LandmarkEventMinimumMouthQuality { get; init; }

        public double? LandmarkEventMinimumOverallQuality { get; init; }

        public double? LandmarkEventAverageOverallQuality { get; init; }

        public int LandmarkEventCaptureQualitySamples { get; init; }

        public int LandmarkEventCaptureQualityCanCollectSamples { get; init; }

        public int LandmarkEventCaptureQualityAvatarGradeSamples { get; init; }

        public double? LandmarkEventMinimumCaptureQualityScore { get; init; }

        public double? LandmarkEventMaximumCaptureQualityScore { get; init; }

        public double? LandmarkEventAverageCaptureQualityScore { get; init; }

        public IReadOnlyList<string> LandmarkEventCaptureQualityLabels { get; init; } = [];

        public IReadOnlyList<string> LandmarkEventCaptureQualityIssues { get; init; } = [];

        public int LandmarkEventFaceReliabilitySamples { get; init; }

        public int LandmarkEventFaceReliabilityUsableSamples { get; init; }

        public double? LandmarkEventMinimumFaceReliability { get; init; }

        public double? LandmarkEventAverageFaceReliability { get; init; }

        public double? LandmarkEventMinimumFaceContinuity { get; init; }

        public double? LandmarkEventAverageFaceContinuity { get; init; }

        public double? LandmarkEventMinimumEyeReliability { get; init; }

        public double? LandmarkEventAverageEyeReliability { get; init; }

        public double? LandmarkEventMinimumMouthReliability { get; init; }

        public double? LandmarkEventAverageMouthReliability { get; init; }

        public double? LandmarkEventMinimumEyeOpening { get; init; }

        public double? LandmarkEventMaximumEyeClosure { get; init; }

        public double? LandmarkEventMaximumMouthOpening { get; init; }

        public double? LandmarkEventMaximumMouthOpeningChange { get; init; }

        public double? LandmarkEventMaximumMouthOpeningVelocity { get; init; }

        public double? LandmarkEventMaximumJawDroop { get; init; }

        public double? LandmarkEventMaximumJawDroopChange { get; init; }

        public double? LandmarkEventMaximumJawDroopVelocity { get; init; }

        public double? LandmarkEventMaximumMediaPipeAverageEyeBlink { get; init; }

        public double? LandmarkEventMaximumMediaPipeJawOpen { get; init; }

        public double? LandmarkEventMinimumMediaPipeMouthClose { get; init; }

        public double? LandmarkEventMaximumMediaPipeBlinkChange { get; init; }

        public double? LandmarkEventMaximumMediaPipeJawOpenChange { get; init; }

        public double? LandmarkEventMaximumMediaPipeMouthCloseDrop { get; init; }

        public double? LandmarkEventMaximumMediaPipeMouthOpeningEvidence { get; init; }

        public int LandmarkEventMediaPipeEyeOpeningCorrectedSamples { get; init; }

        public int LandmarkEventMediaPipeMouthOpeningCorrectedSamples { get; init; }

        public double? LandmarkEventMaximumAbsoluteMediaPipeEyeOpeningCorrection { get; init; }

        public double? LandmarkEventMaximumAbsoluteMediaPipeMouthOpeningCorrection { get; init; }

        public double? LandmarkEventMaximumCueScore { get; init; }

        public double? LandmarkEventMaximumEyeClosingTrend { get; init; }

        public double? LandmarkEventMaximumMouthOpeningTrend { get; init; }

        public double? LandmarkEventMinimumEyeOpeningSlope { get; init; }

        public double? LandmarkEventMaximumMouthOpeningSlope { get; init; }

        public double? LandmarkEventMaximumTrendScore { get; init; }

        public double? LandmarkEventMaximumEyeGlare { get; init; }

        public double? LandmarkEventMaximumMouthGlare { get; init; }

        public double? LandmarkEventMinimumEyeContrast { get; init; }

        public double? LandmarkEventMinimumMouthContrast { get; init; }

        public double? LandmarkEventMinimumEyeSharpness { get; init; }

        public double? LandmarkEventMinimumMouthSharpness { get; init; }

        public double? LandmarkEventMaximumRawEyeAsymmetry { get; init; }

        public double? LandmarkEventMaximumEyeAsymmetry { get; init; }

        public int LandmarkEventPossibleOneEyeArtifactSamples { get; init; }

        public int LandmarkEventLeftEyeReconstructedSamples { get; init; }

        public int LandmarkEventRightEyeReconstructedSamples { get; init; }

        public int LandmarkEventMouthReconstructedSamples { get; init; }

        public int LandmarkEventEyeArtifactSuppressedSamples { get; init; }

        public string VideoFile { get; init; } = "";

        public bool VideoOverlayBurnedIn { get; init; }

        public int PreEventVideoSeconds { get; init; }

        public int LandmarkTimelineSamples { get; init; }

        public string LandmarkTimelineJsonFile { get; init; } = "";

        public string LandmarkTimelineCsvFile { get; init; } = "";

        public string StartSnapshot { get; init; } = "";

        public string EndSnapshot { get; init; } = "";

        public string OutputFolder { get; init; } = "";
    }

    private sealed record CameraControlBinding(
        CameraDevice Camera,
        CameraControlItem Control,
        TextBlock ValueText,
        Slider Slider,
        CheckBox AutoCheckBox);

    private readonly record struct MeshPoint3D(double X, double Y, double Z);

    private sealed record LiveWireframeProjectedPoint(int Index, double X, double Y, double Z);

    private sealed record LiveWireframeProjection(
        IReadOnlyDictionary<int, LiveWireframeProjectedPoint> Points,
        string Mode,
        bool HeadLocked,
        double AxisOriginX,
        double AxisOriginY,
        double AxisUnitPixels);

    private sealed record TrackingFidelityOption(string Label, int MaxOutputWidth, double MaxFramesPerSecond)
    {
        public string ShortLabel
        {
            get
            {
                if (MaxOutputWidth >= 3840)
                {
                    return "4K";
                }

                if (MaxOutputWidth >= 1920)
                {
                    return "HD";
                }

                return "Safe";
            }
        }
    }

    private sealed record AvatarLearningState(bool Active, string Title, string Detail, Color Accent);

    private sealed record AvatarTrackingSanityState(string Detail, Color Accent);

    private sealed record PersonalFaceReportSnapshot(
        string Folder,
        PersonalFaceModel Model,
        IReadOnlyList<PersonalFaceCollectionAuditObservation> CollectionAuditObservations,
        PersonalFaceCaptureQualityAssessment CaptureQuality,
        bool SubjectConfirmed,
        bool AvatarLearningRequested,
        bool AvatarLearningActive,
        string AvatarLearningStatus,
        string AvatarLearningCorrection,
        HeadPoseEstimate HeadPose,
        LastGoodFeatureMeshStabilityReport LastGoodFeatureMeshStability,
        IReadOnlyList<LastGoodFeatureMeshSample> LastGoodFeatureMeshSamples);

    private sealed record PersonalFaceReportSaveResult(
        string PersonalFaceModelPath,
        string PersonalFaceMotionModelPath,
        string PersonalFaceCorpusReadinessPath,
        string PersonalFaceCollectionAuditPath,
        string MeasurementFacePreviewPath,
        string MeasurementAvatarTrainingPackagePath,
        string MeasurementAvatarCapturePlanPath,
        string LastGoodFeatureMeshJsonPath,
        string LastGoodFeatureMeshHtmlPath,
        string MeasurementAvatarSystemDashboardPath,
        long MeasurementJournalBytes,
        PersonalFaceMotionModel MotionModel,
        PersonalFaceCorpusReadiness CorpusReadiness,
        PersonalFaceCollectionAudit CollectionAudit,
        MeasurementAvatarCapturePlan CapturePlan);

    private sealed record PersonalFaceAuditSnapshot(
        PersonalFaceMotionModel MotionModel,
        PersonalFaceCorpusReadiness Readiness);

    private sealed class EpisodeMonitorSettings
    {
        public string OutputFolder { get; set; } = "";

        public DateTime UpdatedAtUtc { get; set; }
    }

    private sealed class AlertBaselineFile
    {
        public int Version { get; set; }

        public DateTime SavedAtUtc { get; set; }

        public string CameraName { get; set; } = "";

        public string CameraModeLabel { get; set; } = "";

        public FaceCueBaselineSnapshot? RegionBaseline { get; set; }

        public FaceLandmarkCueBaselineSnapshot? LandmarkBaseline { get; set; }
    }

    private sealed record BufferedVideoFrame(DateTime Timestamp, byte[] JpegBytes);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
