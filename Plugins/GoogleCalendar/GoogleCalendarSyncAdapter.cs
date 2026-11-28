using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.GoogleCalendar
{
    /// <summary>
    /// Sync adapter that maps AIA tasks onto Google Calendar events (event start acts as the due date).
    /// </summary>
    public class GoogleCalendarSyncAdapter : ISyncAdapter
    {
        private readonly GoogleCalendarService _service;

        public GoogleCalendarSyncAdapter(GoogleCalendarService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string SourceKey => "googlecalendar";

        public SyncCapabilities Capabilities =>
            SyncCapabilities.ReadSourceDueDate | SyncCapabilities.WriteSourceDueDate;

        public async Task<ExternalSyncState?> GetStateAsync(string sourceId, CancellationToken cancellationToken)
        {
            try
            {
                var (startUtc, htmlLink) = await _service.GetEventSyncStateAsync(sourceId, cancellationToken).ConfigureAwait(false);

                return new ExternalSyncState
                {
                    SourceId = sourceId,
                    SourceUrl = htmlLink,
                    IsCompleted = null,
                    DueUtc = startUtc
                };
            }
            catch (GoogleCalendarApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Google Calendar events do not have a completion state, so AIA cannot write completion to Google Calendar.");
        }

        public Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken)
        {
            return _service.SetEventStartAsync(sourceId, dueUtc, cancellationToken);
        }
    }
}
