using CrashDumpAnalyzer.IssueTrackers.Data;
using System.Net.Http;

namespace CrashDumpAnalyzer.IssueTrackers.Interfaces
{
    public interface IIssueTracker
    {
        public Task<Dictionary<string, IssueData>> GetIssueDataAsync(HttpClient httpClient, List<string> issueIds, CancellationToken token);
    }
}
