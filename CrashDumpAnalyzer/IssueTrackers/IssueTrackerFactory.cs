using CrashDumpAnalyzer.IssueTrackers.Data;
using CrashDumpAnalyzer.IssueTrackers.Interfaces;

namespace CrashDumpAnalyzer.IssueTrackers
{
    public class IssueTrackerFactory
    {
        public static IIssueTracker? GetIssueTracker(IConfiguration configuration)
        {
            var issueTrackerName = configuration.GetSection("IssueTracker").GetValue<string>("Type");
            switch (issueTrackerName)
            {
                case "Jira":
                    return new Integrations.Jira(configuration);
                default:
                    return new DummyIssueTracker();
            }
        }
    }

    public class DummyIssueTracker : IIssueTracker
    {
        public Task<Dictionary<string, IssueData>> GetIssueDataAsync(HttpClient httpClient, List<string> issueIds, CancellationToken token)
        {
            return Task.FromResult(new Dictionary<string, IssueData>());
        }
    }
}
