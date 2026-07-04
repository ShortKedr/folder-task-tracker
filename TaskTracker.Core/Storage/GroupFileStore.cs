using System.Text.Json;
using TaskTracker.Core.Models;

namespace TaskTracker.Core.Storage;

public sealed class GroupFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IGroupStorage _storage;
    private string? _folderPath;

    public string? FolderPath => _folderPath;

    public GroupFileStore()
        : this(new FileSystemGroupStorage())
    {
    }

    public GroupFileStore(IGroupStorage storage)
    {
        _storage = storage;
    }

    public void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path cannot be empty.", nameof(folderPath));
        }

        _storage.OpenFolder(folderPath);
        _folderPath = folderPath;
    }

    public GroupLoadResult Load()
    {
        EnsureOpen();

        var groups = new List<TaskGroup>();
        var errors = new List<string>();

        foreach (var file in _storage.EnumerateFiles(GroupFileNames.Extension).OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = _storage.OpenRead(file);
                var group = JsonSerializer.Deserialize<TaskGroup>(stream, JsonOptions);
                if (group is null)
                {
                    errors.Add($"{file.Name}: empty group file.");
                    continue;
                }

                Normalize(group);
                groups.Add(group);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{file.Name}: {ex.Message}");
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

        var fileName = GroupFileNames.BuildFileName(group.Name, group.Id);
        WriteGroup(fileName, group);
        DeleteStaleGroupFiles(group.Id, fileName);
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
            if (!string.Equals(oldPath.Path, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                _storage.Delete(oldPath);
            }
        }
    }

    public void DeleteGroup(TaskGroup group)
    {
        EnsureOpen();
        var existingFile = FindExistingPath(group.Id);
        if (existingFile is not null)
        {
            _storage.Delete(existingFile);
        }
    }

    public string GetPath(TaskGroup group)
    {
        EnsureOpen();
        var fileName = GroupFileNames.BuildFileName(group.Name, group.Id);
        return _storage.GetPath(fileName);
    }

    private void DeleteStaleGroupFiles(string groupId, string currentFileName)
    {
        foreach (var file in _storage.EnumerateFiles(GroupFileNames.Extension))
        {
            if (string.Equals(file.Name, currentFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var stream = _storage.OpenRead(file);
                var group = JsonSerializer.Deserialize<TaskGroup>(stream, JsonOptions);
                if (group?.Id == groupId)
                {
                    _storage.Delete(file);
                }
            }
            catch
            {
                // Broken files are reported during Load; saving a valid group should not fail because of them.
            }
        }
    }

    private GroupStorageFile? FindExistingPath(string groupId)
    {
        foreach (var file in _storage.EnumerateFiles(GroupFileNames.Extension))
        {
            try
            {
                using var stream = _storage.OpenRead(file);
                var group = JsonSerializer.Deserialize<TaskGroup>(stream, JsonOptions);
                if (group?.Id == groupId)
                {
                    return file;
                }
            }
            catch
            {
                // Ignore unreadable files here; Load is responsible for surfacing them.
            }
        }

        return null;
    }

    private void WriteGroup(string fileName, TaskGroup group)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(group, JsonOptions);
        _storage.WriteAllBytes(fileName, json);
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
