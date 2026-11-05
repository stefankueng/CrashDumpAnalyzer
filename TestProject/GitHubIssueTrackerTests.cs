using System.Collections.Concurrent;
using System.Net;
using System.Text;
using CrashDumpAnalyzer.IssueTrackers.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CrashDumpAnalyzer.Tests
{
    [TestClass]
    public class GitHubIssueTrackerTests
    {
        [TestMethod]
        public async Task GetIssueDataAsync_ResolvesConfiguredAndRepositoryQualifiedIssues()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["IssueTracker:Owner"] = "stefankueng",
                    ["IssueTracker:Repository"] = "CrashDumpAnalyzer",
                    ["IssueTracker:Token"] = "test-token"
                })
                .Build();
            var handler = new StubHttpMessageHandler();
            using var httpClient = new HttpClient(handler);
            var tracker = new GitHub(configuration);

            var results = await tracker.GetIssueDataAsync(httpClient, ["#17", "#18"], CancellationToken.None);

            Assert.AreEqual(2, results.Count);
            Assert.AreEqual("wrong text in popup", results["#17"].Title);
            Assert.AreEqual("closed", results["#17"].State);
            Assert.AreEqual("#8250df", results["#17"].StateColor);
            Assert.AreEqual("https://github.com/stefankueng/CrashDumpAnalyzer/issues/17", results["#17"].Url);
            Assert.IsTrue(handler.Requests.Any(request => request.Url == "https://api.github.com/repos/stefankueng/CrashDumpAnalyzer/issues/17"));
            Assert.IsTrue(handler.Requests.All(request => request.Authorization == "Bearer test-token"));
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            public ConcurrentBag<(string? Url, string? Authorization)> Requests { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add((request.RequestUri?.ToString(), request.Headers.Authorization?.ToString()));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"title\":\"wrong text in popup\",\"state\":\"closed\",\"html_url\":\"https://github.com/stefankueng/CrashDumpAnalyzer/issues/17\"}",
                        Encoding.UTF8,
                        "application/json")
                });
            }
        }
    }
}
