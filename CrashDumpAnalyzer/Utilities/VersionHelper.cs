using System.Reflection;

namespace CrashDumpAnalyzer.Utilities
{
    public class VersionHelper
    {
        public static string GetAssemblyVersion()
        {
            var infoVersion = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false).FirstOrDefault() as AssemblyInformationalVersionAttribute;
            return infoVersion?.InformationalVersion ?? string.Empty;
        }
    }
}
