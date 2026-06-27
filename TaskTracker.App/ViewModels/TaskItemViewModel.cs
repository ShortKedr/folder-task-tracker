using TaskTracker.Core.Models;
using TaskStatus = TaskTracker.Core.Models.TaskStatus;

namespace TaskTracker.App.ViewModels;

public sealed class TaskItemViewModel : ViewModelBase
{
    private readonly Action _save;
    private readonly Action<TaskItemViewModel, TaskItem> _recordChange;

    public TaskItemViewModel(TaskItem model, Action save, Action<TaskItemViewModel, TaskItem>? recordChange = null)
    {
        Model = model;
        _save = save;
        _recordChange = recordChange ?? ((_, _) => { });
    }

    public TaskItem Model { get; }

    public string Title
    {
        get => Model.Title;
        set
        {
            if (Model.Title == value)
            {
                return;
            }

            var before = TaskCloner.Clone(Model);
            Model.Title = value;
            OnPropertyChanged();
            _save();
            _recordChange(this, before);
        }
    }

    public DateTimeOffset? DateValue
    {
        get => new(Model.Date.ToDateTime(TimeOnly.MinValue));
        set
        {
            if (value is null)
            {
                return;
            }

            var next = DateOnly.FromDateTime(value.Value.DateTime);
            if (Model.Date == next)
            {
                return;
            }

            var before = TaskCloner.Clone(Model);
            Model.Date = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayDate));
            OnPropertyChanged(nameof(DisplaySchedule));
            _save();
            _recordChange(this, before);
        }
    }

    public string TimeText
    {
        get => Model.Time.ToString("HH:mm");
        set
        {
            if (!TimeOnly.TryParse(value, out var next) || Model.Time == next)
            {
                return;
            }

            var before = TaskCloner.Clone(Model);
            Model.Time = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTime));
            OnPropertyChanged(nameof(DisplaySchedule));
            _save();
            _recordChange(this, before);
        }
    }

    public string? Description
    {
        get => Model.Description;
        set
        {
            if (Model.Description == value)
            {
                return;
            }

            var before = TaskCloner.Clone(Model);
            Model.Description = value;
            OnPropertyChanged();
            _save();
            _recordChange(this, before);
        }
    }

    public bool IsDone
    {
        get => Model.Status == TaskStatus.Done;
        set
        {
            var next = value ? TaskStatus.Done : TaskStatus.Todo;
            if (Model.Status == next)
            {
                return;
            }

            var before = TaskCloner.Clone(Model);
            Model.Status = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            _save();
            _recordChange(this, before);
        }
    }

    public string StatusText => IsDone ? "Done" : "Todo";
    public string DisplayDate => Model.Date.ToString("yyyy-MM-dd");
    public string DisplayTime => Model.Time.ToString("HH:mm");
    public string DisplaySchedule => $"{DisplayDate} {DisplayTime}";

    public void Apply(TaskEditResult result)
    {
        Model.Title = result.Title;
        Model.Date = result.Date;
        Model.Time = result.Time;
        Model.Description = result.Description;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DateValue));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(DisplayDate));
        OnPropertyChanged(nameof(DisplayTime));
        OnPropertyChanged(nameof(DisplaySchedule));
    }

    public void RefreshAll()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DateValue));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DisplayDate));
        OnPropertyChanged(nameof(DisplayTime));
        OnPropertyChanged(nameof(DisplaySchedule));
    }
}
