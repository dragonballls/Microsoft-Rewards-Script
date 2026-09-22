using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MicrosoftRewardsApp.Models;

namespace MicrosoftRewardsApp.Services;

public sealed class RewardsRuntime : IDisposable
{
    public string BaseDirectory { get; } =
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public string AppDataRoot { get; }

    public string BotPath { get; }
    public string DashboardPath { get; }
    public string NodePath { get; }
    public string BrowserPath { get; }

    private bool UsesSharedPackagedRoot =>
        File.Exists(Path.Combine(BaseDirectory, "package.json")) &&
        File.Exists(Path.Combine(BaseDirectory, "dist", "index.js")) &&
        File.Exists(Path.Combine(BaseDirectory, "scripts", "api", "server.js"));

    private Process? _api;
    private Process? _dashboard;
    private bool _ownsApi;
    private bool _ownsDashboard;
    private bool _externalApi;
    private bool _externalDashboard;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public RewardsRuntime()
    {
        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MicrosoftRewardsApp",
            "runtime");

        AppDataRoot = localRoot;
        BotPath = UsesSharedPackagedRoot
            ? BaseDirectory
            : Path.Combine(localRoot, "bot");
        DashboardPath = UsesSharedPackagedRoot
            ? Path.Combine(BaseDirectory, "dashboard")
            : Path.Combine(localRoot, "dashboard");

        var packagedNode = Path.Combine(
            BaseDirectory,
            "runtime",
            "node",
            "node.exe");

        var legacyNode = Path.Combine(
            BaseDirectory,
            "runtime",
            "node.exe");

        NodePath = File.Exists(packagedNode)
            ? packagedNode
            : File.Exists(legacyNode)
                ? legacyNode
                : FindInstalledNode();

