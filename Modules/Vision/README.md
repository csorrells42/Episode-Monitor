# Vision Module

Namespace root: `EpisodeMonitor.Modules.Vision`

This module owns visible-face evidence only. It should answer: where is the face, where are the eyes/lips, how open are they, how trustworthy is the measurement, and whether the trend is changing.

Recognition responsibilities should stay split into reusable capability layers. Face localization/identity gating, eyelid opening and droop, jaw/lip opening and droop, measurement quality, temporal reconstruction, personalization, and future avatar/3D reconstruction can share data models, but each should remain callable without the WPF shell. When a new model or AI wrapper is added, put the backend adapter in its library folder and keep the decision/measurement logic in `Analysis`, `Personalization`, or a new focused submodule with backend-neutral DTOs.

## Common

Namespace: `EpisodeMonitor.Modules.Vision.Common`

Backend-neutral contracts and frame/result models:

- `FaceCueGuideLayout.cs`: normalized guide-box layout used by cue tracking and overlay alignment.
- `FaceFeatureDetection.cs`: coarse face/eye/mouth boxes, contours, confidence, and image-quality diagnostics emitted by region trackers.
- `FaceFeatureDetectionExtensions.cs`: conversion from feature detections into landmark frames.
- `FaceLandmarkFrame.cs`: backend-neutral face, eye, lip, and jaw landmarks plus reconstruction flags.
- `FaceLandmarkTrackingResult.cs`: one tracker result with frame, backend status, and availability information.
- `IFaceLandmarkTracker.cs`: common contract for stateless landmark trackers.
- `IStatefulFaceLandmarkTracker.cs`: common contract for trackers that keep temporal state and need reset/dispose semantics.

Change this folder when backend-neutral data shapes or tracker interfaces need to change.

## Analysis

Namespace: `EpisodeMonitor.Modules.Vision.Analysis`

Backend-neutral measurement and scoring logic.

Files:

- `ContourOpeningEstimator.cs`: measures eye/lip opening from normalized contours without depending on OpenCV.
- `EyeInsetAgreementAnalyzer.cs`: compares full-frame eye opening measurements with a zoomed eye-inset reference, including paired rate, correlation, normalized error, direction agreement, slope-direction agreement, and an agreement trust score.
- `EyeInsetCueAnalysis.cs`: result object for baseline-relative zoomed eye-inset closure evidence.
- `EyeInsetCueAnalyzer.cs`: builds an awake baseline from high-quality zoomed eye-inset measurements and turns later eyelid closure into a cue score.
- `FaceCueAnalysis.cs`: result object for baseline-relative eye/jaw cue scoring.
- `FaceCueAnalyzer.cs`: image-region baseline analyzer used for older guide-box eye/jaw cues.
- `FaceCueAutoLayoutEstimator.cs`: estimates default guide-box placement from a face feature detection.
- `FaceLandmarkCueAnalysis.cs`: result object for landmark-driven eyelid, mouth, and structural jaw-droop cue scoring, including baseline-relative MediaPipe blink and mouth corroboration fields.
- `FaceLandmarkCueAnalyzer.cs`: compares current landmark metrics against an awake baseline, turns structural jaw-contour droop and MediaPipe blendshapes into baseline-relative corroboration, and produces cue eligibility/scores.
- `FaceLandmarkMetricCalculator.cs`: turns landmark contours into raw and smoothed eye/mouth opening metrics, structural jaw-contour droop, quality, asymmetry, and velocity; when MediaPipe blendshapes are available, it conservatively stabilizes false-open eyelid/lip contours and false-closed lip contours while keeping raw contour values and signed correction deltas for audit. The blink stabilizer ramps in early for glasses-heavy frames only after an open-eye reference has been learned.
- `FaceLandmarkMetrics.cs`: current-frame landmark measurement record consumed by overlays, triggers, summaries, and evaluators, including raw eye/mouth openings, working corrected/smoothed openings, MediaPipe/guard correction ratios, and corrected-frame flags. Offline evaluator CSV/HTML output keeps raw-versus-working values visible so glasses or fallback-contour repairs can be audited frame by frame.
- `FaceLandmarkTemporalReconstructor.cs`: repairs short missing-eye/mouth gaps, mirrors first-frame missing-eye geometry from the paired eye, suppresses likely glasses/occlusion shape artifacts, remembers safe eye geometry instead of rejected glare contours, and prevents low-fidelity fallback contours from rapidly reopening eyelids without stronger evidence.
- `FaceLandmarkTrendAnalysis.cs`: result object for rolling eye-closing and mouth-opening trend scoring.
- `FaceLandmarkTrendAnalyzer.cs`: robust rolling-slope trend detector for gradual eyelid closure and jaw/lip opening.
- `IFaceCueAnalyzer.cs`: contract for guide-box cue analyzers.

