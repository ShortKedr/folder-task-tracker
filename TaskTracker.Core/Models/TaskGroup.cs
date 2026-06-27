namespace TaskTracker.Core.Models;

public sealed class TaskGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<TaskItem> Tasks { get; set; } = new();
}
