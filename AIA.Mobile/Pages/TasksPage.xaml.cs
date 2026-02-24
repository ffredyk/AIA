using AIA.Mobile.ViewModels;

namespace AIA.Mobile.Pages;

public partial class TasksPage : ContentPage
{
    public TasksPage(TasksViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
