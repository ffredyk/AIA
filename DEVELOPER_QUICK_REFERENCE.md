# Task Management Quick Reference Guide

## For Developers: How to Use the New Features

### 1. Duplicating Tasks

**From Code:**
```csharp
var duplicate = OverlayViewModel.Singleton.DuplicateTask(
    task,               // Task to duplicate
    resetStatus: true,  // Reset to NotStarted
    includeSubtasks: true  // Include all subtasks
);
```

**From UI:**
- Right-click task ? Duplicate
- Or use task details toolbar button

### 2. Using Templates

**Create Task from Template:**
```csharp
var template = OverlayViewModel.Singleton.TaskTemplates.First();
var newTask = OverlayViewModel.Singleton.CreateTaskFromTemplate(template);
```

**Save Task as Template:**
```csharp
var template = await OverlayViewModel.Singleton.SaveTaskAsTemplateAsync(
    task,
    "My Template Name",
    "Description of what this template is for"
);
```

**From UI:**
- Click "From Template" button ? Select template
- Right-click task ? Save as Template

### 3. Filtering Tasks

**Apply Filter:**
```csharp
var filter = new TaskFilter
{
    StatusFilter = new List<TaskStatus> { TaskStatus.InProgress, TaskStatus.NotStarted },
    PriorityFilter = new List<TaskPriority> { TaskPriority.High, TaskPriority.Critical },
    ShowCompleted = false,
    ShowArchived = false,
    SortBy = TaskSortCriteria.DueDate,
    SortDescending = false
};

OverlayViewModel.Singleton.CurrentTaskFilter = filter;
// Filter is automatically applied
```

**Preset Filters:**
```csharp
// Show all tasks
OverlayViewModel.Singleton.CurrentTaskFilter = TaskFilter.CreateDefault();

// Show only active (non-completed) tasks
OverlayViewModel.Singleton.CurrentTaskFilter = TaskFilter.CreateActiveOnly();

// Show only overdue tasks
OverlayViewModel.Singleton.CurrentTaskFilter = TaskFilter.CreateOverdue();
```

**From UI:**
- Click "Filters" button ? Configure filters ? Apply

### 4. Bulk Operations

**Enable Bulk Mode:**
```csharp
OverlayViewModel.Singleton.IsBulkSelectionMode = true;
```

**Select/Deselect:**
```csharp
// Select all filtered tasks
OverlayViewModel.Singleton.SelectAllTasks();

// Deselect all
OverlayViewModel.Singleton.DeselectAllTasks();

// Get selected tasks
var selectedTasks = OverlayViewModel.Singleton.GetSelectedTasks();
```

**Bulk Operations:**
```csharp
// Change status for all selected
OverlayViewModel.Singleton.BulkChangeStatus(TaskStatus.Completed);

// Change priority for all selected
OverlayViewModel.Singleton.BulkChangePriority(TaskPriority.High);

// Archive selected tasks
OverlayViewModel.Singleton.BulkArchiveTasks();

// Delete selected tasks
OverlayViewModel.Singleton.BulkDeleteTasks();

// Add tag to selected tasks
OverlayViewModel.Singleton.BulkAddTag("urgent");
```

**From UI:**
- Click "Bulk Mode" button
- Select tasks with checkboxes
- Use bulk toolbar operations

### 5. Tags

**Add/Remove Tags:**
```csharp
// Add tag
OverlayViewModel.Singleton.AddTagToTask(task, "important");

// Remove tag
OverlayViewModel.Singleton.RemoveTagFromTask(task, "important");

// Get all unique tags
var allTags = OverlayViewModel.Singleton.GetAllTags();
```

**From UI:**
- Task details ? Tags section ? Add Tag
- Click X on tag to remove

### 6. Dependencies

**Add/Remove Dependencies:**
```csharp
// Task B depends on Task A (A must complete before B can start)
OverlayViewModel.Singleton.AddTaskDependency(taskB, taskA);

// Remove dependency
OverlayViewModel.Singleton.RemoveTaskDependency(taskB, taskA.Id);

// Check if task can start
bool canStart = OverlayViewModel.Singleton.CanStartTask(taskB);

// Get tasks that depend on this task
var dependents = OverlayViewModel.Singleton.GetDependentTasks(taskA);
```

**From UI:**
- Task details ? Dependencies section ? Add Dependency
- Select task from list
- Click X to remove dependency

### 7. Recurring Tasks

**Configure Recurrence:**
```csharp
task.RecurrenceType = RecurrenceType.Weekly;
task.RecurrenceInterval = 2;  // Every 2 weeks
task.RecurrenceEndDate = DateTime.Now.AddMonths(6);  // Optional end date

await OverlayViewModel.Singleton.SaveTasksAndRemindersAsync();
```

**Process Recurring Tasks:**
```csharp
// Call this periodically (e.g., on app startup)
await OverlayViewModel.Singleton.ProcessRecurringTasksAsync();
// This creates next instances of completed recurring tasks
```

**From UI:**
- Task details ? Recurrence section ? Configure
- Set type, interval, and optional end date

### 8. Archiving

**Archive/Unarchive:**
```csharp
// Archive task (hide from normal view)
OverlayViewModel.Singleton.ArchiveTask(task);

// Restore from archive
OverlayViewModel.Singleton.UnarchiveTask(task);

// Show archived tasks in filter
var filter = new TaskFilter { ShowArchived = true };
OverlayViewModel.Singleton.CurrentTaskFilter = filter;
```

**From UI:**
- Click Archive button in task details
- Or right-click ? Archive
- Use filter to show archived tasks

### 9. Import/Export

**Export:**
```csharp
// Export to file
bool success = await OverlayViewModel.Singleton.ExportTasksAsync(
    @"C:\backup\tasks.json",
    includeArchived: true
);
```

