﻿using CrashDumpAnalyzer.IssueTrackers.Data;
using CrashDumpAnalyzer.IssueTrackers.Interfaces;
using System.Text;
using System.Text.Json;

namespace CrashDumpAnalyzer.IssueTrackers.Integrations
{
    public class Jira : IIssueTracker
    {
        private readonly string? _jiraUrl;
        private readonly string? _jiraUsername;
        private readonly string? _jiraPassword;

        public Jira(IConfiguration configuration)
        {
            // get Jira configuration
            var jiraSection = configuration.GetSection("IssueTracker");
            _jiraUrl = jiraSection.GetValue<string>("Url");
            _jiraUsername = jiraSection.GetValue<string>("Username");
            _jiraPassword = jiraSection.GetValue<string>("Password");
        }
        public async Task<Dictionary<string, IssueData>> GetIssueDataAsync(HttpClient httpClient, List<string> issueIds, CancellationToken token)
        {
            Dictionary<string, IssueData> issueDataList = new Dictionary<string, IssueData>();
            string issueListString = string.Join(", ", issueIds);
            string jiraUrl = $"{_jiraUrl}search?maxResults=10000&jql=key in ({issueListString})&fields=key,title,status,summary";
 
            using (var request = new HttpRequestMessage(HttpMethod.Get, jiraUrl))
            {
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                var base64authorization = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_jiraUsername}:{_jiraPassword}"));
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {base64authorization}");

                var response = await httpClient.SendAsync(request, token);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    // Parse the content and populate issueData
                    // Assuming the response content is in JSON format
                    var jsonResponse = JsonDocument.Parse(content);
                    foreach (var issue in jsonResponse.RootElement.GetProperty("issues").EnumerateArray())
                    {
                        var data = new IssueData
                        {
                            Id = issue.GetProperty("key").GetString(),
                            Title = issue.GetProperty("fields").GetProperty("summary").GetString(),
                            State = issue.GetProperty("fields").GetProperty("status").GetProperty("name").GetString(),
                            StateColor = issue.GetProperty("fields").GetProperty("status").GetProperty("statusCategory").GetProperty("colorName").GetString()
                        };
                        issueDataList.Add(data.Id, data);
                    }
                }
            }

            return issueDataList;
        }

    }
}
