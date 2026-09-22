using System.Diagnostics;
using System.Net.Http.Headers;

namespace MicrosoftRewardsApp.Services;

public static class SelfTest
{
    public static async Task RunAsync()
    {
        var root = RewardsManager.ProjectPaths.Root;
        var node = Path.Combine(root, "tools", "node", "node.exe");
        var api = Path.Combine(root, "scripts", "api", "server.js");
        var browserMarker = Path.Combine(root, "node_modules", ".local-browsers");

        Assert(File.Exists(Path.Combine(root, "package.json")), "desktop package.json");
        Assert(File.Exists(Path.Combine(root, "dist", "index.js")), "built Rewards bot");
        Assert(File.Exists(api), "Control API server");
        Assert(File.Exists(node), "bundled Node runtime");
        Assert(Directory.Exists(Path.Combine(root, "node_modules")), "bundled node_modules");
        Assert(Directory.Exists(browserMarker), "bundled Patchright browser");

        var token = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
        var psi = new ProcessStartInfo
        {
            FileName = node,
            Arguments = "\"" + api + "\"",
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        psi.Environment["API_HOST"] = "127.0.0.1";
        psi.Environment["API_PORT"] = "3010";
        psi.Environment["API_TOKEN"] = token;
        psi.Environment["PLAYWRIGHT_BROWSERS_PATH"] = "0";
        psi.Environment["PATCHRIGHT_BROWSERS_PATH"] = "0";

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Control API process could not be started.");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var ready = false;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:3010/health");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                try
                {
                    using var response = await http.SendAsync(request);
                    if (response.IsSuccessStatusCode) { ready = true; break; }
                }
                catch { }
                await Task.Delay(500);
            }
            Assert(ready, "authenticated Control API health");
            Console.WriteLine("SELF_TEST_PASS desktop-package");
            Console.WriteLine("SELF_TEST_PASS control-api");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Self-test failed: " + name);
    }
}