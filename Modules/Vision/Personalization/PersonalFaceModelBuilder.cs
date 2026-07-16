using System.Windows;
using EpisodeMonitor.Modules.Vision.Analysis;
using EpisodeMonitor.Modules.Vision.Common;

namespace EpisodeMonitor.Modules.Vision.Personalization;

public sealed class PersonalFaceModelBuilder
{
    private const double EventCueRejectThreshold = 28d;
    private const double EventTrendRejectThreshold = 24d;
    private const double MinimumOverallQualityPercent = 48d;
    private const double MinimumReliabilityPercent = 68d;
    private const double DistributionEmaAlpha = 0.035d;
    private const double MaximumStableSampleWeight = 1.25d;
    private const double EventLikeSampleWeightMultiplier = 0.45d;
    private const double AnchorTargetWeight = 180d;

    private readonly DistributionAccumulator _faceCenterX = new();
    private readonly DistributionAccumulator _faceCenterY = new();
    private readonly DistributionAccumulator _faceWidth = new();
    private readonly DistributionAccumulator _faceHeight = new();
    private readonly DistributionAccumulator _headYaw = new();
    private readonly DistributionAccumulator _headPitch = new();
    private readonly DistributionAccumulator _headRoll = new();
    private readonly DistributionAccumulator _leftEyeOpening = new();
    private readonly DistributionAccumulator _rightEyeOpening = new();
    private readonly DistributionAccumulator _averageEyeOpening = new();
    private readonly DistributionAccumulator _eyeAgreement = new();
    private readonly DistributionAccumulator _mouthOpening = new();
    private readonly DistributionAccumulator _jawDroop = new();
    private readonly DistributionAccumulator _mediaPipeAverageBlink = new();
    private readonly DistributionAccumulator _mediaPipeJawOpen = new();
    private readonly DistributionAccumulator _mediaPipeMouthClose = new();
    private readonly DistributionAccumulator _eyeGlare = new();
    private readonly DistributionAccumulator _eyeContrast = new();
    private readonly DistributionAccumulator _eyeSharpness = new();
    private readonly DistributionAccumulator _faceAspectRatio = new();
    private readonly DistributionAccumulator _interEyeDistanceToFaceWidth = new();
    private readonly DistributionAccumulator _leftEyeWidthToFaceWidth = new();
    private readonly DistributionAccumulator _rightEyeWidthToFaceWidth = new();
    private readonly DistributionAccumulator _mouthWidthToFaceWidth = new();
    private readonly DistributionAccumulator _eyeMidlineYToFaceHeight = new();
    private readonly DistributionAccumulator _mouthCenterYToFaceHeight = new();
    private readonly DistributionAccumulator _eyeToMouthYDistanceToFaceHeight = new();
    private readonly ContourShapeAccumulator _leftEyeShape = new("left_eye_shape", "Left eye contour shape", pointCount: 8, closed: true);
    private readonly ContourShapeAccumulator _rightEyeShape = new("right_eye_shape", "Right eye contour shape", pointCount: 8, closed: true);
    private readonly ContourShapeAccumulator _outerLipShape = new("outer_lip_shape", "Outer lip contour shape", pointCount: 12, closed: true);
    private readonly ContourShapeAccumulator _innerLipShape = new("inner_lip_shape", "Inner lip contour shape", pointCount: 10, closed: true);
    private readonly ContourShapeAccumulator _jawShape = new("jaw_shape", "Jaw contour shape", pointCount: 9, closed: false);
    private readonly PoseBucketCollectionAccumulator _poseBuckets = new();

    private DateTime _createdAtUtc;
    private DateTime _updatedAtUtc;
    private int _observedSamples;
    private int _acceptedSamples;
    private int _rejectedSamples;
    private int _eventLikeRejectedSamples;
    private int _lowQualityRejectedSamples;
    private int _noFaceRejectedSamples;
    private int _subjectMismatchRejectedSamples;
    private int _identitySignatureSamples;
    private int _possibleOneEyeArtifactSamples;
    private int _eyeArtifactSuppressedSamples;
    private int _leftEyeReconstructedSamples;
    private int _rightEyeReconstructedSamples;
    private int _mouthReconstructedSamples;
    private int _mediaPipeEyeOpeningCorrectedSamples;
    private int _mediaPipeMouthOpeningCorrectedSamples;
    private double _acceptedSampleWeight;
    private double _faceReliabilityTotal;
    private double _faceContinuityTotal;
    private double _eyeReliabilityTotal;
    private double _mouthReliabilityTotal;
    private string _subjectId;
    private string _subjectDisplayName;
    private string _subjectCollectionMode;

    public PersonalFaceModelBuilder(
        string subjectId = PersonalFaceSubject.DefaultSubjectId,
        string subjectDisplayName = PersonalFaceSubject.DefaultSubjectDisplayName,
        string subjectCollectionMode = PersonalFaceSubject.ManualConfirmationMode)
    {
        _subjectId = string.IsNullOrWhiteSpace(subjectId) ? PersonalFaceSubject.DefaultSubjectId : subjectId.Trim();
        _subjectDisplayName = string.IsNullOrWhiteSpace(subjectDisplayName) ? PersonalFaceSubject.DefaultSubjectDisplayName : subjectDisplayName.Trim();
        _subjectCollectionMode = string.IsNullOrWhiteSpace(subjectCollectionMode) ? PersonalFaceSubject.ManualConfirmationMode : subjectCollectionMode.Trim();
    }

    public PersonalFaceModel CurrentModel => CreateModel();

