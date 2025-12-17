using System.Windows;
using System.Windows.Controls;

namespace AIA.Views
{
    public partial class ToolbarView
    {
        public ToolbarView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Event raised when New Task button is clicked
        /// </summary>
        public event EventHandler? NewTaskClicked;

        /// <summary>
        /// Event raised when New Reminder button is clicked
        /// </summary>
        public event EventHandler? NewReminderClicked;

        /// <summary>
        /// Event raised when Settings button is clicked
        /// </summary>
        public event EventHandler? SettingsClicked;

        /// <summary>
        /// Event raised when Orchestration button is clicked
        /// </summary>
        public event EventHandler? OrchestrationClicked;

        /// <summary>
        /// Event raised when Shutdown button is clicked
        /// </summary>
        public event EventHandler? ShutdownClicked;

        /// <summary>
        /// Event raised when Close button is clicked
        /// </summary>
        public event EventHandler? CloseClicked;

        private void BtnNewTask(object sender, RoutedEventArgs e)
        {
            NewTaskClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BtnNewReminder(object sender, RoutedEventArgs e)
        {
            NewReminderClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BtnSettings(object sender, RoutedEventArgs e)
        {
            SettingsClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BtnOrchestration(object sender, RoutedEventArgs e)
        {
            OrchestrationClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BtnShutdown(object sender, RoutedEventArgs e)
        {
            ShutdownClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BtnClose(object sender, RoutedEventArgs e)
        {
            CloseClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
