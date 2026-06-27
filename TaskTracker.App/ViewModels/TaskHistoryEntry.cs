using TaskTracker.Core.Models;

namespace TaskTracker.App.ViewModels;

public sealed record TaskHistoryEntry(string GroupId, string TaskId, TaskItem? Before, TaskItem? After);
