using System.Collections.ObjectModel;
using AIA.Mobile.Services;
using AIA.Sync.Contracts;

namespace AIA.Mobile.Pages
{
    public partial class TasksPage : ContentPage
    {
        private readonly ObservableCollection<TaskDto> _tasks = new();

        public TasksPage()
        {
            InitializeComponent();
            TasksList.ItemsSource = _tasks;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadAsync();
            await TrySyncAsync(silent: true);
        }

        private async Task LoadAsync()
        {
            try
            {
                await MobileApp.InitializeAsync();
                var tasks = await MobileApp.Store.GetTasksAsync();
                _tasks.Clear();
                foreach (var task in tasks)
                    _tasks.Add(task);

                StatusLabel.Text = $"{_tasks.Count} tasks" + (MobileApp.Sync.LastSyncUtc is { } last
                    ? $" · synced {last.ToLocalTime():HH:mm}"
                    : string.Empty);
            }
            catch (Exception ex)
            {
                StatusLabel.Text = ex.Message;
            }
        }

        private async void BtnNew(object sender, EventArgs e)
        {
            var title = await DisplayPromptAsync("New task", "Title", "Create", "Cancel", "Task title");
            if (string.IsNullOrWhiteSpace(title))
                return;

            var task = new TaskDto
            {
                Id = Guid.NewGuid(),
                Title = title.Trim(),
                CreatedDate = DateTime.Now,
                Status = SyncTaskStatus.NotStarted,
                Priority = SyncTaskPriority.Medium
            };

            await MobileApp.Store.SaveTaskAsync(task);
            await LoadAsync();
            await TrySyncAsync(silent: true);
        }

        private async void BtnComplete(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: TaskDto task })
                return;

            task.Status = SyncTaskStatus.Completed;
            task.CompletedDate = DateTime.Now;
            await MobileApp.Store.SaveTaskAsync(task);
            await LoadAsync();
            await TrySyncAsync(silent: true);
        }

        private async void BtnDelete(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: TaskDto task })
                return;

            var confirm = await DisplayAlert("Delete task", task.Title, "Delete", "Cancel");
            if (!confirm)
                return;

            await MobileApp.Store.DeleteAsync(SyncEntityType.Task, task.Id);
            await LoadAsync();
            await TrySyncAsync(silent: true);
        }

        private async void BtnSync(object sender, EventArgs e)
        {
            await TrySyncAsync(silent: false);
            await LoadAsync();
        }

        private async void OnRefreshing(object sender, EventArgs e)
        {
            await TrySyncAsync(silent: true);
            await LoadAsync();
            Refresh.IsRefreshing = false;
        }

        private async Task TrySyncAsync(bool silent)
        {
            try
            {
                await MobileApp.Sync.SyncNowAsync();
                if (!silent)
                    StatusLabel.Text = "Sync complete.";
            }
            catch (Exception ex)
            {
                if (!silent || MobileSettingsService.ServerUrl.Length > 0)
                    StatusLabel.Text = ex.Message;
            }
        }
    }
}
