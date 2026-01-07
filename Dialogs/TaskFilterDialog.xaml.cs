using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AIA.Models;
using WpfTaskStatus = AIA.Models.TaskStatus;

namespace AIA.Dialogs
{
    public partial class TaskFilterDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly TaskFilter _filter;
        private List<string> _selectedTags = new List<string>();
        public TaskFilter ResultFilter { get; private set; }
        public bool DialogResultValue { get; private set; }

        public TaskFilterDialog(TaskFilter currentFilter)
        {
            InitializeComponent();
            
            _filter = new TaskFilter();
            CopyFilterSettings(currentFilter, _filter);
            
            LoadFilterSettings();
            RefreshTagFilters();
        }

        private void CopyFilterSettings(TaskFilter source, TaskFilter dest)
        {
            dest.StatusFilter = source.StatusFilter != null ? new List<WpfTaskStatus>(source.StatusFilter) : null;
            dest.PriorityFilter = source.PriorityFilter != null ? new List<TaskPriority>(source.PriorityFilter) : null;
            dest.TagsFilter = source.TagsFilter != null ? new List<string>(source.TagsFilter) : null;
            dest.ShowCompleted = source.ShowCompleted;
            dest.ShowOverdue = source.ShowOverdue;
            dest.ShowArchived = source.ShowArchived;
            dest.DueDateFrom = source.DueDateFrom;
            dest.DueDateTo = source.DueDateTo;
            dest.SearchText = source.SearchText;
            dest.SortBy = source.SortBy;
            dest.SortDescending = source.SortDescending;
            
            if (source.TagsFilter != null)
            {
                _selectedTags = new List<string>(source.TagsFilter);
            }
        }

        private void LoadFilterSettings()
        {
            // Load status filters
            if (_filter.StatusFilter != null)
            {
                ChkStatusNotStarted.IsChecked = _filter.StatusFilter.Contains(WpfTaskStatus.NotStarted);
                ChkStatusInProgress.IsChecked = _filter.StatusFilter.Contains(WpfTaskStatus.InProgress);
                ChkStatusOnHold.IsChecked = _filter.StatusFilter.Contains(WpfTaskStatus.OnHold);
                ChkStatusCompleted.IsChecked = _filter.StatusFilter.Contains(WpfTaskStatus.Completed);
                ChkStatusCancelled.IsChecked = _filter.StatusFilter.Contains(WpfTaskStatus.Cancelled);
            }
            
            // Load priority filters
            if (_filter.PriorityFilter != null)
            {
                ChkPriorityLow.IsChecked = _filter.PriorityFilter.Contains(TaskPriority.Low);
                ChkPriorityMedium.IsChecked = _filter.PriorityFilter.Contains(TaskPriority.Medium);
                ChkPriorityHigh.IsChecked = _filter.PriorityFilter.Contains(TaskPriority.High);
                ChkPriorityCritical.IsChecked = _filter.PriorityFilter.Contains(TaskPriority.Critical);
            }
            
            // Load date range
            DueDateFrom.SelectedDate = _filter.DueDateFrom;
            DueDateTo.SelectedDate = _filter.DueDateTo;
            
            // Load options
            ChkShowCompleted.IsChecked = _filter.ShowCompleted ?? true;
            ChkShowArchived.IsChecked = _filter.ShowArchived ?? false;
            ChkOnlyOverdue.IsChecked = _filter.ShowOverdue ?? false;
            
            // Load sort options
            CmbSortBy.SelectedIndex = _filter.SortBy switch
            {
                TaskSortCriteria.Title => 0,
                TaskSortCriteria.Status => 1,
                TaskSortCriteria.Priority => 2,
                TaskSortCriteria.DueDate => 3,
                TaskSortCriteria.CreatedDate => 4,
                _ => 3
            };
            ChkSortDescending.IsChecked = _filter.SortDescending;
        }

        private void RefreshTagFilters()
        {
            SelectedTagsControl.ItemsSource = null;
            SelectedTagsControl.ItemsSource = _selectedTags;
        }