This folder should not know which camera backend produced a frame.

Change this folder when trigger math, calibration behavior, quality gates, reconstruction behavior, or trend detection needs work.

## OpenCv

Namespace: `EpisodeMonitor.Modules.Vision.OpenCv`

OpenCV-backed implementations.

Files:

- `ApertureRegionRefiner.cs`: probes nearby eye/mouth boxes and chooses the strongest aperture evidence when initial boxes are slightly wrong.
- `EyeInsetApertureAnalyzer.cs`: measures the bottom-right zoomed eye inset used by synthetic/sample videos.
- `FaceCandidateSelector.cs`: chooses the best face candidate while preserving continuity with the previously tracked head, with a delayed high-confidence reacquire path for camera auto-follow reframes.
- `OpenCvApertureEstimator.cs`: low-level aperture extraction and image-quality diagnostics for eyes and mouth; uses managed 3x3 smoothing before thresholding to avoid native OpenCV blur failures on bad candidate regions.
- `OpenCvApertureLandmarkTracker.cs`: fallback landmark tracker built from detected face boxes and aperture contours.
- `OpenCvFaceFeatureTracker.cs`: face/eye/mouth region tracker using YuNet/Haar/aperture heuristics.
- `OpenCvFacemarkLandmarkTracker.cs`: OpenCV LBF 68-point landmark backend.
- `OpenCvFacemarkModelInfo.cs`: portable LBF model manifest/path/status reader.
- `OpenCvYuNetFaceDetector.cs`: YuNet ONNX face detector wrapper.
- `OpenCvYuNetModelInfo.cs`: portable YuNet model path/status reader.
- `README.md`: OpenCvSharp reference notes, package-family guidance, CUDA boundary, and resource-lifetime rules.

Keep OpenCvSharp types contained here where possible.

Change this folder when OpenCV detection/tracking behavior, model loading, glasses-related aperture refinement, or synthetic evaluator measurements need work.

## MediaPipe

Namespace: `EpisodeMonitor.Modules.Vision.MediaPipe`

Target home for MediaPipe Face Landmarker integration. The proof-of-concept Python/MediaPipe sidecar lives behind the same tracker interface as OpenCV so the UI and episode pipeline do not care whether landmarks come from Python, OpenCV, or a later native runtime.

Files:

- `DenseFaceLandmarkModelInfo.cs`: MediaPipe dense face landmark manifest/model/runtime status reader.
- `DenseFaceMeshLandmarkTracker.cs`: reserved dense tracker slot; currently reports unavailable until an inference bridge is implemented.
- `MediaPipeFaceLandmarkerSidecarTracker.cs`: stateful tracker adapter that prefers the local Python sidecar when model, script, Python, and imports are ready.
- `MediaPipeFaceLandmarkerSidecarClient.cs`: process runner for the sidecar, including JSON line requests, JPEG frame encoding, timeouts, and restart-on-failure behavior.
- `MediaPipeFaceLandmarkerMapper.cs`: maps dense MediaPipe landmarks and blendshape scores into backend-neutral face, eye, lip, jaw, blink, and jaw-open evidence.
- `MediaPipeSidecarProtocol.cs`: request/response DTOs shared by the C# sidecar client, including dense landmarks and Face Landmarker blendshapes.
- `MediaPipeSidecarPythonEnvironment.cs`: finds the sidecar script, model file, configured Python executable, and import readiness.
- `README.md`: official MediaPipe Solutions/Face Landmarker reference notes and sidecar boundary rules.
- `Sidecar/mediapipe_face_landmarker_sidecar.py`: Python MediaPipe Tasks Face Landmarker process that returns dense normalized landmarks.
- `Sidecar/requirements.txt`: Python package requirements for the local proof-of-concept sidecar.

