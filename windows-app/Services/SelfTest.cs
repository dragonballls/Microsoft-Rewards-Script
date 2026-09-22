using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using MicrosoftRewardsApp.Models;

namespace MicrosoftRewardsApp.Services;

public static class SelfTest
{
    public static async Task RunAsync()
    {
        var state = SecureStore.Load();
        var originalAccounts = state.Accounts;
        var originalToken = state.ApiToken;

        var testAccount = new AccountProfile
        {
            Index = 1,
            Email = "self-test@example.invalid",
            Password = "self-test-password",
            TotpSecret = "self-test-totp",
            RecoveryEmail = "recovery@example.invalid",
            GeoLocale = "auto",
            LangCode = "en",
            ProxyUrl = "http://127.0.0.1",
            ProxyPort = 8080,
            ProxyUsername = "self-test-user",
            ProxyPassword = "self-test-proxy-password",
            ProxyHttp = true,
            SaveFingerprintMobile = true,
            SaveFingerprintDesktop = true
        };

        try
        {
            state.ApiToken = RewardsEnvironment.NewToken();
            state.Accounts = [testAccount];

            SecureStore.Save(state);

            var loaded = SecureStore.Load();

            Assert(loaded.Accounts.Count == 1, "encrypted account count");
            Assert(loaded.Accounts[0].Email == testAccount.Email, "encrypted email");
            Assert(loaded.Accounts[0].Password == testAccount.Password, "encrypted password");
            Assert(loaded.Accounts[0].TotpSecret == testAccount.TotpSecret, "encrypted TOTP");
            Assert(loaded.Accounts[0].ProxyPassword == testAccount.ProxyPassword, "encrypted proxy password");
            Assert(loaded.Accounts[0].ProxyHttp, "proxy HTTP flag");
            Assert(loaded.Accounts[0].SaveFingerprintMobile, "mobile fingerprint flag");
            Assert(loaded.Accounts[0].SaveFingerprintDesktop, "desktop fingerprint flag");

            using var runtime = new RewardsRuntime();
            await runtime.EnsureRunningAsync(loaded);

            Assert(runtime.ApiRunning, "Control API process");

            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            using var apiRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "http://127.0.0.1:3010/health");

            apiRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", loaded.ApiToken);

            using var apiResponse =
                await http.SendAsync(apiRequest);

            Assert(apiResponse.IsSuccessStatusCode, "authenticated Control API health");

            using var dashboardResponse =
                await http.GetAsync("http://127.0.0.1:8890/");

            Assert(dashboardResponse.IsSuccessStatusCode, "dashboard HTTP response");
            Assert(runtime.DashboardRunning, "dashboard process");

            Console.WriteLine("SELF_TEST_PASS encrypted-store");
            Console.WriteLine("SELF_TEST_PASS control-api");
            Console.WriteLine("SELF_TEST_PASS dashboard");
            Console.WriteLine("SELF_TEST_PASS runtime-processes");
        }
        finally
        {
            state.Accounts = originalAccounts;
            state.ApiToken = originalToken;

            try
            {
                if (string.IsNullOrWhiteSpace(state.ApiToken))
                    File.Delete(SecureStore.StateFile);
                else
                    SecureStore.Save(state);
            }
            catch
            {
                try { File.Delete(SecureStore.StateFile); } catch { }
            }
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Self-test failed: {name}");
    }
}
