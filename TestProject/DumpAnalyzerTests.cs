using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace CrashDumpAnalyzer.Tests
{
    [TestClass]
    public class DumpAnalyzerTests
    {
        private const string PrimaryOutput = """
            PROCESS_NAME: App.exe
            ExceptionCode: c0000005 (Access violation)
            Key  : WER.Process.Version
            Value: 1.2.3.4

            STACK_TEXT:
            00000001 00000002 : 00000003 : App!Crash+0x12
            00000002 00000003 : 00000004 : App!Worker+0x34
            00000003 00000004 : 00000005 : App!Main+0x56

            PEB at 00000000
                CommandLine: 'App.exe --test'
                COMPUTERNAME=workstation
                USERDOMAIN=company

            """;

        [TestMethod]
        public void ParseOutput_PreservesExistingMetadataAndStack()
        {
            var data = Analyzer().ParseOutput(PrimaryOutput);

            Assert.AreEqual("App.exe", data.processName);
            Assert.AreEqual("c0000005 (Access violation)", data.exceptionCode);
            Assert.AreEqual("1.2.3.4", data.version);
            Assert.AreEqual("'App.exe --test'", data.commandLine);
            Assert.AreEqual("workstation", data.computerName);
            Assert.AreEqual("company", data.domain);
            Assert.AreEqual("App!Crash+0x12\nApp!Worker+0x34\nApp!Main+0x56\n\n", data.callstackString);
            Assert.AreEqual("App!Crash\nApp!Worker\nApp!Main", data.cleanCallstackString);
            Assert.AreEqual(string.Empty, data.diagnostics);
        }

        [TestMethod]
        public void ParseOutput_IsolatesDiagnosticsFromGroupingAndMetadata()
        {
            var analyzer = Analyzer();
            var expected = analyzer.ParseOutput(PrimaryOutput);
            var report = """
                === System and process ===
                Windows 11 Version 26100
                Effective machine: x64 (AMD64)
                Process Uptime: 0 days 0:12:34.000
                === Exception record and registers ===
                ExceptionAddress: 0000000000001234
                ExceptionCode: 80000003
                Parameter[0]: 0000000000000001
                === All thread stacks ===
                Child-SP          RetAddr           Call Site
                00000001 00000002 Other!Wait+0xab
                STACK_TEXT:
                PROCESS_NAME: Wrong.exe
                CommandLine: 'Wrong.exe'
                PEB at 1234
                COMPUTERNAME=wrong
                USERDOMAIN=wrong
                === Thread CPU times and elapsed times ===
                 User Mode Time
                  0:1234 0:00:00.001
                === Virtual memory summary ===
                MEM_COMMIT 1000
                === Loaded modules and symbol status ===
                start             end                 module name
                00000001 00000002 Other
                    Product version: 9.9.9.9
                """;
            // CDB can echo the entire command line before printing the marker itself.
            var output = "0:000> .echo === CDA_DIAGNOSTICS_BEGIN ===; ~* kL; .echo === CDA_DIAGNOSTICS_END ===\n" +
                PrimaryOutput + "\n=== CDA_DIAGNOSTICS_BEGIN ===\n" + report + "\n=== CDA_DIAGNOSTICS_END ===\nquit:\n";

            var actual = analyzer.ParseOutput(output.ReplaceLineEndings("\r\n"));

            Assert.AreEqual(expected.callstackString, actual.callstackString);
            Assert.AreEqual(expected.cleanCallstackString, actual.cleanCallstackString);
            Assert.AreEqual(expected.processName, actual.processName);
            Assert.AreEqual(expected.exceptionCode, actual.exceptionCode);
            Assert.AreEqual(expected.version, actual.version);
            Assert.AreEqual(expected.commandLine, actual.commandLine);
            Assert.AreEqual(expected.environment, actual.environment);
            Assert.AreEqual(expected.computerName, actual.computerName);
            Assert.AreEqual(expected.domain, actual.domain);
            Assert.AreEqual(report.ReplaceLineEndings(Environment.NewLine), actual.diagnostics);
        }

        [TestMethod]
        public void ParseOutput_PreservesDiagnosticErrorsAndPartialReports()
        {
            var report = "Unable to get exception context\nUnable to get thread times\nMemory access error at 00000000";
            var output = PrimaryOutput + "\n=== CDA_DIAGNOSTICS_BEGIN ===\n" + report;

            var actual = Analyzer().ParseOutput(output);

            Assert.AreEqual(report.ReplaceLineEndings(Environment.NewLine), actual.diagnostics);
            Assert.AreEqual("App!Crash\nApp!Worker\nApp!Main", actual.cleanCallstackString);
        }

        [TestMethod]
        public void ParseOutput_DiagnosticThreadStacksDoNotReplaceFallbackStack()
        {
            var output = """
                PROCESS_NAME: App.exe
                Child-SP          RetAddr           Call Site
                00000001 00000002 App!Crash+0x12
                00000002 00000003 App!Worker+0x34
                00000003 00000004 App!Main+0x56
                00000004 00000005 App!Start+0x78
                === CDA_DIAGNOSTICS_BEGIN ===
                Child-SP          RetAddr           Call Site
                00000005 00000006 Other!Wait+0xab
                === CDA_DIAGNOSTICS_END ===
                quit:
                """;

            var data = Analyzer().ParseOutput(output);

            Assert.AreEqual("App!Crash\nApp!Worker\nApp!Main\nApp!Start", data.cleanCallstackString);
            StringAssert.Contains(data.diagnostics, "Other!Wait+0xab");
        }

        [TestMethod]
        public async Task DiagnosticsMigration_PreservesExistingDumpMetadataAndDefaultsReportToEmpty()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            await using var db = new ApplicationDbContext(options);
            await db.Database.MigrateAsync();
            var callstack = new DumpCallstack { ApplicationName = "App", CleanCallstack = "App!Crash" };
            callstack.DumpInfos.Add(new DumpFileInfo { FilePath = "existing.dmp", CommandLine = "App --option", Diagnostics = "report" });
            db.Add(callstack);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260418084919_DumpTimeOffset");

            await migrator.MigrateAsync();

            Assert.IsFalse(db.Database.HasPendingModelChanges());
            var stored = await db.Set<DumpFileInfo>().Include(d => d.DumpCallstack).SingleAsync();
            Assert.AreEqual("existing.dmp", stored.FilePath);
            Assert.AreEqual("App --option", stored.CommandLine);
            Assert.AreEqual("App!Crash", stored.DumpCallstack?.CleanCallstack);
            Assert.AreEqual(string.Empty, stored.Diagnostics);
        }

        [TestMethod]
        [TestCategory("Debugger")]
        [DataRow(false)]
        [DataRow(true)]
        public async Task AnalyzeDump_CollectsDiagnosticsWithCdbAndPathsContainingSpaces(bool useSymbols)
        {
            var cdb = Environment.GetEnvironmentVariable("CDB_TEST_EXE");
            if (string.IsNullOrEmpty(cdb))
                Assert.Inconclusive("Set CDB_TEST_EXE to run the optional debugger integration test.");
            var symbolPath = useSymbols ? Environment.GetEnvironmentVariable("CDB_TEST_SYMBOL_PATH") : string.Empty;
            if (symbolPath == null)
                Assert.Inconclusive("Set CDB_TEST_SYMBOL_PATH to also test the memory summary with OS symbols.");
            Assert.IsTrue(File.Exists(cdb), "CDB_TEST_EXE must point to cdb.exe.");
            var directory = Path.Combine(Path.GetTempPath(), "CDA debugger test " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var dumpPath = Path.Combine(directory, "test dump.dmp");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = cdb;
                    process.StartInfo.ArgumentList.Add("-c");
                    process.StartInfo.ArgumentList.Add($".dump /ma \"{dumpPath}\"; q");
                    process.StartInfo.ArgumentList.Add(Path.Combine(Environment.SystemDirectory, "where.exe"));
                    process.StartInfo.EnvironmentVariables["_NT_SYMBOL_PATH"] = string.Empty;
                    process.Start();
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token);
                    }
                    finally
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            await process.WaitForExitAsync(CancellationToken.None);
                        }
                    }
                    Assert.AreEqual(0, process.ExitCode);
                }
                Assert.IsTrue(File.Exists(dumpPath));

                var data = await new DumpAnalyzer(cdb, symbolPath, NullLogger.Instance).AnalyzeDump(dumpPath, timeout.Token);

                StringAssert.Contains(data.diagnostics, "=== System and process ===");
                StringAssert.Contains(data.diagnostics, "Effective machine:");
                StringAssert.Contains(data.diagnostics, "Process Uptime:");
                StringAssert.Contains(data.diagnostics, "=== Exception record and registers ===");
                StringAssert.Contains(data.diagnostics, "=== All thread stacks ===");
                StringAssert.Contains(data.diagnostics, "where");
                StringAssert.Contains(data.diagnostics, "=== Thread CPU times and elapsed times ===");
                StringAssert.Contains(data.diagnostics, "User Mode Time");
                StringAssert.Contains(data.diagnostics, "=== Virtual memory summary ===");
                if (useSymbols)
                    StringAssert.Contains(data.diagnostics, "MEM_COMMIT");
                else
                    StringAssert.Contains(data.diagnostics, "No symbols for ntdll. Cannot continue.");
                StringAssert.Contains(data.diagnostics, "=== Loaded modules and symbol status ===");
                Assert.IsFalse(data.diagnostics.Contains("CDA_DIAGNOSTICS_END", StringComparison.Ordinal));
                Assert.IsFalse(data.callstackString.Contains("===", StringComparison.Ordinal));
                Assert.IsFalse(data.cleanCallstackString.Contains("===", StringComparison.Ordinal));
            }
            finally
            {
                File.Delete(dumpPath);
                Directory.Delete(directory);
            }
        }

        private static DumpAnalyzer Analyzer() => new(string.Empty, string.Empty, NullLogger.Instance);
    }
}
