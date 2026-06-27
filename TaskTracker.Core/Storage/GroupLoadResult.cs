using TaskTracker.Core.Models;

namespace TaskTracker.Core.Storage;

public sealed class GroupLoadResult
{
    public GroupLoadResult(IReadOnlyList<TaskGroup> groups, IReadOnlyList<string> errors)
    {
        Groups = groups;
        Errors = errors;
    }

    public IReadOnlyList<TaskGroup> Groups { get; }
    public IReadOnlyList<string> Errors { get; }
}
