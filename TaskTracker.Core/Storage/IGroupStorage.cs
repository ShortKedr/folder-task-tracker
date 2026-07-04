namespace TaskTracker.Core.Storage;

public interface IGroupStorage
{
    void OpenFolder(string folderPath);
    IEnumerable<GroupStorageFile> EnumerateFiles(string extension);
    Stream OpenRead(GroupStorageFile file);
    void WriteAllBytes(string fileName, byte[] bytes);
    void Delete(GroupStorageFile file);
    string GetPath(string fileName);
}
