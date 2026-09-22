using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MicrosoftRewardsApp.Models;

namespace MicrosoftRewardsApp.Services;

public static class RewardsEnvironment
{
    private static string Unquote(string value)
    {
        value = value.Trim();

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1]
                .Replace("\\\\", "\\")
                .Replace("\\\"", "\"")
                .Replace("\\r", "\r")
                .Replace("\\n", "\n");
        }

        return value;
    }

    private static Dictionary<string, string> ParseEnv(string file)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(file))
            return values;

        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var match = Regex.Match(
                line,
                @"^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$");

            if (match.Success)
                values[match.Groups[1].Value] = Unquote(match.Groups[2].Value);
        }

        return values;
    }

    public static List<AccountProfile> ImportFromEnv(string file)
    {
        var env = ParseEnv(file);

        var indexes = env.Keys
            .Select(k => Regex.Match(
                k,
                @"^ACCOUNT_(\d+)_EMAIL$",
                RegexOptions.IgnoreCase))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var accounts = new List<AccountProfile>();

        foreach (var index in indexes)
        {
            string Get(string suffix, string fallback = "") =>
                env.TryGetValue($"ACCOUNT_{index}_{suffix}", out var value)
                    ? value
                    : fallback;

            _ = int.TryParse(Get("PROXY_PORT"), out var proxyPort);

            accounts.Add(new AccountProfile
            {
                Index = index,
                Email = Get("EMAIL"),
                Password = Get("PASSWORD"),
                TotpSecret = Get("TOTP_SECRET"),
                RecoveryEmail = Get("RECOVERY_EMAIL"),
                GeoLocale = Get("GEO_LOCALE", "auto"),
                LangCode = Get("LANG_CODE", "en"),
                ProxyUrl = Get("PROXY_URL"),
                ProxyPort = proxyPort,
                ProxyUsername = Get("PROXY_USERNAME"),
                ProxyPassword = Get("PROXY_PASSWORD"),
                ProxyHttp = string.Equals(Get("PROXY_HTTP"), "true", StringComparison.OrdinalIgnoreCase),
                SaveFingerprintMobile = string.Equals(Get("SAVE_FINGERPRINT_MOBILE"), "true", StringComparison.OrdinalIgnoreCase),
                SaveFingerprintDesktop = string.Equals(Get("SAVE_FINGERPRINT_DESKTOP"), "true", StringComparison.OrdinalIgnoreCase),
                ManualAuthenticatorVerification = string.Equals(
                    Get("MANUAL_AUTHENTICATOR_VERIFICATION"),
                    "true",
                    StringComparison.OrdinalIgnoreCase)
            });
        }

        return accounts;
    }

    public static string GetApiToken(string file) =>
        ParseEnv(file).TryGetValue("API_TOKEN", out var value) ? value : "";

    public static void SanitizeLegacyEnv(string file)
    {
        if (!File.Exists(file))
            return;

        var lines = File.ReadAllLines(file)
            .Where(line => !Regex.IsMatch(
                line,
                @"^\s*ACCOUNT_\d+_",
                RegexOptions.IgnoreCase))
            .Where(line => !Regex.IsMatch(
                line,
                @"^\s*API_TOKEN\s*=",
                RegexOptions.IgnoreCase))
            .ToList();

        File.WriteAllLines(file, lines, new UTF8Encoding(false));
    }

    public static void Apply(
        ProcessStartInfo psi,
        AppState state,
        string botPath,
        string browserPath)
    {
        psi.Environment["API_HOST"] = "127.0.0.1";
        psi.Environment["API_PORT"] = "3010";
        psi.Environment["API_TOKEN"] = state.ApiToken;
        psi.Environment["API_ALLOW_CONFIG_WRITE"] = "true";
        psi.Environment["API_ALLOW_SCHEDULE_WRITE"] = "true";
        psi.Environment["API_ALLOW_CONFIG_REVEAL"] = "false";
        psi.Environment["API_CORS_ORIGIN"] = "*";
        if (Directory.Exists(browserPath))
        {
            psi.Environment["PLAYWRIGHT_BROWSERS_PATH"] = browserPath;
            psi.Environment["PATCHRIGHT_BROWSERS_PATH"] = browserPath;
        }

        psi.Environment["TZ"] = "America/Los_Angeles";

        foreach (var account in state.Accounts.OrderBy(x => x.Index))
        {
            Set(psi, $"ACCOUNT_{account.Index}_EMAIL", account.Email);
            Set(psi, $"ACCOUNT_{account.Index}_PASSWORD", account.Password);
            Set(psi, $"ACCOUNT_{account.Index}_TOTP_SECRET", account.TotpSecret);
            Set(psi, $"ACCOUNT_{account.Index}_RECOVERY_EMAIL", account.RecoveryEmail);
            Set(psi, $"ACCOUNT_{account.Index}_GEO_LOCALE", account.GeoLocale);
            Set(psi, $"ACCOUNT_{account.Index}_LANG_CODE", account.LangCode);
            Set(psi, $"ACCOUNT_{account.Index}_PROXY_URL", account.ProxyUrl);
            Set(psi, $"ACCOUNT_{account.Index}_PROXY_PORT", account.ProxyPort.ToString());
            Set(psi, $"ACCOUNT_{account.Index}_PROXY_USERNAME", account.ProxyUsername);
            Set(psi, $"ACCOUNT_{account.Index}_PROXY_PASSWORD", account.ProxyPassword);
            Set(psi, $"ACCOUNT_{account.Index}_PROXY_HTTP", account.ProxyHttp ? "true" : "false");
            Set(psi, $"ACCOUNT_{account.Index}_SAVE_FINGERPRINT_MOBILE", account.SaveFingerprintMobile ? "true" : "false");
            Set(psi, $"ACCOUNT_{account.Index}_SAVE_FINGERPRINT_DESKTOP", account.SaveFingerprintDesktop ? "true" : "false");
            Set(psi, $"ACCOUNT_{account.Index}_MANUAL_AUTHENTICATOR_VERIFICATION", account.ManualAuthenticatorVerification ? "true" : "false");
        }
    }

    private static void Set(
        ProcessStartInfo psi,
        string key,
        string value)
    {
        if (!string.IsNullOrEmpty(value))
            psi.Environment[key] = value;
    }

    public static string NewToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
