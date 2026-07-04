using TaskTracker.App.ViewModels;
using TaskTracker.Core.Storage;

namespace TaskTracker.Presentation.Services;

public interface ITaskTrackerPlatformServices
{
    AppStoragePaths StoragePaths { get; }
    IGroupStorage? GroupStorage => null;
    Task<string?> PickFolderAsync();
    Task<string?> RequestTextAsync(string title, string? initialValue);
    Task<TaskEditResult?> RequestTaskEditAsync(string title, TaskTracker.Core.Models.TaskItem? initialValue);
    Task ShowErrorAsync(string message);
    Task OpenDataFolderAsync(string folderPath);
}
