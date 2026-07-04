using TaskTracker.App.ViewModels;
using TaskTracker.Core.Storage;

namespace TaskTracker.Presentation.Services;

public static class MainWindowViewModelFactory
{
    public static MainWindowViewModel Create(ITaskTrackerPlatformServices services)
    {
        return new MainWindowViewModel(
            new GroupFileStore(services.GroupStorage ?? new FileSystemGroupStorage()),
            new UserSettingsStore(services.StoragePaths.SettingsPath))
        {
            PickFolderAsync = services.PickFolderAsync,
            RequestTextAsync = services.RequestTextAsync,
            RequestTaskEditAsync = services.RequestTaskEditAsync,
            ShowErrorAsync = services.ShowErrorAsync,
            OpenDataFolderAsync = services.OpenDataFolderAsync
        };
    }
}