    public void LoadModel(PersonalFaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _subjectId = string.IsNullOrWhiteSpace(model.SubjectId)
            ? PersonalFaceSubject.DefaultSubjectId
            : model.SubjectId.Trim();
        _subjectDisplayName = string.IsNullOrWhiteSpace(model.SubjectDisplayName)
            ? PersonalFaceSubject.DefaultSubjectDisplayName
            : model.SubjectDisplayName.Trim();
        _subjectCollectionMode = string.IsNullOrWhiteSpace(model.SubjectCollectionMode)
            ? PersonalFaceSubject.ManualConfirmationMode
            : model.SubjectCollectionMode.Trim();

        _createdAtUtc = model.CreatedAtUtc;
        _updatedAtUtc = model.UpdatedAtUtc;
        _observedSamples = Math.Max(0, model.ObservedSamples);
        _acceptedSamples = Math.Max(0, model.AcceptedSamples);
        _rejectedSamples = Math.Max(0, model.RejectedSamples);
        _eventLikeRejectedSamples = Math.Max(0, model.EventLikeRejectedSamples);
        _lowQualityRejectedSamples = Math.Max(0, model.LowQualityRejectedSamples);
        _noFaceRejectedSamples = Math.Max(0, model.NoFaceRejectedSamples);
        _subjectMismatchRejectedSamples = Math.Max(0, model.SubjectMismatchRejectedSamples);
        _identitySignatureSamples = Math.Max(0, model.IdentitySignatureSamples);
        _possibleOneEyeArtifactSamples = Math.Max(0, model.PossibleOneEyeArtifactSamples);
        _eyeArtifactSuppressedSamples = Math.Max(0, model.EyeArtifactSuppressedSamples);
        _leftEyeReconstructedSamples = Math.Max(0, model.LeftEyeReconstructedSamples);
        _rightEyeReconstructedSamples = Math.Max(0, model.RightEyeReconstructedSamples);
        _mouthReconstructedSamples = Math.Max(0, model.MouthReconstructedSamples);
        _mediaPipeEyeOpeningCorrectedSamples = Math.Max(0, model.MediaPipeEyeOpeningCorrectedSamples);
        _mediaPipeMouthOpeningCorrectedSamples = Math.Max(0, model.MediaPipeMouthOpeningCorrectedSamples);
        _acceptedSampleWeight = Math.Max(0d, model.AcceptedSampleWeight);
        _faceReliabilityTotal = model.AverageFaceReliabilityPercent * _acceptedSampleWeight;
        _faceContinuityTotal = model.AverageFaceContinuityPercent * _acceptedSampleWeight;
        _eyeReliabilityTotal = model.AverageEyeReliabilityPercent * _acceptedSampleWeight;
        _mouthReliabilityTotal = model.AverageMouthReliabilityPercent * _acceptedSampleWeight;

        _faceCenterX.Load(model.FaceCenterX);
        _faceCenterY.Load(model.FaceCenterY);
        _faceWidth.Load(model.FaceWidth);
        _faceHeight.Load(model.FaceHeight);
        _headYaw.Load(model.HeadYawDegrees);
        _headPitch.Load(model.HeadPitchDegrees);
        _headRoll.Load(model.HeadRollDegrees);
        _leftEyeOpening.Load(model.LeftEyeOpeningRatio);
        _rightEyeOpening.Load(model.RightEyeOpeningRatio);
        _averageEyeOpening.Load(model.AverageEyeOpeningRatio);
        _eyeAgreement.Load(model.EyeAgreementPercent);
        _mouthOpening.Load(model.MouthOpeningRatio);
        _jawDroop.Load(model.JawDroopRatio);
        _mediaPipeAverageBlink.Load(model.MediaPipeAverageEyeBlinkPercent);
        _mediaPipeJawOpen.Load(model.MediaPipeJawOpenPercent);
        _mediaPipeMouthClose.Load(model.MediaPipeMouthClosePercent);
        _eyeGlare.Load(model.EyeGlarePercent);
        _eyeContrast.Load(model.EyeContrastPercent);
        _eyeSharpness.Load(model.EyeSharpnessPercent);
        _faceAspectRatio.Load(model.FaceAspectRatio);
        _interEyeDistanceToFaceWidth.Load(model.InterEyeDistanceToFaceWidth);
        _leftEyeWidthToFaceWidth.Load(model.LeftEyeWidthToFaceWidth);
        _rightEyeWidthToFaceWidth.Load(model.RightEyeWidthToFaceWidth);
        _mouthWidthToFaceWidth.Load(model.MouthWidthToFaceWidth);
        _eyeMidlineYToFaceHeight.Load(model.EyeMidlineYToFaceHeight);
        _mouthCenterYToFaceHeight.Load(model.MouthCenterYToFaceHeight);
        _eyeToMouthYDistanceToFaceHeight.Load(model.EyeToMouthYDistanceToFaceHeight);
        _leftEyeShape.Load(model.LeftEyeShape);
        _rightEyeShape.Load(model.RightEyeShape);
        _outerLipShape.Load(model.OuterLipShape);
        _innerLipShape.Load(model.InnerLipShape);
        _jawShape.Load(model.JawShape);
        _poseBuckets.Load(model.PoseBuckets);
    }