Change this folder when adding or debugging MediaPipe Face Landmarker integration.

## Pipeline

Namespace: `EpisodeMonitor.Modules.Vision.Pipeline`

Composition layer. `CompositeFaceLandmarkTracker` owns backend order and fusion rules, then emits the shared landmark frame/metrics path used by overlays, triggers, summaries, and offline evaluation.

Files:

- `CompositeFaceLandmarkTracker.cs`: orchestrates dense/MediaPipe, OpenCV LBF, and aperture fallback trackers; protects strong dense/MediaPipe contours from lower-confidence aperture overrides while still allowing aperture repair for weak model/LBF eye and mouth contours.

Change this folder when backend priority, fallback rules, or multi-backend fusion behavior needs work.

## Personalization

Namespace: `EpisodeMonitor.Modules.Vision.Personalization`

Transparent per-person face-motion modeling.

Files:

- `PersonalMetricDistribution.cs`: JSON-friendly summary for learned averages, ranges, standard deviation, and moving averages for one metric.
- `PersonalFaceLearningStability.cs`: JSON-friendly slow-learning policy and stability summary, including accepted weight, anchor status, and maximum next-sample influence so avatar consumers do not treat one new session as a rewrite.
- `PersonalFaceContourShapeProfile.cs`: JSON-friendly aggregate shape profile for fixed resampled eye, lip, and jaw contours. It stores weighted face-local point distributions only, not per-frame raw contours.
- `PersonalFacePoseBucketProfile.cs`: JSON-friendly straight-on, yaw, pitch, and roll pose buckets. These keep neutral identity proportions separate from turned-head evidence while still storing compact aggregate measurements only.
- `PersonalFaceSubject.cs`: constants for subject id, manual confirmation mode, future automatic identity-lock mode, and reject-unknown-subject policy.
- `PersonalFaceModel.cs`: durable model object for personal face position, scale, head pose, eye opening, mouth opening, jaw droop, MediaPipe blendshape baselines, aggregate eye/lip/jaw shape profiles, pose bucket profiles, quality diagnostics, reconstruction counts, accepted/rejected sample counts, and the slow-learning stability block.
- `PersonalFaceModelBuilder.cs`: conservative online updater that accepts only high-reliability, high-quality, non-event-like frames and rejects no-face, low-quality, and symptom-like samples with an auditable reason. Live and evaluator callers run the capture-quality preflight before allowing this builder to learn from a frame. The builder updates aggregate shape profiles only from direct observations, so reconstructed or artifact-suppressed eye/lip contours do not become shape truth. Accepted frames also update pose buckets so front-neutral identity, left/right turns, up/down tilt, and head roll can be audited separately.
- `PersonalFaceModelUpdate.cs`: per-frame accept/reject result used by overlays, evaluator CSV output, and later corpus dashboards.
- `PersonalFaceModelStore.cs`: JSON writer for live app, evaluator, and synthetic stress outputs.
- `PersonalFaceCaptureQualityInput.cs`, `PersonalFaceCaptureQualityAssessment.cs`, `PersonalFaceCaptureQualityAnalyzer.cs`: backend-neutral live capture-quality gate for long-term measurement collection. It combines camera mode, normalized face scale, eye/mouth evidence, temporal stability, glasses/artifact risk, subject-gate state, and storage headroom into an inspectable score and correction suggestions.
- `PersonalFaceMeasurementSample.cs`: compact accepted-frame measurement record for pose bucket ids, face box, eye/mouth/jaw opening, MediaPipe blendshapes, capture-quality verdict, quality, reliability, and artifact flags; it intentionally stores no pixels, images, video paths, or full landmark meshes.
- `PersonalFaceMeasurementJournal.cs`: append-only daily JSONL writer for accepted and capture-quality-collectable samples with a default `10,000,000,000` byte passive-learning budget and oldest-file pruning.
- `PersonalFaceIdentityMeasurement.cs`, `PersonalFaceIdentityAnalyzer.cs`, `PersonalFaceIdentityAnalysis.cs`, `PersonalFaceIdentityFeatureScore.cs`: measurement-only subject identity signature. These classes compare normalized face geometry such as face aspect ratio, eye spacing, eye widths, mouth width, and eye/mouth vertical placement. They keep manual confirmation as the first gate, reject extreme warmup mismatches after a usable subject-confirmed signature exists, and then pause likely non-subject learning after a stronger baseline exists. They do not store images, raw contours, or full landmark meshes.
- `PersonalFaceMotionObservation.cs`: measurement-only observation DTO for eye opening, mouth opening, jaw droop, head pose, blendshapes, quality, and reconstruction/artifact flags.
- `PersonalFaceMotionModel.cs`: durable motion-profile JSON object with velocity distributions, coupling rates, quality/reliability summaries, and storage-policy warnings for future avatar/assistant consumers.
- `PersonalFaceMotionModelBuilder.cs`: builds weighted motion distributions from ordered observations without storing raw frames, images, video, or full landmark meshes.
- `PersonalFaceMotionModelStore.cs`: JSON writer for `personal_face_motion_model.json` in live app, evaluator, and synthetic stress outputs.
- `PersonalFaceCorpusReadiness.cs`: durable readiness/report JSON object for extreme-accuracy corpus coverage, learning-stability anchoring, pose bucket coverage, aggregate contour-shape coverage, direct eye-behind-glasses and mouth/jaw trust, capture-quality coverage, storage health, warnings, and next capture suggestions.
- `PersonalFaceCorpusReadinessBuilder.cs`: scores baseline sample count, slow-learning anchor strength, motion pairs, face-size/distance range, head-pose range, explicit pose bucket coverage, eye/mouth/jaw expression range, aggregate eye/lip/jaw contour-shape profiles, direct feature trust, measurement quality, capture-quality gate pass rate, avatar-grade rate, and storage headroom.
- `PersonalFaceCorpusReadinessStore.cs`: JSON and dark HTML writer for `personal_face_corpus_readiness.json` and `personal_face_corpus_readiness.html` in live app, evaluator, and synthetic stress outputs, including pose bucket, contour-shape, direct feature-trust, and capture-quality score bars plus issue rollups.
- `PersonalFaceCollectionAuditObservation.cs`: one measurement-only reviewed-frame attempt with subject gate, event/calibration hold, face-lock state, personal-model accept/reject reason, capture-quality scores, issues, and suggestions.
- `PersonalFaceCollectionAudit.cs`: durable collection audit/report JSON object explaining why attempted frames were or were not learned, including face-lock rate, subject-gate rate, accepted rate, collectable/avatar-grade rates, rejection counts, quality subscores, top issues, and next actions.
- `PersonalFaceCollectionAuditBuilder.cs`: rolls live/evaluator/synthetic reviewed-frame observations into the collection audit without keeping pixels, frame images, raw contours, video, or full meshes.
- `PersonalFaceCollectionAuditStore.cs`: JSON and dark HTML writer for `personal_face_collection_audit.json` and `personal_face_collection_audit.html`.

