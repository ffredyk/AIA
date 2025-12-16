using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using System.Reflection;
using AIA.Models;

namespace AIA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? notifyIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create the NotifyIcon
            notifyIcon = new NotifyIcon();
            
            // Load custom icon from embedded resource
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("AIA.Icons.AIA.ico");
            if (stream != null)
            {
                notifyIcon.Icon = new Icon(stream);
            }
            else
            {
                notifyIcon.Icon = SystemIcons.Application;
            }
            
            notifyIcon.Text = "AIA";
            notifyIcon.Visible = true;

            // Double-click to show/restore window
            notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

            // Create context menu
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (s, args) => ShowMainWindow());
            contextMenu.Items.Add("Exit", null, (s, args) => Shutdown());
            notifyIcon.ContextMenuStrip = contextMenu;
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
            // Save tasks and reminders before exiting
            OverlayViewModel.Singleton.SaveTasksAndRemindersAsync().GetAwaiter().GetResult();
            
            notifyIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