    public PersonalFaceModelUpdate Update(
        FaceLandmarkFrame frame,
        FaceLandmarkMetrics metrics,
        FaceLockStabilityAnalysis stability,
        FaceLandmarkCueAnalysis? cueAnalysis,
        FaceLandmarkTrendAnalysis? trendAnalysis,
        bool allowEventLikeMeasurements = false)
    {
        var timestamp = metrics.CapturedAtUtc != default
            ? metrics.CapturedAtUtc
            : frame.CapturedAtUtc != default ? frame.CapturedAtUtc : DateTime.UtcNow;
        if (_observedSamples == 0)
        {
            _createdAtUtc = timestamp;
        }

        _observedSamples++;
        _updatedAtUtc = timestamp;

        var gate = EvaluateGate(frame, metrics, stability, cueAnalysis, trendAnalysis, allowEventLikeMeasurements);
        if (!gate.Accepted)
        {
            _rejectedSamples++;
            CountRejectedGate(gate.Kind);

            return new PersonalFaceModelUpdate(false, gate.Kind, gate.Reason, 0d, CreateModel());
        }

        var identityMeasurement = PersonalFaceIdentityMeasurement.FromFrame(frame);
        var identityAnalysis = PersonalFaceIdentityAnalyzer.Analyze(CreateModel(), identityMeasurement);
        if (!identityAnalysis.Accepted)
        {
            _rejectedSamples++;
            CountRejectedGate(PersonalFaceModelRejectionKind.SubjectMismatch);
            return new PersonalFaceModelUpdate(
                false,
                PersonalFaceModelRejectionKind.SubjectMismatch,
                identityAnalysis.Reason,
                0d,
                CreateModel(),
                identityAnalysis);
        }

        _acceptedSamples++;
        var sampleWeight = CalculateSampleWeight(metrics, stability) * gate.SampleWeightMultiplier;
        var eyeWeight = sampleWeight * FeatureWeight(metrics.EyeMeasurementQualityPercent, stability.EyeReliabilityPercent);
        var mouthWeight = sampleWeight * FeatureWeight(metrics.MouthMeasurementQualityPercent, stability.MouthReliabilityPercent);
        _acceptedSampleWeight += sampleWeight;
        _faceReliabilityTotal += stability.CompositeReliabilityPercent * sampleWeight;
        _faceContinuityTotal += stability.FaceContinuityPercent * sampleWeight;
        _eyeReliabilityTotal += stability.EyeReliabilityPercent * sampleWeight;
        _mouthReliabilityTotal += stability.MouthReliabilityPercent * sampleWeight;

        var faceBounds = GetFaceBounds(frame);
        if (faceBounds is Rect bounds)
        {
            _faceCenterX.Add(bounds.Left + bounds.Width / 2d, sampleWeight);
            _faceCenterY.Add(bounds.Top + bounds.Height / 2d, sampleWeight);
            _faceWidth.Add(bounds.Width, sampleWeight);
            _faceHeight.Add(bounds.Height, sampleWeight);
        }

        _headYaw.Add(frame.HeadYawDegrees, sampleWeight);
        _headPitch.Add(frame.HeadPitchDegrees, sampleWeight);
        _headRoll.Add(frame.HeadRollDegrees, sampleWeight);
        _leftEyeOpening.Add(metrics.LeftEyeOpeningRatio, eyeWeight);
        _rightEyeOpening.Add(metrics.RightEyeOpeningRatio, eyeWeight);
        _averageEyeOpening.Add(metrics.AverageEyeOpeningRatio, eyeWeight);
        _eyeAgreement.Add(metrics.LeftEyeOpeningRatio.HasValue && metrics.RightEyeOpeningRatio.HasValue ? metrics.EyeAgreementPercent : null, eyeWeight);
        _mouthOpening.Add(metrics.MouthOpeningRatio, mouthWeight);
        _jawDroop.Add(metrics.JawDroopRatio, mouthWeight);
        _mediaPipeAverageBlink.Add(metrics.MediaPipeAverageEyeBlinkPercent, eyeWeight);
        _mediaPipeJawOpen.Add(metrics.MediaPipeJawOpenPercent, mouthWeight);
        _mediaPipeMouthClose.Add(metrics.MediaPipeMouthClosePercent, mouthWeight);
        if (metrics.EyeImageQualityAvailable)
        {
            _eyeGlare.Add(metrics.EyeGlarePercent, eyeWeight);
            _eyeContrast.Add(metrics.EyeContrastPercent, eyeWeight);
            _eyeSharpness.Add(metrics.EyeSharpnessPercent, eyeWeight);
        }

        if (identityMeasurement.HasMeasurement)
        {
            _identitySignatureSamples++;
            var identityWeight = sampleWeight * 0.74d + Math.Clamp(metrics.TrackingConfidence, 0d, 1d) * 0.26d;
            _faceAspectRatio.Add(identityMeasurement.FaceAspectRatio, identityWeight);
            _interEyeDistanceToFaceWidth.Add(identityMeasurement.InterEyeDistanceToFaceWidth, identityWeight);
            _leftEyeWidthToFaceWidth.Add(identityMeasurement.LeftEyeWidthToFaceWidth, identityWeight);
            _rightEyeWidthToFaceWidth.Add(identityMeasurement.RightEyeWidthToFaceWidth, identityWeight);
            _mouthWidthToFaceWidth.Add(identityMeasurement.MouthWidthToFaceWidth, identityWeight);
            _eyeMidlineYToFaceHeight.Add(identityMeasurement.EyeMidlineYToFaceHeight, identityWeight);
            _mouthCenterYToFaceHeight.Add(identityMeasurement.MouthCenterYToFaceHeight, identityWeight);
            _eyeToMouthYDistanceToFaceHeight.Add(identityMeasurement.EyeToMouthYDistanceToFaceHeight, identityWeight);
        }

        _poseBuckets.Add(frame, metrics, stability, identityMeasurement, sampleWeight);

        if (faceBounds is Rect contourBounds)
        {
            if (!metrics.EyeArtifactSuppressed && !metrics.AnyEyeReconstructed)
            {
                _leftEyeShape.Add(frame.LeftEyeContour, contourBounds, eyeWeight);
                _rightEyeShape.Add(frame.RightEyeContour, contourBounds, eyeWeight);
            }

            if (!metrics.MouthReconstructed)
            {
                _outerLipShape.Add(frame.OuterLipContour, contourBounds, mouthWeight);
                _innerLipShape.Add(frame.InnerLipContour, contourBounds, mouthWeight);
                _jawShape.Add(frame.JawContour, contourBounds, mouthWeight);
            }
        }

        if (metrics.PossibleOneEyeArtifact)
        {
            _possibleOneEyeArtifactSamples++;
        }

        if (metrics.EyeArtifactSuppressed)
        {
            _eyeArtifactSuppressedSamples++;
        }

        if (metrics.LeftEyeReconstructed)
        {
            _leftEyeReconstructedSamples++;
        }

        if (metrics.RightEyeReconstructed)
        {
            _rightEyeReconstructedSamples++;
        }

        if (metrics.MouthReconstructed)
        {
            _mouthReconstructedSamples++;
        }

        if (metrics.MediaPipeEyeOpeningCorrected)
        {
            _mediaPipeEyeOpeningCorrectedSamples++;
        }

        if (metrics.MediaPipeMouthOpeningCorrected)
        {
            _mediaPipeMouthOpeningCorrectedSamples++;
        }

        return new PersonalFaceModelUpdate(true, PersonalFaceModelRejectionKind.None, gate.Reason, sampleWeight, CreateModel(), identityAnalysis);
    }

