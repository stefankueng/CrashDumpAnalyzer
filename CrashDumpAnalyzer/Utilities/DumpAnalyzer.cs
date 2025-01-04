using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CrashDumpAnalyzer.Utilities
{
    public enum DumpContext
    {
        None,
        StackText,
        AlternateStackText,
        AlternateStackText2,
        Version,
        Modules,
        MainModule,
        Peb,
        VersionResource,
        VersionResourceHeader
    }
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
        public string versionResource = string.Empty;
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
        public async Task<DumpData> AnalyzeDump(string dumpFilePath, CancellationToken token)
        {
            DumpData dumpData = new DumpData();

            var tmpFilePath = Path.GetTempFileName();
            using Process process = new();
            process.StartInfo.FileName = _cdbExe;
            process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_cdbExe);
            process.StartInfo.Arguments = $"-netsyms:yes -lines -z {dumpFilePath} -c \".logopen /u {tmpFilePath}; !analyze -v; lm lv; .ecxr; kL; .cxr; kL; !peb; q\"";
            process.StartInfo.EnvironmentVariables["_NT_SYMBOL_PATH"] = _symbolPath;
            process.StartInfo.EnvironmentVariables["_NT_SOURCE_PATH "] = "srv\\*";
            process.Start();

            //string output = await process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);

            // read the unicode text in tmpFilePath into a string
            string output = await File.ReadAllTextAsync(tmpFilePath, Encoding.Unicode, token);
            // delete the temp file
            File.Delete(tmpFilePath);

            // go through the output and find the important bits
            _logger.LogInformation(output);
            DumpContext context = DumpContext.None;
            string callstackString = string.Empty;
            string alternateCallstackString = string.Empty;
            string alternateCallstackString2 = string.Empty;
            string processName = string.Empty;
            string exceptionCode = string.Empty;
            string version = string.Empty;
            string computerName = string.Empty;
            string domain = string.Empty;
            string environment = string.Empty;
            string versionResource = string.Empty;
            DateTime dumpTime = DateTime.Now;
            int childSpCount = 0;
            foreach (var lineStringOrig in output.Split(["\n"], StringSplitOptions.None))
            {
                // count whitespaces at start of lineString
                var whitespaceCount = lineStringOrig.Length - lineStringOrig.TrimStart().Length;
                var lineString = lineStringOrig.Trim();
                switch (context)
                {
                    case DumpContext.StackText:
                    {
                        // the rightmost part is the 'interesting' part for us
                        var lineParts = lineString.Split([" : "], StringSplitOptions.TrimEntries);
                        switch (lineParts.Length)
                        {
                            case 3:
                                callstackString += lineParts[2] + "\n";
                                break;
                            case 2:
                                callstackString += lineParts[1] + "\n";
                                break;
                            case 1:
                                callstackString += lineParts[0].Substring(lineParts[0].LastIndexOf(' ') + 1) + "\n";
                                break;
                            default:
                                break;
                        }
                    }
                    break;
                    case DumpContext.AlternateStackText:
                    {
                        if (lineString.Contains("quit:") || lineString.Contains("PEB at"))
                        {
                            context = DumpContext.None;
                        }
                        else
                        {
                            // the rightmost part is the 'interesting' part for us
                            alternateCallstackString += lineString.Substring(lineString.LastIndexOf(' ') + 1) + "\n";
                        }
                    }
                    break;
                    case DumpContext.AlternateStackText2:
                    {
                        if (lineString.Contains("quit:") || lineString.Contains("PEB at"))
                        {
                            context = DumpContext.None;
                        }
                        else
                        {
                            // the rightmost part is the 'interesting' part for us
                            alternateCallstackString2 += lineString.Substring(lineString.LastIndexOf(' ') + 1) + "\n";
                        }
                    }
                    break;
                    case DumpContext.Version:
                    {
                        context = DumpContext.None;
                        version = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                    }
                    break;
                    case DumpContext.Modules:
                    {
                        if (lineString.Contains(processName) || (lineString.Length > 0 && processName == "unknown"))
                        {
                            context = DumpContext.MainModule;
                        }
                    }
                    break;
                    case DumpContext.MainModule:
                    {
                        if (lineString.Contains("Image name:") && processName == "unknown" && lineString.Contains(".exe"))
                        {
                            processName = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                        }
                        if (lineString.Contains("Product version:") && processName != "unknown")
                        {
                            context = DumpContext.None;
                            version = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                        }
                    }
                    break;
                    case DumpContext.Peb:
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
                            context = DumpContext.None;
                        else
                            environment += lineString + "\n";
                    }
                    break;
                    case DumpContext.VersionResourceHeader:
                        if (lineString.Contains("Information from resource tables:"))
                        {
                            context = DumpContext.VersionResource;
                            versionResource = string.Empty;
                        }
                        break;
                    case DumpContext.VersionResource:
                    {
                        if (whitespaceCount > 0)
                        {
                            versionResource += lineString + "\n";
                        }
                        else
                        {
                            context = DumpContext.None;
                        }
                    }
                    break;
                }
                if (lineString.Contains("Could not open dump file"))
                {
                    callstackString = lineString;
                    context = DumpContext.StackText;
                }
                if (lineString.Contains("STACK_TEXT:"))
                {
                    context = DumpContext.StackText;
                }
                if (lineString.Contains("---------"))
                {
                    context = DumpContext.Modules;
                }
                if (lineString.Contains("Key  : WER.Process.Version"))
                {
                    context = DumpContext.Version;
                }
                if (lineString.Contains("PROCESS_NAME:"))
                {
                    context = DumpContext.None;
                    processName = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                }
                if (lineString.Contains("ExceptionCode:"))
                {
                    context = DumpContext.None;
                    exceptionCode = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                }
                if (lineString.Contains($"Loaded symbol image file: {processName}"))
                {
                    context = DumpContext.VersionResourceHeader;
                }
                if (lineString.Contains("Debug session time:"))
                {
                    context = DumpContext.None;
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
                    if (childSpCount == 0)
                        context = DumpContext.AlternateStackText;
                    else
                        context = DumpContext.AlternateStackText2;
                    ++childSpCount;
                }
                if (lineString.Contains("PEB at"))
                {
                    context = DumpContext.Peb;
                }
                if (lineString.Contains(":quit"))
                {
                    context = DumpContext.None;
                }
                if (lineString.Length <= 1 && context != DumpContext.None && context != DumpContext.Modules)
                    context = DumpContext.None;
            }
            var csLength = callstackString.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            var alternateCsLength = alternateCallstackString.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            if (csLength < 3 && alternateCsLength > 3)
            {
                callstackString = alternateCallstackString;
            }
            var alternateCsLength2 = alternateCallstackString2.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            if (csLength < 3 && alternateCsLength2 > 3)
            {
                callstackString = alternateCallstackString2;
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
            dumpData.versionResource = versionResource;
            dumpData.dumpTime = dumpTime;

            return dumpData;
        }
    }
}
