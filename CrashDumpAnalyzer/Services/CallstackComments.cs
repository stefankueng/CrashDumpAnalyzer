using CrashDumpAnalyzer.Models;

namespace CrashDumpAnalyzer.Services
{
    internal static class CallstackComments
    {
        public const string VersionTooOld = "Version is too old, it won't be analyzed anymore.";

        public static void Update(DumpCallstack callstack, string comment)
        {
            if (!callstack.Deleted)
            {
                if (string.IsNullOrEmpty(callstack.Comment))
                    callstack.Comment = comment;
                else if (!callstack.Comment.Contains(VersionTooOld, StringComparison.InvariantCultureIgnoreCase))
                    callstack.Comment = callstack.Comment + "\n" + comment;
            }
        }

        public static void Remove(DumpCallstack callstack, string comment)
        {
            if (callstack == null || string.IsNullOrEmpty(callstack.Comment))
                return;

            if (!callstack.Comment.Contains(comment, StringComparison.InvariantCultureIgnoreCase))
                return;

            var cleanedLines = callstack.Comment
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.Contains(comment, StringComparison.InvariantCultureIgnoreCase))
                .ToArray();

            callstack.Comment = string.Join(Environment.NewLine, cleanedLines);
        }
    }
}