    private void CountRejectedGate(PersonalFaceModelRejectionKind kind)
    {
        switch (kind)
        {
            case PersonalFaceModelRejectionKind.NoFace:
                _noFaceRejectedSamples++;
                break;
            case PersonalFaceModelRejectionKind.EventLike:
                _eventLikeRejectedSamples++;
                break;
            case PersonalFaceModelRejectionKind.SubjectMismatch:
                _subjectMismatchRejectedSamples++;
                break;
            default:
                _lowQualityRejectedSamples++;
                break;
        }
    }

    private static PersonalFaceModelGate EvaluateGate(
        FaceLandmarkFrame frame,
        FaceLandmarkMetrics metrics,
        FaceLockStabilityAnalysis stability,
        FaceLandmarkCueAnalysis? cueAnalysis,
        FaceLandmarkTrendAnalysis? trendAnalysis,
        bool allowEventLikeMeasurements)
    {
        if (!metrics.HasFace || !frame.HasFace)
        {
            return PersonalFaceModelGate.Reject(PersonalFaceModelRejectionKind.NoFace, "no face lock");
        }

        if (metrics.OverallMeasurementQualityPercent < MinimumOverallQualityPercent)
        {
            return PersonalFaceModelGate.Reject(
                PersonalFaceModelRejectionKind.LowQuality,
                $"overall quality {metrics.OverallMeasurementQualityPercent:0}% below {MinimumOverallQualityPercent:0}%");
        }

        if (stability.SampleCount < 3 || stability.CompositeReliabilityPercent < MinimumReliabilityPercent)
        {
            return PersonalFaceModelGate.Reject(
                PersonalFaceModelRejectionKind.LowQuality,
                $"face reliability {stability.CompositeReliabilityPercent:0}% warming or below {MinimumReliabilityPercent:0}%");
        }

        if (!metrics.IsEyeMeasurementUsable && !metrics.MediaPipeAverageEyeBlinkPercent.HasValue)
        {
            return PersonalFaceModelGate.Reject(PersonalFaceModelRejectionKind.LowQuality, "eye measurement not usable");
        }

        var cueScore = cueAnalysis?.CompositeCuePercent ?? 0d;
        var trendScore = trendAnalysis?.TrendCuePercent ?? 0d;
        if (cueScore >= EventCueRejectThreshold || trendScore >= EventTrendRejectThreshold)
        {
            if (allowEventLikeMeasurements)
            {
                return PersonalFaceModelGate.AcceptEventLike(
                    $"accepted event-like face range/motion measurement; cue {cueScore:0}% or trend {trendScore:0}%");
            }

            return PersonalFaceModelGate.Reject(
                PersonalFaceModelRejectionKind.EventLike,
                $"event-like cue {cueScore:0}% or trend {trendScore:0}%");
        }

        return PersonalFaceModelGate.Accept;
    }

