namespace CrashDumpAnalyzer.Services
{
    public sealed record DumpDownload(Stream Content, string ContentType, string FileName);
}
