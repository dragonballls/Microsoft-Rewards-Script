using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MicrosoftRewardsApp.Models;

namespace MicrosoftRewardsApp.Services;

public sealed class AppState
{
    public string ApiToken { get; set; } = "";
    public bool StartWithWindows { get; set; } = true;
    public string? LegacyBotPath { get; set; }
    public List<AccountProfile> Accounts { get; set; } = [];
}

public static class SecureStore
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MicrosoftRewardsApp");

    public static string StateFile => Path.Combine(DataDirectory, "state.dat");

    private static readonly byte[] Entropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("MicrosoftRewardsApp.AccountStore.v1"));

    public static AppState Load()
    {
        Directory.CreateDirectory(DataDirectory);

        if (!File.Exists(StateFile))
            return new AppState();

        try
        {
            var blob = File.ReadAllBytes(StateFile);
            var json = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<AppState>(json)
                ?? new AppState();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The stored Microsoft Rewards account data could not be decrypted or read.",
                ex);
        }
    }

    public static void Save(AppState state)
    {
        Directory.CreateDirectory(DataDirectory);

        var json = JsonSerializer.SerializeToUtf8Bytes(state, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        var blob = ProtectedData.Protect(
            json,
            Entropy,
            DataProtectionScope.CurrentUser);

        var tmp = StateFile + ".tmp";
        File.WriteAllBytes(tmp, blob);
        File.Move(tmp, StateFile, true);
    }
}