Change this folder when the app needs better subject-specific baselines, long-running personalization, future 3D/avatar corpus fields, motion/animation measurements, corpus-readiness guidance, subject identity-lock gating, or different rules for which frames are safe to learn from. This folder should not own camera capture, event recording, or UI controls.

## Reconstruction

Namespace: `EpisodeMonitor.Modules.Vision.Reconstruction`

Reusable 3D face reconstruction and avatar-preview contracts. This folder owns JSON work-item/result shapes for a future local worker, including a Deep3D/PyTorch sidecar slot that can run under WSL2/Linux without pulling PyTorch or renderer dependencies into the WPF app.

Files:

- `FaceReconstructionBackendIds.cs`: stable backend ids for measurement-only preview and Deep3D/PyTorch sidecar reconstruction.
- `MeasurementFacePreviewPoint.cs`: JSON-friendly normalized point used by the first measurement-only preview, with point-level provenance and confidence.
- `MeasurementFacePreviewPolyline.cs`: JSON-friendly line/path object for face, eye, lip, jaw, nose, and droop preview geometry, with path-level provenance and confidence.
- `MeasurementFacePreviewModel.cs`: subject-gated preview artifact built from the personal model plus a labeled low-trust canonical template prior when measured data is still sparse.
- `MeasurementFacePreviewBuilder.cs`: turns accepted per-subject distributions into a normalized 3D-ish wireframe for face shape, eye opening, mouth opening, jaw droop, and head pose, while reporting measured-vs-template contribution so early preview geometry is never confused with learned truth. When available, it prefers the front-neutral pose bucket for neutral identity geometry so turned-head measurements do not distort the straight-on preview.
- `MeasurementFacePreviewStore.cs`: writes `measurement_face_preview.json` and `measurement_face_preview.html` beside the personal face model, including an interactive canvas 3D construction view plus SVG fallback.
- `MeasurementAvatarTrainingMetric.cs`, `MeasurementAvatarReadinessScores.cs`, `MeasurementAvatarTrainingArtifact.cs`, `MeasurementAvatarTrainingPackage.cs`: JSON-friendly measurement-only avatar package DTOs for neutral face metrics, motion metrics, identity metrics, pose bucket profiles, aggregate contour-shape profiles, direct feature-trust scores, quality/capture-quality context, readiness scores, source artifact names, safety boundary, and integration notes.
- `MeasurementAvatarTrainingPackageBuilder.cs`: combines the personal face model, motion model, corpus readiness report, subject gate, and storage usage into one bounded contract for future 3D/avatar consumers.
- `MeasurementAvatarTrainingPackageStore.cs`: writes `measurement_avatar_training_package.json` and a dark dependency-free HTML inspector beside the other personal model artifacts.
- `MeasurementAvatarCapturePlanItem.cs`, `MeasurementAvatarCapturePlan.cs`: JSON-friendly capture-plan DTOs for prioritized subject-gated, symptom-free, measurement-only data collection tasks.
- `MeasurementAvatarCapturePlanBuilder.cs`: turns readiness gaps into concrete next-session tasks for baseline, distance, pose, expression, identity, contour-shape, behind-glasses eye trust, mouth/jaw trust, quality, capture-quality, motion, and storage coverage.
- `MeasurementAvatarCapturePlanStore.cs`: writes `measurement_avatar_capture_plan.json` and a dark dependency-free HTML inspector beside the other personal model artifacts.
- `FaceReconstructionSourceKinds.cs`: stable source-kind strings for personal models, measurement journals, explicit training images, and explicit training video frames.
- `FaceReconstructionSubjectGate.cs`: subject gate copied from the personal model, requiring manual confirmation now and allowing automatic identity confidence later.
- `FaceReconstructionSourceFrame.cs`: metadata for explicit avatar-training media without storing image bytes in the manifest.
- `FaceReconstructionWorkItem.cs`: portable reconstruction job manifest.
- `FaceReconstructionResult.cs`: portable output manifest for meshes, coefficients, textures, preview renders, and quality reports.
- `Deep3DFaceReconstructionSidecarSpec.cs`: contract constants and factory for a Deep3D-style Linux/WSL sidecar.
- `FaceReconstructionJobStore.cs`: JSON read/write helper for work items.

Change this folder when adding a 3D reconstruction worker, avatar preview, mesh/coefficients export, or a main-AI integration path that consumes the face-motion corpus. The current measurement-only preview is intentionally not photoreal; it is a small inspectable artifact so the user can watch the subject-gated personal model take shape over time without storing passive webcam footage. Keep reconstruction worker dependencies out of the WPF app; the app should write/read manifests and let sidecars do backend-specific work.
