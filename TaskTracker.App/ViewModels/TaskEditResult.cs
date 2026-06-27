namespace TaskTracker.App.ViewModels;

public sealed record TaskEditResult(string Title, DateOnly Date, TimeOnly Time, string? Description);
