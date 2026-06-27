using System.Text.Json;
using TaskTracker.Core.Models;

namespace TaskTracker.Core.Storage;

public sealed class GroupFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private string? _folderPath;

    public string? FolderPath => _folderPath;

    public void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path cannot be empty.", nameof(folderPath));
        }

        Directory.CreateDirectory(folderPath);
        _folderPath = folderPath;
    }

    public GroupLoadResult Load()
    {
        EnsureOpen();

        var groups = new List<TaskGroup>();
        var errors = new List<string>();

        foreach (var filePath in Directory.EnumerateFiles(_folderPath!, $"*{GroupFileNames.Extension}").OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var group = JsonSerializer.Deserialize<TaskGroup>(stream, JsonOptions);
                if (group is null)
                {
                    errors.Add($"{Path.GetFileName(filePath)}: empty group file.");
                    continue;
                }

                Normalize(group);
                groups.Add(group);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        groups.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return new GroupLoadResult(groups, errors);
    }

    public TaskGroup CreateGroup(string name)
    {
        EnsureOpen();
        TaskValidation.ValidateGroupName(name);

        var group = new TaskGroup
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim()
        };

        SaveGroup(group);
        return group;
    }

    public void SaveGroup(TaskGroup group)
    {
        EnsureOpen();
        Normalize(group);
        TaskValidation.ValidateGroupName(group.Name);

        foreach (var task in group.Tasks)
        {
            TaskValidation.ValidateTask(task);
        }

        var targetPath = GetPath(group);
        WriteAtomically(targetPath, group);
        DeleteStaleGroupFiles(group.Id, targetPath);
    }

    public void RenameGroup(TaskGroup group, string newName)
    {
        EnsureOpen();
        TaskValidation.ValidateGroupName(newName);

        var oldPath = FindExistingPath(group.Id);
        group.Name = newName.Trim();
        SaveGroup(group);

        if (oldPath is not null)
        {
            var currentPath = GetPath(group);
            if (!string.Equals(oldPath, currentPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }
        }
    }

    public void DeleteGroup(TaskGroup group)
    {
        EnsureOpen();
        var existingPath = FindExistingPath(group.Id) ?? GetPath(group);
        if (File.Exists(existingPath))
        {
            File.Delete(existingPath);
        }
    }

    public string GetPath(TaskGroup group)
    {
        EnsureOpen();
        var fileName = GroupFileNames.BuildFileName(group.Name, group.Id);
        return Path.Combine(_folderPath!, fileName);
    }

    private void DeleteStaleGroupFiles(string groupId, string currentPath)
    {
        foreach (var filePath in Directory.EnumerateFiles(_folderPath!, $"*{GroupFileNames.Extension}"))
        {
            if (string.Equals(filePath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(filePath);
                var group = JsonSerializer.Deserialize<TaskGroup>(stream, JsonOptions);
                if (group?.Id == groupId)
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // Broken files are reported during Load; saving a valid group should not fail because of them.
            }
        }
    }

    private string? FindExistingPath(string groupId)
    {
        foreach (var filePath in Directory.EnumerateFiles(_folderPath!, $"*{GroupFileNames.Extension}"))
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var group = JsonSerializer.Deserialize<TaskGroup>(stream, JsonOptions);
                if (group?.Id == groupId)
                {
                    return filePath;
                }
            }
            catch
            {
                // Ignore unreadable files here; Load is responsible for surfacing them.
            }
        }

        return null;
    }

    private static void WriteAtomically(string targetPath, TaskGroup group)
    {
        var directory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.SerializeToUtf8Bytes(group, JsonOptions);
        File.WriteAllBytes(tempPath, json);

        if (!File.Exists(targetPath))
        {
            File.Move(tempPath, targetPath);
            return;
        }

        try
        {
            File.Replace(tempPath, targetPath, null);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            File.Move(tempPath, targetPath, true);
        }
    }

    private static void Normalize(TaskGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Id))
        {
            group.Id = Guid.NewGuid().ToString("N");
        }

        group.Name = group.Name.Trim();

        foreach (var task in group.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                task.Id = Guid.NewGuid().ToString("N");
            }

            task.Title = task.Title.Trim();
            task.Description = string.IsNullOrWhiteSpace(task.Description) ? null : task.Description.Trim();
        }
    }

    private void EnsureOpen()
    {
        if (_folderPath is null)
        {
            throw new InvalidOperationException("Open a folder before using the group store.");
        }
    }
}
