using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIA.Models;

namespace AIA.Views
{
    public partial class TasksTabView
    {
        public TasksTabView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

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

        private void BtnToggleExpand(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is TaskItem task)
            {
                task.IsExpanded = !task.IsExpanded;
            }
            e.Handled = true;
        }

        #endregion

        #region Task Details Events

        private void BtnDeleteTask(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTask == null) return;
            ViewModel.DeleteTask(ViewModel.SelectedTask);
        }

        private void DataEntryField_LostFocus(object sender, RoutedEventArgs e)
        {
            // Tasks auto-save via property binding, but this could trigger explicit save if needed
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