**Import:**
```csharp
// Import from file
int importedCount = await OverlayViewModel.Singleton.ImportTasksAsync(
    @"C:\backup\tasks.json",
    replaceExisting: false  // Merge with existing
);
```

**From UI:**
- Click Import/Export button
- Choose export/import option
- Select file location

### 10. Common Patterns

**Complete Workflow Example:**
```csharp
// 1. Create task from template
var template = OverlayViewModel.Singleton.TaskTemplates
    .FirstOrDefault(t => t.Name == "Bug Fix");
var newTask = OverlayViewModel.Singleton.CreateTaskFromTemplate(template);

// 2. Customize the task
newTask.Title = "Fix login issue";
newTask.DueDate = DateTime.Now.AddDays(3);

// 3. Add tags
OverlayViewModel.Singleton.AddTagToTask(newTask, "urgent");
OverlayViewModel.Singleton.AddTagToTask(newTask, "bug");

// 4. Set up recurrence
newTask.RecurrenceType = RecurrenceType.Monthly;
newTask.RecurrenceInterval = 1;

// 5. Save
await OverlayViewModel.Singleton.SaveTasksAndRemindersAsync();
```

**Batch Task Creation:**
```csharp
var tasks = new[]
{
    "Write documentation",
    "Update tests",
    "Review code"
};

foreach (var title in tasks)
{
    var task = new TaskItem
    {
        Title = title,
        Priority = TaskPriority.Medium,
        DueDate = DateTime.Now.AddDays(7)
    };
    
    OverlayViewModel.Singleton.Tasks.Add(task);
    OverlayViewModel.Singleton.AddTagToTask(task, "project-x");
}

await OverlayViewModel.Singleton.SaveTasksAndRemindersAsync();
```

## Event Handling

**Listen for Filter Changes:**
```csharp
// FilteredTasks is automatically updated when CurrentTaskFilter changes
OverlayViewModel.Singleton.PropertyChanged += (s, e) =>
{
    if (e.PropertyName == nameof(OverlayViewModel.FilteredTasks))
    {
        // Update UI or perform other actions
    }
};
```

**Listen for Template Changes:**
```csharp
OverlayViewModel.Singleton.TaskTemplates.CollectionChanged += (s, e) =>
{
    // Template added, removed, or modified
};
```

## Best Practices

1. **Always save after modifications:**
   ```csharp
   // After any task modification
   await OverlayViewModel.Singleton.SaveTasksAndRemindersAsync();
   ```

2. **Use FilteredTasks for display:**
   ```csharp
   // Bind to FilteredTasks, not Tasks
   TasksListBox.ItemsSource = OverlayViewModel.Singleton.FilteredTasks;
   ```

3. **Check dependencies before changing status:**
   ```csharp
   if (!OverlayViewModel.Singleton.CanStartTask(task))
   {
       MessageBox.Show("This task has incomplete dependencies!");
       return;
   }
   task.Status = TaskStatus.InProgress;
   ```

4. **Process recurring tasks on startup:**
   ```csharp
   // In App.xaml.cs OnStartup
   await OverlayViewModel.Singleton.ProcessRecurringTasksAsync();
   ```

5. **Validate template data:**
   ```csharp
   if (string.IsNullOrWhiteSpace(templateName))
   {
       MessageBox.Show("Template name is required!");
       return;
   }
   ```

## Performance Tips

1. **Batch operations:** Use bulk methods instead of loops
   ```csharp
   // Good
   OverlayViewModel.Singleton.BulkAddTag("important");
   
   // Avoid
   foreach (var task in selectedTasks)
   {
       OverlayViewModel.Singleton.AddTagToTask(task, "important");
   }
   ```

2. **Filter efficiency:** Use specific filters instead of filtering in code
   ```csharp
   // Good
   var filter = new TaskFilter { PriorityFilter = new List<TaskPriority> { TaskPriority.High } };
   
   // Avoid
   var highPriorityTasks = allTasks.Where(t => t.Priority == TaskPriority.High);
   ```

3. **Dispose properly:** Templates and filters are lightweight, but large task sets should be managed
   ```csharp
   // Export before clearing
   await OverlayViewModel.Singleton.ExportTasksAsync("backup.json", true);
   OverlayViewModel.Singleton.Tasks.Clear();
   ```

## Troubleshooting

**Tasks not appearing after filter:**
- Check if `ShowArchived` is set correctly
- Verify status and priority filters
- Clear search text

**Template not creating tasks:**
- Ensure template has required fields (Name, Title)
- Check if relative due dates are valid
- Verify template is in TaskTemplates collection

**Bulk operations not working:**
- Ensure `IsBulkSelectionMode` is true
- Check if tasks are actually selected (IsSelected = true)
- Verify there are filtered tasks visible

**Recurring tasks not creating:**
- Ensure task is marked as Completed
- Check RecurrenceType is not None
- Verify RecurrenceEndDate hasn't passed
- Call ProcessRecurringTasksAsync()

## Integration with Plugins

Plugins can access task features through the ITaskService:

```csharp
// In plugin
var taskService = Context.Services.GetService<ITaskService>();

// Create task
var task = taskService.CreateTask("Plugin Task", "Created by plugin");

// Get all tasks
var tasks = taskService.GetAllTasks();

// Save changes
await taskService.SaveAsync();
```

## Summary

All task management features are:
- ? **Thread-safe** (use Dispatcher for UI updates)
- ? **Persistent** (automatically saved)
- ? **Observable** (PropertyChanged notifications)
- ? **Filterable** (real-time filtering)
- ? **Extensible** (easy to add new features)
- ? **Localized** (full i18n support)
- ? **Plugin-compatible** (ITaskService interface)

Happy coding! ??
