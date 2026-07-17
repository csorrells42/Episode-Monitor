namespace EpisodeMonitor.Modules.Vision.Reconstruction;

public sealed class LastGoodFeatureMeshReport
{
    public string SchemaVersion { get; init; } = "last-good-feature-mesh-v1";

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public string SubjectId { get; init; } = "";

    public string SubjectDisplayName { get; init; } = "";

    public string StoragePolicy { get; init; } =
        "Inspection-only rolling dense mesh cache. Stores only the last 10 good feature-recognition landmark sets; it is not a long-term full-mesh archive.";

    public LastGoodFeatureMeshStabilityReport HeadLockedStability { get; init; } = new();

    public IReadOnlyList<LastGoodFeatureMeshSample> Samples { get; init; } = [];
}
