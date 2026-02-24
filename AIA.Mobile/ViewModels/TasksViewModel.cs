using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIA.Domain.Models;
using AIA.Mobile.Data;
using TaskStatus = AIA.Domain.Models.TaskStatus;

namespace AIA.Mobile.ViewModels
{
    public partial class TasksViewModel : ObservableObject
    {
        private readonly AiaDatabase _database;

        [ObservableProperty]
        private ObservableCollection<TaskItem> _tasks = new();

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _isRefreshing;

        public TasksViewModel(AiaDatabase database)
        {
            _database = database;
            _ = LoadTasksAsync();
        }

        [RelayCommand]
        private async Task LoadTasksAsync()
        {
            try
            {
                var tasks = await _database.GetTasksAsync();
                Tasks.Clear();
                foreach (var task in tasks)
                {
                    Tasks.Add(task);
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", $"Failed to load tasks: {ex.Message}", "OK");
            }
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            IsRefreshing = true;
            await LoadTasksAsync();
            IsRefreshing = false;
        }

        [RelayCommand]
        private async Task AddTaskAsync()
        {
            var result = await Shell.Current.DisplayPromptAsync(
                "New Task",
                "Enter task title:",
                "Create",
                "Cancel",
                placeholder: "Task title");

            if (!string.IsNullOrWhiteSpace(result))
            {
                var task = new TaskItem
                {
                    Title = result,
                    Status = TaskStatus.NotStarted,
                    Priority = TaskPriority.Medium,
                    CreatedDate = DateTime.Now,
                    ModifiedDate = DateTime.Now
                };

                await _database.SaveTaskAsync(task);
                await LoadTasksAsync();
            }
        }

        [RelayCommand]
        private async Task EditTaskAsync(TaskItem task)
        {
            if (task == null) return;

            // Navigate to task details page
            var parameters = new Dictionary<string, object>
            {
                { "Task", task }
            };

            await Shell.Current.GoToAsync("taskdetails", parameters);
        }

        [RelayCommand]
        private async Task DeleteTaskAsync(TaskItem task)
        {
            if (task == null) return;

            var confirm = await Shell.Current.DisplayAlert(
                "Delete Task",
                $"Are you sure you want to delete '{task.Title}'?",
                "Delete",
                "Cancel");

            if (confirm)
            {
                await _database.DeleteTaskAsync(task);
                await LoadTasksAsync();
            }
        }

        [RelayCommand]
        private async Task SearchAsync()
        {
            // TODO: Implement search filtering
            await LoadTasksAsync();
        }

        [RelayCommand]
        private async Task ShowFilterAsync()
        {
            // TODO: Navigate to filter page
            await Task.CompletedTask;
        }
    }
}
