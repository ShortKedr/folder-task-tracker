namespace TaskTracker.Presentation.Services;

public sealed record AppStoragePaths(string DataFolderPath, string SettingsPath)
{
    public static AppStoragePaths FromBaseFolder(string baseFolder)
    {
        if (string.IsNullOrWhiteSpace(baseFolder))
        {
            throw new ArgumentException("Base folder path cannot be empty.", nameof(baseFolder));
        }

        return new AppStoragePaths(
            Path.Combine(baseFolder, "groups"),
            Path.Combine(baseFolder, "settings.json"));
    }
}
