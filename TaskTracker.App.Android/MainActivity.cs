using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Window;
using Avalonia.Android;
using System.Runtime.Versioning;

namespace TaskTracker.App.Android;

[Activity(
    Label = "Folder Task Tracker",
    Icon = "@drawable/app_icon",
    RoundIcon = "@drawable/app_icon",
    Theme = "@style/AppTheme.NoActionBar",
    MainLauncher = true,
    EnableOnBackInvokedCallback = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    private const int OpenDocumentTreeRequestCode = 4201;

    private static MainActivity? _current;
    private BackCallback? _backCallback;
    private TaskCompletionSource<string?>? _folderPickerCompletion;
    public static Func<bool>? CustomBackRequested { get; set; }

    public static Task<string?> PickFolderAsync()
    {
        return _current?.PickFolderFromDeviceAsync() ?? Task.FromResult<string?>(null);
    }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        _current = this;
        base.OnCreate(savedInstanceState);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            RegisterBackCallback();
        }
    }

    protected override void OnDestroy()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            UnregisterBackCallback();
        }

        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        base.OnDestroy();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode != OpenDocumentTreeRequestCode || _folderPickerCompletion is null)
        {
            return;
        }

        var completion = _folderPickerCompletion;
        _folderPickerCompletion = null;

        if (resultCode != Result.Ok || data?.Data is null)
        {
            completion.TrySetResult(null);
            return;
        }

        var uri = data.Data;
        var flags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        try
        {
            ContentResolver?.TakePersistableUriPermission(uri, flags);
        }
        catch
        {
            // Some providers grant temporary access only; the open attempt will surface any real failure.
        }

        completion.TrySetResult(uri.ToString());
    }

    private Task<string?> PickFolderFromDeviceAsync()
    {
        if (_folderPickerCompletion is not null)
        {
            return _folderPickerCompletion.Task;
        }

        _folderPickerCompletion = new TaskCompletionSource<string?>();
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission |
                        ActivityFlags.GrantWriteUriPermission |
                        ActivityFlags.GrantPersistableUriPermission |
                        ActivityFlags.GrantPrefixUriPermission);
        StartActivityForResult(intent, OpenDocumentTreeRequestCode);
        return _folderPickerCompletion.Task;
    }

#pragma warning disable CS0672
    public override void OnBackPressed()
#pragma warning restore CS0672
    {
        if (!TryHandleBackRequested())
        {
#pragma warning disable CS0618, CA1422
            base.OnBackPressed();
#pragma warning restore CS0618, CA1422
        }
    }

    private bool TryHandleBackRequested()
    {
        return CustomBackRequested?.Invoke() == true;
    }

    [SupportedOSPlatform("android33.0")]
    private void RegisterBackCallback()
    {
        _backCallback = new BackCallback(this);
        OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(IOnBackInvokedDispatcher.PriorityDefault, _backCallback);
    }

    [SupportedOSPlatform("android33.0")]
    private void UnregisterBackCallback()
    {
        if (_backCallback is null)
        {
            return;
        }

        OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(_backCallback);
        _backCallback = null;
    }

    private void HandleBackInvoked()
    {
        if (!TryHandleBackRequested())
        {
            Finish();
        }
    }

    private sealed class BackCallback : Java.Lang.Object, IOnBackInvokedCallback
    {
        private readonly WeakReference<MainActivity> _activity;

        public BackCallback(MainActivity activity)
        {
            _activity = new WeakReference<MainActivity>(activity);
        }

        public void OnBackInvoked()
        {
            if (_activity.TryGetTarget(out var activity))
            {
                activity.HandleBackInvoked();
            }
        }
    }
}
