namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public static class MeasurementAvatarEasyModeAdvisor
{
    public static MeasurementAvatarEasyModeState Create(MeasurementAvatarEasyModeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var nextItem = SelectNextItem(input.CapturePlan);
        var nextText = FormatNextItem(nextItem);
        if (input.HistoricalDataSuspect)
        {
            return Blocked(
                "Rebuild avatar data",
                $"{Clean(input.HistoricalDataAuditSummary, "Saved avatar data needs review before collecting more.")} Open the Avatar System, inspect the reports, then rebuild before easy capture.",
                actionText: "Open Avatar System");
        }

        if (input.TrackingAuditHold)
        {
            return Blocked(
                "Review tracking first",
                $"{Clean(input.TrackingAuditHoldSummary, "Tracking audit is holding avatar learning.")} Check that the head rotates while eyes, nose, and mouth stay attached to the face.",
                actionText: "Open Avatar System");
        }

        if (!input.SubjectConfirmed)
        {
            return Blocked(
                "Confirm Chris",
                $"Easy mode is paused until the subject checkbox confirms Chris is the person in front of the camera. {nextText}",
                actionText: "Confirm Subject");
        }

        if (!input.CameraActive)
        {
            return Warning(
                "Turn camera on",
                $"Easy mode is ready, but the camera is off. Turn the camera on, keep your face visible, then start guided capture. {nextText}",
                actionText: "Turn Camera On",
                canStartLearning: true,
                nextItem);
        }

        if (!input.FaceLocked)
        {
            return Warning(
                "Get face lock",
                $"Easy mode is waiting for a stable face, eye, and mouth lock. Sit where the overlay can see your full face and glasses clearly. {nextText}",
                actionText: "Waiting For Face",
                canStartLearning: true,
                nextItem);
        }

        if (!input.AvatarLearningRequested)
        {
            return Warning(
                "Ready for guided capture",
                $"Click Easy Avatar Mode to start measurement-only avatar learning. {nextText}",
                actionText: "Start Easy Avatar Mode",
                canStartLearning: true,
                nextItem);
        }

        if (!input.CaptureQuality.CanCollectMeasurements)
        {
            var fix = input.CaptureQuality.Suggestions.FirstOrDefault()
                ?? input.CaptureQuality.PrimaryReason
                ?? "Improve camera mode, lighting, face scale, eye visibility, mouth visibility, or stability.";
            return Warning(
                "Fix capture quality",
                $"Learning is on, but measurements are paused: {Clean(input.CaptureQuality.PrimaryReason, "capture quality is not ready")}. Fix: {fix}",
                actionText: "Fix Capture",
                canStartLearning: true,
                nextItem);
        }

        if (input.CapturePlan is { CanCollectMeasurements: false })
        {
            return Warning(
                "Capture plan paused",
                $"{Clean(input.CapturePlan.CollectionDecision, "capture plan is waiting")}. Confirm the subject and review the Avatar System report.",
                actionText: "Open Avatar System",
                canStartLearning: true,
                nextItem);
        }

        if (nextItem is not null)
        {
            return Good(
                $"Collect: {nextItem.Title}",
                $"{nextItem.Instructions} Target {Math.Max(0, nextItem.TargetMinutes)} min. Why: {nextItem.WhyItMatters}",
                actionText: "Easy Mode Running",
                nextItem);
        }

        return Good(
            "Balanced maintenance",
            "No urgent capture-plan gap is available. Keep a relaxed, subject-confirmed session running with natural blinks, speech, small head turns, and distance changes.",
            actionText: "Easy Mode Running",
            nextItem: null);
    }

    private static MeasurementAvatarCapturePlanItem? SelectNextItem(MeasurementAvatarCapturePlan? plan)
    {
        return plan?.Items
            .OrderBy(static item => item.Priority)
            .ThenByDescending(static item => item.TargetMinutes)
            .ThenBy(static item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string FormatNextItem(MeasurementAvatarCapturePlanItem? item)
    {
        if (item is null)
        {
            return "Next capture target will appear after the Avatar System report refreshes.";
        }

        var target = item.TargetMinutes > 0 ? $" Target {item.TargetMinutes} min." : "";
        return $"Next: {item.Title}. {item.Instructions}{target}";
    }

    private static MeasurementAvatarEasyModeState Good(
        string title,
        string detail,
        string actionText,
        MeasurementAvatarCapturePlanItem? nextItem)
    {
        return CreateState(
            title,
            detail,
            actionText,
            MeasurementAvatarEasyModeSeverity.Good,
            canStartLearning: true,
            nextItem);
    }

    private static MeasurementAvatarEasyModeState Warning(
        string title,
        string detail,
        string actionText,
        bool canStartLearning,
        MeasurementAvatarCapturePlanItem? nextItem)
    {
        return CreateState(
            title,
            detail,
            actionText,
            MeasurementAvatarEasyModeSeverity.Warning,
            canStartLearning,
            nextItem);
    }

    private static MeasurementAvatarEasyModeState Blocked(string title, string detail, string actionText)
    {
        return CreateState(
            title,
            detail,
            actionText,
            MeasurementAvatarEasyModeSeverity.Blocked,
            canStartLearning: false,
            nextItem: null);
    }

    private static MeasurementAvatarEasyModeState CreateState(
        string title,
        string detail,
        string actionText,
        string severity,
        bool canStartLearning,
        MeasurementAvatarCapturePlanItem? nextItem)
    {
        return new MeasurementAvatarEasyModeState
        {
            Title = title,
            Detail = detail,
            ActionText = actionText,
            Severity = severity,
            CanStartLearning = canStartLearning,
            CapturePlanItemId = nextItem?.Id ?? ""
        };
    }

    private static string Clean(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
