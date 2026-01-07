using System;
using System.Collections.Generic;
using System.Linq;

namespace AIA.Models
{
    /// <summary>
    /// Represents filter and sort criteria for tasks
    /// </summary>
    public class TaskFilter
    {
        public List<TaskStatus>? StatusFilter { get; set; }
        public List<TaskPriority>? PriorityFilter { get; set; }
        public List<string>? TagsFilter { get; set; }
        public bool? ShowCompleted { get; set; }
        public bool? ShowOverdue { get; set; }
        public bool? ShowArchived { get; set; }
        public DateTime? DueDateFrom { get; set; }
        public DateTime? DueDateTo { get; set; }
        public string? SearchText { get; set; }
        public TaskSortCriteria SortBy { get; set; } = TaskSortCriteria.CreatedDate;
        public bool SortDescending { get; set; } = true;

        /// <summary>
        /// Applies the filter to a collection of tasks
        /// </summary>
        public IEnumerable<TaskItem> Apply(IEnumerable<TaskItem> tasks)
        {
            var filtered = tasks.AsEnumerable();

            // Status filter
            if (StatusFilter != null && StatusFilter.Count > 0)
            {
                filtered = filtered.Where(t => StatusFilter.Contains(t.Status));
            }

            // Priority filter
            if (PriorityFilter != null && PriorityFilter.Count > 0)
            {
                filtered = filtered.Where(t => PriorityFilter.Contains(t.Priority));
            }

            // Tags filter
            if (TagsFilter != null && TagsFilter.Count > 0)
            {
                filtered = filtered.Where(t => t.Tags.Any(tag => TagsFilter.Contains(tag)));
            }

            // Show completed
            if (ShowCompleted.HasValue && !ShowCompleted.Value)
            {
                filtered = filtered.Where(t => !t.IsCompleted);
            }

            // Show overdue
            if (ShowOverdue.HasValue && ShowOverdue.Value)
            {
                filtered = filtered.Where(t => t.IsOverdue);
            }

            // Show archived
            if (ShowArchived.HasValue)
            {
                filtered = filtered.Where(t => t.IsArchived == ShowArchived.Value);
            }
            else
            {
                // By default, don't show archived tasks
                filtered = filtered.Where(t => !t.IsArchived);
            }

            // Due date range
            if (DueDateFrom.HasValue)
            {
                filtered = filtered.Where(t => t.DueDate.HasValue && t.DueDate.Value >= DueDateFrom.Value);
            }

            if (DueDateTo.HasValue)
            {
                filtered = filtered.Where(t => t.DueDate.HasValue && t.DueDate.Value <= DueDateTo.Value);
            }

            // Search text
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.ToLower();
                filtered = filtered.Where(t =>
                    t.Title.ToLower().Contains(search) ||
                    t.Description.ToLower().Contains(search) ||
                    t.Notes.ToLower().Contains(search));
            }

            // Apply sorting
            filtered = SortBy switch
            {
                TaskSortCriteria.Title => SortDescending
                    ? filtered.OrderByDescending(t => t.Title)
                    : filtered.OrderBy(t => t.Title),
                TaskSortCriteria.Status => SortDescending
                    ? filtered.OrderByDescending(t => t.Status)
                    : filtered.OrderBy(t => t.Status),
                TaskSortCriteria.Priority => SortDescending
                    ? filtered.OrderByDescending(t => t.Priority)
                    : filtered.OrderBy(t => t.Priority),
                TaskSortCriteria.DueDate => SortDescending
                    ? filtered.OrderByDescending(t => t.DueDate ?? DateTime.MaxValue)
                    : filtered.OrderBy(t => t.DueDate ?? DateTime.MaxValue),
                TaskSortCriteria.CreatedDate => SortDescending
                    ? filtered.OrderByDescending(t => t.CreatedDate)
                    : filtered.OrderBy(t => t.CreatedDate),
                _ => filtered
            };

            return filtered;
        }

        /// <summary>
        /// Creates a default filter that shows active (non-archived, non-completed) tasks
        /// </summary>
        public static TaskFilter CreateDefault()
        {
            return new TaskFilter
            {
                ShowArchived = false,
                ShowCompleted = true,
                SortBy = TaskSortCriteria.DueDate,
                SortDescending = false
            };
        }

        /// <summary>
        /// Creates a filter that shows only active tasks
        /// </summary>
        public static TaskFilter CreateActiveOnly()
        {
            return new TaskFilter
            {
                ShowArchived = false,
                ShowCompleted = false,
                SortBy = TaskSortCriteria.Priority,
                SortDescending = true
            };
        }

        /// <summary>
        /// Creates a filter that shows overdue tasks
        /// </summary>
        public static TaskFilter CreateOverdue()
        {
            return new TaskFilter
            {
                ShowOverdue = true,
                ShowArchived = false,
                ShowCompleted = false,
                SortBy = TaskSortCriteria.DueDate,
                SortDescending = false
            };
        }
    }

    public enum TaskSortCriteria
    {
        Title,
        Status,
        Priority,
        DueDate,
        CreatedDate
    }
}
