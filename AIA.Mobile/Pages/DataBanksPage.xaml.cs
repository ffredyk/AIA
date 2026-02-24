using AIA.Mobile.ViewModels;

namespace AIA.Mobile.Pages;

public partial class DataBanksPage : ContentPage
{
    public DataBanksPage(DataBanksViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