    private PersonalFaceModel CreateModel()
    {
        return new PersonalFaceModel
        {
            SubjectId = _subjectId,
            SubjectDisplayName = _subjectDisplayName,
            SubjectCollectionMode = _subjectCollectionMode,
            UnknownSubjectPolicy = PersonalFaceSubject.UnknownSubjectPolicy,
            IdentityGatePolicy = PersonalFaceSubject.IdentityGatePolicy,
            CreatedAtUtc = _createdAtUtc,
            UpdatedAtUtc = _updatedAtUtc,
            ObservedSamples = _observedSamples,
            AcceptedSamples = _acceptedSamples,
            AcceptedSampleWeight = _acceptedSampleWeight,
            LearningStability = BuildLearningStability(_acceptedSampleWeight),
            RejectedSamples = _rejectedSamples,
            EventLikeRejectedSamples = _eventLikeRejectedSamples,
            LowQualityRejectedSamples = _lowQualityRejectedSamples,
            NoFaceRejectedSamples = _noFaceRejectedSamples,
            SubjectMismatchRejectedSamples = _subjectMismatchRejectedSamples,
            AverageFaceReliabilityPercent = Average(_faceReliabilityTotal, _acceptedSampleWeight),
            AverageFaceContinuityPercent = Average(_faceContinuityTotal, _acceptedSampleWeight),
            AverageEyeReliabilityPercent = Average(_eyeReliabilityTotal, _acceptedSampleWeight),
            AverageMouthReliabilityPercent = Average(_mouthReliabilityTotal, _acceptedSampleWeight),
            FaceCenterX = _faceCenterX.ToModel(),
            FaceCenterY = _faceCenterY.ToModel(),
            FaceWidth = _faceWidth.ToModel(),
            FaceHeight = _faceHeight.ToModel(),
            HeadYawDegrees = _headYaw.ToModel(),
            HeadPitchDegrees = _headPitch.ToModel(),
            HeadRollDegrees = _headRoll.ToModel(),
            LeftEyeOpeningRatio = _leftEyeOpening.ToModel(),
            RightEyeOpeningRatio = _rightEyeOpening.ToModel(),
            AverageEyeOpeningRatio = _averageEyeOpening.ToModel(),
            EyeAgreementPercent = _eyeAgreement.ToModel(),
            MouthOpeningRatio = _mouthOpening.ToModel(),
            JawDroopRatio = _jawDroop.ToModel(),
            MediaPipeAverageEyeBlinkPercent = _mediaPipeAverageBlink.ToModel(),
            MediaPipeJawOpenPercent = _mediaPipeJawOpen.ToModel(),
            MediaPipeMouthClosePercent = _mediaPipeMouthClose.ToModel(),
            EyeGlarePercent = _eyeGlare.ToModel(),
            EyeContrastPercent = _eyeContrast.ToModel(),
            EyeSharpnessPercent = _eyeSharpness.ToModel(),
            IdentitySignatureSamples = _identitySignatureSamples,
            FaceAspectRatio = _faceAspectRatio.ToModel(),
            InterEyeDistanceToFaceWidth = _interEyeDistanceToFaceWidth.ToModel(),
            LeftEyeWidthToFaceWidth = _leftEyeWidthToFaceWidth.ToModel(),
            RightEyeWidthToFaceWidth = _rightEyeWidthToFaceWidth.ToModel(),
            MouthWidthToFaceWidth = _mouthWidthToFaceWidth.ToModel(),
            EyeMidlineYToFaceHeight = _eyeMidlineYToFaceHeight.ToModel(),
            MouthCenterYToFaceHeight = _mouthCenterYToFaceHeight.ToModel(),
            EyeToMouthYDistanceToFaceHeight = _eyeToMouthYDistanceToFaceHeight.ToModel(),
            LeftEyeShape = _leftEyeShape.ToModel(),
            RightEyeShape = _rightEyeShape.ToModel(),
            OuterLipShape = _outerLipShape.ToModel(),
            InnerLipShape = _innerLipShape.ToModel(),
            JawShape = _jawShape.ToModel(),
            PoseBuckets = _poseBuckets.ToModel(),
            PossibleOneEyeArtifactSamples = _possibleOneEyeArtifactSamples,
            EyeArtifactSuppressedSamples = _eyeArtifactSuppressedSamples,
            LeftEyeReconstructedSamples = _leftEyeReconstructedSamples,
            RightEyeReconstructedSamples = _rightEyeReconstructedSamples,
            MouthReconstructedSamples = _mouthReconstructedSamples,
            MediaPipeEyeOpeningCorrectedSamples = _mediaPipeEyeOpeningCorrectedSamples,
            MediaPipeMouthOpeningCorrectedSamples = _mediaPipeMouthOpeningCorrectedSamples
        };
    }

    private static PersonalFaceLearningStability BuildLearningStability(double acceptedSampleWeight)
    {
        var weight = Math.Max(0d, acceptedSampleWeight);
        var maximumNextSampleInfluence = CalculateNextSampleInfluencePercent(weight, MaximumStableSampleWeight);
        var maximumEventLikeWeight = MaximumStableSampleWeight * EventLikeSampleWeightMultiplier;
        var anchorPercent = Math.Clamp(weight / AnchorTargetWeight * 100d, 0d, 100d);

        return new PersonalFaceLearningStability
        {
            AcceptedSampleWeight = weight,
            AnchorTargetWeight = AnchorTargetWeight,
            AnchorPercent = anchorPercent,
            AnchorStatus = anchorPercent switch
            {
                >= 95d => "anchored",
                >= 50d => "warming",
                > 0d => "starting",
                _ => "waiting"
            },
            BaseExponentialMovingAverageAlphaPercent = DistributionEmaAlpha * 100d,
            MaximumStableSampleWeight = MaximumStableSampleWeight,
            EventLikeSampleWeightMultiplier = EventLikeSampleWeightMultiplier,
            MaximumNextSampleInfluencePercent = maximumNextSampleInfluence,
            MaximumEventLikeNextSampleInfluencePercent = CalculateNextSampleInfluencePercent(weight, maximumEventLikeWeight)
        };
    }

    private static double CalculateNextSampleInfluencePercent(double acceptedSampleWeight, double nextSampleWeight)
    {
        var boundedAcceptedWeight = Math.Max(0d, acceptedSampleWeight);
        var boundedNextSampleWeight = Math.Max(0d, nextSampleWeight);
        if (boundedNextSampleWeight <= 0d)
        {
            return 0d;
        }

        return boundedNextSampleWeight / (boundedAcceptedWeight + boundedNextSampleWeight) * 100d;
    }

    private static Rect? GetFaceBounds(FaceLandmarkFrame frame)
    {
        if (frame.FaceContour.Count == 0)
        {
            return null;
        }

        var left = frame.FaceContour.Min(static point => point.X);
        var top = frame.FaceContour.Min(static point => point.Y);
        var right = frame.FaceContour.Max(static point => point.X);
        var bottom = frame.FaceContour.Max(static point => point.Y);
        return right > left && bottom > top
            ? new Rect(left, top, right - left, bottom - top)
            : null;
    }

    private static double CalculateSampleWeight(FaceLandmarkMetrics metrics, FaceLockStabilityAnalysis stability)
    {
        var reliability = Math.Clamp(stability.CompositeReliabilityPercent / 100d, 0d, 1d);
        var continuity = Math.Clamp(stability.FaceContinuityPercent / 100d, 0d, 1d);
        var quality = Math.Clamp(metrics.OverallMeasurementQualityPercent / 100d, 0d, 1d);
        var directObservationPenalty =
            metrics.AnyEyeReconstructed || metrics.MouthReconstructed || metrics.EyeArtifactSuppressed
                ? 0.78d
                : 1d;
        return Math.Clamp(
            (reliability * 0.42d + continuity * 0.24d + quality * 0.34d) * directObservationPenalty,
            0.10d,
            MaximumStableSampleWeight);
    }

