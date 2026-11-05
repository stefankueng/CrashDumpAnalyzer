using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using CrashDumpAnalyzer.IssueTrackers.Data;
using CrashDumpAnalyzer.IssueTrackers.Interfaces;

namespace CrashDumpAnalyzer.IssueTrackers.Integrations
{
    public class GitHub : IIssueTracker
    {
        private readonly Uri _apiBaseUri;
        private readonly string? _owner;
        private readonly string? _repository;
        private readonly string? _token;

        public GitHub(IConfiguration configuration)
        {
            var issueTrackerSection = configuration.GetSection("IssueTracker");
            var apiUrl = issueTrackerSection.GetValue<string>("Url") ?? "https://api.github.com/";
            _apiBaseUri = new Uri(apiUrl.EndsWith('/') ? apiUrl : apiUrl + "/", UriKind.Absolute);
            _owner = issueTrackerSection.GetValue<string>("Owner");
            _repository = issueTrackerSection.GetValue<string>("Repository");
            _token = issueTrackerSection.GetValue<string>("Token");
        }

        public async Task<Dictionary<string, IssueData>> GetIssueDataAsync(HttpClient httpClient, List<string> issueIds, CancellationToken token)
        {
            var issueData = new ConcurrentDictionary<string, IssueData>();
            await Parallel.ForEachAsync(issueIds, new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = 4
            }, async (issueId, cancellationToken) =>
            {
                if (!TryParseIssueReference(issueId, out var owner, out var repository, out var number))
                    return;

                var escapedOwner = Uri.EscapeDataString(owner);
                var escapedRepository = Uri.EscapeDataString(repository);
                var requestUri = new Uri(_apiBaseUri,
                    $"repos/{escapedOwner}/{escapedRepository}/issues/{number.ToString(CultureInfo.InvariantCulture)}");
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CrashDumpAnalyzer", "1.0"));
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                if (!string.IsNullOrWhiteSpace(_token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

                using var response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var json = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
                var issue = json.RootElement;
                var state = issue.GetProperty("state").GetString() ?? string.Empty;
                var data = new IssueData
                {
                    Id = issueId,
                    Title = issue.GetProperty("title").GetString() ?? string.Empty,
                    State = state,
                    StateColor = state.Equals("closed", StringComparison.OrdinalIgnoreCase) ? "#8250df" : "#1f883d",
                    Url = issue.GetProperty("html_url").GetString()
                };
                issueData.TryAdd(issueId, data);
            });

            return new Dictionary<string, IssueData>(issueData);
        }

        private bool TryParseIssueReference(string issueId, out string owner, out string repository, out int number)
        {
            owner = string.Empty;
            repository = string.Empty;
            number = 0;
            var reference = issueId.Trim();

            if (Uri.TryCreate(reference, UriKind.Absolute, out var issueUri))
            {
                var segments = issueUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length != 4 || !segments[2].Equals("issues", StringComparison.OrdinalIgnoreCase))
                    return false;

                owner = Uri.UnescapeDataString(segments[0]);
                repository = Uri.UnescapeDataString(segments[1]);
                return int.TryParse(segments[3], NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
            }

            var separator = reference.LastIndexOf('#');
            if (separator > 0)
            {
                var repositoryReference = reference[..separator].Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (repositoryReference.Length != 2)
                    return false;
                owner = repositoryReference[0];
                repository = repositoryReference[1];
                reference = reference[(separator + 1)..];
            }
            else
            {
                owner = _owner ?? string.Empty;
                repository = _repository ?? string.Empty;
                if (separator == 0)
                    reference = reference[1..];
            }

            return !string.IsNullOrWhiteSpace(owner) &&
                   !string.IsNullOrWhiteSpace(repository) &&
                   int.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
                   number > 0;
        }
    }
}
