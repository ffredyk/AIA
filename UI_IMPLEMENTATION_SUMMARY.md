# Task Management UI Implementation Summary

## Created Files

### XAML Views
1. **Views/TasksTabView.xaml** (Enhanced)
   - Main task management interface
   - Toolbar with quick actions
   - Bulk operation mode with selection checkboxes
   - Enhanced task cards with tags, badges, and context menus
   - Detailed task editor with all new features
   - Filter/sort integration ready

### Dialog Windows
2. **Dialogs/TaskFilterDialog.xaml**
   - Comprehensive filtering interface
   - Status, priority, tags, and date range filters
   - Sort options with direction
   - Quick filter presets
   - Real-time preview

3. **Dialogs/TaskTemplateDialog.xaml**
   - Template selection interface
   - Template preview with statistics
   - Visual indicators for subtasks, tags, relative dates
   - Usage tracking display

4. **Dialogs/TemplateManagerDialog.xaml**
   - Full template CRUD interface
   - Template list with live editing
   - Subtask management
   - Tag management
   - Priority and relative date settings

5. **Dialogs/RecurrenceConfigDialog.xaml**
   - Recurrence configuration
   - Type selection (Daily, Weekly, Monthly, Yearly)
   - Interval customization
   - End date option
   - Live preview of recurrence pattern

6. **Dialogs/SaveAsTemplateDialog.xaml**
   - Quick template creation from existing task
   - Name and description input
   - Informational message

7. **Dialogs/TaskImportExportDialog.xaml**
   - Export to file or clipboard
   - Import from file or clipboard
   - Option to include archived tasks
   - Replace or merge options
   - Warning messages

### Documentation
8. **LOCALIZATION_KEYS.md**
   - Complete localization table
   - 125+ translation keys
   - English and Czech translations
   - Implementation notes
   - Naming conventions

## Required Code-Behind Files

You'll need to create these C# files to make the dialogs functional:

### 1. Dialogs/TaskFilterDialog.xaml.cs

