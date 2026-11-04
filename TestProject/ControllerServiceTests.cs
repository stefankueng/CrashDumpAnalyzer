using CrashDumpAnalyzer.Controllers;
using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.IssueTrackers.Data;
using CrashDumpAnalyzer.IssueTrackers.Interfaces;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Services;
using CrashDumpAnalyzer.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace CrashDumpAnalyzer.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class ControllerServiceTests
    {
        [TestMethod]
        public async Task RegisterUpload_ReusesAndResetsUnassignedCallstack()
        {
            await using var fixture = await Fixture.Create();
            var files = fixture.Get<DumpFileService>();
            await files.RegisterUpload("first.dmp", 100, "192.0.2.1", "first", "user", "user@example.test");
            var unassigned = await fixture.Db.Set<DumpCallstack>().SingleAsync();
            unassigned.Deleted = true;
            unassigned.FixedVersion = "1.0.0.0";
            unassigned.Ticket = "OLD-1";
            unassigned.Comment = "old";
            await fixture.Db.SaveChangesAsync();

            await files.RegisterUpload("second.dmp", 200, "192.0.2.2", "second", "another", "another@example.test");

            Assert.AreEqual(1, await fixture.Db.Set<DumpCallstack>().CountAsync());
            Assert.IsFalse(unassigned.Deleted);
            Assert.AreEqual(string.Empty, unassigned.FixedVersion);
            Assert.AreEqual(string.Empty, unassigned.Ticket);
            Assert.AreEqual(string.Empty, unassigned.Comment);
            var stored = await fixture.Db.Set<DumpFileInfo>().SingleAsync(f => f.FilePath == "second.dmp");
            Assert.AreEqual(200L, stored.FileSize);
            Assert.AreEqual("192.0.2.2", stored.UploadedFromIp);
            Assert.AreEqual("second", stored.Comment);
            Assert.AreEqual("another", stored.UploadedFromUsername);
            Assert.AreEqual("another@example.test", stored.UploadedFromUserEmail);
        }

        [TestMethod]
        public async Task OpenDownload_EncodesLogLinesAndRetainsLineAnchors()
        {
            await using var fixture = await Fixture.Create();
            var callstack = fixture.Callstack("App", "1.0.0.0", "example.LOG");
            await File.WriteAllTextAsync(fixture.FilePath("example.LOG"), "<script>alert('x')</script>\nA & B");
            fixture.Db.Add(callstack);
            await fixture.Db.SaveChangesAsync();

            var download = await fixture.Get<DumpFileService>().OpenDownload(callstack.DumpInfos[0].DumpFileInfoId);

            Assert.IsNotNull(download);
            await using var stream = download.Content;
            Assert.AreEqual("text/html", download.ContentType);
            Assert.AreEqual(string.Empty, download.FileName);
            Assert.AreEqual(0L, stream.Position);
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();
            StringAssert.Contains(html, "<div id=\"1\">&lt;script&gt;");
            StringAssert.Contains(html, "<div id=\"2\">A &amp; B</div>");
            Assert.IsFalse(html.Contains("<script>"));
        }

        [TestMethod]
        public async Task OpenDownload_PreservesBinaryContentAndMissingFileResponse()
        {
            await using var fixture = await Fixture.Create();
            var callstack = fixture.Callstack("App", "1.0.0.0", "example.dmp");
            byte[] content = [0, 1, 2, 255];
            await File.WriteAllBytesAsync(fixture.FilePath("example.dmp"), content);
            fixture.Db.Add(callstack);
            await fixture.Db.SaveChangesAsync();

            var download = await fixture.Get<DumpFileService>().OpenDownload(callstack.DumpInfos[0].DumpFileInfoId);

            Assert.IsNotNull(download);
            await using var stream = download.Content;
            Assert.AreEqual("application/octet-stream", download.ContentType);
            Assert.AreEqual("Dump.dmp", download.FileName);
            using var actual = new MemoryStream();
            await stream.CopyToAsync(actual);
            CollectionAssert.AreEqual(content, actual.ToArray());
            Assert.IsNull(await fixture.Get<DumpFileService>().OpenDownload(int.MaxValue));
        }

        [TestMethod]
        public async Task Mutations_UpdateParentAndClearLinkedMetadata()
        {
            await using var fixture = await Fixture.Create();
            var parent = fixture.Callstack("App", "1.0.0.0");
            fixture.Db.Add(parent);
            await fixture.Db.SaveChangesAsync();
            var child = fixture.Callstack("App", "1.1.0.0");
            child.LinkedToDumpCallstackId = parent.DumpCallstackId;
            child.FixedVersion = "0.9.0.0";
            child.FixedBuildType = 3;
            child.Ticket = "OLD-1";
            child.Comment = "old";
            fixture.Db.Add(child);
            await fixture.Db.SaveChangesAsync();
            var service = fixture.Get<CallstackService>();

            await service.SetFixedVersion(parent.DumpCallstackId, "2.0.0.0", "release");
            await service.SetTicket(parent.DumpCallstackId, "  APP-123  ");
            await service.SetComment(parent.DumpCallstackId, "new\ncomment");
            await service.SetApplicationVersion(parent.DumpCallstackId, "1.5.0.0", "release");

            Assert.AreEqual("2.0.0.0", parent.FixedVersion);
            Assert.AreEqual("APP-123", parent.Ticket);
            Assert.AreEqual("new\ncomment", parent.Comment);
            Assert.AreEqual("1.5.0.0", parent.ApplicationVersion);
            Assert.AreEqual(BuildTypes.ParseBuildType("release"), parent.BuildType);
            Assert.AreEqual(string.Empty, child.FixedVersion);
            Assert.AreEqual(-1, child.FixedBuildType);
            Assert.AreEqual(string.Empty, child.Ticket);
            Assert.AreEqual(string.Empty, child.Comment);
        }

        [TestMethod]
        public async Task Linking_ReparentsChildrenAndUnlinkingSeparatesThem()
        {
            await using var fixture = await Fixture.Create();
            var target = fixture.Callstack("App", "1.0.0.0");
            var source = fixture.Callstack("App", "1.0.0.0");
            source.Deleted = true;
            fixture.Db.AddRange(target, source);
            await fixture.Db.SaveChangesAsync();
            var child = fixture.Callstack("App", "1.0.0.0");
            child.LinkedToDumpCallstackId = source.DumpCallstackId;
            child.Deleted = true;
            fixture.Db.Add(child);
            await fixture.Db.SaveChangesAsync();
            var service = fixture.Get<CallstackService>();

            Assert.IsTrue(await service.LinkCallstack(source.DumpCallstackId, target.DumpCallstackId));
            Assert.AreEqual(target.DumpCallstackId, source.LinkedToDumpCallstackId);
            Assert.AreEqual(target.DumpCallstackId, child.LinkedToDumpCallstackId);
            Assert.IsFalse(source.Deleted);
            Assert.IsFalse(child.Deleted);
            Assert.IsFalse(await service.LinkCallstack(target.DumpCallstackId, target.DumpCallstackId));
            Assert.IsTrue(await service.UnlinkDumpCallstack(target.DumpCallstackId));
            Assert.AreEqual(0, source.LinkedToDumpCallstackId);
            Assert.AreEqual(0, child.LinkedToDumpCallstackId);
        }

        [TestMethod]
        public async Task DeletingFile_PreservesItsMetadata()
        {
            await using var fixture = await Fixture.Create();
            var callstack = fixture.Callstack("App", "1.0.0.0", "delete.dmp");
            await File.WriteAllTextAsync(fixture.FilePath("delete.dmp"), "dump");
            fixture.Db.Add(callstack);
            await fixture.Db.SaveChangesAsync();
            var file = callstack.DumpInfos[0];

            Assert.IsTrue(await fixture.Get<CallstackService>().DeleteDumpFile(file.DumpFileInfoId));

            Assert.IsFalse(File.Exists(fixture.FilePath("delete.dmp")));
            Assert.AreEqual(string.Empty, file.FilePath);
            Assert.AreEqual(1, await fixture.Db.Set<DumpFileInfo>().CountAsync());
            Assert.IsFalse(await fixture.Get<CallstackService>().DeleteDumpFile(int.MaxValue));
        }

        [TestMethod]
        public async Task SoftDeletion_KeepsCallstackAndRemovesItsPhysicalFile()
        {
            await using var fixture = await Fixture.Create();
            var callstack = fixture.Callstack("App", "1.0.0.0", "soft.dmp");
            await File.WriteAllTextAsync(fixture.FilePath("soft.dmp"), "dump");
            fixture.Db.Add(callstack);
            await fixture.Db.SaveChangesAsync();

            Assert.IsTrue(await fixture.Get<CallstackService>().DeleteDumpCallstack(callstack.DumpCallstackId, "obsolete", false));

            Assert.IsTrue(callstack.Deleted);
            Assert.AreEqual("obsolete", callstack.Comment);
            Assert.AreEqual(1, await fixture.Db.Set<DumpCallstack>().CountAsync());
            Assert.AreEqual(string.Empty, callstack.DumpInfos[0].FilePath);
            Assert.IsFalse(File.Exists(fixture.FilePath("soft.dmp")));
        }

        [TestMethod]
        public async Task HardDeletion_RemovesLinkedRowsAndOrphanedFiles()
        {
            await using var fixture = await Fixture.Create();
            var parent = fixture.Callstack("App", "1.0.0.0", "parent.dmp");
            fixture.Db.Add(parent);
            await fixture.Db.SaveChangesAsync();
            var child = fixture.Callstack("App", "1.0.0.0", "child.dmp");
            child.LinkedToDumpCallstackId = parent.DumpCallstackId;
            fixture.Db.Add(child);
            await fixture.Db.SaveChangesAsync();
            await File.WriteAllTextAsync(fixture.FilePath("parent.dmp"), "parent");
            await File.WriteAllTextAsync(fixture.FilePath("child.dmp"), "child");

            Assert.IsTrue(await fixture.Get<CallstackService>().DeleteDumpCallstack(child.DumpCallstackId, string.Empty, true));

            fixture.Db.ChangeTracker.Clear();
            Assert.AreEqual(0, await fixture.Db.Set<DumpCallstack>().CountAsync());
            Assert.AreEqual(0, await fixture.Db.Set<DumpFileInfo>().CountAsync());
            Assert.AreEqual(0, Directory.GetFiles(fixture.DirectoryPath).Length);
        }

        [TestMethod]
        public async Task OldEntryBatches_RespectMajorVersionTicketsCommentsAndBatchSize()
        {
            await using var fixture = await Fixture.Create();
            var older = fixture.Callstack("App", "2.0.0.0");
            var another = fixture.Callstack("App", "2.1.0.0");
            var ticketed = fixture.Callstack("App", "2.0.0.0");
            ticketed.Ticket = "APP-1";
            var commented = fixture.Callstack("App", "2.0.0.0");
            commented.Comment = "keep";
            fixture.Db.AddRange(older, another, ticketed, commented,
                fixture.Callstack("App", "1.0.0.0"), fixture.Callstack("App", "2.9.0.0"));
            await fixture.Db.SaveChangesAsync();
            var api = fixture.CreateApi();

            var countResult = (OkObjectResult)await api.CountOldEntries("2.5.0.0");
            Assert.AreEqual("{\"count\":2}", JsonSerializer.Serialize(countResult.Value));
            var deleteResult = (OkObjectResult)await api.DeleteOldEntries("2.5.0.0", 1);
            var payload = JsonSerializer.SerializeToElement(deleteResult.Value);
            Assert.AreEqual(1, payload.GetProperty("deletedCount").GetInt32());
            Assert.AreEqual(1, payload.GetProperty("deletedIds").GetArrayLength());
            Assert.AreEqual(1, await fixture.Get<CallstackService>().CountOldEntries("2.5.0.0"));
            Assert.IsInstanceOfType<BadRequestObjectResult>(await api.CountOldEntries(""));
        }

        [TestMethod]
        public async Task DumpResults_MergeMatchingStacksAndPreserveNewestVersionAndMetadata()
        {
            await using var fixture = await Fixture.Create();
            var files = fixture.Get<DumpFileService>();
            var results = fixture.Get<AnalysisResultService>();
            await files.RegisterUpload("first.dmp", 10, "", "", "", "");
            await results.UpdateDumpDataInDatabase("first.dmp", "host", new DumpData
            {
                processName = "App", version = "2.0.0.0", callstackString = "raw stack", cleanCallstackString = "stack",
                exceptionCode = "c0000005", computerName = "workstation", domain = "domain", environment = "env",
                versionResource = "resource", commandLine = "App --option", dumpTimeOffset = TimeSpan.FromHours(2)
            }, null, CancellationToken.None);
            await files.RegisterUpload("second.dmp", 10, "", "", "", "");
            await results.UpdateDumpDataInDatabase("second.dmp", "host", new DumpData
            {
                processName = "App", version = "1.0.0.0", callstackString = "another raw stack", cleanCallstackString = "stack", exceptionCode = "c0000005"
            }, null, CancellationToken.None);

            var callstack = await fixture.Db.Set<DumpCallstack>().Include(c => c.DumpInfos).SingleAsync(c => c.ApplicationName == "App");
            Assert.AreEqual(2, callstack.DumpInfos.Count);
            Assert.AreEqual("2.0.0.0", callstack.ApplicationVersion);
            var first = callstack.DumpInfos.Single(f => f.FilePath == "first.dmp");
            Assert.AreEqual("host", first.UploadedFromHostname);
            Assert.AreEqual("workstation", first.ComputerName);
            Assert.AreEqual("domain", first.Domain);
            Assert.AreEqual("env", first.Environment);
            Assert.AreEqual("resource", first.VersionResource);
            Assert.AreEqual("App --option", first.CommandLine);
            Assert.AreEqual(TimeSpan.FromHours(2), first.DumpTimeOffset);
            Assert.AreEqual(2, await fixture.Db.Set<DumpFileInfo>().CountAsync());
        }

        [TestMethod]
        public async Task Reanalysis_UpdatesOriginalCallstackWhenNoOtherStackMatches()
        {
            await using var fixture = await Fixture.Create();
            var original = fixture.Callstack("App", "1.0.0.0", "reanalyze.dmp");
            original.CleanCallstack = "old";
            fixture.Db.Add(original);
            await fixture.Db.SaveChangesAsync();

            await fixture.Get<AnalysisResultService>().UpdateDumpDataInDatabase("reanalyze.dmp", null,
                new DumpData { processName = "App", version = "2.0.0.0", callstackString = "new raw", cleanCallstackString = "new" },
                original.DumpCallstackId, CancellationToken.None);

            Assert.AreEqual(1, await fixture.Db.Set<DumpCallstack>().CountAsync());
            Assert.AreEqual("new", original.CleanCallstack);
            Assert.AreEqual("new raw", original.Callstack);
            Assert.AreEqual("2.0.0.0", original.ApplicationVersion);
        }

        [TestMethod]
        public async Task Reanalysis_ReassignsAMatchingFileAndMarksTheEmptyOriginalDeleted()
        {
            await using var fixture = await Fixture.Create();
            var original = fixture.Callstack("App", "1.0.0.0", "reanalyze.dmp");
            original.CleanCallstack = "old";
            var target = fixture.Callstack("App", "2.0.0.0", "existing.dmp");
            target.CleanCallstack = "target";
            fixture.Db.AddRange(original, target);
            await fixture.Db.SaveChangesAsync();

            await fixture.Get<AnalysisResultService>().UpdateDumpDataInDatabase("reanalyze.dmp", null,
                new DumpData { processName = "App", version = "2.0.0.0", cleanCallstackString = "target" },
                original.DumpCallstackId, CancellationToken.None);

            Assert.IsTrue(original.Deleted);
            Assert.AreEqual(0, original.DumpInfos.Count);
            Assert.AreEqual(2, target.DumpInfos.Count);
            fixture.Db.ChangeTracker.Clear();
            var reassigned = await fixture.Db.Set<DumpFileInfo>().SingleAsync(f => f.FilePath == "reanalyze.dmp");
            Assert.AreEqual(target.DumpCallstackId, reassigned.DumpCallstackId);
        }

        [TestMethod]
        public async Task MinimumVersions_DeleteOldResultsAndRemoveOnlyTheAutomaticCommentOnRecovery()
        {
            await using var fixture = await Fixture.Create();
            fixture.Configuration["MinVersions:^App$:2"] = "2.5.0.0";
            MinVersions.Initialize(fixture.Get<ILogger<ControllerServiceTests>>(), fixture.Configuration);
            await fixture.Get<DumpFileService>().RegisterUpload("old.dmp", 100, "", "", "", "");
            await fixture.Get<AnalysisResultService>().UpdateDumpDataInDatabase("old.dmp", null,
                new DumpData { processName = "App", version = "2.0.0.0", cleanCallstackString = "stack" }, null, CancellationToken.None);
            var callstack = await fixture.Db.Set<DumpCallstack>().SingleAsync(c => c.ApplicationName == "App");
            Assert.IsTrue(callstack.Deleted);
            StringAssert.Contains(callstack.Comment, "Version is too old");
            callstack.Comment = "keep this comment\n" + callstack.Comment;
            await fixture.Db.SaveChangesAsync();
            await fixture.Get<DumpFileService>().RegisterUpload("new.dmp", 100, "", "", "", "");

            await fixture.Get<AnalysisResultService>().UpdateDumpDataInDatabase("new.dmp", null,
                new DumpData { processName = "App", version = "2.6.0.0", cleanCallstackString = "stack" }, null, CancellationToken.None);

            Assert.IsFalse(callstack.Deleted);
            Assert.AreEqual("keep this comment", callstack.Comment);
            Assert.AreEqual("2.6.0.0", callstack.ApplicationVersion);
        }

        [TestMethod]
        public async Task DashboardOrdering_PrioritizesUnassignedRegressionsAndUnticketedIssues()
        {
            await using var fixture = await Fixture.Create();
            var unassigned = fixture.Callstack(Constants.UnassignedDumpNames, "", "unassigned.dmp");
            var regression = fixture.Callstack("Regression", "3.0.0.0", "regression.dmp");
            regression.FixedVersion = "2.0.0.0";
            var open = fixture.Callstack("Open", "1.0.0.0", "open.dmp");
            var ticketed = fixture.Callstack("Ticketed", "1.0.0.0", "ticketed.dmp");
            ticketed.Ticket = "APP-1";
            var fixedCallstack = fixture.Callstack("Fixed", "1.0.0.0", "fixed.dmp");
            fixedCallstack.FixedVersion = "2.0.0.0";
            fixture.Db.AddRange(fixedCallstack, ticketed, open, regression, unassigned);
            await fixture.Db.SaveChangesAsync();

            var rows = await fixture.Get<CallstackQueryService>().FetchCallStacks(null, null);

            CollectionAssert.AreEqual(new[] { unassigned.DumpCallstackId, regression.DumpCallstackId, open.DumpCallstackId,
                ticketed.DumpCallstackId, fixedCallstack.DumpCallstackId }, rows.Select(c => c.DumpCallstackId).ToArray());
        }

        [TestMethod]
        public async Task LogResults_StoreLineNumbersAndKeepTheScopeContextUsable()
        {
            await using var fixture = await Fixture.Create();
            await fixture.Get<DumpFileService>().RegisterUpload("issues.log", 100, "", "", "", "");
            var date = DateTime.Now;
            var issue = new LogIssue { ApplicationName = "App", ApplicationVersion = "2.0.0.0", IssueType = "Assertion", IssueText = "failure", IssueTextClean = "failure" };
            var data = new Dictionary<LogIssue, List<(DateTime date, long lineNumber)>> { [issue] = [(date, 3), (date.AddMinutes(1), 5)] };

            await fixture.Get<AnalysisResultService>().UpdateLogDataInDatabase("issues.log", "host", data, CancellationToken.None);

            fixture.Db.ChangeTracker.Clear();
            var callstack = await fixture.Db.Set<DumpCallstack>().Include(c => c.LogFileDatas).SingleAsync(c => c.ApplicationName == "App");
            Assert.AreEqual("failure", callstack.CleanCallstack);
            Assert.AreEqual("3,5", callstack.LogFileDatas.Single().LineNumberString);
            Assert.AreEqual(date.AddMinutes(1), callstack.LogFileDatas.Single().LatestTime);
            var file = await fixture.Db.Set<DumpFileInfo>().SingleAsync();
            Assert.AreEqual("host", file.UploadedFromHostname);
            Assert.AreEqual("Log contains 1 issues", file.LogSummary);
        }

        [TestMethod]
        public async Task Queries_MergeLinkedCallstacksWithoutPersistingPresentationChanges()
        {
            await using var fixture = await Fixture.Create();
            var parent = fixture.Callstack("App", "1.0.0.0", "parent.dmp");
            parent.Ticket = "APP-1";
            parent.Callstack = "parent stack";
            fixture.Db.Add(parent);
            await fixture.Db.SaveChangesAsync();
            var child = fixture.Callstack("App", "2.0.0.0", "child.dmp");
            child.Ticket = "APP-2";
            child.Callstack = "child stack";
            child.LinkedToDumpCallstackId = parent.DumpCallstackId;
            fixture.Db.Add(child);
            await fixture.Db.SaveChangesAsync();
            fixture.Db.ChangeTracker.Clear();

            var rows = await fixture.Get<CallstackQueryService>().FetchCallStacks(null, null);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(2, rows[0].DumpInfos.Count);
            Assert.AreEqual("2.0.0.0", rows[0].ApplicationVersion);
            StringAssert.Contains(rows[0].Ticket, "APP-1");
            StringAssert.Contains(rows[0].Ticket, "APP-2");
            StringAssert.Contains(rows[0].Callstack, "child stack");
            var stored = await fixture.Db.Set<DumpCallstack>().SingleAsync(c => c.DumpCallstackId == parent.DumpCallstackId);
            Assert.AreEqual("1.0.0.0", stored.ApplicationVersion);
            Assert.AreEqual("parent stack", stored.Callstack);
        }

        [TestMethod]
        public async Task Search_IncludesDeletedMatchesAndDiscoversStoredLogIssueTypes()
        {
            await using var fixture = await Fixture.Create();
            var deleted = fixture.Callstack("SearchApp", "1.0.0.0", "deleted.dmp");
            deleted.Deleted = true;
            var storedLog = fixture.Callstack("LogApp", "1.0.0.0", "log.txt");
            storedLog.ExceptionType = "StoredIssue";
            storedLog.LogFileDatas.Add(new LogFileData { DumpFileInfo = storedLog.DumpInfos[0], LineNumberString = "1,2", LatestTime = DateTime.Now });
            fixture.Db.AddRange(deleted, storedLog);
            await fixture.Db.SaveChangesAsync();
            var queries = fixture.Get<CallstackQueryService>();

            var matches = await queries.FetchCallStacks(null, null, "Search*");

            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual(deleted.DumpCallstackId, matches[0].DumpCallstackId);
            var types = await queries.GetIssueTypes();
            CollectionAssert.Contains(types, "StoredIssue");
            CollectionAssert.Contains(types, "Assertion");
            Assert.AreEqual("CrashDumps", types[0]);
        }

        [TestMethod]
        public async Task HomePages_PreserveViewModelsStatisticsAndGroupOrdering()
        {
            await using var fixture = await Fixture.Create();
            var first = fixture.Callstack("App", "1.0.0.0", "first.dmp");
            first.Comment = "review";
            first.DumpInfos[0].Comment = "team-alpha";
            var second = fixture.Callstack("App", "1.0.0.0", "second.dmp");
            second.Ticket = "APP-1";
            second.FixedVersion = "2.0.0.0";
            second.DumpInfos[0].Comment = "team-beta";
            fixture.Db.AddRange(first, second);
            await fixture.Db.SaveChangesAsync();
            var pages = fixture.Get<HomePageService>();

            var index = await pages.GetIndex(null, "", 0);
            Assert.AreEqual(2, index.Callstacks.Count());
            Assert.AreEqual(2, index.UploadedDumps.Count());
            Assert.AreEqual(0, index.IssueData.Count);
            var statistics = await pages.GetStatistics();
            Assert.IsNotNull(statistics);
            Assert.AreEqual(1, statistics.OpenCallstacks["CrashDumps"]);
            Assert.AreEqual(1, statistics.ClosedCallstacks["CrashDumps"]);
            var grouped = await pages.GetGroupedDumps(8);
            Assert.IsNotNull(grouped);
            Assert.AreEqual(8, grouped.WeeksBack);
            CollectionAssert.AreEquivalent(new[] { "team-alpha", "team-beta" }, grouped.Groups.Select(g => g.GroupKey).ToArray());
            Assert.AreEqual(2, grouped.Groups.Sum(g => g.DumpCount));
            var home = new HomeController(pages);
            Assert.IsInstanceOfType<IndexPageData>(((ViewResult)await home.Index(null, "", 0)).Model);
            Assert.IsInstanceOfType<DumpPageData>(((ViewResult)await home.Dump(first.DumpCallstackId)).Model);
            Assert.IsInstanceOfType<StatisticsData>(((ViewResult)await home.Statistics()).Model);
            Assert.IsInstanceOfType<GroupedDumpsData>(((ViewResult)await home.GroupedDumpsAsync()).Model);
            Assert.IsNull(((ViewResult)await home.Dump(int.MaxValue)).Model);
        }

        [TestMethod]
        public async Task IssueTracker_ReceivesDistinctSplitTicketIds()
        {
            await using var fixture = await Fixture.Create();
            var callstack = fixture.Callstack("App", "1.0.0.0", "ticket.dmp");
            callstack.Ticket = "APP-1, APP-2 APP-1";
            fixture.Db.Add(callstack);
            await fixture.Db.SaveChangesAsync();
            var tracker = new Mock<IIssueTracker>();
            tracker.Setup(t => t.GetIssueDataAsync(It.IsAny<HttpClient>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, IssueData> { ["APP-1"] = new IssueData { Id = "APP-1", Title = "title" } });
            var pages = new HomePageService(fixture.Db, fixture.Get<CallstackQueryService>(), fixture.Get<IHttpClientFactory>(),
                fixture.Configuration, fixture.Get<ILogger<HomePageService>>(), tracker.Object);

            var page = await pages.GetIndex(null, "", 0);

            Assert.AreEqual("title", page.IssueData["APP-1"].Title);
            tracker.Verify(t => t.GetIssueDataAsync(It.IsAny<HttpClient>(),
                It.Is<List<string>>(ids => ids.Count == 2 && ids.Contains("APP-1") && ids.Contains("APP-2")), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task UploadController_PreservesMultipartMetadataResponseAndQueuedWork()
        {
            await using var fixture = await Fixture.Create();
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("support report"), "fileComment");
            content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("example log")), "files", "report.log");
            var http = new DefaultHttpContext();
            http.Request.ContentType = content.Headers.ContentType!.ToString();
            http.Request.Body = await content.ReadAsStreamAsync();
            http.Connection.RemoteIpAddress = IPAddress.Loopback;
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "uploader"), new Claim(ClaimTypes.Email, "uploader@example.test") }));
            var api = fixture.CreateApi();
            api.ControllerContext = new ControllerContext { HttpContext = http };

            var response = await api.UploadFiles();

            Assert.IsInstanceOfType<CreatedResult>(response);
            var file = await fixture.Db.Set<DumpFileInfo>().SingleAsync();
            Assert.AreEqual("support report", file.Comment);
            Assert.AreEqual("uploader", file.UploadedFromUsername);
            Assert.AreEqual("uploader@example.test", file.UploadedFromUserEmail);
            Assert.IsTrue(File.Exists(fixture.FilePath(file.FilePath)));
            Assert.AreEqual("example log", await File.ReadAllTextAsync(fixture.FilePath(file.FilePath)));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.IsNotNull(await fixture.Get<IBackgroundTaskQueue>().DequeueAsync(timeout.Token));
        }

        [TestMethod]
        public async Task Queue_CreatesAndDisposesScopeAfterTheRequestScopeHasEnded()
        {
            await using var fixture = await Fixture.Create();
            await File.WriteAllTextAsync(fixture.FilePath("queued.log"), "Application: App\nVersion: 1.0.0.0\n2026/11/04 14:50:00 ASSERT: failure");
            await using (var request = fixture.CreateScope())
            {
                await request.ServiceProvider.GetRequiredService<DumpFileService>().RegisterUpload("queued.log", 100, "127.0.0.1", "", "", "");
                var createdBeforeEnqueue = fixture.Lifetime.Created;
                await request.ServiceProvider.GetRequiredService<DumpAnalysisQueue>().QueueUpload("queued.log", "127.0.0.1");
                Assert.AreEqual(createdBeforeEnqueue, fixture.Lifetime.Created);
            }
            var createdBefore = fixture.Lifetime.Created;
            var disposedBefore = fixture.Lifetime.Disposed;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var work = await fixture.Get<IBackgroundTaskQueue>().DequeueAsync(timeout.Token);

            await work(timeout.Token);

            Assert.AreEqual(createdBefore + 1, fixture.Lifetime.Created);
            Assert.AreEqual(disposedBefore + 1, fixture.Lifetime.Disposed);
            fixture.Db.ChangeTracker.Clear();
            Assert.AreEqual("Log contains 1 issues", (await fixture.Db.Set<DumpFileInfo>().SingleAsync()).LogSummary);
        }

        [TestMethod]
        public async Task Queue_AwaitsCapacityInsteadOfDiscardingPendingWrites()
        {
            await using var fixture = await Fixture.Create();
            var queue = fixture.Get<DumpAnalysisQueue>();
            await queue.QueueReanalysis("first.dmp", 1);
            await queue.QueueReanalysis("second.dmp", 2);

            var pending = queue.QueueReanalysis("third.dmp", 3);

            Assert.IsFalse(pending.IsCompleted);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await fixture.Get<IBackgroundTaskQueue>().DequeueAsync(timeout.Token);
            await pending.AsTask().WaitAsync(timeout.Token);
            Assert.AreEqual(1, fixture.Lifetime.Created);
        }

        [TestMethod]
        public async Task ApiController_PreservesMissingItemAndInvalidLinkResponses()
        {
            await using var fixture = await Fixture.Create();
            var api = fixture.CreateApi();
            Assert.IsInstanceOfType<NotFoundResult>(await api.DownloadFile(int.MaxValue));
            Assert.IsInstanceOfType<NotFoundResult>(await api.DeleteDumpFile(int.MaxValue));
            Assert.IsInstanceOfType<NotFoundResult>(await api.DeleteDumpCallstack(int.MaxValue, false));
            Assert.IsInstanceOfType<NotFoundResult>(await api.ReAnalyzeDumpFile(int.MaxValue));
            Assert.IsInstanceOfType<BadRequestResult>(await api.SetApplicationVersion(int.MaxValue, "1.0.0.0", null));
            Assert.IsInstanceOfType<BadRequestResult>(await api.LinkCallstack(1, 1));
            Assert.IsInstanceOfType<NoContentResult>(await api.SetFixedVersion(int.MaxValue, "1.0.0.0", null));
            Assert.IsInstanceOfType<NoContentResult>(await api.SetTicket(int.MaxValue, "APP-1"));
            Assert.IsInstanceOfType<NoContentResult>(await api.SetComment(int.MaxValue, "comment"));
        }

        [TestMethod]
        public async Task Retention_RemovesOnlyOldFilesFromClosedCallstacks()
        {
            await using var fixture = await Fixture.Create();
            var oldClosed = fixture.Callstack("App", "1.0.0.0", "old-closed.dmp");
            oldClosed.FixedVersion = "2.0.0.0";
            oldClosed.DumpInfos[0].UploadDate = DateTime.Now.AddDays(-31);
            var oldOpen = fixture.Callstack("App", "1.0.0.0", "old-open.dmp");
            oldOpen.DumpInfos[0].UploadDate = DateTime.Now.AddDays(-31);
            var recent = fixture.Callstack("App", "1.0.0.0", "recent.dmp");
            recent.FixedVersion = "2.0.0.0";
            fixture.Db.AddRange(oldClosed, oldOpen, recent);
            await fixture.Db.SaveChangesAsync();
            foreach (var callstack in new[] { oldClosed, oldOpen, recent })
                await File.WriteAllTextAsync(fixture.FilePath(callstack.DumpInfos[0].FilePath), "dump");

            await fixture.Get<DumpCleanupService>().CleanupTask(CancellationToken.None);

            Assert.AreEqual(string.Empty, oldClosed.DumpInfos[0].FilePath);
            Assert.IsFalse(File.Exists(fixture.FilePath("old-closed.dmp")));
            Assert.IsTrue(File.Exists(fixture.FilePath("old-open.dmp")));
            Assert.IsTrue(File.Exists(fixture.FilePath("recent.dmp")));
            Assert.AreEqual(3, await fixture.Db.Set<DumpFileInfo>().CountAsync());
        }

        [TestMethod]
        public async Task FailedFileDeletion_IsSurfacedAndPreservesStoredPath()
        {
            await using var fixture = await Fixture.Create();
            var callstack = fixture.Callstack("App", "1.0.0.0", "directory.dmp");
            callstack.FixedVersion = "2.0.0.0";
            callstack.DumpInfos[0].UploadDate = DateTime.Now.AddDays(-31);
            fixture.Db.Add(callstack);
            await fixture.Db.SaveChangesAsync();
            Directory.CreateDirectory(fixture.FilePath("directory.dmp"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Get<CallstackService>().DeleteDumpFile(callstack.DumpInfos[0].DumpFileInfoId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Get<DumpCleanupService>().CleanupTask(CancellationToken.None));

            Assert.AreEqual("directory.dmp", callstack.DumpInfos[0].FilePath);
            fixture.Db.ChangeTracker.Clear();
            Assert.AreEqual("directory.dmp", (await fixture.Db.Set<DumpFileInfo>().SingleAsync()).FilePath);
        }

        [TestMethod]
        public async Task Breakpoints_AreNotAutomaticallyMerged()
        {
            await using var fixture = await Fixture.Create();
            foreach (var name in new[] { "first.dmp", "second.dmp" })
            {
                await fixture.Get<DumpFileService>().RegisterUpload(name, 100, "", "", "", "");
                await fixture.Get<AnalysisResultService>().UpdateDumpDataInDatabase(name, null, new DumpData
                {
                    processName = "App", version = "1.0.0.0", cleanCallstackString = "main thread", exceptionCode = "80000003"
                }, null, CancellationToken.None);
            }

            Assert.AreEqual(2, await fixture.Db.Set<DumpCallstack>().CountAsync(c => c.ApplicationName == "App"));
        }

        [TestMethod]
        public async Task FailedJob_DisposesItsScopeAndPropagatesTheError()
        {
            await using var fixture = await Fixture.Create();
            await fixture.Get<DumpAnalysisQueue>().QueueUpload("missing.log", "127.0.0.1");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var work = await fixture.Get<IBackgroundTaskQueue>().DequeueAsync(timeout.Token);
            var disposedBefore = fixture.Lifetime.Disposed;

            await Assert.ThrowsAsync<FileNotFoundException>(() => work(timeout.Token).AsTask());

            Assert.AreEqual(disposedBefore + 1, fixture.Lifetime.Disposed);
        }

        [TestMethod]
        public void ApiRoutes_PreserveActionNamesAndHttpVerbs()
        {
            var type = typeof(ApiController);
            var route = (RouteAttribute)type.GetCustomAttributes(typeof(RouteAttribute), false).Single();
            Assert.AreEqual("Api/[Action]", route.Template);
            foreach (var name in new[] { "UploadFiles", "DeleteDumpCallstack", "DeleteOldEntries", "UnlinkDumpCallstack", "DeleteDumpFile", "SetFixedVersion", "SetApplicationVersion", "SetTicket", "SetComment", "LinkCallstack", "ReAnalyzeDumpFile" })
            {
                var method = type.GetMethod(name);
                Assert.IsNotNull(method);
                Assert.IsTrue(method.IsDefined(typeof(HttpPostAttribute), false), name);
            }
            Assert.IsTrue(type.GetMethod("CountOldEntries")!.IsDefined(typeof(HttpGetAttribute), false));
            var downloadRoute = (HttpGetAttribute)type.GetMethod("DownloadFile")!.GetCustomAttributes(typeof(HttpGetAttribute), false).Single();
            Assert.AreEqual("{id}", downloadRoute.Template);
        }

        private sealed class ContextLifetime
        {
            public int Created { get; set; }
            public int Disposed { get; set; }
        }

        private sealed class TrackingContext : ApplicationDbContext
        {
            private readonly ContextLifetime _lifetime;

            public TrackingContext(DbContextOptions<ApplicationDbContext> options, ContextLifetime lifetime) : base(options)
            {
                _lifetime = lifetime;
                _lifetime.Created++;
            }

            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync();
                _lifetime.Disposed++;
            }
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly SqliteConnection _connection;
            private readonly ServiceProvider _provider;
            private readonly AsyncServiceScope _scope;
            public IConfiguration Configuration { get; }
            public ContextLifetime Lifetime { get; } = new();
            public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "CrashDumpAnalyzer-tests-" + Guid.NewGuid().ToString("N"));
            public ApplicationDbContext Db => Get<ApplicationDbContext>();

            private Fixture(SqliteConnection connection)
            {
                _connection = connection;
                Directory.CreateDirectory(DirectoryPath);
                Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DumpPath"] = DirectoryPath,
                    ["BuildTypes:beta"] = "2",
                    ["BuildTypes:"] = "4",
                    ["BuildTypes:emptybuildtype"] = "release",
                    ["BuildTypeRegex"] = "Build: (.*)",
                    ["LogFileAnalyzer:LogIssueTypes:Assertion:Regex"] = "ASSERT: (.*)"
                }).Build();
                BuildTypes.Initialize(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Configuration);
                MinVersions.Initialize(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Configuration);
                var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddHttpClient();
                services.AddSingleton(Configuration);
                services.AddScoped<ApplicationDbContext>(_ => new TrackingContext(options, Lifetime));
                services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
                services.AddSingleton<DumpAnalysisQueue>();
                services.AddScoped<DumpFileService>();
                services.AddScoped<DumpCleanupService>();
                services.AddScoped<CallstackService>();
                services.AddScoped<AnalysisResultService>();
                services.AddScoped<CallstackQueryService>();
                services.AddScoped<HomePageService>();
                _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
                _scope = _provider.CreateAsyncScope();
            }

            public static async Task<Fixture> Create()
            {
                var connection = new SqliteConnection("Data Source=:memory:");
                await connection.OpenAsync();
                var fixture = new Fixture(connection);
                await fixture.Db.Database.EnsureCreatedAsync();
                return fixture;
            }

            public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();
            public string FilePath(string fileName) => Path.Combine(DirectoryPath, fileName);
            public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();
            public ApiController CreateApi() => new(Configuration, Get<ILogger<ApiController>>(), Get<DumpFileService>(), Get<CallstackService>(), Get<DumpAnalysisQueue>());

            public DumpCallstack Callstack(string application, string version, string? fileName = null)
            {
                var callstack = new DumpCallstack { ApplicationName = application, ApplicationVersion = version };
                if (fileName != null)
                    callstack.DumpInfos.Add(new DumpFileInfo { FilePath = fileName, FileSize = 100, UploadDate = DateTime.Now, DumpTime = DateTime.Now });
                return callstack;
            }

            public async ValueTask DisposeAsync()
            {
                await _scope.DisposeAsync();
                await _provider.DisposeAsync();
                await _connection.DisposeAsync();
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
