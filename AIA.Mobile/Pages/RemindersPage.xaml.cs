using AIA.Mobile.ViewModels;

namespace AIA.Mobile.Pages;

public partial class RemindersPage : ContentPage
{
    public RemindersPage(RemindersViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
