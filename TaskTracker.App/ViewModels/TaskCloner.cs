using TaskTracker.Core.Models;

namespace TaskTracker.App.ViewModels;

public static class TaskCloner
{
    public static TaskItem Clone(TaskItem task)
    {
        return new TaskItem
        {
            Id = task.Id,
            Title = task.Title,
            Date = task.Date,
            Time = task.Time,
            Description = task.Description,
            Status = task.Status
        };
    }

    public static void CopyTo(TaskItem source, TaskItem target)
    {
        target.Id = source.Id;
        target.Title = source.Title;
        target.Date = source.Date;
        target.Time = source.Time;
        target.Description = source.Description;
        target.Status = source.Status;
    }
}