        BrowserPath = Path.Combine(
            BaseDirectory,
            "runtime",
            "browser");
    }


    private static string FindInstalledNode()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "nodejs", "node.exe"),

            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "nodejs", "node.exe")
        };

        return candidates.FirstOrDefault(File.Exists) ?? "";
    }

    public bool ApiRunning =>
        _api is { HasExited: false } || _externalApi;

    public bool DashboardRunning =>
        _dashboard is { HasExited: false } || _externalDashboard;

    public async Task EnsureRunningAsync(
        AppState state,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(NodePath))
            throw new InvalidOperationException(
                "Node runtime is missing from the application package.");

        await EnsureWritableRuntimeAsync(cancellationToken);

        if (_api?.HasExited == true)
        {
            _api.Dispose();
            _api = null;
            _ownsApi = false;
        }

        if (_dashboard?.HasExited == true)
        {
            _dashboard.Dispose();
            _dashboard = null;
            _ownsDashboard = false;
        }

        var config = Path.Combine(BotPath, "config.json");
        var example = Path.Combine(BotPath, "config.example.json");

        if (!File.Exists(config) && File.Exists(example))
            File.Copy(example, config);

        if (_externalApi &&
            !await IsApiAvailableAsync(
                state.ApiToken,
                cancellationToken))
        {
            _externalApi = false;
        }

        if (!ApiRunning)
        {
            if (await IsApiAvailableAsync(
                    state.ApiToken,
                    cancellationToken))
            {
                _externalApi = true;
                _ownsApi = false;
            }
            else
            {
                _api = Start(
                    Path.Combine(
                        BotPath,
                        "scripts",
                        "api",
                        "server.js"),
                    BotPath,
                    state,
                    dashboard: false);

                _ownsApi = true;
                await WaitForApiAsync(
                    state.ApiToken,
                    cancellationToken);
            }
        }

        if (_externalDashboard &&
            !await IsDashboardAvailableAsync(cancellationToken))
        {
            _externalDashboard = false;
        }

        if (!DashboardRunning)
        {
            if (await IsDashboardAvailableAsync(cancellationToken))
            {
                _externalDashboard = true;
                _ownsDashboard = false;
            }
            else
            {
                _dashboard = Start(
                    "server.js",
                    DashboardPath,
                    state,
                    dashboard: true);

                _ownsDashboard = true;
                await WaitForDashboardAsync(cancellationToken);
            }
        }
    }

    private async Task EnsureWritableRuntimeAsync(
        CancellationToken cancellationToken)
    {
        if (UsesSharedPackagedRoot)
        {
            if (!Directory.Exists(DashboardPath))
                throw new DirectoryNotFoundException(
                    "Packaged dashboard is missing.");

            await Task.CompletedTask;
            return;
        }

        var sourceBot = Path.Combine(BaseDirectory, "bot");
        var sourceDashboard = Path.Combine(BaseDirectory, "dashboard");

        if (!Directory.Exists(sourceBot))
            throw new DirectoryNotFoundException(
                "Packaged Rewards bot is missing.");

        if (!Directory.Exists(sourceDashboard))
            throw new DirectoryNotFoundException(
                "Packaged dashboard is missing.");

        Directory.CreateDirectory(AppDataRoot);

        await Task.Run(() =>
        {
            if (NeedsSync(sourceBot, BotPath))
            {
                SyncApplicationDirectory(
                    sourceBot,
                    BotPath,
                    preserveConfig: true,
                    preserveDataDirectory: false);
            }

            if (NeedsSync(sourceDashboard, DashboardPath))
            {
                SyncApplicationDirectory(
                    sourceDashboard,
                    DashboardPath,
                    preserveConfig: false,
                    preserveDataDirectory: true);
            }
        }, cancellationToken);

        Directory.CreateDirectory(
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "MicrosoftRewardsApp",
                "diagnostics"));
    }

    private static bool NeedsSync(string source, string destination)
    {
        if (!Directory.Exists(destination))
            return true;

        var required = new[]
        {
            "package.json",
            "bundle-version.txt",
            string.Equals(
                Path.GetFileName(source),
                "dashboard",
                StringComparison.OrdinalIgnoreCase)
                ? "server.js"
                : Path.Combine("dist", "index.js")
        };

        if (required.Any(path => !File.Exists(Path.Combine(destination, path))))
            return true;

        try
        {
            var sourcePackage = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(source, "package.json")));

            var destinationPackage = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(destination, "package.json")));

            var sourceVersion =
                sourcePackage.RootElement.TryGetProperty("version", out var sv)
                    ? sv.GetString()
                    : null;

            var destinationVersion =
                destinationPackage.RootElement.TryGetProperty("version", out var dv)
                    ? dv.GetString()
                    : null;

            var sourceBundleVersion =
                File.ReadAllText(Path.Combine(source, "bundle-version.txt")).Trim();

            var destinationBundleVersion =
                File.ReadAllText(Path.Combine(destination, "bundle-version.txt")).Trim();

            return !string.Equals(
                       sourceVersion,
                       destinationVersion,
                       StringComparison.Ordinal) ||
                   !string.Equals(
                       sourceBundleVersion,
                       destinationBundleVersion,
                       StringComparison.Ordinal);
        }
        catch
        {
            return true;
        }
    }

    private static void SyncApplicationDirectory(
        string source,
        string destination,
        bool preserveConfig,
        bool preserveDataDirectory)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var name = Path.GetFileName(file);

            if (preserveConfig &&
                (string.Equals(
                    name,
                    "config.json",
                    StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                    name,
                    ".env",
                    StringComparison.OrdinalIgnoreCase)))
            {
                if (!File.Exists(Path.Combine(destination, name)) &&
                    !string.Equals(
                        name,
                        ".env",
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(file, Path.Combine(destination, name));
                }

                continue;
            }

            File.Copy(
                file,
                Path.Combine(destination, name),
                overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(directory);

            if (preserveConfig &&
                (string.Equals(
                    name,
                    "diagnostics",
                    StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                    name,
                    "sessions",
                    StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                    name,
                    "config",
                    StringComparison.OrdinalIgnoreCase)))
            {
                Directory.CreateDirectory(
                    Path.Combine(destination, name));
                continue;
            }

            if (preserveDataDirectory &&
                string.Equals(
                    name,
                    "data",
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(
                    Path.Combine(destination, name));
                continue;
            }

            SyncApplicationDirectory(
                directory,
                Path.Combine(destination, name),
                preserveConfig,
                preserveDataDirectory);
        }
    }

    private Process Start(
        string script,
        string workingDirectory,
        AppState state,
        bool dashboard)
    {
        if (!File.Exists(script))
            throw new FileNotFoundException(
                dashboard
                    ? "Rewards dashboard server was not found."
                    : "Rewards API server was not found.",
                script);

        var psi = new ProcessStartInfo
        {
            FileName = NodePath,
            Arguments = $"\"{script}\"",
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

        if (dashboard)
        {
            psi.Environment["CONTROL_API_URL"] = "http://127.0.0.1:3010";
            psi.Environment["CONTROL_API_TOKEN"] = state.ApiToken;
            psi.Environment["TZ"] = "America/Los_Angeles";
            psi.Environment["DASHBOARD_TITLE"] = "Microsoft Rewards";
            psi.Environment["DATA_DIR"] = Path.Combine(AppDataRoot, "dashboard-data");
        }

        return Process.Start(psi)
            ?? throw new InvalidOperationException(
                "Unable to start the Rewards runtime.");
    }

    private async Task<bool> IsApiAvailableAsync(
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "http://127.0.0.1:3010/health");

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            using var response =
                await _http.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task WaitForApiAsync(
        string token,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    "http://127.0.0.1:3010/health");

                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                using var response =
                    await _http.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // The API may still be starting.
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException(
            "Rewards Control API did not become ready.");
    }

    private async Task WaitForDashboardAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsDashboardAvailableAsync(cancellationToken))
                return;

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException(
            "Rewards dashboard did not become ready.");
    }

    private async Task<bool> IsDashboardAvailableAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(
                "http://127.0.0.1:8890/api/health",
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task<string> StartRewardsAsync(
        AppState state,
        CancellationToken cancellationToken = default) =>
        SendControlAsync(
            HttpMethod.Post,
            "/start",
            state.ApiToken,
            cancellationToken);

    public Task<string> StopRewardsAsync(
        AppState state,
        CancellationToken cancellationToken = default) =>
        SendControlAsync(
            HttpMethod.Post,
            "/stop",
            state.ApiToken,
            cancellationToken);

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
            Content = new StringContent(
                "{}",
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        using var response =
            await _http.SendAsync(request, cancellationToken);

        var body =
            await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(body);

        return body;
    }

    public void Restart()
    {
        if (_ownsDashboard)
            Kill(ref _dashboard);

        if (_ownsApi)
            Kill(ref _api);

        _ownsDashboard = false;
        _ownsApi = false;
        _externalDashboard = false;
        _externalApi = false;
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
        catch
        {
            // Best effort.
        }
        finally
        {
            process.Dispose();
            process = null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();

        if (_ownsDashboard)
            Kill(ref _dashboard);

        if (_ownsApi)
            Kill(ref _api);
    }
}