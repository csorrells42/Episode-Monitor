# Episode Monitor

Standalone WPF camera-based low-motion episode monitor.

This app records observable sustained low-motion events, optional start/end snapshots, event video clips, manual markers, and CSV exports for review. It is a data-gathering tool only and does not diagnose a medical condition.

The output folder is user-selectable and can be set to an external HDD. Detected events are saved into timestamped folders containing the event video, snapshots, `event_summary.json`, and `event_summary.csv`.

The live preview includes an overlay showing the current tracking state, motion threshold, low-motion timer, and active event duration so the user can confirm what the monitor is treating as acceptable baseline versus an event cue. The same overlay is burned into saved event videos, and videos include up to 60 seconds of pre-event lead-in frames from the rolling buffer.

Face cue tracking adds local, offline eye-region and lower-face/jaw-region change scores compared with an awake baseline. Eye openness is the primary face cue; jaw/lower-face droop is treated as supporting evidence. Use **Calibrate Face Cues** while awake, centered, and naturally alert before relying on the eye/jaw cue thresholds. These cues document visible changes only.

Use **Mark Symptoms Now** when symptoms are active or recently active. Calibration is locked until one symptom-free hour has passed, so the app does not learn a drowsy/cataplexy state as the awake baseline. Event captures also extend this one-hour calibration guard.

Use **Start Capture** when an episode feels like it is beginning before automatic thresholds fire. Manual captures save the same event video, overlay, summaries, and snapshots as automatic captures.

The vision code is analyzer-based so the current calibrated local analyzer can be replaced with a dense landmark backend. A state-of-the-art target is a local face-landmark model that outputs eye landmarks, mouth/jaw landmarks, facial asymmetry, expression blendshapes, head pose, and image-quality confidence into the same event pipeline.

## Runtime Dependency

FFmpeg is bundled under:

```text
dependencies/ffmpeg/win-x64/ffmpeg.exe
```

The app resolves FFmpeg relative to the executable folder and the project copies `dependencies` beside the exe during build and publish.

## Build

```powershell
dotnet build .\EpisodeMonitor.csproj --no-restore
```
