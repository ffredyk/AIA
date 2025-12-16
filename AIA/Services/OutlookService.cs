using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AIA.Models;

namespace AIA.Services
{
    public class OutlookService
    {
        private static readonly object _lock = new();
        private static bool _isOutlookAvailable = true;

        /// <summary>
        /// Retrieves all flagged emails from Outlook with timeout detection
        /// </summary>
        public static async Task<(List<OutlookEmail> emails, bool timedOut)> GetFlaggedEmailsWithTimeoutAsync()
        {
            // Use a timeout to prevent hanging indefinitely
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
            
            try
            {
                var fetchTask = Task.Run(() => GetFlaggedEmailsSync(), cts.Token);
                var emails = await fetchTask;
                return (emails, false);
            }
            catch (OperationCanceledException)
            {
                return (new List<OutlookEmail>(), true);
            }
            catch (Exception)
            {
                return (new List<OutlookEmail>(), false);
            }
        }

        /// <summary>
        /// Retrieves all flagged emails from Outlook (legacy method)
        /// </summary>
        public static async Task<List<OutlookEmail>> GetFlaggedEmailsAsync()
        {
            var (emails, _) = await GetFlaggedEmailsWithTimeoutAsync();
            return emails;
        }

        private static List<OutlookEmail> GetFlaggedEmailsSync()
        {
            var emails = new List<OutlookEmail>();

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
                    // Create a new Outlook instance
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
                    
                    // Get the Inbox folder (olFolderInbox = 6)
                    inbox = ns.GetDefaultFolder(6);
                    items = inbox.Items;

                    // Use Restrict to filter flagged items first - much faster than iterating all
                    string filter = "[FlagStatus] = 1"; // olFlagMarked = 2
                    flaggedItems = items.Restrict(filter);
                    
                    // Sort by received time descending
                    flaggedItems.Sort("[ReceivedTime]", true);

                    int emailCount = 0;
                    const int MaxEmails = 100;

                    foreach (dynamic item in flaggedItems)
                    {
                        // Limit to newest 100 flagged emails
                        if (emailCount >= MaxEmails)
                        {
                            if (item != null)
                                Marshal.ReleaseComObject(item);
                            break;
                        }

                        try
                        {
                            // Check if item is a mail item (class 43 = olMail)
                            if (item.Class != 43)
                                continue;

                            var email = new OutlookEmail
                            {
                                EntryId = item.EntryID,
                                Subject = item.Subject ?? "(No Subject)",
                                SenderName = GetSenderName(item),
                                SenderEmail = GetSenderEmail(item),
                                ReceivedDate = item.ReceivedTime,
                                BodyPreview = TruncateText(item.Body ?? "", 150),
                                Body = item.Body ?? "",
                                FlagStatus = EmailFlagStatus.Flagged,
                                IsRead = !item.UnRead,
                                Importance = GetImportanceText(item.Importance)
                            };

                            // Try to get flag due date if available
                            try
                            {
                                if (item.TaskDueDate != null && item.TaskDueDate.Year > 1900)
                                {
                                    email.FlagDueDate = item.TaskDueDate;
                                }
                            }
                            catch
                            {
                                // Flag due date not available
                            }

                            emails.Add(email);
                            emailCount++;
                        }
                        catch
                        {
                            // Skip items that can't be read
                        }
                        finally
                        {
                            if (item != null)
                                Marshal.ReleaseComObject(item);
                        }
                    }
                }
                catch (Exception)
                {
                    // Outlook not available or error accessing it
                    _isOutlookAvailable = false;
                }
                finally
                {
                    // Release COM objects in reverse order
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
        /// Marks an email flag as complete in Outlook
        /// </summary>
        public static async Task<bool> MarkFlagCompleteAsync(string entryId)
        {
            return await Task.Run(() => MarkFlagCompleteSync(entryId));
        }

        private static bool MarkFlagCompleteSync(string entryId)
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
                        // olFlagComplete = 1
                        item.FlagStatus = 1;
                        item.Save();
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
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
        /// Removes flag from an email in Outlook
        /// </summary>
        public static async Task<bool> ClearFlagAsync(string entryId)
        {
            return await Task.Run(() => ClearFlagSync(entryId));
        }

        private static bool ClearFlagSync(string entryId)
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
                        // olNoFlag = 0
                        item.FlagStatus = 0;
                        item.Save();
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
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
        public static async Task<bool> OpenEmailAsync(string entryId)
        {
            return await Task.Run(() => OpenEmailSync(entryId));
        }

        private static bool OpenEmailSync(string entryId)
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
                catch
                {
                    return false;
                }
                finally
                {
                    // Don't release item - it's being displayed
                    if (ns != null) Marshal.ReleaseComObject(ns);
                    if (outlookApp != null) Marshal.ReleaseComObject(outlookApp);
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if Outlook is available on this system
        /// </summary>
        public static bool IsOutlookAvailable()
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
        /// Resets the Outlook availability check (useful for retry scenarios)
        /// </summary>
        public static void ResetAvailabilityCheck()
        {
            _isOutlookAvailable = true;
        }

        private static string GetSenderName(dynamic item)
        {
            try
            {
                return item.SenderName ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string GetSenderEmail(dynamic item)
        {
            try
            {
                // Try to get sender email address
                if (item.SenderEmailType == "EX")
                {
                    // Exchange sender - try to get SMTP address
                    try
                    {
                        var sender = item.Sender;
                        if (sender != null)
                        {
                            var exchangeUser = sender.GetExchangeUser();
                            if (exchangeUser != null)
                            {
                                return exchangeUser.PrimarySmtpAddress ?? item.SenderEmailAddress ?? "";
                            }
                        }
                    }
                    catch
                    {
                        return item.SenderEmailAddress ?? "";
                    }
                }
                return item.SenderEmailAddress ?? "";
            }
            catch
            {
                return "";
            }
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
            if (string.IsNullOrEmpty(text))
                return "";

            // Clean up whitespace
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            if (text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength - 3) + "...";
        }
    }
}