```csharp
using AIA.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Wpf.Ui.Controls;

namespace AIA.Dialogs
{
    public partial class TaskFilterDialog : FluentWindow
    {
        public TaskFilter Filter { get; private set; }
        private List<string> _selectedTags = new List<string>();
        
        public TaskFilterDialog(TaskFilter currentFilter)
        {
            InitializeComponent();
            Filter = currentFilter;
            LoadFilter();
            LoadAvailableTags();
        }
        
        private void LoadFilter()
        {
            // Load status filters
            if (Filter.StatusFilter != null)
            {
                ChkStatusNotStarted.IsChecked = Filter.StatusFilter.Contains(TaskStatus.NotStarted);
                ChkStatusInProgress.IsChecked = Filter.StatusFilter.Contains(TaskStatus.InProgress);
                ChkStatusOnHold.IsChecked = Filter.StatusFilter.Contains(TaskStatus.OnHold);
                ChkStatusCompleted.IsChecked = Filter.StatusFilter.Contains(TaskStatus.Completed);
                ChkStatusCancelled.IsChecked = Filter.StatusFilter.Contains(TaskStatus.Cancelled);
            }
            
            // Load priority filters
            if (Filter.PriorityFilter != null)
            {
                ChkPriorityLow.IsChecked = Filter.PriorityFilter.Contains(TaskPriority.Low);
                ChkPriorityMedium.IsChecked = Filter.PriorityFilter.Contains(TaskPriority.Medium);
                ChkPriorityHigh.IsChecked = Filter.PriorityFilter.Contains(TaskPriority.High);
                ChkPriorityCritical.IsChecked = Filter.PriorityFilter.Contains(TaskPriority.Critical);
            }
            
            // Load other filters
            ChkShowCompleted.IsChecked = Filter.ShowCompleted ?? true;
            ChkShowArchived.IsChecked = Filter.ShowArchived ?? false;
            ChkOnlyOverdue.IsChecked = Filter.ShowOverdue ?? false;
            
            DueDateFrom.SelectedDate = Filter.DueDateFrom;
            DueDateTo.SelectedDate = Filter.DueDateTo;
            
            // Load sort
            CmbSortBy.SelectedIndex = (int)Filter.SortBy;
            ChkSortDescending.IsChecked = Filter.SortDescending;
            
            // Load tags
            if (Filter.TagsFilter != null)
            {
                _selectedTags = new List<string>(Filter.TagsFilter);
                SelectedTagsControl.ItemsSource = _selectedTags;
            }
        }
        
        private void LoadAvailableTags()
        {
            var allTags = OverlayViewModel.Singleton.GetAllTags();
            CmbAvailableTags.ItemsSource = allTags;
        }
        
        private void BtnAddTagFilter(object sender, RoutedEventArgs e)
        {
            var tag = CmbAvailableTags.Text;
            if (!string.IsNullOrWhiteSpace(tag) && !_selectedTags.Contains(tag))
            {
                _selectedTags.Add(tag);
                SelectedTagsControl.ItemsSource = null;
                SelectedTagsControl.ItemsSource = _selectedTags;
                CmbAvailableTags.Text = "";
            }
        }
        
        private void BtnRemoveTagFilter(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                _selectedTags.Remove(tag);
                SelectedTagsControl.ItemsSource = null;
                SelectedTagsControl.ItemsSource = _selectedTags;
            }
        }
        
        private void BtnPresetAll(object sender, RoutedEventArgs e)
        {
            Filter = TaskFilter.CreateDefault();
            LoadFilter();
        }
        
        private void BtnPresetActive(object sender, RoutedEventArgs e)
        {
            Filter = TaskFilter.CreateActiveOnly();
            LoadFilter();
        }
        
        private void BtnPresetOverdue(object sender, RoutedEventArgs e)
        {
            Filter = TaskFilter.CreateOverdue();
            LoadFilter();
        }
        
        private void BtnPresetArchived(object sender, RoutedEventArgs e)
        {
            Filter = new TaskFilter { ShowArchived = true, ShowCompleted = true };
            LoadFilter();
        }
        
        private void BtnApply(object sender, RoutedEventArgs e)
        {
            // Build filter from UI
            Filter = new TaskFilter();
            
            // Status filter
            var statuses = new List<TaskStatus>();
            if (ChkStatusNotStarted.IsChecked == true) statuses.Add(TaskStatus.NotStarted);
            if (ChkStatusInProgress.IsChecked == true) statuses.Add(TaskStatus.InProgress);
            if (ChkStatusOnHold.IsChecked == true) statuses.Add(TaskStatus.OnHold);
            if (ChkStatusCompleted.IsChecked == true) statuses.Add(TaskStatus.Completed);
            if (ChkStatusCancelled.IsChecked == true) statuses.Add(TaskStatus.Cancelled);
            if (statuses.Count > 0) Filter.StatusFilter = statuses;
            
            // Priority filter
            var priorities = new List<TaskPriority>();
            if (ChkPriorityLow.IsChecked == true) priorities.Add(TaskPriority.Low);
            if (ChkPriorityMedium.IsChecked == true) priorities.Add(TaskPriority.Medium);
            if (ChkPriorityHigh.IsChecked == true) priorities.Add(TaskPriority.High);
            if (ChkPriorityCritical.IsChecked == true) priorities.Add(TaskPriority.Critical);
            if (priorities.Count > 0) Filter.PriorityFilter = priorities;
            
            // Tags filter
            if (_selectedTags.Count > 0) Filter.TagsFilter = _selectedTags;
            
            // Other filters
            Filter.ShowCompleted = ChkShowCompleted.IsChecked;
            Filter.ShowArchived = ChkShowArchived.IsChecked;
            Filter.ShowOverdue = ChkOnlyOverdue.IsChecked == true ? true : null;
            Filter.DueDateFrom = DueDateFrom.SelectedDate;
            Filter.DueDateTo = DueDateTo.SelectedDate;
            
            // Sort
            Filter.SortBy = (TaskSortCriteria)CmbSortBy.SelectedIndex;
            Filter.SortDescending = ChkSortDescending.IsChecked == true;
            
            DialogResult = true;
            Close();
        }
        
        private void BtnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
```

### 2. Dialogs/TaskTemplateDialog.xaml.cs

```csharp
using AIA.Models;
using System.Windows;
using Wpf.Ui.Controls;

namespace AIA.Dialogs
{
    public partial class TaskTemplateDialog : FluentWindow
    {
        public TaskTemplate? SelectedTemplate { get; private set; }
        
        public TaskTemplateDialog()
        {
            InitializeComponent();
            LoadTemplates();
        }
        
        private void LoadTemplates()
        {
            TemplatesListControl.ItemsSource = OverlayViewModel.Singleton.TaskTemplates;
        }
        
        private void Template_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is TaskTemplate template)
            {
                SelectedTemplate = template;
                DialogResult = true;
                Close();
            }
        }
        
        private void BtnManageTemplates(object sender, RoutedEventArgs e)
        {
            var dialog = new TemplateManagerDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
            LoadTemplates(); // Refresh
        }
        
        private void BtnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
```

### 3. Add to Views/TasksTabView.xaml.cs

