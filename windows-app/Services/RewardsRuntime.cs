using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using MicrosoftRewardsApp.Models;

namespace MicrosoftRewardsApp.Services;

public sealed class RewardsRuntime : IDisposable
{
    public string BaseDirectory { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public string BotPath { get; }
    public string DashboardPath { get; }
    public string NodePath { get; }
    public string BrowserPath { get; }

    private Process? _api;
    private Process? _dashboard;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public RewardsRuntime()
    {
        var packagedBot = Path.Combine(BaseDirectory, "bot");
        var packagedDashboard = Path.Combine(BaseDirectory, "dashboard");

        BotPath = Directory.Exists(packagedBot)
            ? packagedBot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "MicrosoftRewardsFull", "Microsoft-Rewards-Script");

        DashboardPath = Directory.Exists(packagedDashboard)
            ? packagedDashboard
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "MicrosoftRewardsFull", "rewards-dashboard", "rewards-dashboard");

        var packagedNode = Path.Combine(BaseDirectory, "runtime", "node", "node.exe");
        var legacyNode = Path.Combine(BaseDirectory, "runtime", "node.exe");

        NodePath = File.Exists(packagedNode)
            ? packagedNode
            : File.Exists(legacyNode)
                ? legacyNode
                : FindInstalledNode();

        BrowserPath = Path.Combine(BaseDirectory, "runtime", "browser");
    }

    private static string FindInstalledNode()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe")
        };

        return candidates.FirstOrDefault(File.Exists) ?? "";
    }

    public bool ApiRunning => _api is { HasExited: false };
    public bool DashboardRunning => _dashboard is { HasExited: false };

    public async Task EnsureRunningAsync(AppState state, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(NodePath))
            throw new InvalidOperationException("Node runtime is missing from the application package.");

        Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "MicrosoftRewardsApp"));

        if (!File.Exists(Path.Combine(BotPath, "config.json")) &&
            File.Exists(Path.Combine(BotPath, "config.example.json")))
        {
            File.Copy(Path.Combine(BotPath, "config.example.json"),
                Path.Combine(BotPath, "config.json"));
        }

        if (!ApiRunning)
        {
            _api = Start(
                Path.Combine(BotPath, "scripts", "api", "server.js"),
                BotPath,
                state,
                isDashboard: false);

            await WaitForApiAsync(state.ApiToken, cancellationToken);
        }

        if (!DashboardRunning)
        {
            _dashboard = Start(
                "server.js",
                DashboardPath,
                state,
                isDashboard: true);
        }
    }

    private Process Start(
        string script,
        string workingDirectory,
        AppState state,
        bool isDashboard)
    {
        if (!File.Exists(script) && !isDashboard)
            throw new FileNotFoundException("Rewards API server not found.", script);

        if (!Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException(workingDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = NodePath,
            Arguments = $""{script}"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        RewardsEnvironment.Apply(
            psi,
            state,
            BotPath,
            BrowserPath);

        if (isDashboard)
        {
            psi.Environment["CONTROL_API_URL"] = "http://127.0.0.1:3010";
            psi.Environment["CONTROL_API_TOKEN"] = state.ApiToken;
            psi.Environment["TZ"] = "America/Los_Angeles";
            psi.Environment["DASHBOARD_TITLE"] = "Microsoft Rewards";
        }

        return Process.Start(psi)
            ?? throw new InvalidOperationException("Unable to start application process.");
    }

    private async Task WaitForApiAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:3010/health");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await _http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Retry while the API boots.
            }

            await Task.Delay(500, cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        throw new TimeoutException("Rewards Control API did not become ready.");
    }

    public async Task<string> StartRewardsAsync(AppState state, CancellationToken cancellationToken = default)
        => await SendControlAsync(HttpMethod.Post, "/start", state.ApiToken, cancellationToken);

    public async Task<string> StopRewardsAsync(AppState state, CancellationToken cancellationToken = default)
        => await SendControlAsync(HttpMethod.Post, "/stop", state.ApiToken, cancellationToken);

    private async Task<string> SendControlAsync(
        HttpMethod method,
        string path,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method,
            "http://127.0.0.1:3010" + path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(body);

        return body;
    }

    public void Restart()
    {
        Kill(ref _dashboard);
        Kill(ref _api);
    }

    private static void Kill(ref Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
        finally
        {
            process.Dispose();
            process = null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        Kill(ref _dashboard);
        Kill(ref _api);
    }
}