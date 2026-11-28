using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.GitHub
{
    /// <summary>
    /// Synchronizes AIA item completion state with GitHub issues.
    /// GitHub issues have no due date, so only completion is read and written.
    /// </summary>
    public sealed class GitHubSyncAdapter : ISyncAdapter
    {
        private readonly GitHubService _service;

        public GitHubSyncAdapter(GitHubService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string SourceKey => "github";

        public SyncCapabilities Capabilities =>
            SyncCapabilities.ReadSourceCompletion | SyncCapabilities.WriteSourceCompletion;

        public async Task<ExternalSyncState?> GetStateAsync(string sourceId, CancellationToken cancellationToken)
        {
            var (repository, number) = ParseSourceId(sourceId);

            var state = new ExternalSyncState
            {
                SourceId = GetSourceId(repository, number),
                SourceUrl = GetSourceUrl(repository, number),
                Title = string.Empty,
                DueUtc = null
            };

            try
            {
                state.IsCompleted = await _service.GetIssueCompletedAsync(repository, number, cancellationToken);
            }
            catch (GitHubApiException ex) when (ex.StatusCode == 404)
            {
                return null;
            }

            return state;
        }

        public Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken)
        {
            var (repository, number) = ParseSourceId(sourceId);
            return _service.SetIssueCompletedAsync(repository, number, completed, cancellationToken);
        }

        public Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("GitHub issues do not support due dates.");
        }

        /// <summary>
        /// Builds the sync source identifier for a GitHub issue, for example "owner/repo#42".
        /// </summary>
        public static string GetSourceId(string repository, int number)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);
            return $"{repository.Trim()}#{number}";
        }

        /// <summary>
        /// Builds the web URL of a GitHub issue.
        /// </summary>
        public static string GetSourceUrl(string repository, int number)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);
            return $"https://github.com/{repository.Trim()}/issues/{number}";
        }

        private static (string Repository, int Number) ParseSourceId(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("GitHub sync source id is empty.", nameof(sourceId));

            var separator = sourceId.LastIndexOf('#');
            if (separator <= 0 || separator == sourceId.Length - 1)
                throw new ArgumentException($"Invalid GitHub sync source id '{sourceId}'. Expected 'owner/repo#number'.", nameof(sourceId));

            var repository = sourceId.Substring(0, separator).Trim();
            var numberPart = sourceId.Substring(separator + 1).Trim();

            if (repository.Length == 0 || !int.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                throw new ArgumentException($"Invalid GitHub sync source id '{sourceId}'. Expected 'owner/repo#number'.", nameof(sourceId));

            return (repository, number);
        }
    }
}