    private static double FeatureWeight(double qualityPercent, double reliabilityPercent)
    {
        return Math.Clamp(
            Math.Clamp(qualityPercent / 100d, 0d, 1d) * 0.58d
            + Math.Clamp(reliabilityPercent / 100d, 0d, 1d) * 0.42d,
            0.30d,
            1.10d);
    }

    private static double Average(double total, double weight)
    {
        return weight <= 0d ? 0d : total / weight;
    }

    private sealed class PoseBucketCollectionAccumulator
    {
        private readonly Dictionary<string, PoseBucketAccumulator> _buckets;

        public PoseBucketCollectionAccumulator()
        {
            _buckets = PersonalFacePoseBuckets.Definitions.ToDictionary(
                static definition => definition.BucketId,
                static definition => new PoseBucketAccumulator(definition),
                StringComparer.OrdinalIgnoreCase);
        }

        public void Add(
            FaceLandmarkFrame frame,
            FaceLandmarkMetrics metrics,
            FaceLockStabilityAnalysis stability,
            PersonalFaceIdentityMeasurement identityMeasurement,
            double weight)
        {
            foreach (var definition in PersonalFacePoseBuckets.Classify(
                         metrics.HeadYawDegrees,
                         metrics.HeadPitchDegrees,
                         metrics.HeadRollDegrees))
            {
                if (_buckets.TryGetValue(definition.BucketId, out var bucket))
                {
                    bucket.Add(frame, metrics, stability, identityMeasurement, weight);
                }
            }
        }

        public void Load(IEnumerable<PersonalFacePoseBucketProfile>? profiles)
        {
            foreach (var profile in profiles ?? [])
            {
                if (string.IsNullOrWhiteSpace(profile.BucketId))
                {
                    continue;
                }

                if (!_buckets.TryGetValue(profile.BucketId, out var bucket))
                {
                    var definition = new PersonalFacePoseBucketDefinition(
                        profile.BucketId,
                        profile.Label,
                        profile.Description,
                        profile.CaptureInstruction,
                        profile.PrimaryNeutralReference,
                        profile.RequiredForAvatarCoverage);
                    bucket = new PoseBucketAccumulator(definition);
                    _buckets[profile.BucketId] = bucket;
                }

                bucket.Load(profile);
            }
        }

        public List<PersonalFacePoseBucketProfile> ToModel()
        {
            return PersonalFacePoseBuckets.Definitions
                .Select(static definition => definition.BucketId)
                .Concat(_buckets.Keys.Except(
                    PersonalFacePoseBuckets.Definitions.Select(static definition => definition.BucketId),
                    StringComparer.OrdinalIgnoreCase))
                .Where(_buckets.ContainsKey)
                .Select(bucketId => _buckets[bucketId].ToModel())
                .ToList();
        }
    }

    private sealed class PoseBucketAccumulator
    {
        private readonly PersonalFacePoseBucketDefinition _definition;
        private readonly DistributionAccumulator _headYaw = new();
        private readonly DistributionAccumulator _headPitch = new();
        private readonly DistributionAccumulator _headRoll = new();
        private readonly DistributionAccumulator _faceAspectRatio = new();
        private readonly DistributionAccumulator _interEyeDistanceToFaceWidth = new();
        private readonly DistributionAccumulator _mouthWidthToFaceWidth = new();
        private readonly DistributionAccumulator _eyeMidlineYToFaceHeight = new();
        private readonly DistributionAccumulator _mouthCenterYToFaceHeight = new();
        private readonly DistributionAccumulator _averageEyeOpening = new();
        private readonly DistributionAccumulator _mouthOpening = new();
        private readonly DistributionAccumulator _jawDroop = new();
        private int _sampleCount;
        private double _totalWeight;
        private double _faceReliabilityTotal;
        private double _eyeReliabilityTotal;
        private double _mouthReliabilityTotal;

        public PoseBucketAccumulator(PersonalFacePoseBucketDefinition definition)
        {
            _definition = definition;
        }

        public void Add(
            FaceLandmarkFrame frame,
            FaceLandmarkMetrics metrics,
            FaceLockStabilityAnalysis stability,
            PersonalFaceIdentityMeasurement identityMeasurement,
            double weight)
        {
            if (!frame.HasFace || weight <= 0d)
            {
                return;
            }

            var boundedWeight = Math.Clamp(weight, 0.05d, MaximumStableSampleWeight);
            _sampleCount++;
            _totalWeight += boundedWeight;
            _faceReliabilityTotal += stability.CompositeReliabilityPercent * boundedWeight;
            _eyeReliabilityTotal += stability.EyeReliabilityPercent * boundedWeight;
            _mouthReliabilityTotal += stability.MouthReliabilityPercent * boundedWeight;
            _headYaw.Add(metrics.HeadYawDegrees, boundedWeight);
            _headPitch.Add(metrics.HeadPitchDegrees, boundedWeight);
            _headRoll.Add(metrics.HeadRollDegrees, boundedWeight);
            _averageEyeOpening.Add(metrics.AverageEyeOpeningRatio, boundedWeight);
            _mouthOpening.Add(metrics.MouthOpeningRatio, boundedWeight);
            _jawDroop.Add(metrics.JawDroopRatio, boundedWeight);

            if (identityMeasurement.HasMeasurement)
            {
                _faceAspectRatio.Add(identityMeasurement.FaceAspectRatio, boundedWeight);
                _interEyeDistanceToFaceWidth.Add(identityMeasurement.InterEyeDistanceToFaceWidth, boundedWeight);
                _mouthWidthToFaceWidth.Add(identityMeasurement.MouthWidthToFaceWidth, boundedWeight);
                _eyeMidlineYToFaceHeight.Add(identityMeasurement.EyeMidlineYToFaceHeight, boundedWeight);
                _mouthCenterYToFaceHeight.Add(identityMeasurement.MouthCenterYToFaceHeight, boundedWeight);
            }
        }

