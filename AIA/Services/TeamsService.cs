using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using AIA.Models;

namespace AIA.Services
{
    public class TeamsService
    {
        private static readonly object _lock = new();
        private static bool _isOutlookAvailable = true;
        private static HttpClient? _httpClient;
        private static string? _accessToken;
        private static DateTime _tokenExpiry = DateTime.MinValue;

        // Graph API configuration - set these for real Teams integration
        public static string? ClientId { get; set; }
        public static string? TenantId { get; set; }
        public static string? ClientSecret { get; set; }

        public static bool IsGraphApiConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(TenantId);

        private static HttpClient GetHttpClient()
        {
            _httpClient ??= new HttpClient
            {
                BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")
            };
            return _httpClient;
        }

        #region Graph API Authentication

        /// <summary>
        /// Authenticates with Microsoft Graph API using client credentials
        /// For user-delegated permissions, use interactive authentication
        /// </summary>
        public static async Task<bool> AuthenticateAsync()
        {
            if (!IsGraphApiConfigured)
                return false;

            try
            {
                using var client = new HttpClient();
                var tokenEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";

                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId!,
                    ["client_secret"] = ClientSecret ?? "",
                    ["scope"] = "https://graph.microsoft.com/.default",
                    ["grant_type"] = "client_credentials"
                });

                var response = await client.PostAsync(tokenEndpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    _accessToken = doc.RootElement.GetProperty("access_token").GetString();
                    var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
                    _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // Refresh 1 minute early
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Graph API auth failed: {ex.Message}");
            }

            return false;
        }

        private static async Task EnsureAuthenticatedAsync()
        {
            if (string.IsNullOrEmpty(_accessToken) || DateTime.UtcNow >= _tokenExpiry)
            {
                await AuthenticateAsync();
            }
        }

        #endregion

        #region Teams Meetings

        /// <summary>
        /// Retrieves today's meetings - uses Graph API if configured, otherwise falls back to Outlook
        /// </summary>
        public static async Task<(List<TeamsMeeting> meetings, bool timedOut)> GetTodaysMeetingsWithTimeoutAsync()
        {
            // Try Graph API first if configured
            if (IsGraphApiConfigured && !string.IsNullOrEmpty(_accessToken))
            {
                try
                {
                    var meetings = await GetMeetingsFromGraphAsync();
                    if (meetings.Count > 0)
                        return (meetings, false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Graph API meetings failed: {ex.Message}");
                }
            }

            // Fallback to Outlook calendar
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                var fetchTask = Task.Run(() => GetTodaysMeetingsSync(), cts.Token);
                var meetings = await fetchTask;
                return (meetings, false);
            }
            catch (OperationCanceledException)
            {
                return (new List<TeamsMeeting>(), true);
            }
            catch (Exception)
            {
                return (new List<TeamsMeeting>(), false);
            }
        }

        private static async Task<List<TeamsMeeting>> GetMeetingsFromGraphAsync()
        {
            var meetings = new List<TeamsMeeting>();
            await EnsureAuthenticatedAsync();

            if (string.IsNullOrEmpty(_accessToken))
                return meetings;

            try
            {
                var client = GetHttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);
                var startDateTime = today.ToString("yyyy-MM-ddTHH:mm:ss");
                var endDateTime = tomorrow.ToString("yyyy-MM-ddTHH:mm:ss");

                var response = await client.GetAsync(
                    $"me/calendar/calendarView?startDateTime={startDateTime}&endDateTime={endDateTime}&$orderby=start/dateTime");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
                    {
                        var meeting = new TeamsMeeting
                        {
                            Id = item.GetProperty("id").GetString() ?? Guid.NewGuid().ToString(),
                            Subject = item.GetProperty("subject").GetString() ?? "(No Subject)",
                            StartTime = DateTime.Parse(item.GetProperty("start").GetProperty("dateTime").GetString() ?? DateTime.Now.ToString()),
                            EndTime = DateTime.Parse(item.GetProperty("end").GetProperty("dateTime").GetString() ?? DateTime.Now.ToString()),
                            IsAllDay = item.GetProperty("isAllDay").GetBoolean()
                        };

                        if (item.TryGetProperty("organizer", out var organizer))
                        {
                            if (organizer.TryGetProperty("emailAddress", out var email))
                            {
                                meeting.Organizer = email.GetProperty("name").GetString() ?? "Unknown";
                                meeting.OrganizerEmail = email.GetProperty("address").GetString() ?? "";
                            }
                        }

                        if (item.TryGetProperty("location", out var location))
                        {
                            meeting.Location = location.GetProperty("displayName").GetString() ?? "";
                        }

                        if (item.TryGetProperty("isOnlineMeeting", out var isOnline))
                        {
                            meeting.IsOnlineMeeting = isOnline.GetBoolean();
                        }

                        if (item.TryGetProperty("onlineMeeting", out var onlineMeeting) && onlineMeeting.ValueKind != JsonValueKind.Null)
                        {
                            if (onlineMeeting.TryGetProperty("joinUrl", out var joinUrl))
                            {
                                meeting.JoinUrl = joinUrl.GetString() ?? "";
                            }
                        }

                        // Determine status
                        if (DateTime.Now >= meeting.StartTime && DateTime.Now <= meeting.EndTime)
                            meeting.Status = MeetingStatus.InProgress;
                        else if (DateTime.Now > meeting.EndTime)
                            meeting.Status = MeetingStatus.Completed;

                        meetings.Add(meeting);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching meetings from Graph: {ex.Message}");
            }

            return meetings;
        }

