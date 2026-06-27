namespace TaskTracker.Core.Models;

public sealed class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public TimeOnly Time { get; set; } = TimeOnly.FromDateTime(DateTime.Now);
    public string? Description { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Todo;
}
