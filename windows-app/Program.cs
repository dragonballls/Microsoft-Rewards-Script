using System.Text;
using MicrosoftRewardsApp.Services;

namespace MicrosoftRewardsApp;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (HasArg(args, "--self-test"))
        {
            SelfTest.RunAsync().GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();
        if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "node_modules")))
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", "0");
            Environment.SetEnvironmentVariable("PATCHRIGHT_BROWSERS_PATH", "0");
        }

        var initialTab = ReadIntArg(args, "--tab", 0);
        var setGap = ReadIntArg(args, "--setgap", -1);
        var verify = HasArg(args, "--verify");
        var verifySwitch = HasArg(args, "--verify-switch");

        if (!verify && !verifySwitch && RewardsManager.EnvCheck.NeedsSetup())
        {
            EnsureConfig();
            using var wizard = new RewardsManager.EnvWizardForm(standalone: true);
            if (wizard.ShowDialog() != DialogResult.OK)
                return;
        }

        Application.Run(new RewardsManager.MainForm(initialTab, setGap, verify, verifySwitch));
    }

    private static bool HasArg(string[] args, string value)
    {
        return args.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
    }

    private static int ReadIntArg(string[] args, string name, int fallback)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                continue;
            return int.TryParse(args[i + 1], out var value) ? value : fallback;
        }
        return fallback;
    }

    private static void EnsureConfig()
    {
        try
        {
            var config = Path.Combine(RewardsManager.ProjectPaths.Root, "config.json");
            var example = Path.Combine(RewardsManager.ProjectPaths.Root, "config.example.json");
            if (!File.Exists(config) && File.Exists(example))
                File.Copy(example, config, overwrite: false);
        }
        catch { }
    }
}