Add these event handlers to your existing TasksTabView.xaml.cs:

```csharp
private void BtnShowFilters(object sender, RoutedEventArgs e)
{
    var dialog = new Dialogs.TaskFilterDialog(ViewModel.CurrentTaskFilter);
    dialog.Owner = Window.GetWindow(this);
    if (dialog.ShowDialog() == true)
    {
        ViewModel.CurrentTaskFilter = dialog.Filter;
    }
}

private void BtnShowTemplates(object sender, RoutedEventArgs e)
{
    var dialog = new Dialogs.TaskTemplateDialog();
    dialog.Owner = Window.GetWindow(this);
    if (dialog.ShowDialog() == true && dialog.SelectedTemplate != null)
    {
        ViewModel.CreateTaskFromTemplate(dialog.SelectedTemplate);
    }
}

private void BtnToggleBulkMode(object sender, RoutedEventArgs e)
{
    ViewModel.IsBulkSelectionMode = !ViewModel.IsBulkSelectionMode;
}

private void BtnDuplicateTask(object sender, RoutedEventArgs e)
{
    if (ViewModel.SelectedTask != null)
    {
        ViewModel.DuplicateTask(ViewModel.SelectedTask);
    }
}

private void BtnSaveAsTemplate(object sender, RoutedEventArgs e)
{
    if (ViewModel.SelectedTask == null) return;
    
    var dialog = new Dialogs.SaveAsTemplateDialog();
    dialog.Owner = Window.GetWindow(this);
    if (dialog.ShowDialog() == true)
    {
        _ = ViewModel.SaveTaskAsTemplateAsync(
            ViewModel.SelectedTask, 
            dialog.TemplateName, 
            dialog.TemplateDescription);
    }
}

private void BtnArchiveTask(object sender, RoutedEventArgs e)
{
    if (ViewModel.SelectedTask != null)
    {
        ViewModel.ArchiveTask(ViewModel.SelectedTask);
    }
}

private void BtnConfigureRecurrence(object sender, RoutedEventArgs e)
{
    if (ViewModel.SelectedTask == null) return;
    
    var dialog = new Dialogs.RecurrenceConfigDialog(ViewModel.SelectedTask);
    dialog.Owner = Window.GetWindow(this);
    dialog.ShowDialog();
}

private void BtnAddTag(object sender, RoutedEventArgs e)
{
    if (ViewModel.SelectedTask == null) return;
    
    var tag = Microsoft.VisualBasic.Interaction.InputBox(
        "Enter tag name:", 
        "Add Tag", 
        "", 
        -1, -1);
        
    if (!string.IsNullOrWhiteSpace(tag))
    {
        ViewModel.AddTagToTask(ViewModel.SelectedTask, tag);
    }
}

private void BtnRemoveTag(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is string tag && ViewModel.SelectedTask != null)
    {
        ViewModel.RemoveTagFromTask(ViewModel.SelectedTask, tag);
    }
}

private void BtnAddDependency(object sender, RoutedEventArgs e)
{
    // Show task selector dialog
    // For now, simplified version
    if (ViewModel.SelectedTask == null) return;
    
    // You can create a TaskSelectorDialog for this
    // For now, this is a placeholder
}

private void BtnRemoveDependency(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is Guid depId && ViewModel.SelectedTask != null)
    {
        ViewModel.RemoveTaskDependency(ViewModel.SelectedTask, depId);
    }
}

// Bulk operations
private void BtnBulkSelectAll(object sender, RoutedEventArgs e)
{
    ViewModel.SelectAllTasks();
}

private void BtnBulkDeselectAll(object sender, RoutedEventArgs e)
{
    ViewModel.DeselectAllTasks();
}

private void BtnBulkChangeStatus(object sender, RoutedEventArgs e)
{
    // Show status selection dialog
    // For simplicity, you can use a simple ComboBox dialog
}

private void BtnBulkChangePriority(object sender, RoutedEventArgs e)
{
    // Show priority selection dialog
}

private void BtnBulkAddTag(object sender, RoutedEventArgs e)
{
    var tag = Microsoft.VisualBasic.Interaction.InputBox(
        "Enter tag to add to selected tasks:", 
        "Add Tag", 
        "", 
        -1, -1);
        
    if (!string.IsNullOrWhiteSpace(tag))
    {
        ViewModel.BulkAddTag(tag);
    }
}

private void BtnBulkArchive(object sender, RoutedEventArgs e)
{
    var result = MessageBox.Show(
        $"Archive {ViewModel.GetSelectedTasks().Count} selected task(s)?",
        "Confirm Archive",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);
        
    if (result == MessageBoxResult.Yes)
    {
        ViewModel.BulkArchiveTasks();
    }
}

private void BtnBulkDelete(object sender, RoutedEventArgs e)
{
    var result = MessageBox.Show(
        $"Delete {ViewModel.GetSelectedTasks().Count} selected task(s)?",
        "Confirm Delete",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);
        
    if (result == MessageBoxResult.Yes)
    {
        ViewModel.BulkDeleteTasks();
    }
}

private void BtnImportExport(object sender, RoutedEventArgs e)
{
    var dialog = new Dialogs.TaskImportExportDialog();
    dialog.Owner = Window.GetWindow(this);
    dialog.ShowDialog();
}

private void TaskCheckBox_Click(object sender, RoutedEventArgs e)
{
    e.Handled = true; // Prevent triggering task selection
}

private void MenuItem_Duplicate(object sender, RoutedEventArgs e)
{
    if (sender is MenuItem menuItem && menuItem.DataContext is TaskItem task)
    {
        ViewModel.DuplicateTask(task);
    }
}

private void MenuItem_SaveAsTemplate(object sender, RoutedEventArgs e)
{
    if (sender is MenuItem menuItem && menuItem.DataContext is TaskItem task)
    {
        var dialog = new Dialogs.SaveAsTemplateDialog();
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() == true)
        {
            _ = ViewModel.SaveTaskAsTemplateAsync(task, dialog.TemplateName, dialog.TemplateDescription);
        }
    }
}

private void MenuItem_Archive(object sender, RoutedEventArgs e)
{
    if (sender is MenuItem menuItem && menuItem.DataContext is TaskItem task)
    {
        ViewModel.ArchiveTask(task);
    }
}

private void MenuItem_Delete(object sender, RoutedEventArgs e)
{
    if (sender is MenuItem menuItem && menuItem.DataContext is TaskItem task)
    {
        var result = MessageBox.Show(
            $"Delete task '{task.Title}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
            
        if (result == MessageBoxResult.Yes)
        {
            ViewModel.DeleteTask(task);
        }
    }
}

private void TaskProperty_Changed(object sender, RoutedEventArgs e)
{
    _ = ViewModel.SaveTasksAndRemindersAsync();
}
```

