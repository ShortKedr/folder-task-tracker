using TaskTracker.Core.Models;

namespace TaskTracker.Core.Storage;

public static class TaskValidation
{
    public const int MaxTaskTitleLength = 100;
    public const int MaxDescriptionLength = 500;
    public const int MaxGroupNameLength = 80;

    public static void ValidateGroupName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Group name cannot be empty.", nameof(name));
        }

        if (name.Length > MaxGroupNameLength)
        {
            throw new ArgumentException($"Group name cannot be longer than {MaxGroupNameLength} characters.", nameof(name));
        }
    }

    public static void ValidateTask(TaskItem task)
    {
        if (string.IsNullOrWhiteSpace(task.Title))
        {
            throw new ArgumentException("Task title cannot be empty.", nameof(task));
        }

        if (task.Title.Length > MaxTaskTitleLength)
        {
            throw new ArgumentException($"Task title cannot be longer than {MaxTaskTitleLength} characters.", nameof(task));
        }

        if (task.Description is { Length: > MaxDescriptionLength })
        {
            throw new ArgumentException($"Task description cannot be longer than {MaxDescriptionLength} characters.", nameof(task));
        }
    }
}
