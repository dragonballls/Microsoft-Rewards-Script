using MicrosoftRewardsApp.Services;

namespace MicrosoftRewardsApp;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(x => string.Equals(
                x,
                "--self-test",
                StringComparison.OrdinalIgnoreCase)))
        {
            SelfTest.RunAsync().GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();

        var state = SecureStore.Load();

        var runtime = new RewardsRuntime();
        try
        {
            MigrateLegacyConfiguration(state);

            if (string.IsNullOrWhiteSpace(state.ApiToken))
            {
                state.ApiToken = RewardsEnvironment.NewToken();
                SecureStore.Save(state);
            }

            if (state.StartWithWindows)
                WindowsStartup.SetEnabled(true);

            var startHidden = args.Any(x =>
                string.Equals(x, "--background", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x, "--tray", StringComparison.OrdinalIgnoreCase));

            Application.Run(new MainForm(state, runtime, startHidden));
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    private static void MigrateLegacyConfiguration(
        AppState state)
    {
        if (state.Accounts.Count > 0 && !string.IsNullOrWhiteSpace(state.ApiToken))
            return;

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "MicrosoftRewardsFull", "Microsoft-Rewards-Script", ".env"),

            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Microsoft-Rewards-Script", ".env"),

            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "microsoft-rewards-script", ".env"),

            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "MicrosoftRewards", ".env")
        };

        string? migratedFile = null;

        foreach (var file in candidates.Where(File.Exists))
        {
            var imported = RewardsEnvironment.ImportFromEnv(file);

            if (state.Accounts.Count == 0 && imported.Count > 0)
            {
                state.Accounts = imported;
                state.LegacyBotPath = Path.GetDirectoryName(file);
                migratedFile = file;
            }

            if (string.IsNullOrWhiteSpace(state.ApiToken))
            {
                var apiToken = RewardsEnvironment.GetApiToken(file);
                if (!string.IsNullOrWhiteSpace(apiToken))
                {
                    state.ApiToken = apiToken;
                    migratedFile ??= file;
                }
            }

            if (state.Accounts.Count > 0)
                break;
        }

        if (state.Accounts.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(state.ApiToken))
            state.ApiToken = RewardsEnvironment.NewToken();

        SecureStore.Save(state);

        if (migratedFile is not null)
            RewardsEnvironment.SanitizeLegacyEnv(migratedFile);
    }
}