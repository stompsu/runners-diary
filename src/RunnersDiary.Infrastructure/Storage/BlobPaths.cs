namespace RunnersDiary.Infrastructure.Storage;

/// <summary>Blob naming (data-model.md): one private container, and every blob a user owns lives under <c>users/{userId:N}/</c>.</summary>
public static class BlobPaths
{
    public const string ContainerName = "gpx";

    /// <summary>Prefix of everything a user owns; deleting it removes all of the user's blobs.</summary>
    public static string UserPrefix(Guid userId) => $"users/{userId:N}/";

    /// <summary>Prefix of one track's blobs (the uploaded file and the processed GeoJSON); deleting it removes the track's storage.</summary>
    public static string TrackPrefix(Guid userId, Guid trackId) => $"{UserPrefix(userId)}tracks/{trackId:N}/";

    public static string TrackOriginal(Guid userId, Guid trackId) => $"{TrackPrefix(userId, trackId)}original.gpx";

    /// <summary>The processed track document (<c>application/geo+json</c>, ADR 0021 §5).</summary>
    public static string TrackProcessed(Guid userId, Guid trackId) => $"{TrackPrefix(userId, trackId)}track.json";
}
