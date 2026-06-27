using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TaskTracker.App.Views;

public sealed partial class TextInputWindow : Window
{
    public TextInputWindow()
        : this("Input", "")
    {
    }

    public TextInputWindow(string title, string? initialValue)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        ValueBox.Text = initialValue ?? "";
        ValueBox.SelectAll();
    }

    private void OkClick(object? sender, RoutedEventArgs e)
    {
        Close(ValueBox.Text?.Trim());
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
