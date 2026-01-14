using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIA.Models;
using AIA.Models.AI;
using WpfClipboard = System.Windows.Clipboard;
using WpfButton = System.Windows.Controls.Button;

namespace AIA.Views
{
    public partial class ChatPanelView
    {
        private DispatcherTimer? _clearChatConfirmTimer;
        private bool _isClearChatConfirmMode;

        // Pending image attachments
        private readonly ObservableCollection<ChatImageAttachment> _pendingAttachments = new();

        public ChatPanelView()
        {
            InitializeComponent();
            
            // Bind pending attachments to the preview control
            PendingImagesControl.ItemsSource = _pendingAttachments;
            
            // Subscribe to collection changes to update UI
            _pendingAttachments.CollectionChanged += PendingAttachments_CollectionChanged;
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        /// <summary>
        /// Event raised when a message is sent and processed
        /// </summary>
        public event EventHandler<string>? MessageSent;

        #region Pending Attachments Management

        private void PendingAttachments_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateAttachmentUI();
        }

        private void UpdateAttachmentUI()
        {
            var hasAttachments = _pendingAttachments.Count > 0;
            
            // Show/hide the attachments preview area
            AttachmentsPreviewArea.Visibility = hasAttachments ? Visibility.Visible : Visibility.Collapsed;
            
            // Update badge on send button
            SendButtonBadge.Visibility = hasAttachments ? Visibility.Visible : Visibility.Collapsed;
            SendButtonBadgeText.Text = _pendingAttachments.Count.ToString();
            
            // Update count text
            AttachmentCountText.Text = _pendingAttachments.Count == 1 
                ? "1 image" 
                : $"{_pendingAttachments.Count} images";
        }

