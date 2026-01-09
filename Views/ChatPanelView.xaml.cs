using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIA.Models;
using AIA.Models.AI;

namespace AIA.Views
{
    public partial class ChatPanelView
    {
        public ChatPanelView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        /// <summary>
        /// Event raised when a message is sent and processed
        /// </summary>
        public event EventHandler<string>? MessageSent;

        #region Chat Management Event Handlers

        private void BtnNewChat_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.CreateNewChatSession();
        }

        private void BtnDeleteChat_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedChatSession == null) return;

            // Show confirmation
            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete '{ViewModel.SelectedChatSession.ChatTitle}'?",
                "Delete Chat",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ViewModel.DeleteSelectedChatSession();
            }
        }

        private void BtnRenameChat_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedChatSession == null) return;
            
            ViewModel.StartRenamingSelectedChat();
            
            // Focus the rename textbox
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RenameChatTextBox.Focus();
                RenameChatTextBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void BtnClearChat_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedChatSession == null) return;

            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to clear all messages in this chat?",
                "Clear Chat",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ViewModel.ClearSelectedChatSession();
            }
        }

        private void BtnConfirmRename_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ConfirmRenameChatSession();
        }

        private void BtnCancelRename_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.CancelRenameChatSession();
        }

        private void RenameChatTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ViewModel?.ConfirmRenameChatSession();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ViewModel?.CancelRenameChatSession();
            }
        }

        #endregion

        #region Message Sending

        private void BtnSendMessage(object sender, RoutedEventArgs e)
        {
            _ = SendMessageAsync();
        }

        private void MessageInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                _ = SendMessageAsync();
            }
        }

        private async Task SendMessageAsync()
        {
            if (ViewModel == null || ViewModel.SelectedChatSession == null)
                return;

            var userMessage = MessageInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(userMessage))
                return;

            MessageInput.Clear();
            MessageInput.Focus();

            var userChatMessage = new ChatMessage
            {
                Role = "user",
                Content = userMessage
            };
            ViewModel.SelectedChatSession.Messages.Add(userChatMessage);

            var assistantMessage = new ChatMessage
            {
                Role = "assistant",
                Content = ""
            };
            ViewModel.SelectedChatSession.Messages.Add(assistantMessage);

            // Scroll to bottom
            ChatScrollViewer.ScrollToEnd();

            try
            {
                ViewModel.IsAiProcessing = true;
                ViewModel.AiStatusMessage = "Processing...";

                // Build conversation history for AI
                var conversationHistory = new List<AIMessage>();
                foreach (var msg in ViewModel.SelectedChatSession.Messages)
                {
                    if (msg != assistantMessage)
                    {
                        conversationHistory.Add(new AIMessage
                        {
                            Role = msg.Role,
                            Content = msg.Content
                        });
                    }
                }

                // Use streaming with tool support
                var contentBuilder = new System.Text.StringBuilder();

                await foreach (var (chunk, status) in ViewModel.AIOrchestration.GenerateStreamWithToolsAsync(
                    userMessage,
                    conversationHistory.Take(conversationHistory.Count - 1).ToList()))
                {
                    // Update status message
                    if (!string.IsNullOrEmpty(status) && status != "Streaming")
                    {
                        ViewModel.AiStatusMessage = status;
                    }
                    
                    // Append content chunk
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        contentBuilder.Append(chunk);
                        assistantMessage.Content = contentBuilder.ToString();

                        // Scroll to bottom as content updates
                        ChatScrollViewer.ScrollToEnd();
                    }
                }

                ViewModel.AiStatusMessage = "Response complete";

                MessageSent?.Invoke(this, userMessage);
            }
            catch (Exception ex)
            {
                assistantMessage.Content = $"Error: {ex.Message}";
                ViewModel.AiStatusMessage = "Error occurred";
            }
            finally
            {
                ViewModel.IsAiProcessing = false;
                ChatScrollViewer.ScrollToEnd();

                // Save chat session after message exchange
                _ = ViewModel.SaveChatsAsync();
            }
        }

        #endregion
    }
}
