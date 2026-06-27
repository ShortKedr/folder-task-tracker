using Avalonia.Controls;
using Avalonia.Interactivity;
using TaskTracker.App.ViewModels;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.App.Views;

public sealed partial class TaskEditorWindow : Window
{
    public TaskEditorWindow()
        : this("Task", null)
    {
    }

    public TaskEditorWindow(string title, TaskItem? initialValue)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;

        var now = DateTime.Now;
        TitleBox.Text = initialValue?.Title ?? "";
        DateBox.SelectedDate = new DateTimeOffset((initialValue?.Date ?? DateOnly.FromDateTime(now)).ToDateTime(TimeOnly.MinValue));
        TimeBox.Text = (initialValue?.Time ?? TimeOnly.FromDateTime(now)).ToString("HH:mm");
        DescriptionBox.Text = initialValue?.Description ?? "";

        Opened += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    private void SaveClick(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        var title = TitleBox.Text?.Trim() ?? "";
        var description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();

        if (DateBox.SelectedDate is null)
        {
            ErrorText.Text = "Choose a date.";
            return;
        }

        if (!TimeOnly.TryParse(TimeBox.Text, out var time))
        {
            ErrorText.Text = "Enter time as HH:mm.";
            return;
        }

        var result = new TaskEditResult(
            title,
            DateOnly.FromDateTime(DateBox.SelectedDate.Value.DateTime),
            time,
            description);

        try
        {
            TaskValidation.ValidateTask(new TaskItem
            {
                Title = result.Title,
                Date = result.Date,
                Time = result.Time,
                Description = result.Description
            });
        }
        catch (Exception)
        {
            ErrorText.Text = string.IsNullOrWhiteSpace(title)
                ? "Enter a task name."
                : "Check the task fields.";
            return;
        }

        Close(result);
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
