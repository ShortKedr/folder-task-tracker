using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TaskTracker.App.Views;

public sealed partial class MessageWindow : Window
{
    public MessageWindow()
        : this("Message", "")
    {
    }

    public MessageWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void OkClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
