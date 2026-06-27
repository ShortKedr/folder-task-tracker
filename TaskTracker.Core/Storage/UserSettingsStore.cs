using System.Text.Json;

namespace TaskTracker.Core.Storage;

public sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public UserSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FolderTaskTracker",
            "settings.json");
    }

    public string? LoadLastFolder()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            using var stream = File.OpenRead(_settingsPath);
            var settings = JsonSerializer.Deserialize<UserSettings>(stream, JsonOptions);
            return string.IsNullOrWhiteSpace(settings?.LastFolderPath) ? null : settings.LastFolderPath;
        }
        catch
        {
            return null;
        }
    }

    public void SaveLastFolder(string folderPath)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        var settings = new UserSettings(folderPath);
        var tempPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions));
        File.Move(tempPath, _settingsPath, true);
    }

    private sealed record UserSettings(string LastFolderPath);
}