        private static List<TeamsMeeting> GetTodaysMeetingsSync()
        {
            var meetings = new List<TeamsMeeting>();

            if (!_isOutlookAvailable)
                return meetings;

            lock (_lock)
            {
                dynamic? outlookApp = null;
                dynamic? ns = null;
                dynamic? calendar = null;
                dynamic? items = null;
                dynamic? filteredItems = null;

                try
                {
                    var outlookType = Type.GetTypeFromProgID("Outlook.Application");
                    if (outlookType == null)
                    {
                        _isOutlookAvailable = false;
                        return meetings;
                    }

                    outlookApp = Activator.CreateInstance(outlookType);
                    if (outlookApp == null)
                    {
                        _isOutlookAvailable = false;
                        return meetings;
                    }

                    ns = outlookApp.GetNamespace("MAPI");
                    calendar = ns.GetDefaultFolder(9);
                    items = calendar.Items;
                    items.IncludeRecurrences = true;
                    items.Sort("[Start]");

                    var today = DateTime.Today;
                    var tomorrow = today.AddDays(1);
                    string filter = $"[Start] >= '{today:g}' AND [Start] < '{tomorrow:g}'";
                    filteredItems = items.Restrict(filter);

                    foreach (dynamic item in filteredItems)
                    {
                        try
                        {
                            if (item.Class != 26)
                                continue;

                            var meeting = new TeamsMeeting
                            {
                                Id = item.EntryID ?? Guid.NewGuid().ToString(),
                                Subject = item.Subject ?? "(No Subject)",
                                Organizer = GetOrganizerName(item),
                                OrganizerEmail = GetOrganizerEmail(item),
                                StartTime = item.Start,
                                EndTime = item.End,
                                Location = item.Location ?? "",
                                IsAllDay = item.AllDayEvent,
                                BodyPreview = TruncateText(item.Body ?? "", 200)
                            };

                            try
                            {
                                string body = item.Body ?? "";
                                if (body.Contains("teams.microsoft.com") || 
                                    body.Contains("Join Microsoft Teams Meeting") ||
                                    (item.Location?.Contains("Microsoft Teams") ?? false))
                                {
                                    meeting.IsOnlineMeeting = true;
                                    meeting.JoinUrl = ExtractTeamsUrl(body);
                                }
                            }
                            catch { }

                            if (DateTime.Now >= meeting.StartTime && DateTime.Now <= meeting.EndTime)
                                meeting.Status = MeetingStatus.InProgress;
                            else if (DateTime.Now > meeting.EndTime)
                                meeting.Status = MeetingStatus.Completed;

                            meetings.Add(meeting);
                        }
                        catch { }
                        finally
                        {
                            if (item != null)
                                Marshal.ReleaseComObject(item);
                        }
                    }
                }
                catch (Exception)
                {
                    _isOutlookAvailable = false;
                }
                finally
                {
                    if (filteredItems != null) Marshal.ReleaseComObject(filteredItems);
                    if (items != null) Marshal.ReleaseComObject(items);
                    if (calendar != null) Marshal.ReleaseComObject(calendar);
                    if (ns != null) Marshal.ReleaseComObject(ns);
                    if (outlookApp != null) Marshal.ReleaseComObject(outlookApp);
                }
            }

            meetings.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            return meetings;
        }

        #endregion

        #region Teams Messages

        /// <summary>
        /// Gets unread Teams messages - uses Graph API if configured, otherwise returns sample data
        /// </summary>
        public static async Task<List<TeamsMessage>> GetUnreadMessagesAsync()
        {
            if (IsGraphApiConfigured && !string.IsNullOrEmpty(_accessToken))
            {
                try
                {
                    return await GetMessagesFromGraphAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Graph API messages failed: {ex.Message}");
                }
            }

            return GetSampleUnreadMessages();
        }

