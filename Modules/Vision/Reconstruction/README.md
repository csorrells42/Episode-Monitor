# Vision Reconstruction

Namespace: `EpisodeMonitor.Modules.Vision.Reconstruction`

This module is the reusable boundary for future 3D face reconstruction and avatar-preview work. It does not run MediaPipe, OpenCV, PyTorch, Deep3D, WSL, or a renderer directly. It defines subject-gated job and result contracts so Episode Monitor can export clean evidence to an out-of-process reconstruction worker.

Files:

- `FaceReconstructionBackendIds.cs`: stable backend identifiers, including the measurement-only preview path and a Deep3D/PyTorch sidecar slot.
- `MeasurementFacePreviewPoint.cs`: normalized point DTO for the first measurement-only face wireframe, including provenance and confidence.
- `MeasurementFacePreviewPolyline.cs`: normalized line DTO for face, eye, mouth, jaw, nose, and droop paths, including provenance and confidence.
- `MeasurementFacePreviewModel.cs`: subject-gated preview payload built from personal-model distributions plus a clearly labeled low-trust canonical template prior when measurements are not mature yet.
- `MeasurementFacePreviewBuilder.cs`: converts the rolling personal face model into a normalized wireframe with eye opening, mouth opening, jaw droop, head pose, confidence, warnings, learned aggregate contour profiles as the preferred eye/lip/jaw outlines, and measured-vs-template contribution percentages.
- `MeasurementFacePreviewStore.cs`: writes `measurement_face_preview.json` and a dependency-free dark HTML preview with an interactive canvas 3D construction view plus SVG fallback beside `personal_face_model.json`, `personal_face_motion_model.json`, `personal_face_corpus_readiness.json`, and `personal_face_corpus_readiness.html`.
- `MeasurementAvatarTrainingMetric.cs`: JSON-friendly metric DTO with samples, weight, averages, ranges, moving average, normal band, units, and avatar-use text.
- `MeasurementAvatarReadinessScores.cs`: compact readiness score DTO copied from corpus readiness for package consumers, including contour-shape, direct feature-trust, and capture-quality coverage.
- `MeasurementAvatarTrainingArtifact.cs`: source artifact DTO listing related measurement files and explicitly marking whether they contain raw pixels or continuous video.
- `MeasurementAvatarTrainingPackage.cs`: subject-gated package payload for future avatar/3D consumers. It groups neutral face, motion, identity, aggregate contour shape, direct feature-trust, quality, capture-quality metrics, and measured-vs-template provenance with readiness, storage budget, warnings, suggestions, integration notes, and the digital-representation safety boundary.
- `MeasurementAvatarTrainingPackageBuilder.cs`: combines the personal model, motion model, readiness report, subject gate, and measurement-journal size into the package without storing raw frames, images, video, full meshes, or per-frame contour dumps.
- `MeasurementAvatarTrainingPackageStore.cs`: writes `measurement_avatar_training_package.json` and `measurement_avatar_training_package.html`.
- `MeasurementAvatarCapturePlanItem.cs`: one prioritized measurement-only capture task with instructions, reason, related readiness score, target minutes, estimated measurement bytes, and completion criteria.
- `MeasurementAvatarCapturePlan.cs`: subject-gated capture plan payload with session checks, stop rules, total target time, storage estimate, and collection decision.
- `MeasurementAvatarCapturePlanBuilder.cs`: turns corpus readiness gaps, including weak contour-shape coverage, weak behind-glasses eye trust, weak mouth/jaw trust, and weak avatar-grade capture quality, into the next safe capture plan while keeping passive learning measurement-only and under the storage budget.
- `MeasurementAvatarCapturePlanStore.cs`: writes `measurement_avatar_capture_plan.json` and `measurement_avatar_capture_plan.html`.
- `FaceReconstructionSourceKinds.cs`: stable source-kind strings for personal models, measurement journals, explicit training images, and explicit training video frames.
- `FaceReconstructionSubjectGate.cs`: subject identity gate copied from the personal model, with manual confirmation first, warmup extreme-mismatch protection, and room for stronger automatic identity confidence as the measurement signature matures.
- `FaceReconstructionSourceFrame.cs`: metadata for explicit avatar-training media; it references files and quality scores instead of storing image bytes.
- `FaceReconstructionWorkItem.cs`: portable job manifest for a reconstruction worker.
- `FaceReconstructionResult.cs`: portable result manifest for meshes, coefficients, textures, preview renders, and quality reports.
- `Deep3DFaceReconstructionSidecarSpec.cs`: sidecar contract for a Linux/WSL Deep3D-style worker.
- `FaceReconstructionJobStore.cs`: JSON read/write helper for work items.

Rules:

- Never learn from an unconfirmed or unknown subject.
- Keep raw continuous webcam video out of passive learning.
- Use explicit training images or deliberate training clips only when photoreal 3D reconstruction needs pixels.
- Keep worker-specific dependencies out of the WPF app. A sidecar can be Linux-only as long as the contract is JSON and the app can inspect the result.

The measurement-only preview is the first visible "digital me" artifact. It is not photoreal and does not claim to reconstruct skin texture or identity. It gives the user a stable way to inspect how the accepted measurements are shaping the face, aggregate eye/lip/jaw outlines, eyelid aperture, mouth aperture, jaw droop, and pose over time while keeping passive storage small. The preview page includes an interactive dependency-free canvas construction view driven by the same measurement points as the JSON contract, with the SVG outline retained as a fallback. The preview may start from a canonical face scaffold once the subject gate is accepted, but that scaffold is tagged as template-prior geometry and is not written back into the personal model or counted as observed evidence. As measurements arrive, the live preview and package report measured contribution versus remaining template-prior contribution so future 3D consumers can prefer real subject data and treat the scaffold as temporary topology only. The measurement avatar training package is the machine-readable bridge from this corpus to future avatar work: it exports the slowly weighted neutral face, motion, identity, aggregate contour-shape, direct feature-trust, quality, readiness summaries, and provenance policy as a bounded contract that a separate renderer, 3D fitter, or main AI program can consume. The capture plan closes the loop by converting readiness gaps into the next symptom-free, subject-confirmed collection session so the corpus improves deliberately instead of collecting random redundant data.

Use `personal_face_corpus_readiness.json`, `personal_face_corpus_readiness.html`, `personal_face_collection_audit.json`, and `personal_face_collection_audit.html` from `Vision/Personalization` before scheduling explicit avatar-training media. The readiness files report whether passive measurements already cover enough face scale, head pose, eyelid range, mouth/jaw range, aggregate eye/lip/jaw contour shape, direct eye-behind-glasses trust, direct mouth/jaw trust, capture quality, measurement quality, and motion pairs. The collection audit explains why attempted frames were or were not learned, including face lock, subject gate, event/calibration hold, low camera mode, glasses/eye evidence, mouth evidence, and avatar-grade capture issues.
