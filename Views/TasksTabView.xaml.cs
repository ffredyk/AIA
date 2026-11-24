using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIA.Models;
using AIA.Dialogs;
using WpfMessageBox = System.Windows.MessageBox;
using TaskStatus = AIA.Models.TaskStatus;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfContextMenu = System.Windows.Controls.ContextMenu;

namespace AIA.Views
{
    public partial class TasksTabView
    {
        public TasksTabView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        #region Toolbar Events

        private void BtnAddTask(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.IsAddingNewTask = true;
            FocusNewTaskInput();
        }

        private void BtnShowTemplates(object sender, RoutedEventArgs e)
        {
            var templateDialog = new TaskTemplateDialog();
            templateDialog.Owner = Window.GetWindow(this);
            if (templateDialog.ShowDialog() == true && templateDialog.SelectedTemplate != null)
            {
                ViewModel?.CreateTaskFromTemplate(templateDialog.SelectedTemplate);
            }
        }

        private void BtnShowFilters(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            
            var filterDialog = new TaskFilterDialog(ViewModel.CurrentTaskFilter);
            filterDialog.Owner = Window.GetWindow(this);
            filterDialog.ShowDialog();
            if (filterDialog.DialogResultValue && filterDialog.ResultFilter != null)
            {
                ViewModel.CurrentTaskFilter = filterDialog.ResultFilter;
            }
        }

        private void BtnToggleBulkMode(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.IsBulkSelectionMode = !ViewModel.IsBulkSelectionMode;
        }

        private void BtnImportExport(object sender, RoutedEventArgs e)
        {
            var importExportDialog = new TaskImportExportDialog();
            importExportDialog.Owner = Window.GetWindow(this);
            importExportDialog.ShowDialog();
        }

        private void BtnManageTemplates(object sender, RoutedEventArgs e)
        {
            var templateManager = new TemplateManagerDialog();
            templateManager.Owner = Window.GetWindow(this);
            templateManager.ShowDialog();
        }

        #endregion

        #region Bulk Operations

        private void BtnBulkSelectAll(object sender, RoutedEventArgs e)
        {
            ViewModel?.SelectAllTasks();
        }

        private void BtnBulkDeselectAll(object sender, RoutedEventArgs e)
        {
            ViewModel?.DeselectAllTasks();
        }

        private void BtnBulkChangeStatus(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            if (ChoiceDialog.Show(
                Window.GetWindow(this),
                "Change status",
                "Set the status for selected tasks:",
                Enum.GetValues<TaskStatus>(),
                value => value.ToString(),
                out var status))
            {
                ViewModel.BulkChangeStatus(status);
            }
        }

        private void BtnBulkChangePriority(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            if (ChoiceDialog.Show(
                Window.GetWindow(this),
                "Change priority",
                "Set the priority for selected tasks:",
                Enum.GetValues<TaskPriority>(),
                value => value.ToString(),
                out var priority))
            {
                ViewModel.BulkChangePriority(priority);
            }
        }

        private void BtnBulkAddTag(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            var tag = TextInputDialog.Show(
                Window.GetWindow(this),
                "Add tag",
                "Tag name:");

            if (tag != null)
                ViewModel.BulkAddTag(tag);
        }

        private void BtnBulkArchive(object sender, RoutedEventArgs e)
        {
            ViewModel?.BulkArchiveTasks();
        }

        private void BtnBulkDelete(object sender, RoutedEventArgs e)
        {
            if (WpfMessageBox.Show("Are you sure you want to delete the selected tasks?", 
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                ViewModel?.BulkDeleteTasks();
            }
        }

        private void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // Prevent triggering the task click event
        }

        #endregion

        #region New Task Input

        public void FocusNewTaskInput()
        {
            NewTaskTitleInput?.Focus();
        }

        private void BtnConfirmNewTask(object sender, RoutedEventArgs e)
        {
            ViewModel?.AddNewTask(ViewModel.NewTaskTitle);
        }

        private void BtnCancelNewTask(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.NewTaskTitle = string.Empty;
            ViewModel.IsAddingNewTask = false;
        }

        private void NewTaskTitleInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewTask(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewTask(sender, e);
                e.Handled = true;
            }
        }

        #endregion

        #region Task List Events

