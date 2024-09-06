using CrashDumpAnalyzer.Controllers;
using CrashDumpAnalyzer.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CrashDumpAnalyzer.Utilities
{
    public class DumpData
    {
        public string callstackString = string.Empty;
        public string cleanCallstackString = string.Empty;
        public string processName = string.Empty;
        public string exceptionCode = string.Empty;
        public string version = string.Empty;
        public string computerName = string.Empty;
        public string domain = string.Empty;
        public string environment = string.Empty;
        public DateTime dumpTime = DateTime.Now;
    }
    public class DumpAnalyzer
    {
        private readonly Regex _cleanCallstackRegex;
        private readonly string _cdbExe;
        private readonly string _symbolPath;
        private readonly ILogger _logger;

        public DumpAnalyzer(string cdbExe, string symbolPath, ILogger logger)
        {
            _cdbExe = cdbExe;
            _symbolPath = symbolPath;
            _logger = logger;
            string pattern = @"^((.*)\+0x([a-f0-9]+)|0x.*)$";
            RegexOptions options = RegexOptions.Multiline;
            _cleanCallstackRegex = new Regex(pattern, options);
        }
        private string RemoveEmptyLines(string lines)
        {
            return Regex.Replace(lines, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline).TrimEnd();
        }
        public async Task<DumpData> AnalyzeDump(string dumpFilePath,  CancellationToken token)
        {
            DumpData dumpData = new DumpData();

            using Process process = new();
            process.StartInfo.FileName = _cdbExe;
            process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_cdbExe);
            process.StartInfo.Arguments = $"-netsyms:yes -lines -z {dumpFilePath} -c \"!analyze -v; lm lv; .ecxr; kL; !peb; q\"";
            process.StartInfo.EnvironmentVariables["_NT_SYMBOL_PATH"] = _symbolPath;
            process.StartInfo.EnvironmentVariables["_NT_SOURCE_PATH "] = "srv\\*";
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();

            StreamReader sr = process.StandardOutput;
            string output = await sr.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);

            // go through the output and find the important bits
            _logger.LogInformation(output);
            string context = string.Empty;
            string callstackString = string.Empty;
            string alternateCallstackString = string.Empty;
            string processName = string.Empty;
            string exceptionCode = string.Empty;
            string version = string.Empty;
            string computerName = string.Empty;
            string domain = string.Empty;
            string environment = string.Empty;
            DateTime dumpTime = DateTime.Now;
            foreach (var lineString in output.Split(["\n"], StringSplitOptions.TrimEntries))
            {
                if (context == "STACK_TEXT")
                {
                    // the rightmost part is the 'interesting' part for us
                    var lineParts = lineString.Split([" : "], StringSplitOptions.TrimEntries);
                    if (lineParts.Length == 3)
                    {
                        callstackString += lineParts[2] + "\n";
                    }
                    if (lineParts.Length == 2)
                    {
                        callstackString += lineParts[1] + "\n";
                    }
                    if (lineParts.Length == 1)
                    {
                        callstackString += lineParts[0].Substring(lineParts[0].LastIndexOf(' ') + 1) + "\n";
                    }
                }
                if (context == "ALTERNATE_STACK_TEXT")
                {
                    if (lineString.Contains("quit:") || lineString.Contains("PEB at"))
                    {
                        context = "";
                    }
                    else
                    {
                        // the rightmost part is the 'interesting' part for us
                        alternateCallstackString += lineString.Substring(lineString.LastIndexOf(' ') + 1) + "\n";
                    }
                }
                if (context == "VERSION")
                {
                    context = string.Empty;
                    version = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                }
                if (context == "MODULES")
                {
                    if (lineString.Contains(processName) || (lineString.Length > 0 && processName == "unknown"))
                    {
                        context = "MAIN_MODULE";
                    }
                }
                if (context == "MAIN_MODULE")
                {
                    if (lineString.Contains("Image name:") && processName == "unknown" && lineString.Contains(".exe"))
                    {
                        processName = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                    }
                    if (lineString.Contains("Product version:") && processName != "unknown")
                    {
                        context = string.Empty;
                        version = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                    }
                }
                if (context == "PEB")
                {
                    if (lineString.Contains("COMPUTERNAME="))
                    {
                        var parts = lineString.Split(["="], StringSplitOptions.TrimEntries);
                        if (parts.Length == 2)
                        {
                            computerName = parts[1];
                        }
                    }
                    if (lineString.Contains("USERDOMAIN="))
                    {
                        // USERDOMAIN
                        var parts = lineString.Split(["="], StringSplitOptions.TrimEntries);
                        if (parts.Length == 2)
                        {
                            domain = parts[1];
                        }
                    }
                    if (lineString.Contains(":quit"))
                        context = string.Empty;
                    else
                        environment += lineString + "\n";
                }
                if (lineString.Contains("Could not open dump file"))
                {
                    callstackString = lineString;
                    context = "STACK_TEXT";
                }
                if (lineString.Contains("STACK_TEXT:"))
                {
                    context = "STACK_TEXT";
                }
                if (lineString.Contains("---------"))
                {
                    context = "MODULES";
                }
                if (lineString.Contains("Key  : WER.Process.Version"))
                {
                    context = "VERSION";
                }
                if (lineString.Contains("PROCESS_NAME:"))
                {
                    context = string.Empty;
                    processName = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                }
                if (lineString.Contains("ExceptionCode:"))
                {
                    context = string.Empty;
                    exceptionCode = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                }
                if (lineString.Contains("Debug session time:"))
                {
                    context = string.Empty;
                    var dateString = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                    dateString = dateString.Replace("UTC + ", "UTC +");
                    dateString = dateString.Replace("UTC - ", "UTC -");
                    dateString = dateString.Replace("  ", " ");
                    try
                    {
                        var dt = DateTime.ParseExact(dateString, "ddd MMM d HH:mm:ss.fff yyyy (UTC z:00)", new CultureInfo("en-us"));
                        dumpTime = dt;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing dump time");
                    }
                }
                if (lineString.Contains("Child-SP"))
                {
                    context = "ALTERNATE_STACK_TEXT";
                }
                if (lineString.Contains("PEB at"))
                {
                    context = "PEB";
                }
                if (lineString.Contains(":quit"))
                {
                    context = string.Empty;
                }
                if (lineString.Length <= 1 && context.Length > 0 && context != "MODULES")
                    context = string.Empty;

            }
            var csLength = callstackString.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            var alternateCsLength = alternateCallstackString.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            if (csLength < 3 && alternateCsLength > 3)
            {
                callstackString = alternateCallstackString;
            }
            // filter out offsets and lines with no symbols
            string cleanCallstackString = _cleanCallstackRegex.Replace(callstackString, @"$2");
            cleanCallstackString = RemoveEmptyLines(cleanCallstackString);
            if (string.IsNullOrEmpty(cleanCallstackString))
            {
                callstackString = alternateCallstackString;
                cleanCallstackString = _cleanCallstackRegex.Replace(callstackString, @"$2");
                cleanCallstackString = RemoveEmptyLines(cleanCallstackString);
            }
            dumpData.domain = domain;
            dumpData.version = version;
            dumpData.environment = environment;
            dumpData.computerName = computerName;
            dumpData.exceptionCode = exceptionCode;
            dumpData.processName = processName;
            dumpData.callstackString = callstackString;
            dumpData.cleanCallstackString = cleanCallstackString;
            dumpData.dumpTime = dumpTime;

            return dumpData;
        }
    }
}
