using System.ComponentModel;
using System.Diagnostics;
using System.Net;

namespace CrashDumpAnalyzer.Services
{
    public static class SetupBrowser
    {
        public static void Open(IEnumerable<string> addresses, ILogger logger)
        {
            var url = GetSetupUrl(addresses);
            if (url is null)
            {
                logger.LogWarning("Setup is required, but no HTTP listener accepts localhost connections. Configure a loopback or wildcard listener and open /Setup on the server.");
                return;
            }

            logger.LogInformation("Configure CrashDumpAnalyzer at {SetupUrl}", url);
            if (!Environment.UserInteractive)
            {
                logger.LogInformation("No interactive desktop is available. Open {SetupUrl} in a browser on the server to complete setup.", url);
                return;
            }

            try
            {
                using var browser = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                logger.LogWarning(ex, "Could not open the setup browser. Open {SetupUrl} manually on the server.", url);
            }
        }

        public static Uri? GetSetupUrl(IEnumerable<string> addresses)
        {
            foreach (var address in addresses.OrderByDescending(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                var normalized = address.Replace("://*:", "://localhost:").Replace("://+:", "://localhost:");
                if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    || uri.Port == 0 || !string.IsNullOrEmpty(uri.UserInfo))
                    continue;

                var host = uri.Host.Trim('[', ']');
                if (IPAddress.TryParse(host, out var ip))
                {
                    if (ip.Equals(IPAddress.Any))
                        host = "127.0.0.1";
                    else if (ip.Equals(IPAddress.IPv6Any))
                        host = "::1";
                    else if (!IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip))
                        continue;
                }
                else
                {
                    // Kestrel treats non-IP host names as wildcard bindings.
                    host = "localhost";
                }

                return new UriBuilder(uri) { Host = host, Path = "/Setup", Query = "", Fragment = "" }.Uri;
            }
            return null;
        }
    }
}
