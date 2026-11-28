using AIA.Mobile.Services;

namespace AIA.Mobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            _ = MobileApp.InitializeAsync();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}
