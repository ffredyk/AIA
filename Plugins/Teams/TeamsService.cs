using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIA.Plugins.Teams
{
    #region Data Transfer Objects

    public class TeamsMeetingData
    {
        public string Id { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Organizer { get; set; } = string.Empty;
        public string OrganizerEmail { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Location { get; set; } = string.Empty;
        public bool IsAllDay { get; set; }
        public bool IsOnlineMeeting { get; set; }
        public string JoinUrl { get; set; } = string.Empty;
        public string BodyPreview { get; set; } = string.Empty;
        public MeetingStatus Status { get; set; }
    }

    public class TeamsMessageData
    {
        public string Id { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
        public string ChatName { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string ContentPreview { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public bool IsRead { get; set; }
        public bool IsMention { get; set; }
        public TeamsMessageType MessageType { get; set; }
    }

    public class TeamsReminderData
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public string Source { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
    }

    public enum MeetingStatus
    {
        Scheduled,
        InProgress,
        Completed
    }

    public enum TeamsMessageType
    {
        Chat,
        Channel
    }

    #endregion

    /// <summary>
    /// Service for Teams integration via COM (Outlook) and Graph API
    /// </summary>
    public class TeamsService
    {
        private static readonly object _lock = new();
        private bool _isOutlookAvailable = true;
        private HttpClient? _httpClient;
        private string? _accessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        // Graph API configuration
        public string? ClientId { get; set; }
        public string? TenantId { get; set; }
        public string? ClientSecret { get; set; }

        public bool IsGraphApiConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(TenantId);

        private HttpClient GetHttpClient()
        {
            _httpClient ??= new HttpClient { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") };
            return _httpClient;
        }

        #region Availability

        public bool IsTeamsAvailable()
        {
            if (IsGraphApiConfigured) return true;
            if (!_isOutlookAvailable) return false;

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

        public void ResetAvailabilityCheck()
        {
            _isOutlookAvailable = true;
        }

        #endregion

        #region Meetings

        public async Task<(List<TeamsMeetingData> meetings, bool timedOut)> GetTodaysMeetingsWithTimeoutAsync()
        {
            if (IsGraphApiConfigured && !string.IsNullOrEmpty(_accessToken))
            {
                try
                {
                    var meetings = await GetMeetingsFromGraphAsync();
                    if (meetings.Count > 0) return (meetings, false);
                }
                catch { }
            }

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var fetchTask = Task.Run(() => GetTodaysMeetingsSync(), cts.Token);
                var meetings = await fetchTask;
                return (meetings, false);
            }
            catch (OperationCanceledException) { return (new List<TeamsMeetingData>(), true); }
            catch { return (new List<TeamsMeetingData>(), false); }
        }

        private async Task<List<TeamsMeetingData>> GetMeetingsFromGraphAsync()
        {
            var meetings = new List<TeamsMeetingData>();
            await EnsureAuthenticatedAsync();
            if (string.IsNullOrEmpty(_accessToken)) return meetings;

            try
            {
                var client = GetHttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);
                var response = await client.GetAsync(
                    $"me/calendar/calendarView?startDateTime={today:yyyy-MM-ddTHH:mm:ss}&endDateTime={tomorrow:yyyy-MM-ddTHH:mm:ss}&$orderby=start/dateTime");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
                    {
                        var meeting = new TeamsMeetingData
                        {
                            Id = item.GetProperty("id").GetString() ?? Guid.NewGuid().ToString(),
                            Subject = item.GetProperty("subject").GetString() ?? "(No Subject)",
                            StartTime = DateTime.Parse(item.GetProperty("start").GetProperty("dateTime").GetString() ?? DateTime.Now.ToString()),
                            EndTime = DateTime.Parse(item.GetProperty("end").GetProperty("dateTime").GetString() ?? DateTime.Now.ToString()),
                            IsAllDay = item.GetProperty("isAllDay").GetBoolean()
                        };

                        if (item.TryGetProperty("organizer", out var organizer) && organizer.TryGetProperty("emailAddress", out var email))
                        {
                            meeting.Organizer = email.GetProperty("name").GetString() ?? "Unknown";
                            meeting.OrganizerEmail = email.GetProperty("address").GetString() ?? "";
                        }

                        if (item.TryGetProperty("location", out var location))
                            meeting.Location = location.GetProperty("displayName").GetString() ?? "";

                        if (item.TryGetProperty("isOnlineMeeting", out var isOnline))
                            meeting.IsOnlineMeeting = isOnline.GetBoolean();

                        if (item.TryGetProperty("onlineMeeting", out var onlineMeeting) && onlineMeeting.ValueKind != JsonValueKind.Null)
                            if (onlineMeeting.TryGetProperty("joinUrl", out var joinUrl))
                                meeting.JoinUrl = joinUrl.GetString() ?? "";

                        meeting.Status = DateTime.Now >= meeting.StartTime && DateTime.Now <= meeting.EndTime
                            ? MeetingStatus.InProgress
                            : DateTime.Now > meeting.EndTime ? MeetingStatus.Completed : MeetingStatus.Scheduled;

                        meetings.Add(meeting);
                    }
                }
            }
            catch { }

            return meetings;
        }

        private List<TeamsMeetingData> GetTodaysMeetingsSync()
        {
            var meetings = new List<TeamsMeetingData>();
            if (!_isOutlookAvailable) return meetings;

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
                    if (outlookType == null) { _isOutlookAvailable = false; return meetings; }

                    outlookApp = Activator.CreateInstance(outlookType);
                    if (outlookApp == null) { _isOutlookAvailable = false; return meetings; }

                    ns = outlookApp.GetNamespace("MAPI");
                    calendar = ns.GetDefaultFolder(9); // olFolderCalendar
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
                            if (item.Class != 26) continue; // olAppointment

                            var meeting = new TeamsMeetingData
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

                            meeting.Status = DateTime.Now >= meeting.StartTime && DateTime.Now <= meeting.EndTime
                                ? MeetingStatus.InProgress
                                : DateTime.Now > meeting.EndTime ? MeetingStatus.Completed : MeetingStatus.Scheduled;

                            meetings.Add(meeting);
                        }
                        catch { }
                        finally { if (item != null) Marshal.ReleaseComObject(item); }
                    }
                }
                catch { _isOutlookAvailable = false; }
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

        #region Messages

        public async Task<List<TeamsMessageData>> GetUnreadMessagesAsync()
        {
            if (IsGraphApiConfigured && !string.IsNullOrEmpty(_accessToken))
            {
                try { return await GetMessagesFromGraphAsync(); }
                catch { }
            }
            return GetSampleUnreadMessages();
        }

        private async Task<List<TeamsMessageData>> GetMessagesFromGraphAsync()
        {
            var messages = new List<TeamsMessageData>();
            await EnsureAuthenticatedAsync();
            if (string.IsNullOrEmpty(_accessToken)) return messages;

            try
            {
                var client = GetHttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                var response = await client.GetAsync("me/chats?$expand=lastMessagePreview");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    foreach (var chat in doc.RootElement.GetProperty("value").EnumerateArray())
                    {
                        if (chat.TryGetProperty("lastMessagePreview", out var lastMessage) && lastMessage.ValueKind != JsonValueKind.Null)
                        {
                            var message = new TeamsMessageData
                            {
                                Id = lastMessage.TryGetProperty("id", out var id) ? id.GetString() ?? "" : Guid.NewGuid().ToString(),
                                ChatId = chat.GetProperty("id").GetString() ?? "",
                                ChatName = chat.TryGetProperty("topic", out var topic) ? topic.GetString() ?? "Chat" : "Chat",
                                MessageType = TeamsMessageType.Chat
                            };

                            if (lastMessage.TryGetProperty("from", out var from) && from.ValueKind != JsonValueKind.Null)
                                if (from.TryGetProperty("user", out var user))
                                    message.SenderName = user.TryGetProperty("displayName", out var name) ? name.GetString() ?? "Unknown" : "Unknown";

                            if (lastMessage.TryGetProperty("body", out var body))
                            {
                                var content = body.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                                message.Content = StripHtml(content);
                                message.ContentPreview = TruncateText(message.Content, 100);
                            }

                            if (lastMessage.TryGetProperty("createdDateTime", out var created))
                                message.ReceivedDate = DateTime.Parse(created.GetString() ?? DateTime.Now.ToString());

                            messages.Add(message);
                        }
                    }
                }
            }
            catch { }

            return messages;
        }

        private static List<TeamsMessageData> GetSampleUnreadMessages()
        {
            return new List<TeamsMessageData>
            {
                new TeamsMessageData
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
                new TeamsMessageData
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
                }
            };
        }

        #endregion

        #region Reminders

        public async Task<List<TeamsReminderData>> GetTeamsRemindersAsync()
        {
            if (IsGraphApiConfigured && !string.IsNullOrEmpty(_accessToken))
            {
                try { return await GetTasksFromGraphAsync(); }
                catch { }
            }
            return GetSampleReminders();
        }

        private async Task<List<TeamsReminderData>> GetTasksFromGraphAsync()
        {
            var reminders = new List<TeamsReminderData>();
            await EnsureAuthenticatedAsync();
            if (string.IsNullOrEmpty(_accessToken)) return reminders;

            try
            {
                var client = GetHttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                var listsResponse = await client.GetAsync("me/todo/lists");
                if (listsResponse.IsSuccessStatusCode)
                {
                    var listsJson = await listsResponse.Content.ReadAsStringAsync();
                    using var listsDoc = JsonDocument.Parse(listsJson);

                    foreach (var list in listsDoc.RootElement.GetProperty("value").EnumerateArray())
                    {
                        var listId = list.GetProperty("id").GetString();
                        var listName = list.GetProperty("displayName").GetString() ?? "Tasks";

                        var tasksResponse = await client.GetAsync($"me/todo/lists/{listId}/tasks?$filter=status ne 'completed'");
                        if (tasksResponse.IsSuccessStatusCode)
                        {
                            var tasksJson = await tasksResponse.Content.ReadAsStringAsync();
                            using var tasksDoc = JsonDocument.Parse(tasksJson);

                            foreach (var task in tasksDoc.RootElement.GetProperty("value").EnumerateArray())
                            {
                                var reminder = new TeamsReminderData
                                {
                                    Id = task.GetProperty("id").GetString() ?? Guid.NewGuid().ToString(),
                                    Title = task.GetProperty("title").GetString() ?? "Untitled Task",
                                    Source = listName,
                                    IsCompleted = task.GetProperty("status").GetString() == "completed"
                                };

                                if (task.TryGetProperty("body", out var body))
                                    reminder.Description = body.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "";

                                if (task.TryGetProperty("dueDateTime", out var due) && due.ValueKind != JsonValueKind.Null)
                                    if (due.TryGetProperty("dateTime", out var dt))
                                        reminder.DueDate = DateTime.Parse(dt.GetString() ?? DateTime.Now.AddDays(1).ToString());
                                else
                                    reminder.DueDate = DateTime.Now.AddDays(1);

                                reminders.Add(reminder);
                            }
                        }
                    }
                }
            }
            catch { }

            return reminders;
        }

        private static List<TeamsReminderData> GetSampleReminders()
        {
            return new List<TeamsReminderData>
            {
                new TeamsReminderData
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Follow up on project proposal",
                    Description = "Send feedback on the Q4 project proposal",
                    DueDate = DateTime.Now.AddHours(2),
                    Source = "Teams Chat"
                },
                new TeamsReminderData
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

        #region Actions

        public bool JoinMeeting(string joinUrl)
        {
            if (string.IsNullOrEmpty(joinUrl)) return false;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = joinUrl,
                    UseShellExecute = true
                });
                return true;
            }
            catch { return false; }
        }

        public bool OpenTeamsApp()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "msteams:",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://teams.microsoft.com",
                        UseShellExecute = true
                    });
                    return true;
                }
                catch { return false; }
            }
        }

        public bool OpenTeamsChat(string chatId)
        {
            if (string.IsNullOrEmpty(chatId)) return OpenTeamsApp();
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"msteams:/l/chat/0/0?users=&topicName=&message=&chatId={chatId}",
                    UseShellExecute = true
                });
                return true;
            }
            catch { return OpenTeamsApp(); }
        }

        #endregion

        #region Authentication

        private async Task EnsureAuthenticatedAsync()
        {
            if (string.IsNullOrEmpty(_accessToken) || DateTime.UtcNow >= _tokenExpiry)
                await AuthenticateAsync();
        }

        private async Task<bool> AuthenticateAsync()
        {
            if (!IsGraphApiConfigured) return false;

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
                    _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
                    return true;
                }
            }
            catch { }

            return false;
        }

        #endregion

        #region Helpers

        private static string GetOrganizerName(dynamic item)
        {
            try { return item.Organizer ?? "Unknown"; }
            catch { return "Unknown"; }
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
                        if (exchangeUser != null) return exchangeUser.PrimarySmtpAddress ?? "";
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
            if (string.IsNullOrEmpty(body)) return "";
            try
            {
                var patterns = new[]
                {
                    @"https://teams\.microsoft\.com/l/meetup-join/[^\s<>""]+",
                    @"https://teams\.live\.com/meet/[^\s<>""]+"
                };

                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(body, pattern);
                    if (match.Success) return match.Value;
                }
            }
            catch { }
            return "";
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength - 3) + "...";
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ").Trim();
        }

        #endregion
    }
}
