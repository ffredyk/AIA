using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AIA.Models;
using AIA.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace AIA.Dialogs
{
    public partial class TemplateManagerDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly OverlayViewModel _viewModel;
        private TaskTemplate? _selectedTemplate;

        public TemplateManagerDialog()
        {
            InitializeComponent();
            _viewModel = OverlayViewModel.Singleton;
            
            TemplatesListBox.ItemsSource = _viewModel.TaskTemplates;
            
            if (_viewModel.TaskTemplates.Count > 0)
            {
                TemplatesListBox.SelectedIndex = 0;
            }
            else
            {
                DetailsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void TemplatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedTemplate = TemplatesListBox.SelectedItem as TaskTemplate;
            
            if (_selectedTemplate != null)
            {
                DetailsPanel.Visibility = Visibility.Visible;
                LoadTemplateDetails(_selectedTemplate);
            }
            else
            {
                DetailsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadTemplateDetails(TaskTemplate template)
        {
            TxtTemplateName.Text = template.Name;
            TxtTemplateDescription.Text = template.Description;
            TxtTaskTitle.Text = template.Title;
            TxtTaskDescription.Text = template.TaskDescription;
            
            // Set priority
            CmbPriority.SelectedIndex = template.DefaultPriority switch
            {
                TaskPriority.Low => 0,
                TaskPriority.Medium => 1,
                TaskPriority.High => 2,
                TaskPriority.Critical => 3,
                _ => 1
            };
            
            NumRelativeDays.Value = template.RelativeDueDateDays ?? 0;
            
            // Load tags
            TagsListControl.ItemsSource = null;
            TagsListControl.ItemsSource = template.Tags;
            
            // Load subtasks
            SubtasksListControl.ItemsSource = null;
            SubtasksListControl.ItemsSource = template.Subtasks;
        }

        private void SaveCurrentTemplate()
        {
            if (_selectedTemplate == null) return;

            _selectedTemplate.Name = TxtTemplateName.Text?.Trim() ?? "";
            _selectedTemplate.Description = TxtTemplateDescription.Text?.Trim() ?? "";
            _selectedTemplate.Title = TxtTaskTitle.Text?.Trim() ?? "";
            _selectedTemplate.TaskDescription = TxtTaskDescription.Text?.Trim() ?? "";
            
            _selectedTemplate.DefaultPriority = CmbPriority.SelectedIndex switch
            {
                0 => TaskPriority.Low,
                1 => TaskPriority.Medium,
                2 => TaskPriority.High,
                3 => TaskPriority.Critical,
                _ => TaskPriority.Medium
            };
            
            _selectedTemplate.RelativeDueDateDays = (int)(NumRelativeDays.Value ?? 0);
        }

        private void BtnNewTemplate(object sender, RoutedEventArgs e)
        {
            var newTemplate = new TaskTemplate
            {
                Name = "New Template",
                Description = "",
                DefaultPriority = TaskPriority.Medium
            };
            
            _viewModel.TaskTemplates.Add(newTemplate);
            TemplatesListBox.ItemsSource = null;
            TemplatesListBox.ItemsSource = _viewModel.TaskTemplates;
            TemplatesListBox.SelectedItem = newTemplate;
        }

        private async void BtnDeleteTemplate(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null) return;

            var result = WpfMessageBox.Show(
                $"Are you sure you want to delete the template '{_selectedTemplate.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await _viewModel.DeleteTaskTemplateAsync(_selectedTemplate);
                TemplatesListBox.ItemsSource = null;
                TemplatesListBox.ItemsSource = _viewModel.TaskTemplates;
                
                if (_viewModel.TaskTemplates.Count > 0)
                {
                    TemplatesListBox.SelectedIndex = 0;
                }
            }
        }

        private void BtnAddTag(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null) return;

            var tag = TxtNewTag.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(tag) && !_selectedTemplate.Tags.Contains(tag))
            {
                _selectedTemplate.Tags.Add(tag);
                TxtNewTag.Text = string.Empty;
                
                // Refresh tags list
                TagsListControl.ItemsSource = null;
                TagsListControl.ItemsSource = _selectedTemplate.Tags;
            }
        }

        private void BtnRemoveTag(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null) return;
            
            if (sender is FrameworkElement element && element.Tag is string tag)
            {
                _selectedTemplate.Tags.Remove(tag);
                
                // Refresh tags list
                TagsListControl.ItemsSource = null;
                TagsListControl.ItemsSource = _selectedTemplate.Tags;
            }
        }

        private void BtnAddSubtask(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null) return;

            var subtask = new SubtaskTemplate
            {
                Title = "New Subtask",
                Priority = TaskPriority.Medium
            };
            
            _selectedTemplate.Subtasks.Add(subtask);
            
            // Refresh subtasks list
            SubtasksListControl.ItemsSource = null;
            SubtasksListControl.ItemsSource = _selectedTemplate.Subtasks;
        }

        private void BtnRemoveSubtask(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null) return;
            
            if (sender is FrameworkElement element && element.Tag is SubtaskTemplate subtask)
            {
                _selectedTemplate.Subtasks.Remove(subtask);
                
                // Refresh subtasks list
                SubtasksListControl.ItemsSource = null;
                SubtasksListControl.ItemsSource = _selectedTemplate.Subtasks;
            }
        }

        private async void BtnSave(object sender, RoutedEventArgs e)
        {
            SaveCurrentTemplate();
            await _viewModel.SaveTaskTemplatesAsync();
            
            WpfMessageBox.Show("Template saved successfully.", 
                "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose(object sender, RoutedEventArgs e)
        {
            SaveCurrentTemplate();
            _ = _viewModel.SaveTaskTemplatesAsync();
            Close();
        }
    }
}
