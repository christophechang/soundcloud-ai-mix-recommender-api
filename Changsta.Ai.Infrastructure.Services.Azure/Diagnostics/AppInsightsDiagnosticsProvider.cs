using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Changsta.Ai.Core.Contracts.Diagnostics;
using Changsta.Ai.Core.Diagnostics;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Azure.Diagnostics
{
    internal sealed class AppInsightsDiagnosticsProvider : IErrorInsightsProvider
    {
        private const int MaxRequests = 200;
        private const int MaxExceptions = 100;

        private readonly LogAnalyticsOptions _options;

        public AppInsightsDiagnosticsProvider(IOptions<LogAnalyticsOptions> options)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<DiagnosticsResult> GetErrorsAsync(int hours, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_options.WorkspaceId))
            {
                return new DiagnosticsResult
                {
                    GeneratedAt = DateTimeOffset.UtcNow,
                    WindowHours = hours,
                };
            }

            var client = new LogsQueryClient(new DefaultAzureCredential());
            var timeRange = new QueryTimeRange(TimeSpan.FromHours(hours));

            string requestsQuery =
                $"requests" +
                $" | where timestamp > ago({hours}h)" +
                $" | where toint(resultCode) >= 400" +
                $" | project timestamp, name, url, resultCode, duration, operation_Id" +
                $" | order by timestamp desc" +
                $" | take {MaxRequests}";

            string exceptionsQuery =
                $"exceptions" +
                $" | where timestamp > ago({hours}h)" +
                $" | project timestamp, type, outerMessage, operation_Id" +
                $" | order by timestamp desc" +
                $" | take {MaxExceptions}";

            var requestsTask = client.QueryWorkspaceAsync(
                _options.WorkspaceId, requestsQuery, timeRange, cancellationToken: cancellationToken);
            var exceptionsTask = client.QueryWorkspaceAsync(
                _options.WorkspaceId, exceptionsQuery, timeRange, cancellationToken: cancellationToken);

            await Task.WhenAll(requestsTask, exceptionsTask).ConfigureAwait(false);

            var requests = MapRequests(requestsTask.Result.Value.Table);
            var exceptions = MapExceptions(exceptionsTask.Result.Value.Table);

            return new DiagnosticsResult
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                WindowHours = hours,
                Requests = requests,
                Exceptions = exceptions,
            };
        }

        private static IReadOnlyList<DiagnosticsRequest> MapRequests(LogsTable table)
        {
            var results = new List<DiagnosticsRequest>(table.Rows.Count);

            foreach (LogsTableRow row in table.Rows)
            {
                DateTimeOffset timestamp = row["timestamp"] is DateTimeOffset ts ? ts : DateTimeOffset.UtcNow;
                string name = row["name"]?.ToString() ?? string.Empty;
                string? url = row["url"]?.ToString();
                int statusCode = int.TryParse(row["resultCode"]?.ToString(), out int code) ? code : 0;
                double durationMs = row["duration"] is double d ? d : 0;
                string? operationId = row["operation_Id"]?.ToString();

                results.Add(new DiagnosticsRequest
                {
                    Timestamp = timestamp,
                    StatusCode = statusCode,
                    Name = name,
                    Url = url,
                    DurationMs = durationMs,
                    OperationId = operationId,
                });
            }

            return results;
        }

        private static IReadOnlyList<DiagnosticsException> MapExceptions(LogsTable table)
        {
            var results = new List<DiagnosticsException>(table.Rows.Count);

            foreach (LogsTableRow row in table.Rows)
            {
                DateTimeOffset timestamp = row["timestamp"] is DateTimeOffset ts ? ts : DateTimeOffset.UtcNow;
                string? type = row["type"]?.ToString();
                string? message = row["outerMessage"]?.ToString();
                string? operationId = row["operation_Id"]?.ToString();

                results.Add(new DiagnosticsException
                {
                    Timestamp = timestamp,
                    Type = type,
                    Message = message,
                    OperationId = operationId,
                });
            }

            return results;
        }
    }
}