        public void Load(PersonalFacePoseBucketProfile profile)
        {
            _sampleCount = Math.Max(0, profile.SampleCount);
            _totalWeight = Math.Max(0d, profile.TotalWeight);
            _faceReliabilityTotal = profile.AverageFaceReliabilityPercent * _totalWeight;
            _eyeReliabilityTotal = profile.AverageEyeReliabilityPercent * _totalWeight;
            _mouthReliabilityTotal = profile.AverageMouthReliabilityPercent * _totalWeight;
            _headYaw.Load(profile.HeadYawDegrees);
            _headPitch.Load(profile.HeadPitchDegrees);
            _headRoll.Load(profile.HeadRollDegrees);
            _faceAspectRatio.Load(profile.FaceAspectRatio);
            _interEyeDistanceToFaceWidth.Load(profile.InterEyeDistanceToFaceWidth);
            _mouthWidthToFaceWidth.Load(profile.MouthWidthToFaceWidth);
            _eyeMidlineYToFaceHeight.Load(profile.EyeMidlineYToFaceHeight);
            _mouthCenterYToFaceHeight.Load(profile.MouthCenterYToFaceHeight);
            _averageEyeOpening.Load(profile.AverageEyeOpeningRatio);
            _mouthOpening.Load(profile.MouthOpeningRatio);
            _jawDroop.Load(profile.JawDroopRatio);
        }

        public PersonalFacePoseBucketProfile ToModel()
        {
            return new PersonalFacePoseBucketProfile
            {
                BucketId = _definition.BucketId,
                Label = _definition.Label,
                Description = _definition.Description,
                CaptureInstruction = _definition.CaptureInstruction,
                PrimaryNeutralReference = _definition.PrimaryNeutralReference,
                RequiredForAvatarCoverage = _definition.RequiredForAvatarCoverage,
                SampleCount = _sampleCount,
                TotalWeight = _totalWeight,
                HeadYawDegrees = _headYaw.ToModel(),
                HeadPitchDegrees = _headPitch.ToModel(),
                HeadRollDegrees = _headRoll.ToModel(),
                FaceAspectRatio = _faceAspectRatio.ToModel(),
                InterEyeDistanceToFaceWidth = _interEyeDistanceToFaceWidth.ToModel(),
                MouthWidthToFaceWidth = _mouthWidthToFaceWidth.ToModel(),
                EyeMidlineYToFaceHeight = _eyeMidlineYToFaceHeight.ToModel(),
                MouthCenterYToFaceHeight = _mouthCenterYToFaceHeight.ToModel(),
                AverageEyeOpeningRatio = _averageEyeOpening.ToModel(),
                MouthOpeningRatio = _mouthOpening.ToModel(),
                JawDroopRatio = _jawDroop.ToModel(),
                AverageFaceReliabilityPercent = Average(_faceReliabilityTotal, _totalWeight),
                AverageEyeReliabilityPercent = Average(_eyeReliabilityTotal, _totalWeight),
                AverageMouthReliabilityPercent = Average(_mouthReliabilityTotal, _totalWeight)
            };
        }
    }

    private sealed class DistributionAccumulator
    {
        private int _count;
        private double _totalWeight;
        private double _weightedSum;
        private double _weightedSumSquares;
        private double? _minimum;
        private double? _maximum;
        private double? _ema;

        public void Add(double? value, double weight)
        {
            if (value is not double number || double.IsNaN(number) || double.IsInfinity(number))
            {
                return;
            }

            var boundedWeight = Math.Clamp(weight, 0.05d, MaximumStableSampleWeight);
            _count++;
            _totalWeight += boundedWeight;
            _weightedSum += number * boundedWeight;
            _weightedSumSquares += number * number * boundedWeight;
            _minimum = _minimum is double min ? Math.Min(min, number) : number;
            _maximum = _maximum is double max ? Math.Max(max, number) : number;
            var effectiveAlpha = DistributionEmaAlpha * Math.Clamp(boundedWeight, 0.35d, MaximumStableSampleWeight);
            _ema = _ema is double previous
                ? previous + (number - previous) * effectiveAlpha
                : number;
        }

        public void Load(PersonalMetricDistribution? distribution)
        {
            _count = Math.Max(0, distribution?.SampleCount ?? 0);
            _totalWeight = Math.Max(0d, distribution?.TotalWeight ?? 0d);
            _weightedSum = 0d;
            _weightedSumSquares = 0d;
            _minimum = Clean(distribution?.Minimum);
            _maximum = Clean(distribution?.Maximum);
            _ema = Clean(distribution?.ExponentialMovingAverage);

            var average = Clean(distribution?.Average);
            if (_count <= 0 || _totalWeight <= 0d || average is not double mean)
            {
                _count = 0;
                _totalWeight = 0d;
                _minimum = null;
                _maximum = null;
                _ema = null;
                return;
            }

            var standardDeviation = Math.Max(0d, Clean(distribution?.StandardDeviation) ?? 0d);
            _weightedSum = mean * _totalWeight;
            _weightedSumSquares = (standardDeviation * standardDeviation + mean * mean) * _totalWeight;
        }

