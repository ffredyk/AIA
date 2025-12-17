using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AIA.Plugins.Outlook
{
    /// <summary>
    /// Data transfer object for Outlook emails
    /// </summary>
    public class OutlookEmailData
    {
        public string EntryId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public string BodyPreview { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public string Importance { get; set; } = "Normal";
        public DateTime? FlagDueDate { get; set; }
    }

    /// <summary>
    /// Service for interacting with Microsoft Outlook via COM automation
    /// </summary>
    public class OutlookService
    {
        private static readonly object _lock = new();
        private bool _isOutlookAvailable = true;

        /// <summary>
        /// Retrieves all flagged emails from Outlook with timeout detection
        /// </summary>
        public async Task<(List<OutlookEmailData> emails, bool timedOut)> GetFlaggedEmailsWithTimeoutAsync()
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                var fetchTask = Task.Run(() => GetFlaggedEmailsSync(), cts.Token);
                var emails = await fetchTask;
                return (emails, false);
            }
            catch (OperationCanceledException)
            {
                return (new List<OutlookEmailData>(), true);
            }
            catch (Exception)
            {
                return (new List<OutlookEmailData>(), false);
            }
        }

        private List<OutlookEmailData> GetFlaggedEmailsSync()
        {
            var emails = new List<OutlookEmailData>();

            if (!_isOutlookAvailable)
                return emails;

            lock (_lock)
            {
                dynamic? outlookApp = null;
                dynamic? ns = null;
                dynamic? inbox = null;
                dynamic? items = null;
                dynamic? flaggedItems = null;

                try
                {
                    var outlookType = Type.GetTypeFromProgID("Outlook.Application");
                    if (outlookType == null)
                    {
                        _isOutlookAvailable = false;
                        return emails;
                    }

                    outlookApp = Activator.CreateInstance(outlookType);
                    if (outlookApp == null)
                    {
                        _isOutlookAvailable = false;
                        return emails;
                    }

                    ns = outlookApp.GetNamespace("MAPI");
                    inbox = ns.GetDefaultFolder(6); // olFolderInbox
                    items = inbox.Items;

                    string filter = "[FlagStatus] = 1"; // olFlagMarked
                    flaggedItems = items.Restrict(filter);
                    flaggedItems.Sort("[ReceivedTime]", true);

                    int emailCount = 0;
                    const int MaxEmails = 100;

                    foreach (dynamic item in flaggedItems)
                    {
                        if (emailCount >= MaxEmails)
                        {
                            if (item != null)
                                Marshal.ReleaseComObject(item);
                            break;
                        }

                        try
                        {
                            if (item.Class != 43) // olMail
                                continue;

                            var email = new OutlookEmailData
                            {
                                EntryId = item.EntryID,
                                Subject = item.Subject ?? "(No Subject)",
                                SenderName = GetSenderName(item),
                                SenderEmail = GetSenderEmail(item),
                                ReceivedDate = item.ReceivedTime,
                                BodyPreview = TruncateText(item.Body ?? "", 150),
                                Body = item.Body ?? "",
                                IsRead = !item.UnRead,
                                Importance = GetImportanceText(item.Importance)
                            };

                            try
                            {
                                if (item.TaskDueDate != null && item.TaskDueDate.Year > 1900)
                                {
                                    email.FlagDueDate = item.TaskDueDate;
                                }
                            }
                            catch { }

                            emails.Add(email);
                            emailCount++;
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
                    if (flaggedItems != null) Marshal.ReleaseComObject(flaggedItems);
                    if (items != null) Marshal.ReleaseComObject(items);
                    if (inbox != null) Marshal.ReleaseComObject(inbox);
                    if (ns != null) Marshal.ReleaseComObject(ns);
                    if (outlookApp != null) Marshal.ReleaseComObject(outlookApp);
                }
            }

            return emails;
        }

        /// <summary>
        /// Marks an email flag as complete
        /// </summary>
        public async Task<bool> MarkFlagCompleteAsync(string entryId)
        {
            return await Task.Run(() => MarkFlagCompleteSync(entryId));
        }

        private bool MarkFlagCompleteSync(string entryId)
        {
            if (!_isOutlookAvailable || string.IsNullOrEmpty(entryId))
                return false;

            lock (_lock)
            {
                dynamic? outlookApp = null;
                dynamic? ns = null;
                dynamic? item = null;

                try
                {
                    var outlookType = Type.GetTypeFromProgID("Outlook.Application");
                    if (outlookType == null) return false;
                    outlookApp = Activator.CreateInstance(outlookType);
                    if (outlookApp == null) return false;

                    ns = outlookApp.GetNamespace("MAPI");
                    item = ns.GetItemFromID(entryId);

                    if (item != null)
                    {
                        item.FlagStatus = 1; // olFlagComplete
                        item.Save();
                        return true;
                    }
                }
                catch { return false; }
                finally
                {
                    if (item != null) Marshal.ReleaseComObject(item);
                    if (ns != null) Marshal.ReleaseComObject(ns);
                    if (outlookApp != null) Marshal.ReleaseComObject(outlookApp);
                }
            }

            return false;
        }

        /// <summary>
        /// Removes flag from an email
        /// </summary>
        public async Task<bool> ClearFlagAsync(string entryId)
        {
            return await Task.Run(() => ClearFlagSync(entryId));
        }

        private bool ClearFlagSync(string entryId)
        {
            if (!_isOutlookAvailable || string.IsNullOrEmpty(entryId))
                return false;

            lock (_lock)
            {
                dynamic? outlookApp = null;
                dynamic? ns = null;
                dynamic? item = null;

                try
                {
                    var outlookType = Type.GetTypeFromProgID("Outlook.Application");
                    if (outlookType == null) return false;
                    outlookApp = Activator.CreateInstance(outlookType);
                    if (outlookApp == null) return false;

                    ns = outlookApp.GetNamespace("MAPI");
                    item = ns.GetItemFromID(entryId);

                    if (item != null)
                    {
                        item.FlagStatus = 0; // olNoFlag
                        item.Save();
                        return true;
                    }
                }
                catch { return false; }
                finally
                {
                    if (item != null) Marshal.ReleaseComObject(item);
                    if (ns != null) Marshal.ReleaseComObject(ns);
                    if (outlookApp != null) Marshal.ReleaseComObject(outlookApp);
                }
            }

            return false;
        }

        /// <summary>
        /// Opens an email in Outlook
        /// </summary>
        public async Task<bool> OpenEmailAsync(string entryId)
        {
            return await Task.Run(() => OpenEmailSync(entryId));
        }

        private bool OpenEmailSync(string entryId)
        {
            if (!_isOutlookAvailable || string.IsNullOrEmpty(entryId))
                return false;

            lock (_lock)
            {
                dynamic? outlookApp = null;
                dynamic? ns = null;
                dynamic? item = null;

                try
                {
                    var outlookType = Type.GetTypeFromProgID("Outlook.Application");
                    if (outlookType == null) return false;
                    outlookApp = Activator.CreateInstance(outlookType);
                    if (outlookApp == null) return false;

                    ns = outlookApp.GetNamespace("MAPI");
                    item = ns.GetItemFromID(entryId);

                    if (item != null)
                    {
                        item.Display();
                        return true;
                    }
                }
                catch { return false; }
                finally
                {
                    if (ns != null) Marshal.ReleaseComObject(ns);
                    if (outlookApp != null) Marshal.ReleaseComObject(outlookApp);
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if Outlook is available
        /// </summary>
        public bool IsOutlookAvailable()
        {
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

        /// <summary>
        /// Resets availability check
        /// </summary>
        public void ResetAvailabilityCheck()
        {
            _isOutlookAvailable = true;
        }

        private static string GetSenderName(dynamic item)
        {
            try { return item.SenderName ?? "Unknown"; }
            catch { return "Unknown"; }
        }

        private static string GetSenderEmail(dynamic item)
        {
            try
            {
                if (item.SenderEmailType == "EX")
                {
                    try
                    {
                        var sender = item.Sender;
                        if (sender != null)
                        {
                            var exchangeUser = sender.GetExchangeUser();
                            if (exchangeUser != null)
                                return exchangeUser.PrimarySmtpAddress ?? item.SenderEmailAddress ?? "";
                        }
                    }
                    catch { return item.SenderEmailAddress ?? ""; }
                }
                return item.SenderEmailAddress ?? "";
            }
            catch { return ""; }
        }

        private static string GetImportanceText(int importance)
        {
            return importance switch
            {
                0 => "Low",
                2 => "High",
                _ => "Normal"
            };
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength - 3) + "...";
        }
    }
}
