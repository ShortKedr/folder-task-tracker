using System.Collections.ObjectModel;
using TaskTracker.Core.Models;

namespace TaskTracker.App.ViewModels;

public sealed class TaskGroupViewModel : ViewModelBase
{
    private readonly Action<TaskGroupViewModel> _save;
    private readonly Action<TaskGroupViewModel, TaskItemViewModel, TaskItem> _recordTaskChange;

    public TaskGroupViewModel(
        TaskGroup model,
        Action<TaskGroupViewModel> save,
        Action<TaskGroupViewModel, TaskItemViewModel, TaskItem>? recordTaskChange = null)
    {
        Model = model;
        _save = save;
        _recordTaskChange = recordTaskChange ?? ((_, _, _) => { });
        Tasks = new ObservableCollection<TaskItemViewModel>(
            model.Tasks.Select(CreateTaskViewModel));
        SortDisplayedTasks();
    }

    public TaskGroup Model { get; }
    public ObservableCollection<TaskItemViewModel> Tasks { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value)
            {
                return;
            }

            Model.Name = value;
            OnPropertyChanged();
            _save(this);
        }
    }

    public void RefreshName()
    {
        OnPropertyChanged(nameof(Name));
    }

    public int TotalCount => Tasks.Count;
    public int OpenCount => Tasks.Count(static task => !task.IsDone);

    public TaskItemViewModel AddTask(TaskItem task)
    {
        Model.Tasks.Add(task);
        var viewModel = CreateTaskViewModel(task);
        Tasks.Add(viewModel);
        RefreshCounts();
        Save();
        return viewModel;
    }

    public void RemoveTask(TaskItemViewModel task)
    {
        Model.Tasks.Remove(task.Model);
        Tasks.Remove(task);
        RefreshCounts();
        Save();
    }

    public TaskItemViewModel RestoreTask(TaskItem task)
    {
        Model.Tasks.Add(task);
        var viewModel = CreateTaskViewModel(task);
        Tasks.Add(viewModel);
        RefreshCounts();
        Save();
        return viewModel;
    }

    public void RefreshCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(OpenCount));
    }

    public void SaveTaskState()
    {
        Save();
    }

    private void Save()
    {
        SortDisplayedTasks();
        RefreshCounts();
        _save(this);
    }

    private TaskItemViewModel CreateTaskViewModel(TaskItem task)
    {
        return new TaskItemViewModel(task, Save, (taskViewModel, before) => _recordTaskChange(this, taskViewModel, before));
    }

    private void SortDisplayedTasks()
    {
        var sorted = Tasks
            .OrderBy(static task => task.IsDone ? 1 : 0)
            .ThenBy(task => Model.Tasks.IndexOf(task.Model))
            .ToList();

        for (var index = 0; index < sorted.Count; index++)
        {
            var currentIndex = Tasks.IndexOf(sorted[index]);
            if (currentIndex != index)
            {
                Tasks.Move(currentIndex, index);
            }
        }
    }
}
