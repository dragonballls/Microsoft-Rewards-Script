namespace MicrosoftRewardsApp.Models;

public sealed class AccountProfile
{
    public int Index { get; set; }
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string TotpSecret { get; set; } = "";
    public string RecoveryEmail { get; set; } = "";
    public string GeoLocale { get; set; } = "auto";
    public string LangCode { get; set; } = "en";
    public string ProxyUrl { get; set; } = "";
    public int ProxyPort { get; set; }
    public string ProxyUsername { get; set; } = "";
    public string ProxyPassword { get; set; } = "";
}