        private void TaskItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is TaskItem task)
            {
                ViewModel.SelectedTask = task;
            }
        }

        private void TaskItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Context menu is defined in XAML, this just allows it to open
        }

        private void MenuItem_Duplicate(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            
            var menuItem = sender as WpfMenuItem;
            var contextMenu = menuItem?.Parent as WpfContextMenu;
            if (contextMenu?.PlacementTarget is FrameworkElement element && element.Tag is TaskItem task)
            {
                ViewModel.DuplicateTask(task);
            }
        }

        private void MenuItem_SaveAsTemplate(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            
            var menuItem = sender as WpfMenuItem;
            var contextMenu = menuItem?.Parent as WpfContextMenu;
            if (contextMenu?.PlacementTarget is FrameworkElement element && element.Tag is TaskItem task)
            {
                var dialog = new SaveAsTemplateDialog();
                dialog.Owner = Window.GetWindow(this);
                if (dialog.DialogResult)
                {
                    _ = ViewModel.SaveTaskAsTemplateAsync(task, dialog.TemplateName, dialog.TemplateDescription);
                }
            }
        }

        private void MenuItem_Archive(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            
            var menuItem = sender as WpfMenuItem;
            var contextMenu = menuItem?.Parent as WpfContextMenu;
            if (contextMenu?.PlacementTarget is FrameworkElement element && element.Tag is TaskItem task)
            {
                ViewModel.ArchiveTask(task);
            }
        }

        private void MenuItem_Delete(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            
            var menuItem = sender as WpfMenuItem;
            var contextMenu = menuItem?.Parent as WpfContextMenu;
            if (contextMenu?.PlacementTarget is FrameworkElement element && element.Tag is TaskItem task)
            {
                if (WpfMessageBox.Show($"Are you sure you want to delete '{task.Title}'?", 
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    ViewModel.DeleteTask(task);
                }
            }
        }

        private void BtnToggleExpand(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is TaskItem task)
            {
                task.IsExpanded = !task.IsExpanded;
            }
            e.Handled = true;
        }

        private void BtnTaskActions(object sender, RoutedEventArgs e)
        {
            // The button with the three dots - could show a menu or do nothing
            // Currently context menu is handled by right-click
        }

        #endregion

        #region Task Details Events

        private void BtnDuplicateTask(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;
            ViewModel.DuplicateTask(ViewModel.SelectedTask);
        }

        private void BtnSaveAsTemplate(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;
            
            var dialog = new SaveAsTemplateDialog();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.DialogResult)
            {
                _ = ViewModel.SaveTaskAsTemplateAsync(ViewModel.SelectedTask, dialog.TemplateName, dialog.TemplateDescription);
            }
        }

        private void BtnArchiveTask(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;
            ViewModel.ArchiveTask(ViewModel.SelectedTask);
        }

        private void BtnDeleteTask(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;
            
            if (WpfMessageBox.Show($"Are you sure you want to delete '{ViewModel.SelectedTask.Title}'?", 
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                ViewModel.DeleteTask(ViewModel.SelectedTask);
            }
        }

        private void TaskProperty_Changed(object sender, RoutedEventArgs e)
        {
            // Auto-save when task properties change
            _ = ViewModel?.SaveTasksAndRemindersAsync();
        }

        private void DataEntryField_LostFocus(object sender, RoutedEventArgs e)
        {
            // Tasks auto-save via property binding, but this could trigger explicit save if needed
            _ = ViewModel?.SaveTasksAndRemindersAsync();
        }

        private void BtnAddTag(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;

            var tag = TextInputDialog.Show(
                Window.GetWindow(this),
                "Add tag",
                "Tag name:");

            if (tag != null)
                ViewModel.AddTagToTask(ViewModel.SelectedTask, tag);
        }

        private void BtnRemoveTag(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;
            
            if (sender is FrameworkElement element && element.Tag is string tag)
            {
                ViewModel.RemoveTagFromTask(ViewModel.SelectedTask, tag);
            }
        }

        private void BtnConfigureRecurrence(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;
            
            var recurrenceDialog = new RecurrenceConfigDialog(ViewModel.SelectedTask);
            recurrenceDialog.Owner = Window.GetWindow(this);
            recurrenceDialog.ShowDialog();
        }

        private void BtnAddDependency(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;

            var selectedTask = ViewModel.SelectedTask;
            var candidates = ViewModel.Tasks
                .Where(task =>
                    task.Id != selectedTask.Id &&
                    !task.IsArchived &&
                    !selectedTask.DependencyTaskIds.Contains(task.Id))
                .OrderBy(task => task.Title)
                .ToList();

            if (candidates.Count == 0)
            {
                WpfMessageBox.Show(
                    "There are no other tasks available to depend on.",
                    "Add dependency",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (ChoiceDialog.Show(
                Window.GetWindow(this),
                "Add dependency",
                "Choose a task that must be completed first:",
                candidates,
                task => task.Title,
                out var dependency))
            {
                ViewModel.AddTaskDependency(selectedTask, dependency);
            }
        }

        private void BtnRemoveDependency(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;
            
            if (sender is FrameworkElement element && element.Tag is System.Guid dependencyId)
            {
                ViewModel.RemoveTaskDependency(ViewModel.SelectedTask, dependencyId);
            }
        }

        #endregion

        #region Subtask Events

        private void BtnAddSubtask(object sender, RoutedEventArgs e)
        {
            NewSubtaskInputPanel.Visibility = Visibility.Visible;
            NewSubtaskTitleInput?.Focus();
        }

        private void BtnConfirmNewSubtask(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;

            var title = NewSubtaskTitleInput?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                ViewModel.AddSubtask(ViewModel.SelectedTask, title);
                ViewModel.SelectedTask.IsExpanded = true;
            }

            NewSubtaskTitleInput.Text = string.Empty;
            NewSubtaskInputPanel.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelNewSubtask(object sender, RoutedEventArgs e)
        {
            NewSubtaskTitleInput.Text = string.Empty;
            NewSubtaskInputPanel.Visibility = Visibility.Collapsed;
        }

        private void NewSubtaskTitleInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewSubtask(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewSubtask(sender, e);
                e.Handled = true;
            }
        }

        #endregion
    }
}
