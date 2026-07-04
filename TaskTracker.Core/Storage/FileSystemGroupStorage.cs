namespace TaskTracker.Core.Storage;

public sealed class FileSystemGroupStorage : IGroupStorage
{
    private string? _folderPath;

    public void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path cannot be empty.", nameof(folderPath));
        }

        Directory.CreateDirectory(folderPath);
        _folderPath = folderPath;
    }

    public IEnumerable<GroupStorageFile> EnumerateFiles(string extension)
    {
        EnsureOpen();
        return Directory
            .EnumerateFiles(_folderPath!, $"*{extension}")
            .Select(static path => new GroupStorageFile(Path.GetFileName(path), path));
    }

    public Stream OpenRead(GroupStorageFile file)
    {
        return File.OpenRead(file.Path);
    }

    public void WriteAllBytes(string fileName, byte[] bytes)
    {
        EnsureOpen();
        var targetPath = Path.Combine(_folderPath!, fileName);
        var tempPath = Path.Combine(_folderPath!, $"{fileName}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, bytes);

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

    public void Delete(GroupStorageFile file)
    {
        if (File.Exists(file.Path))
        {
            File.Delete(file.Path);
        }
    }

    public string GetPath(string fileName)
    {
        EnsureOpen();
        return Path.Combine(_folderPath!, fileName);
    }

    private void EnsureOpen()
    {
        if (_folderPath is null)
        {
            throw new InvalidOperationException("Open a folder before using the group store.");
        }
    }
}