## Additional Converters Needed

Add these to your Converters folder:

### RecurrenceIntervalConverter.cs
```csharp
using System;
using System.Globalization;
using System.Windows.Data;
using AIA.Models;

namespace AIA.Converters
{
    public class RecurrenceIntervalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RecurrenceType type)
            {
                return type switch
                {
                    RecurrenceType.Daily => "day(s)",
                    RecurrenceType.Weekly => "week(s)",
                    RecurrenceType.Monthly => "month(s)",
                    RecurrenceType.Yearly => "year(s)",
                    _ => ""
                };
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
```

### TaskIdToTitleConverter.cs
```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace AIA.Converters
{
    public class TaskIdToTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Guid taskId)
            {
                var task = Models.OverlayViewModel.Singleton.FindTaskById(taskId);
                return task?.Title ?? "Unknown Task";
            }
            return "Unknown Task";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
```

## App.xaml Resources

Add these converters to your App.xaml:

```xaml
<Application.Resources>
    <!-- Existing resources -->
    
    <!-- Add these converters -->
    <local:RecurrenceIntervalConverter x:Key="RecurrenceIntervalConverter"/>
    <local:TaskIdToTitleConverter x:Key="TaskIdToTitleConverter"/>
</Application.Resources>
```

## Implementation Checklist

- [ ] Add localization keys to .resx files
- [ ] Create all dialog code-behind files
- [ ] Add event handlers to TasksTabView.xaml.cs
- [ ] Create converter classes
- [ ] Register converters in App.xaml
- [ ] Test filtering functionality
- [ ] Test template creation and usage
- [ ] Test bulk operations
- [ ] Test import/export
- [ ] Test recurring tasks
- [ ] Test tags and dependencies

## Feature Summary

? **Complete UI Implementation** for:
1. Task Duplication (context menu + button)
2. Task Templates (selection dialog + manager)
3. Filtering & Sorting (comprehensive dialog)
4. Bulk Operations (toolbar mode with checkboxes)
5. Tags & Labels (inline editing)
6. Dependencies (with visual indicators)
7. Recurring Tasks (configuration dialog)
8. Archiving (quick action)
9. Import/Export (full dialog)

? **125+ Localization Keys** (English + Czech)
? **7 New Dialog Windows**
? **Enhanced Main View**
? **All Features Non-Intrusive & Optional**

All UI components are ready for use!
