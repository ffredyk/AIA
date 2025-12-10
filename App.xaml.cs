using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using AIWrap;

namespace AIA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? notifyIcon;

        public static AIController? AIRoot { get; internal set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create the NotifyIcon
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = SystemIcons.Application; // You can replace with custom icon
            notifyIcon.Text = "AIA";
            notifyIcon.Visible = true;

            // Double-click to show/restore window
            notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

            // Create context menu
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (s, args) => ShowMainWindow());
            contextMenu.Items.Add("Exit", null, (s, args) => Shutdown());
            notifyIcon.ContextMenuStrip = contextMenu;

            AIRoot = AIController.CreateAzureOpenAI("1erEX4sc6tuvbdMgojpKZe3PW8Cd86XcgaZLpPrgprMucxOghVueJQQJ99BIACfhMk5XJ3w3AAAAACOGsPjn", "https://ffredyk-8044sw-resource.services.ai.azure.com/api/projects/ffredyk-8044sw", "mainagent");
        }

        private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            if (MainWindow != null)
            {
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            notifyIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