        private static async Task<List<TeamsMessage>> GetMessagesFromGraphAsync()
        {
            var messages = new List<TeamsMessage>();
            await EnsureAuthenticatedAsync();

            if (string.IsNullOrEmpty(_accessToken))
                return messages;

            try
            {
                var client = GetHttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                // Get chats
                var chatsResponse = await client.GetAsync("me/chats?$expand=lastMessagePreview");
                if (chatsResponse.IsSuccessStatusCode)
                {
                    var json = await chatsResponse.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    foreach (var chat in doc.RootElement.GetProperty("value").EnumerateArray())
                    {
                        if (chat.TryGetProperty("lastMessagePreview", out var lastMessage) && lastMessage.ValueKind != JsonValueKind.Null)
                        {
                            var message = new TeamsMessage
                            {
                                Id = lastMessage.TryGetProperty("id", out var id) ? id.GetString() ?? "" : Guid.NewGuid().ToString(),
                                ChatId = chat.GetProperty("id").GetString() ?? "",
                                ChatName = chat.TryGetProperty("topic", out var topic) ? topic.GetString() ?? "Chat" : "Chat",
                                MessageType = TeamsMessageType.Chat
                            };

                            if (lastMessage.TryGetProperty("from", out var from) && from.ValueKind != JsonValueKind.Null)
                            {
                                if (from.TryGetProperty("user", out var user))
                                {
                                    message.SenderName = user.TryGetProperty("displayName", out var name) ? name.GetString() ?? "Unknown" : "Unknown";
                                }
                            }

                            if (lastMessage.TryGetProperty("body", out var body))
                            {
                                var content = body.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                                message.Content = StripHtml(content);
                                message.ContentPreview = TruncateText(message.Content, 100);
                            }

                            if (lastMessage.TryGetProperty("createdDateTime", out var created))
                            {
                                message.ReceivedDate = DateTime.Parse(created.GetString() ?? DateTime.Now.ToString());
                            }

                            messages.Add(message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching messages from Graph: {ex.Message}");
            }

            return messages;
        }

        /// <summary>
        /// Gets sample unread messages (placeholder for Graph API)
        /// </summary>
        public static List<TeamsMessage> GetSampleUnreadMessages()
        {
            return new List<TeamsMessage>
            {
                new TeamsMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    SenderName = "John Smith",
                    SenderEmail = "john.smith@company.com",
                    ChatName = "Project Discussion",
                    Content = "Can you review the latest design mockups? I've uploaded them to the shared folder.",
                    ContentPreview = "Can you review the latest design mockups?",
                    ReceivedDate = DateTime.Now.AddMinutes(-15),
                    IsRead = false,
                    MessageType = TeamsMessageType.Chat,
                    IsMention = true
                },
                new TeamsMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    SenderName = "Sarah Johnson",
                    SenderEmail = "sarah.j@company.com",
                    TeamName = "Development Team",
                    ChannelName = "General",
                    Content = "The deployment was successful. All services are running smoothly.",
                    ContentPreview = "The deployment was successful.",
                    ReceivedDate = DateTime.Now.AddHours(-2),
                    IsRead = false,
                    MessageType = TeamsMessageType.Channel
                },
                new TeamsMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    SenderName = "Mike Chen",
                    SenderEmail = "mike.chen@company.com",
                    ChatName = "Mike Chen",
                    Content = "Hey, are you available for a quick call? I have some questions about the API.",
                    ContentPreview = "Hey, are you available for a quick call?",
                    ReceivedDate = DateTime.Now.AddMinutes(-45),
                    IsRead = false,
                    MessageType = TeamsMessageType.Chat
                }
            };
        }

        #endregion

        #region Teams Reminders / Tasks

        /// <summary>
        /// Gets Teams-related tasks/reminders from Microsoft To Do
        /// </summary>
        public static async Task<List<TeamsReminder>> GetTeamsRemindersAsync()
        {
            if (IsGraphApiConfigured && !string.IsNullOrEmpty(_accessToken))
            {
                try
                {
                    return await GetTasksFromGraphAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Graph API tasks failed: {ex.Message}");
                }
            }

            return GetSampleReminders();
        }

        private static async Task<List<TeamsReminder>> GetTasksFromGraphAsync()
        {
            var reminders = new List<TeamsReminder>();
            await EnsureAuthenticatedAsync();

            if (string.IsNullOrEmpty(_accessToken))
                return reminders;

            try
            {
                var client = GetHttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                // Get task lists first
                var listsResponse = await client.GetAsync("me/todo/lists");
                if (listsResponse.IsSuccessStatusCode)
                {
                    var listsJson = await listsResponse.Content.ReadAsStringAsync();
                    using var listsDoc = JsonDocument.Parse(listsJson);

                    foreach (var list in listsDoc.RootElement.GetProperty("value").EnumerateArray())
                    {
                        var listId = list.GetProperty("id").GetString();
                        var listName = list.GetProperty("displayName").GetString() ?? "Tasks";

                        // Get tasks from this list
                        var tasksResponse = await client.GetAsync($"me/todo/lists/{listId}/tasks?$filter=status ne 'completed'");
                        if (tasksResponse.IsSuccessStatusCode)
                        {
                            var tasksJson = await tasksResponse.Content.ReadAsStringAsync();
                            using var tasksDoc = JsonDocument.Parse(tasksJson);

                            foreach (var task in tasksDoc.RootElement.GetProperty("value").EnumerateArray())
                            {
                                var reminder = new TeamsReminder
                                {
                                    Id = task.GetProperty("id").GetString() ?? Guid.NewGuid().ToString(),
                                    Title = task.GetProperty("title").GetString() ?? "Untitled Task",
                                    Source = listName,
                                    IsCompleted = task.GetProperty("status").GetString() == "completed"
                                };

                                if (task.TryGetProperty("body", out var body))
                                {
                                    reminder.Description = body.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "";
                                }

                                if (task.TryGetProperty("dueDateTime", out var due) && due.ValueKind != JsonValueKind.Null)
                                {
                                    if (due.TryGetProperty("dateTime", out var dt))
                                    {
                                        reminder.DueDate = DateTime.Parse(dt.GetString() ?? DateTime.Now.AddDays(1).ToString());
                                    }
                                }
                                else
                                {
                                    reminder.DueDate = DateTime.Now.AddDays(1);
                                }

                                reminders.Add(reminder);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching tasks from Graph: {ex.Message}");
            }

            return reminders;
        }

        /// <summary>
        /// Gets sample reminders (placeholder for Graph API)
        /// </summary>
        public static List<TeamsReminder> GetSampleReminders()
        {
            return new List<TeamsReminder>
            {
                new TeamsReminder
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Follow up on project proposal",
                    Description = "Send feedback on the Q4 project proposal",
                    DueDate = DateTime.Now.AddHours(2),
                    Source = "Teams Chat"
                },
                new TeamsReminder
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Review pull request #234",
                    Description = "Code review requested by Sarah",
                    DueDate = DateTime.Now.AddHours(-1),
                    Source = "Development Team"
                }
            };
        }

        #endregion

        #region Utility Methods

        public static bool JoinMeeting(string joinUrl)
        {
            if (string.IsNullOrEmpty(joinUrl))
                return false;

            try
            {
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = joinUrl,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processStartInfo);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool OpenTeamsApp()
        {
            try
            {
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "msteams:",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processStartInfo);
                return true;
            }
            catch
            {
                try
                {
                    var webStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://teams.microsoft.com",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(webStartInfo);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Opens a specific Teams chat by chat ID
        /// </summary>
        public static bool OpenTeamsChat(string chatId)
        {
            if (string.IsNullOrEmpty(chatId))
                return OpenTeamsApp();

            try
            {
                var url = $"msteams:/l/chat/0/0?users=&topicName=&message=&chatId={chatId}";
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processStartInfo);
                return true;
            }
            catch
            {
                return OpenTeamsApp();
            }
        }

        public static bool IsTeamsAvailable()
        {
            // If Graph API is configured, Teams is always available
            if (IsGraphApiConfigured)
                return true;

            if (!_isOutlookAvailable)
                return false;

            try
            {
                var outlookType = Type.GetTypeFromProgID("Outlook.Application");
                return outlookType != null;
            }
            catch
            {
                _isOutlookAvailable = false;
                return false;
            }
        }

        public static void ResetAvailabilityCheck()
        {
            _isOutlookAvailable = true;
        }

        private static string GetOrganizerName(dynamic item)
        {
            try
            {
                return item.Organizer ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string GetOrganizerEmail(dynamic item)
        {
            try
            {
                var organizer = item.GetOrganizer();
                if (organizer != null)
                {
                    try
                    {
                        var exchangeUser = organizer.GetExchangeUser();
                        if (exchangeUser != null)
                        {
                            return exchangeUser.PrimarySmtpAddress ?? "";
                        }
                    }
                    catch { }
                    return organizer.Address ?? "";
                }
            }
            catch { }
            return "";
        }

        private static string ExtractTeamsUrl(string body)
        {
            if (string.IsNullOrEmpty(body))
                return "";

            try
            {
                var patterns = new[]
                {
                    @"https://teams\.microsoft\.com/l/meetup-join/[^\s<>""]+",
                    @"https://teams\.live\.com/meet/[^\s<>""]+",
                };

                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(body, pattern);
                    if (match.Success)
                    {
                        return match.Value;
                    }
                }
            }
            catch { }

            return "";
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            if (text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength - 3) + "...";
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return "";

            return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ").Trim();
        }

        #endregion
    }
}
