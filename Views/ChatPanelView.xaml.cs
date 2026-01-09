using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIA.Models;
using AIA.Models.AI;

namespace AIA.Views
{
    public partial class ChatPanelView
    {
        private DispatcherTimer? _clearChatConfirmTimer;
        private bool _isClearChatConfirmMode;

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

        private void BtnDeleteCurrentChat_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedChatSession == null) return;

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

        private void BtnDeleteAllChats_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            var chatCount = ViewModel.ActiveChats.Count;
            if (chatCount == 0) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete all {chatCount} chat(s)?\n\nThis action cannot be undone.",
                "Delete All Chats",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                ViewModel.DeleteAllChatSessions();
            }
        }

        private void BtnDeleteAllExceptCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedChatSession == null) return;

            var chatCount = ViewModel.ActiveChats.Count - 1;
            if (chatCount <= 0) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete {chatCount} chat(s)?\n\nThe current chat will be kept. This action cannot be undone.",
                "Delete All Chats",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                ViewModel.DeleteAllChatSessionsExceptCurrent();
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

            if (!_isClearChatConfirmMode)
            {
                // Enter confirmation mode
                EnterClearChatConfirmMode();
            }
            else
            {
                // Confirm and clear
                ExitClearChatConfirmMode();
                ViewModel.ClearSelectedChatSession();
            }
        }

        private void EnterClearChatConfirmMode()
        {
            _isClearChatConfirmMode = true;
            BtnClearChat.Tag = "confirm";
            ClearChatIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.QuestionCircle16;
            ClearChatConfirmText.Visibility = Visibility.Visible;

            // Start timer to reset after 3 seconds
            _clearChatConfirmTimer?.Stop();
            _clearChatConfirmTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _clearChatConfirmTimer.Tick += (s, e) =>
            {
                ExitClearChatConfirmMode();
            };
            _clearChatConfirmTimer.Start();
        }

        private void ExitClearChatConfirmMode()
        {
            _isClearChatConfirmMode = false;
            _clearChatConfirmTimer?.Stop();
            _clearChatConfirmTimer = null;
            BtnClearChat.Tag = null;
            ClearChatIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Broom16;
            ClearChatConfirmText.Visibility = Visibility.Collapsed;
        }

        private async void BtnAutoName_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            // Show loading state
            BtnAutoName.IsEnabled = false;
            AutoNameIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowSync12;
            AutoNameText.Text = "...";

            try
            {
                var success = await ViewModel.AutoNameChatSessionAsync();
                
                if (success && ViewModel.SelectedChatSession != null)
                {
                    // Update the textbox with the new name
                    ViewModel.RenameChatTitle = ViewModel.SelectedChatSession.ChatTitle;
                }
            }
            finally
            {
                // Restore button state
                BtnAutoName.IsEnabled = true;
                AutoNameIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Wand16;
                AutoNameText.Text = "Auto";
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
            if (e.Key == Key.Enter)
            {
                if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                {
                    // Shift+Enter: Insert new line manually
                    e.Handled = true;
                    var textBox = sender as System.Windows.Controls.TextBox;
                    if (textBox != null)
                    {
                        int caretIndex = textBox.CaretIndex;
                        textBox.Text = textBox.Text.Insert(caretIndex, Environment.NewLine);
                        textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                    }
                }
                else
                {
                    // Enter alone: Send message
                    e.Handled = true;
                    _ = SendMessageAsync();
                }
            }
        }

        private async Task SendMessageAsync()
        {
            if (ViewModel == null || ViewModel.SelectedChatSession == null)
                return;

            var userMessage = MessageInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(userMessage))
                return;

            // Check if this is the first message (for auto-naming)
            var isFirstMessage = ViewModel.SelectedChatSession.Messages.Count == 0;
            var chatSession = ViewModel.SelectedChatSession;

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

                // Auto-name chat if this was the first message and auto-naming is enabled
                if (isFirstMessage && ViewModel?.AIOrchestration?.Settings?.EnableAutoNaming == true)
                {
                    System.Diagnostics.Debug.WriteLine("ChatPanelView: Auto-naming is enabled, starting background task");
                    
                    // Capture references on UI thread before starting background task
                    var orchestrationService = ViewModel.AIOrchestration;
                    var viewModel = ViewModel;
                    var dispatcher = Dispatcher;
                    
                    // Run auto-naming in background
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("ChatPanelView: Starting auto-naming background task");
                            
                            // Wait a bit for the message to be fully processed
                            await Task.Delay(1000);
                            
                            System.Diagnostics.Debug.WriteLine("ChatPanelView: Calling GenerateChatTitleAsync directly");
                            
                            // Call the AI service directly to get the title
                            var firstUserMessage = chatSession.Messages.FirstOrDefault(m => m.Role == "user");
                            if (firstUserMessage != null && !string.IsNullOrWhiteSpace(firstUserMessage.Content))
                            {
                                var title = await orchestrationService.GenerateChatTitleAsync(firstUserMessage.Content);
                                
                                if (!string.IsNullOrWhiteSpace(title))
                                {
                                    System.Diagnostics.Debug.WriteLine($"ChatPanelView: Got title from AI: {title}");
                                    
                                    // Update UI on the dispatcher thread
                                    await dispatcher.InvokeAsync(() =>
                                    {
                                        System.Diagnostics.Debug.WriteLine($"ChatPanelView: Updating title on UI thread");
                                        chatSession.ChatTitle = title;
                                        viewModel.AiStatusMessage = "Chat auto-named";
                                        System.Diagnostics.Debug.WriteLine("ChatPanelView: Title updated successfully");
                                    });
                                    
                                    // Save chats (can be done on background thread)
                                    await viewModel.SaveChatsAsync();
                                    System.Diagnostics.Debug.WriteLine("ChatPanelView: Chats saved");
                                    
                                    // Clear status after a short delay
                                    await Task.Delay(2000);
                                    await dispatcher.InvokeAsync(() =>
                                    {
                                        if (viewModel.AiStatusMessage == "Chat auto-named")
                                        {
                                            viewModel.AiStatusMessage = string.Empty;
                                        }
                                    });
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("ChatPanelView: No title returned from AI");
                                }
                            }
                        }
                        catch (Exception autoNameEx)
                        {
                            // Log error but don't disrupt the chat
                            System.Diagnostics.Debug.WriteLine($"ChatPanelView: Auto-naming error: {autoNameEx.Message}");
                            System.Diagnostics.Debug.WriteLine($"ChatPanelView: Auto-naming stack trace: {autoNameEx.StackTrace}");
                        }
                    });
                }
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
