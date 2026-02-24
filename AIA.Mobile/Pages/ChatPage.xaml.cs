using AIA.Mobile.ViewModels;

namespace AIA.Mobile.Pages;

public partial class ChatPage : ContentPage
{
    public ChatPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