        private void BtnRemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is WpfButton button && button.Tag is Guid attachmentId)
            {
                var attachment = _pendingAttachments.FirstOrDefault(a => a.Id == attachmentId);
                if (attachment != null)
                {
                    _pendingAttachments.Remove(attachment);
                }
            }
        }

        private void BtnClearAllAttachments_Click(object sender, RoutedEventArgs e)
        {
            _pendingAttachments.Clear();
        }

        /// <summary>
        /// Adds an image to the pending attachments
        /// </summary>
        private void AddImageAttachment(BitmapSource image)
        {
            var thumbnailSize = ViewModel?.AppSettings?.ChatImageThumbnailSize ?? 80;
            
            var attachment = new ChatImageAttachment
            {
                FullImage = image,
                CapturedAt = DateTime.Now
            };
            
            // Create thumbnail
            attachment.CreateThumbnail(thumbnailSize);
            
            _pendingAttachments.Add(attachment);
        }

        #endregion

        #region Paste Detection

        private void MessageInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Check for Ctrl+V (paste)
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Check if clipboard contains an image
                if (WpfClipboard.ContainsImage())
                {
                    e.Handled = true;
                    HandleImagePaste();
                }
                // If clipboard contains text, let the default paste behavior happen
            }
        }

        private void HandleImagePaste()
        {
            try
            {
                var image = WpfClipboard.GetImage();
                if (image != null)
                {
                    // Freeze the image for thread safety
                    if (image.CanFreeze)
                        image.Freeze();
                    
                    AddImageAttachment(image);
                    
                    System.Diagnostics.Debug.WriteLine($"Image pasted: {image.PixelWidth}x{image.PixelHeight}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error pasting image: {ex.Message}");
            }
        }

        #endregion

        #region Chat Management Event Handlers

        private void BtnNewChat_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.CreateNewChatSession();
            // Clear any pending attachments when starting a new chat
            _pendingAttachments.Clear();
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
                _pendingAttachments.Clear();
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
                _pendingAttachments.Clear();
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
                _pendingAttachments.Clear();
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

        /// <summary>
        /// Sends a template message programmatically
        /// </summary>
        public void SendTemplateMessage(string message)
        {
            if (ViewModel == null || ViewModel.SelectedChatSession == null)
                return;

            if (string.IsNullOrWhiteSpace(message))
                return;

            // Set the message and send it
            ViewModel.MessageInput = message;
            _ = SendMessageAsync();
        }

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
            var hasImages = _pendingAttachments.Count > 0;
            
            // Allow sending if there's text OR images
            if (string.IsNullOrWhiteSpace(userMessage) && !hasImages)
                return;

            // Check if this is the first message (for auto-naming)
            var isFirstMessage = ViewModel.SelectedChatSession.Messages.Count == 0;
            var chatSession = ViewModel.SelectedChatSession;

            MessageInput.Clear();
            MessageInput.Focus();

            // Create user message with attached images
            var userChatMessage = new ChatMessage
            {
                Role = "user",
                Content = userMessage ?? ""
            };

            // Move pending attachments to the message
            foreach (var attachment in _pendingAttachments)
            {
                userChatMessage.AttachedImages.Add(attachment);
            }
            _pendingAttachments.Clear();

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

                // Build conversation history for AI (only simple text messages)
                // We don't include tool call details as they're not persisted
                var conversationHistory = new List<AIMessage>();
                foreach (var msg in ViewModel.SelectedChatSession.Messages)
                {
                    // Skip the current assistant placeholder and the current user message
                    // (current user message will be added by the orchestration service)
                    if (msg == assistantMessage || msg == userChatMessage)
                        continue;

                    // Only include simple text exchanges
                    var aiMessage = new AIMessage
                    {
                        Role = msg.Role,
                        Content = msg.Content
                    };

                    conversationHistory.Add(aiMessage);
                }

                // Prepare images if present
                List<AIImageContent>? aiImages = null;
                if (userChatMessage.HasAttachedImages)
                {
                    aiImages = new List<AIImageContent>();
                    foreach (var imgAttachment in userChatMessage.AttachedImages)
                    {
                        if (imgAttachment.FullImage != null)
                        {
                            var base64 = ConvertBitmapToBase64(imgAttachment.FullImage);
                            aiImages.Add(new AIImageContent
                            {
                                Base64Data = base64,
                                MimeType = imgAttachment.MimeType,
                                Description = $"Pasted image ({imgAttachment.ImageSizeText})"
                            });
                        }
                    }
                }

                // Use streaming with tool support
                var contentBuilder = new System.Text.StringBuilder();

                System.Diagnostics.Debug.WriteLine($"Sending message with tool use enabled: {ViewModel.AIOrchestration.Settings.EnableToolUse}");
                System.Diagnostics.Debug.WriteLine($"Message has {userChatMessage.AttachedImagesCount} attached images");
                System.Diagnostics.Debug.WriteLine($"Conversation history has {conversationHistory.Count} messages");

                // Build the effective user message text
                var effectiveUserMessage = string.IsNullOrWhiteSpace(userMessage)
                    ? "Please describe what you see in the attached image(s)."
                    : userMessage;

                await foreach (var (chunk, status) in ViewModel.AIOrchestration.GenerateStreamWithToolsAsync(
                    effectiveUserMessage,
                    conversationHistory,
                    null,  // specificProvider
                    aiImages))  // Pass images directly
                {
                    ProcessStreamChunk(chunk, status, contentBuilder, assistantMessage);
                }

                System.Diagnostics.Debug.WriteLine($"Final assistant content: '{assistantMessage.Content}'");

                // If no content was generated, show a helpful message
                if (string.IsNullOrWhiteSpace(assistantMessage.Content))
                {
                    assistantMessage.Content = "[Processing tool calls - waiting for response...]";
                }

                ViewModel.AiStatusMessage = "Response complete";

                MessageSent?.Invoke(this, userMessage ?? "");

                // Auto-name chat if this was the first message and auto-naming is enabled
                if (isFirstMessage && ViewModel?.AIOrchestration?.Settings?.EnableAutoNaming == true)
                {
                    await HandleAutoNamingAsync(chatSession);
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

        private void ProcessStreamChunk(string chunk, string status, System.Text.StringBuilder contentBuilder, ChatMessage assistantMessage)
        {
            System.Diagnostics.Debug.WriteLine($"Stream chunk - Status: '{status}', Content: '{chunk}'");
            
            // Update status message
            if (!string.IsNullOrEmpty(status) && status != "Streaming")
            {
                if (ViewModel != null)
                    ViewModel.AiStatusMessage = status;
            }
            
            // Append content chunk - but skip if it looks like raw JSON tool arguments
            if (!string.IsNullOrEmpty(chunk))
            {
                // Check if chunk looks like raw JSON (tool arguments being incorrectly returned as content)
                var trimmedChunk = chunk.Trim();
                bool isLikelyToolJson = (trimmedChunk.StartsWith("{") || trimmedChunk.StartsWith("[")) &&
                                       (contentBuilder.Length == 0 || contentBuilder.ToString().Trim().Length == 0);
                
                if (!isLikelyToolJson)
                {
                    contentBuilder.Append(chunk);
                    assistantMessage.Content = contentBuilder.ToString();

                    // Scroll to bottom as content updates
                    ChatScrollViewer.ScrollToEnd();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"?? Filtered out likely tool JSON: {trimmedChunk}");
                }
            }
        }

        private async Task HandleAutoNamingAsync(ChatSession chatSession)
        {
            if (ViewModel == null) return;
            
            System.Diagnostics.Debug.WriteLine("ChatPanelView: Auto-naming is enabled, starting background task");
            
            // Capture references on UI thread before starting background task
            var orchestrationService = ViewModel.AIOrchestration;
            var viewModel = ViewModel;
            var dispatcher = Dispatcher;
            
            // Run auto-naming in background
            await Task.Run(async () =>
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

        /// <summary>
        /// Converts a BitmapSource to a base64-encoded PNG string
        /// </summary>
        private static string ConvertBitmapToBase64(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var memoryStream = new MemoryStream();
            encoder.Save(memoryStream);
            return Convert.ToBase64String(memoryStream.ToArray());
        }

        #endregion
    }
}