        public PersonalMetricDistribution ToModel()
        {
            double? average = _totalWeight <= 0d ? null : _weightedSum / _totalWeight;
            double? standardDeviation = _count <= 1 || average is not double mean
                ? null
                : Math.Sqrt(Math.Max(0d, _weightedSumSquares / _totalWeight - mean * mean));
            return new PersonalMetricDistribution
            {
                SampleCount = _count,
                TotalWeight = _totalWeight,
                Average = average,
                Minimum = _minimum,
                Maximum = _maximum,
                StandardDeviation = standardDeviation,
                ExponentialMovingAverage = _ema,
                NormalLow = average is double lowMean && standardDeviation is double lowStd
                    ? lowMean - lowStd * 2d
                    : _minimum,
                NormalHigh = average is double highMean && standardDeviation is double highStd
                    ? highMean + highStd * 2d
                    : _maximum
            };
        }

        private static double? Clean(double? value)
        {
            return value is double number && !double.IsNaN(number) && !double.IsInfinity(number)
                ? number
                : null;
        }
    }

    private sealed class ContourShapeAccumulator
    {
        private readonly string _featureId;
        private readonly string _label;
        private readonly int _pointCount;
        private readonly bool _closed;
        private readonly DistributionAccumulator[] _x;
        private readonly DistributionAccumulator[] _y;
        private int _sampleCount;
        private double _totalWeight;

        public ContourShapeAccumulator(string featureId, string label, int pointCount, bool closed)
        {
            _featureId = featureId;
            _label = label;
            _pointCount = Math.Max(2, pointCount);
            _closed = closed;
            _x = Enumerable.Range(0, _pointCount).Select(_ => new DistributionAccumulator()).ToArray();
            _y = Enumerable.Range(0, _pointCount).Select(_ => new DistributionAccumulator()).ToArray();
        }

        public void Add(IReadOnlyList<Point> contour, Rect faceBounds, double weight)
        {
            if (contour.Count < (_closed ? 4 : 3)
                || faceBounds.Width <= 0d
                || faceBounds.Height <= 0d)
            {
                return;
            }

            var sampled = Resample(contour, _pointCount, _closed);
            if (sampled.Count != _pointCount)
            {
                return;
            }

            var boundedWeight = Math.Clamp(weight, 0.05d, MaximumStableSampleWeight);
            _sampleCount++;
            _totalWeight += boundedWeight;
            for (var index = 0; index < sampled.Count; index++)
            {
                var point = sampled[index];
                _x[index].Add((point.X - faceBounds.Left) / faceBounds.Width, boundedWeight);
                _y[index].Add((point.Y - faceBounds.Top) / faceBounds.Height, boundedWeight);
            }
        }

        public void Load(PersonalFaceContourShapeProfile? profile)
        {
            _sampleCount = Math.Max(0, profile?.SampleCount ?? 0);
            _totalWeight = Math.Max(0d, profile?.TotalWeight ?? 0d);
            var points = profile?.Points ?? [];
            for (var index = 0; index < _pointCount; index++)
            {
                var point = points.FirstOrDefault(point => point.Index == index);
                _x[index].Load(point?.X);
                _y[index].Load(point?.Y);
            }
        }

        public PersonalFaceContourShapeProfile ToModel()
        {
            return new PersonalFaceContourShapeProfile
            {
                FeatureId = _featureId,
                Label = _label,
                Closed = _closed,
                PointCount = _pointCount,
                SampleCount = _sampleCount,
                TotalWeight = _totalWeight,
                Points = Enumerable.Range(0, _pointCount)
                    .Select(index => new PersonalFaceContourShapePointProfile
                    {
                        Index = index,
                        X = _x[index].ToModel(),
                        Y = _y[index].ToModel()
                    })
                    .ToList()
            };
        }

        private static IReadOnlyList<Point> Resample(IReadOnlyList<Point> contour, int pointCount, bool closed)
        {
            if (contour.Count < 2 || pointCount < 2)
            {
                return [];
            }

            var path = contour.ToList();
            if (closed)
            {
                path.Add(contour[0]);
            }

            var distances = new double[path.Count];
            for (var index = 1; index < path.Count; index++)
            {
                distances[index] = distances[index - 1] + Distance(path[index - 1], path[index]);
            }

            var total = distances[^1];
            if (total <= 0d)
            {
                return [];
            }

            var result = new List<Point>(pointCount);
            for (var index = 0; index < pointCount; index++)
            {
                var targetDistance = closed
                    ? total * index / pointCount
                    : total * index / (pointCount - 1);
                result.Add(InterpolateAt(path, distances, targetDistance));
            }

            return result;
        }

        private static Point InterpolateAt(IReadOnlyList<Point> path, IReadOnlyList<double> distances, double targetDistance)
        {
            for (var index = 1; index < path.Count; index++)
            {
                if (distances[index] < targetDistance)
                {
                    continue;
                }

                var startDistance = distances[index - 1];
                var segmentLength = Math.Max(0.000001d, distances[index] - startDistance);
                var t = Math.Clamp((targetDistance - startDistance) / segmentLength, 0d, 1d);
                var a = path[index - 1];
                var b = path[index];
                return new Point(
                    a.X + (b.X - a.X) * t,
                    a.Y + (b.Y - a.Y) * t);
            }

            return path[^1];
        }

        private static double Distance(Point a, Point b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    private sealed record PersonalFaceModelGate(bool Accepted, PersonalFaceModelRejectionKind Kind, string Reason, double SampleWeightMultiplier)
    {
        public static PersonalFaceModelGate Accept { get; } = new(true, PersonalFaceModelRejectionKind.None, "accepted stable measurement", 1d);

        public static PersonalFaceModelGate AcceptEventLike(string reason)
        {
            return new PersonalFaceModelGate(true, PersonalFaceModelRejectionKind.None, reason, EventLikeSampleWeightMultiplier);
        }

        public static PersonalFaceModelGate Reject(PersonalFaceModelRejectionKind kind, string reason)
        {
            return new PersonalFaceModelGate(false, kind, reason, 0d);
        }
    }
}