        private void BtnAddTagFilter(object sender, RoutedEventArgs e)
        {
            var tag = CmbAvailableTags.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(tag) && !_selectedTags.Contains(tag))
            {
                _selectedTags.Add(tag);
                CmbAvailableTags.Text = string.Empty;
                RefreshTagFilters();
            }
        }

        private void BtnRemoveTagFilter(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string tag)
            {
                _selectedTags.Remove(tag);
                RefreshTagFilters();
            }
        }

        private void BtnPresetAll(object sender, RoutedEventArgs e)
        {
            ChkShowCompleted.IsChecked = true;
            ChkShowArchived.IsChecked = true;
            ChkOnlyOverdue.IsChecked = false;
            
            ChkStatusNotStarted.IsChecked = false;
            ChkStatusInProgress.IsChecked = false;
            ChkStatusOnHold.IsChecked = false;
            ChkStatusCompleted.IsChecked = false;
            ChkStatusCancelled.IsChecked = false;
            
            ChkPriorityLow.IsChecked = false;
            ChkPriorityMedium.IsChecked = false;
            ChkPriorityHigh.IsChecked = false;
            ChkPriorityCritical.IsChecked = false;
            
            _selectedTags.Clear();
            DueDateFrom.SelectedDate = null;
            DueDateTo.SelectedDate = null;
            RefreshTagFilters();
        }

        private void BtnPresetActive(object sender, RoutedEventArgs e)
        {
            ChkShowCompleted.IsChecked = false;
            ChkShowArchived.IsChecked = false;
            ChkOnlyOverdue.IsChecked = false;
            
            ChkStatusNotStarted.IsChecked = true;
            ChkStatusInProgress.IsChecked = true;
            ChkStatusOnHold.IsChecked = false;
            ChkStatusCompleted.IsChecked = false;
            ChkStatusCancelled.IsChecked = false;
            
            RefreshTagFilters();
        }

        private void BtnPresetOverdue(object sender, RoutedEventArgs e)
        {
            ChkShowCompleted.IsChecked = false;
            ChkShowArchived.IsChecked = false;
            ChkOnlyOverdue.IsChecked = true;
            
            ChkStatusNotStarted.IsChecked = false;
            ChkStatusInProgress.IsChecked = false;
            ChkStatusOnHold.IsChecked = false;
            ChkStatusCompleted.IsChecked = false;
            ChkStatusCancelled.IsChecked = false;
            
            RefreshTagFilters();
        }

        private void BtnPresetArchived(object sender, RoutedEventArgs e)
        {
            ChkShowCompleted.IsChecked = true;
            ChkShowArchived.IsChecked = true;
            ChkOnlyOverdue.IsChecked = false;
            
            ChkStatusNotStarted.IsChecked = false;
            ChkStatusInProgress.IsChecked = false;
            ChkStatusOnHold.IsChecked = false;
            ChkStatusCompleted.IsChecked = false;
            ChkStatusCancelled.IsChecked = false;
            
            RefreshTagFilters();
        }

        private void BtnApply(object sender, RoutedEventArgs e)
        {
            // Collect status filters
            var statusFilters = new List<WpfTaskStatus>();
            if (ChkStatusNotStarted.IsChecked == true) statusFilters.Add(WpfTaskStatus.NotStarted);
            if (ChkStatusInProgress.IsChecked == true) statusFilters.Add(WpfTaskStatus.InProgress);
            if (ChkStatusOnHold.IsChecked == true) statusFilters.Add(WpfTaskStatus.OnHold);
            if (ChkStatusCompleted.IsChecked == true) statusFilters.Add(WpfTaskStatus.Completed);
            if (ChkStatusCancelled.IsChecked == true) statusFilters.Add(WpfTaskStatus.Cancelled);
            _filter.StatusFilter = statusFilters.Count > 0 ? statusFilters : null;
            
            // Collect priority filters
            var priorityFilters = new List<TaskPriority>();
            if (ChkPriorityLow.IsChecked == true) priorityFilters.Add(TaskPriority.Low);
            if (ChkPriorityMedium.IsChecked == true) priorityFilters.Add(TaskPriority.Medium);
            if (ChkPriorityHigh.IsChecked == true) priorityFilters.Add(TaskPriority.High);
            if (ChkPriorityCritical.IsChecked == true) priorityFilters.Add(TaskPriority.Critical);
            _filter.PriorityFilter = priorityFilters.Count > 0 ? priorityFilters : null;
            
            // Set tag filters
            _filter.TagsFilter = _selectedTags.Count > 0 ? _selectedTags : null;
            
            // Set date range
            _filter.DueDateFrom = DueDateFrom.SelectedDate;
            _filter.DueDateTo = DueDateTo.SelectedDate;
            
            // Set options
            _filter.ShowCompleted = ChkShowCompleted.IsChecked;
            _filter.ShowArchived = ChkShowArchived.IsChecked;
            _filter.ShowOverdue = ChkOnlyOverdue.IsChecked;
            
            // Set sort options
            _filter.SortBy = CmbSortBy.SelectedIndex switch
            {
                0 => TaskSortCriteria.Title,
                1 => TaskSortCriteria.Status,
                2 => TaskSortCriteria.Priority,
                3 => TaskSortCriteria.DueDate,
                4 => TaskSortCriteria.CreatedDate,
                _ => TaskSortCriteria.DueDate
            };
            _filter.SortDescending = ChkSortDescending.IsChecked ?? false;
            
            ResultFilter = _filter;
            DialogResultValue = true;
            Close();
        }

        private void BtnCancel(object sender, RoutedEventArgs e)
        {
            DialogResultValue = false;
            Close();
        }
    }
}
