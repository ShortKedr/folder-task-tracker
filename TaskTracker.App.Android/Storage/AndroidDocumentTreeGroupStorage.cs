using Android.Content;
using Android.Database;
using Android.Provider;
using TaskTracker.Core.Storage;

namespace TaskTracker.App.Android.Storage;

public sealed class AndroidDocumentTreeGroupStorage : IGroupStorage
{
    private const string JsonMimeType = "application/json";

    private readonly FileSystemGroupStorage _fileStorage = new();
    private global::Android.Net.Uri? _treeUri;
    private bool _useDocumentTree;

    public void OpenFolder(string folderPath)
    {
        if (folderPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            _treeUri = global::Android.Net.Uri.Parse(folderPath);
            _useDocumentTree = true;
            return;
        }

        _useDocumentTree = false;
        _treeUri = null;
        _fileStorage.OpenFolder(folderPath);
    }

    public IEnumerable<GroupStorageFile> EnumerateFiles(string extension)
    {
        if (!_useDocumentTree)
        {
            return _fileStorage.EnumerateFiles(extension);
        }

        return EnumerateDocumentTreeFiles(extension).ToList();
    }

    public Stream OpenRead(GroupStorageFile file)
    {
        if (!_useDocumentTree)
        {
            return _fileStorage.OpenRead(file);
        }

        var documentUri = ParseUri(file.Path);
        var stream = ContentResolver.OpenInputStream(documentUri);
        return stream ?? throw new IOException($"Could not open {file.Name}.");
    }

    public void WriteAllBytes(string fileName, byte[] bytes)
    {
        if (!_useDocumentTree)
        {
            _fileStorage.WriteAllBytes(fileName, bytes);
            return;
        }

        var documentUri = FindDocumentUri(fileName) ?? CreateDocument(fileName);
        using var stream = ContentResolver.OpenOutputStream(documentUri, "wt")
            ?? throw new IOException($"Could not write {fileName}.");
        stream.Write(bytes, 0, bytes.Length);
    }

    public void Delete(GroupStorageFile file)
    {
        if (!_useDocumentTree)
        {
            _fileStorage.Delete(file);
            return;
        }

        DocumentsContract.DeleteDocument(ContentResolver, ParseUri(file.Path));
    }

    public string GetPath(string fileName)
    {
        if (!_useDocumentTree)
        {
            return _fileStorage.GetPath(fileName);
        }

        var documentUri = FindDocumentUri(fileName);
        return documentUri?.ToString() ?? $"{_treeUri}/{fileName}";
    }

    private static ContentResolver ContentResolver =>
        global::Android.App.Application.Context.ContentResolver
        ?? throw new InvalidOperationException("Android content resolver is not available.");

    private IEnumerable<GroupStorageFile> EnumerateDocumentTreeFiles(string extension)
    {
        var treeUri = EnsureTreeUri();
        var treeDocumentId = DocumentsContract.GetTreeDocumentId(treeUri);
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, treeDocumentId)
            ?? throw new IOException("Could not open selected folder.");
        string[] projection =
        [
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnMimeType
        ];

        using var cursor = ContentResolver.Query(childrenUri, projection, null, null, null);
        if (cursor is null)
        {
            yield break;
        }

        var nameIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDisplayName);
        var idIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDocumentId);
        var mimeIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnMimeType);

        while (cursor.MoveToNext())
        {
            var name = GetString(cursor, nameIndex);
            var documentId = GetString(cursor, idIndex);
            var mimeType = GetString(cursor, mimeIndex);
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(documentId) ||
                string.Equals(mimeType, DocumentsContract.Document.MimeTypeDir, StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var documentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, documentId)
                ?? throw new IOException($"Could not resolve {name}.");
            var documentPath = documentUri.ToString();
            if (string.IsNullOrWhiteSpace(documentPath))
            {
                throw new IOException($"Could not resolve {name}.");
            }

            yield return new GroupStorageFile(name, documentPath);
        }
    }

    private global::Android.Net.Uri? FindDocumentUri(string fileName)
    {
        return EnumerateDocumentTreeFiles(GroupFileNames.Extension)
            .FirstOrDefault(file => string.Equals(file.Name, fileName, StringComparison.OrdinalIgnoreCase))
            is { } file
                ? ParseUri(file.Path)
                : null;
    }

    private global::Android.Net.Uri CreateDocument(string fileName)
    {
        var treeUri = EnsureTreeUri();
        var parentDocumentId = DocumentsContract.GetTreeDocumentId(treeUri);
        var parentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, parentDocumentId)
            ?? throw new IOException("Could not open selected folder.");
        var documentUri = DocumentsContract.CreateDocument(ContentResolver, parentUri, JsonMimeType, fileName);
        return documentUri ?? throw new IOException($"Could not create {fileName}.");
    }

    private global::Android.Net.Uri EnsureTreeUri()
    {
        return _treeUri ?? throw new InvalidOperationException("Open a folder before using the group store.");
    }

    private static string? GetString(ICursor cursor, int columnIndex)
    {
        return columnIndex < 0 || cursor.IsNull(columnIndex) ? null : cursor.GetString(columnIndex);
    }

    private static global::Android.Net.Uri ParseUri(string value)
    {
        return global::Android.Net.Uri.Parse(value) ?? throw new IOException($"Invalid document URI: {value}");
    }
}
