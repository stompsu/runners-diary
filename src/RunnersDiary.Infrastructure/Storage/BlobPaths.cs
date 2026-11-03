namespace RunnersDiary.Infrastructure.Storage;

/// <summary>Blob naming (data-model.md): one private container, and every blob a user owns lives under <c>users/{userId:N}/</c>.</summary>
public static class BlobPaths
{
    public const string ContainerName = "gpx";

    /// <summary>Prefix of everything a user owns; deleting it removes all of the user's blobs.</summary>
    public static string UserPrefix(Guid userId) => $"users/{userId:N}/";

    public static string TrackOriginal(Guid userId, Guid trackId) => $"{UserPrefix(userId)}tracks/{trackId:N}/original.gpx";
}
