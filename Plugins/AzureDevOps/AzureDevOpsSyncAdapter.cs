using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.AzureDevOps
{
    /// <summary>
    /// Sync adapter that maps AIA completion and due dates to Azure DevOps work items.
    /// </summary>
    public class AzureDevOpsSyncAdapter : ISyncAdapter
    {
        private readonly AzureDevOpsService _service;

        public AzureDevOpsSyncAdapter(AzureDevOpsService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string SourceKey => "azuredevops";

        public SyncCapabilities Capabilities =>
            SyncCapabilities.ReadSourceCompletion |
            SyncCapabilities.WriteSourceCompletion |
            SyncCapabilities.ReadSourceDueDate |
            SyncCapabilities.WriteSourceDueDate;

        public async Task<ExternalSyncState?> GetStateAsync(string sourceId, CancellationToken cancellationToken)
        {
            if (!TryParseWorkItemId(sourceId, out var workItemId))
                return null;

            var state = await _service.GetSyncStateAsync(workItemId, cancellationToken);
            if (state == null)
                return null;

            // The project is not known from the source id alone, so the caller keeps
            // the SourceUrl it already has from the refresh item.
            return new ExternalSyncState
            {
                SourceId = workItemId.ToString(CultureInfo.InvariantCulture),
                SourceUrl = state.Url,
                Title = string.Empty,
                IsCompleted = string.IsNullOrWhiteSpace(state.State) ? null : state.IsCompleted,
                DueUtc = SyncReconciler.Normalize(state.DueUtc)
            };
        }

        public async Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken)
        {
            await _service.SetCompletedAsync(ParseWorkItemId(sourceId), completed, cancellationToken);
        }

        public async Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken)
        {
            await _service.SetDueDateAsync(ParseWorkItemId(sourceId), dueUtc, cancellationToken);
        }

        private static bool TryParseWorkItemId(string? sourceId, out int workItemId)
        {
            return int.TryParse(sourceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out workItemId);
        }

        private static int ParseWorkItemId(string? sourceId)
        {
            if (!TryParseWorkItemId(sourceId, out var workItemId))
                throw new ArgumentException($"'{sourceId}' is not a valid Azure DevOps work item id.", nameof(sourceId));

            return workItemId;
        }
    }
}
