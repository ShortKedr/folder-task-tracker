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
        var settings = LoadSettings();
        return string.IsNullOrWhiteSpace(settings.LastFolderPath) ? null : settings.LastFolderPath;
    }

    public void SaveLastFolder(string folderPath)
    {
        var settings = LoadSettings();
        settings.LastFolderPath = folderPath;
        SaveSettings(settings);
    }

    public string? LoadLastGroupId()
    {
        var settings = LoadSettings();
        return string.IsNullOrWhiteSpace(settings.LastGroupId) ? null : settings.LastGroupId;
    }

    public void SaveLastGroupId(string? groupId)
    {
        var settings = LoadSettings();
        settings.LastGroupId = string.IsNullOrWhiteSpace(groupId) ? null : groupId;
        SaveSettings(settings);
    }

    public double LoadGroupScrollOffset(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return 0;
        }

        var settings = LoadSettings();
        return settings.GroupScrollOffsets.TryGetValue(groupId, out var offset) && double.IsFinite(offset)
            ? Math.Max(0, offset)
            : 0;
    }

    public void SaveGroupScrollOffset(string groupId, double offset)
    {
        if (string.IsNullOrWhiteSpace(groupId) || !double.IsFinite(offset))
        {
            return;
        }

        var settings = LoadSettings();
        settings.GroupScrollOffsets[groupId] = Math.Max(0, offset);
        SaveSettings(settings);
    }

    public void RemoveGroupScrollOffset(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return;
        }

        var settings = LoadSettings();
        if (settings.GroupScrollOffsets.Remove(groupId))
        {
            SaveSettings(settings);
        }
    }

    private UserSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new UserSettings();
            }

            using var stream = File.OpenRead(_settingsPath);
            var settings = JsonSerializer.Deserialize<UserSettings>(stream, JsonOptions) ?? new UserSettings();
            settings.GroupScrollOffsets ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch
        {
            return new UserSettings();
        }
    }

    private void SaveSettings(UserSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions));
        File.Move(tempPath, _settingsPath, true);
    }

    private sealed class UserSettings
    {
        public string? LastFolderPath { get; set; }
        public string? LastGroupId { get; set; }
        public Dictionary<string, double> GroupScrollOffsets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
