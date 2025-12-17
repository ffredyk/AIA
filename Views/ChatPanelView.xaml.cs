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
                Content = "Thinking..."
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

                // Use AI Orchestration Service - run on UI thread since tools may modify UI-bound collections
                var response = await ViewModel.AIOrchestration.GenerateAsync(
                    userMessage,
                    conversationHistory.Take(conversationHistory.Count - 1).ToList()
                );

                if (response.Success)
                {
                    assistantMessage.Content = response.Content;

                    if (response.UsedProvider != null)
                    {
                        ViewModel.AiStatusMessage = $"Response from {response.UsedProvider.Name} ({response.PromptTokens + response.CompletionTokens} tokens)";
                    }
                }
                else
                {
                    assistantMessage.Content = $"Error: {response.Error}";
                    ViewModel.AiStatusMessage = "Error occurred";
                }

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
            }
        }
    }
}
