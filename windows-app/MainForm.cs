using System.Diagnostics;
using Microsoft.Win32;
using Microsoft.Web.WebView2.WinForms;
using MicrosoftRewardsApp.Models;
using MicrosoftRewardsApp.Services;

namespace MicrosoftRewardsApp;

public sealed class MainForm : Form
{
    private readonly AppState _state;
    private readonly RewardsRuntime _runtime;
    private readonly ListBox _accounts = new();
    private readonly TextBox _email = new();
    private readonly TextBox _password = new();
    private readonly TextBox _totp = new();
    private readonly TextBox _recovery = new();
    private readonly TextBox _geo = new();
    private readonly TextBox _lang = new();
    private readonly TextBox _proxy = new();
    private readonly NumericUpDown _proxyPort = new();
    private readonly TextBox _proxyUser = new();
    private readonly TextBox _proxyPassword = new();
    private readonly CheckBox _proxyHttp = new();
    private readonly TextBox _apiToken = new();
    private readonly CheckBox _fingerprintMobile = new();
    private readonly CheckBox _fingerprintDesktop = new();
    private readonly CheckBox _startup = new();
    private readonly Label _status = new();
    private readonly NotifyIcon _tray = new();
    private readonly System.Windows.Forms.Timer _monitor = new() { Interval = 5000 };
    private readonly WebView2 _dashboardView = new() { Dock = DockStyle.Fill };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    private AccountProfile? _current;
    private bool _allowClose;
    private bool _busy;

    public MainForm(AppState state, RewardsRuntime runtime, bool startHidden)
    {
        _state = state;
        _runtime = runtime;

        Text = "Microsoft Rewards";
        Width = 980;
        Height = 680;
        MinimumSize = new Size(860, 580);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        _apiToken.Text = _state.ApiToken;
        BuildTray();
        RefreshAccounts();

        _startup.Checked = _state.StartWithWindows;
        _startup.CheckedChanged += (_, _) =>
        {
            _state.StartWithWindows = _startup.Checked;
            SecureStore.Save(_state);
            WindowsStartup.SetEnabled(_startup.Checked);
        };

        _monitor.Tick += async (_, _) => await MonitorAsync();
        _monitor.Start();

        Shown += async (_, _) =>
        {
            WindowsStartup.SetEnabled(_state.StartWithWindows);
            await EnsureServicesAsync();
            await InitializeDashboardAsync();

            if (startHidden)
                HideToTray();
        };
    }

    private void BuildUi()
    {
        var left = new Panel
        {
            Dock = DockStyle.Left,
            Width = 310,
            Padding = new Padding(14)
        };

        left.Controls.Add(new Label
        {
            Text = "SAVED ACCOUNTS",
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font("Segoe UI", 12, FontStyle.Bold)
        });

        _accounts.Dock = DockStyle.Fill;
        _accounts.SelectedIndexChanged += (_, _) => LoadSelected();
        left.Controls.Add(_accounts);

        var add = MakeButton("Add Account", 115);
        add.Dock = DockStyle.Bottom;
        add.Click += (_, _) => AddAccount();
        left.Controls.Add(add);

        var right = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14)
        };

        right.Controls.Add(new Label
        {
            Text = "Microsoft Rewards Account",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 16, FontStyle.Bold)
        });

        var apiPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 4)
        };

        apiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        apiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        apiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));

        apiPanel.Controls.Add(new Label
        {
            Text = "Control API key/token",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _apiToken.Dock = DockStyle.Fill;
        _apiToken.UseSystemPasswordChar = true;
        apiPanel.Controls.Add(_apiToken, 1, 0);

        var generateToken = MakeButton("Generate", 95);
        generateToken.Dock = DockStyle.Fill;
        generateToken.Click += (_, _) =>
        {
            _apiToken.Text = RewardsEnvironment.NewToken();
            _state.ApiToken = _apiToken.Text;
            SecureStore.Save(_state);
            _status.Text = "Status: new API key/token generated and saved";
        };
        apiPanel.Controls.Add(generateToken, 2, 0);

        right.Controls.Add(apiPanel);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 416,
            ColumnCount = 2,
            RowCount = 13
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(grid, 0, "Email", _email);
        AddRow(grid, 1, "Password", _password);
        AddRow(grid, 2, "TOTP secret", _totp);
        AddRow(grid, 3, "Recovery email", _recovery);
        AddRow(grid, 4, "Geo locale", _geo);
        AddRow(grid, 5, "Language code", _lang);
        AddRow(grid, 6, "Proxy URL", _proxy);

        _proxyPort.Minimum = 0;
        _proxyPort.Maximum = 65535;
        AddRow(grid, 7, "Proxy port", _proxyPort);

        AddRow(grid, 8, "Proxy username", _proxyUser);
        AddRow(grid, 9, "Proxy password", _proxyPassword);

        _proxyHttp.Text = "Use HTTP proxy";
        _fingerprintMobile.Text = "Save mobile fingerprint";
        _fingerprintDesktop.Text = "Save desktop fingerprint";

        AddRow(grid, 10, "Proxy type", _proxyHttp);
        AddRow(grid, 11, "Fingerprint", _fingerprintMobile);
        AddRow(grid, 12, "Fingerprint", _fingerprintDesktop);

        _password.UseSystemPasswordChar = true;
        _totp.UseSystemPasswordChar = true;
        _proxyPassword.UseSystemPasswordChar = true;

        right.Controls.Add(grid);

        right.Controls.Add(new Label
        {
            Text = "API key/token and account information are stored with Windows user-level encryption. "
                 + "Credentials are supplied to the Rewards runtime only when it starts.",
            Dock = DockStyle.Top,
            Height = 40,
            ForeColor = Color.DimGray
        });

        _startup.Text = "Start this app with Windows";
        _startup.Dock = DockStyle.Top;
        _startup.Height = 30;
        right.Controls.Add(_startup);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46
        };

        var save = MakeButton("Save Account", 120);
        save.Click += (_, _) => SaveCurrent();

        var delete = MakeButton("Delete", 85);
        delete.Click += (_, _) => DeleteCurrent();

        var start = MakeButton("Start Rewards", 120);
        start.Click += async (_, _) => await StartRewardsAsync();

        var stop = MakeButton("Stop Rewards", 110);
        stop.Click += async (_, _) => await StopRewardsAsync();

        var dashboard = MakeButton("Open Dashboard", 135);
        dashboard.Click += async (_, _) => await ShowDashboardAsync();

        buttons.Controls.Add(save);
        buttons.Controls.Add(delete);
        buttons.Controls.Add(start);
        buttons.Controls.Add(stop);
        buttons.Controls.Add(dashboard);
        right.Controls.Add(buttons);

        _status.Text = "Status: starting…";
        _status.Dock = DockStyle.Bottom;
        _status.Height = 28;
        _status.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        right.Controls.Add(_status);

        var settingsTab = new TabPage("Account Setup");
        settingsTab.Controls.Add(right);
        settingsTab.Controls.Add(left);

        var dashboardTab = new TabPage("Dashboard");
        dashboardTab.Controls.Add(_dashboardView);

        _tabs.TabPages.Add(dashboardTab);
        _tabs.TabPages.Add(settingsTab);
        Controls.Add(_tabs);
    }

    private static Button MakeButton(string text, int width) =>
        new()
        {
            Text = text,
            Width = width,
            Height = 32,
            Margin = new Padding(4)
        };

    private static void AddRow(TableLayoutPanel grid, int row, string title, Control control)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        grid.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);

        control.Dock = DockStyle.Fill;
        grid.Controls.Add(control, 1, row);
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();

        var open = new ToolStripMenuItem("Open Microsoft Rewards");
        open.Click += (_, _) => ShowFromTray();

        var start = new ToolStripMenuItem("Start Rewards");
        start.Click += async (_, _) => await StartRewardsAsync();

        var stop = new ToolStripMenuItem("Stop Rewards");
        stop.Click += async (_, _) => await StopRewardsAsync();

        var dashboard = new ToolStripMenuItem("Open Dashboard");
        dashboard.Click += async (_, _) => await ShowDashboardAsync();

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) =>
        {
            _allowClose = true;
            _tray.Visible = false;
            Application.Exit();
        };

        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(start);
        menu.Items.Add(stop);
        menu.Items.Add(dashboard);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _tray.Icon = SystemIcons.Application;
        _tray.Text = "Microsoft Rewards";
        _tray.ContextMenuStrip = menu;
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private void RefreshAccounts()
    {
        _accounts.Items.Clear();

        foreach (var account in _state.Accounts.OrderBy(x => x.Index))
            _accounts.Items.Add(account);

        if (_accounts.Items.Count > 0)
            _accounts.SelectedIndex = 0;
        else
            ClearFields();
    }

    private void LoadSelected()
    {
        if (_accounts.SelectedItem is not AccountProfile selected)
            return;

        _current = selected;
        _email.Text = selected.Email;
        _password.Text = selected.Password;
        _totp.Text = selected.TotpSecret;
        _recovery.Text = selected.RecoveryEmail;
        _geo.Text = selected.GeoLocale;
        _lang.Text = selected.LangCode;
        _proxy.Text = selected.ProxyUrl;
        _proxyPort.Value = Math.Clamp(selected.ProxyPort, 0, 65535);
        _proxyUser.Text = selected.ProxyUsername;
        _proxyPassword.Text = selected.ProxyPassword;
        _proxyHttp.Checked = selected.ProxyHttp;
        _fingerprintMobile.Checked = selected.SaveFingerprintMobile;
        _fingerprintDesktop.Checked = selected.SaveFingerprintDesktop;
    }

    private void ClearFields()
    {
        _current = null;
        _email.Clear();
        _password.Clear();
        _totp.Clear();
        _recovery.Clear();
        _geo.Text = "auto";
        _lang.Text = "en";
        _proxy.Clear();
        _proxyPort.Value = 0;
        _proxyUser.Clear();
        _proxyPassword.Clear();
        _proxyHttp.Checked = false;
        _fingerprintMobile.Checked = false;
        _fingerprintDesktop.Checked = false;
    }

    private void AddAccount()
    {
        var index = 1;
        while (_state.Accounts.Any(x => x.Index == index))
            index++;

        var account = new AccountProfile
        {
            Index = index,
            GeoLocale = "auto",
            LangCode = "en"
        };

        _state.Accounts.Add(account);
        SecureStore.Save(_state);
        RefreshAccounts();
        _accounts.SelectedItem = account;
        ShowFromTray();
        _email.Focus();
    }

    private void ReadFields()
    {
        if (_current is null)
            return;

        _current.Email = _email.Text.Trim();
        _current.Password = _password.Text;
        _current.TotpSecret = _totp.Text;
        _current.RecoveryEmail = _recovery.Text.Trim();
        _current.GeoLocale = string.IsNullOrWhiteSpace(_geo.Text) ? "auto" : _geo.Text.Trim();
        _current.LangCode = string.IsNullOrWhiteSpace(_lang.Text) ? "en" : _lang.Text.Trim();
        _state.ApiToken = _apiToken.Text.Trim();
        _current.ProxyUrl = _proxy.Text.Trim();
        _current.ProxyPort = (int)_proxyPort.Value;
        _current.ProxyUsername = _proxyUser.Text;
        _current.ProxyPassword = _proxyPassword.Text;
        _current.ProxyHttp = _proxyHttp.Checked;
        _current.SaveFingerprintMobile = _fingerprintMobile.Checked;
        _current.SaveFingerprintDesktop = _fingerprintDesktop.Checked;
    }

    private void SaveCurrent()
    {
        if (_current is null)
        {
            AddAccount();
            _current = _state.Accounts.OrderByDescending(x => x.Index).First();
        }

        if (string.IsNullOrWhiteSpace(_email.Text))
        {
            MessageBox.Show(
                "Enter the Microsoft account email first.",
                "Microsoft Rewards",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_password.Text))
        {
            MessageBox.Show(
                "Enter the Microsoft account password before saving.",
                "Microsoft Rewards",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        ReadFields();

        if (string.IsNullOrWhiteSpace(_state.ApiToken))
        {
            MessageBox.Show(
                "Enter a Control API key/token or click Generate.",
                "Microsoft Rewards",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        SecureStore.Save(_state);
        RefreshAccounts();

        _accounts.SelectedItem = _current;
        _status.Text = "Status: account saved securely";

        _runtime.Restart();
        _ = EnsureServicesAsync();
    }

    private void DeleteCurrent()
    {
        if (_current is null)
            return;

        if (MessageBox.Show(
                $"Delete {_current.Email}?",
                "Microsoft Rewards",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _state.Accounts.Remove(_current);
        _current = null;
        SecureStore.Save(_state);
        RefreshAccounts();

        _runtime.Restart();
        _ = EnsureServicesAsync();
    }

    private async Task EnsureServicesAsync()
    {
        if (_busy)
            return;

        _busy = true;

        try
        {
            await _runtime.EnsureRunningAsync(_state);
            _status.Text =
                $"Status: app active • {_state.Accounts.Count} saved account(s)";
        }
        catch (Exception ex)
        {
            _status.Text = $"Status: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task MonitorAsync()
    {
        if (IsDisposed)
            return;

        await EnsureServicesAsync();

        _status.Text =
            $"Status: API {(_runtime.ApiRunning ? "running" : "down")} • " +
            $"Dashboard {(_runtime.DashboardRunning ? "running" : "down")} • " +
            $"{_state.Accounts.Count} saved account(s)";
    }

    private async Task StartRewardsAsync()
    {
        ReadFields();

        if (string.IsNullOrWhiteSpace(_state.ApiToken))
        {
            MessageBox.Show(
                "Enter a Control API key/token or click Generate before starting Rewards.",
                "Microsoft Rewards",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var incomplete = _state.Accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Email) &&
                        string.IsNullOrWhiteSpace(a.Password))
            .OrderBy(a => a.Index)
            .ToList();

        if (incomplete.Count > 0)
        {
            var first = incomplete[0];
            var selected = _state.Accounts.FirstOrDefault(a => a.Index == first.Index);
            if (selected is not null)
                _accounts.SelectedItem = selected;

            MessageBox.Show(
                $"ACCOUNT_{first.Index} has an email but no password. Enter the password in Account Setup and save it before starting Rewards.",
                "Microsoft Rewards",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 1;
            return;
        }

        if (!_state.Accounts.Any(a => !string.IsNullOrWhiteSpace(a.Email) &&
                                      !string.IsNullOrWhiteSpace(a.Password)))
        {
            MessageBox.Show(
                "Add a Microsoft Rewards account with both email and password before starting Rewards.",
                "Microsoft Rewards",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 1;
            return;
        }

        SecureStore.Save(_state);

        try
        {
            await _runtime.EnsureRunningAsync(_state);
            await _runtime.StartRewardsAsync(_state);
            _status.Text = "Status: Rewards run started";
        }
        catch (Exception ex)
        {
            _status.Text = $"Status: unable to start • {ex.Message}";
        }
    }

    private async Task StopRewardsAsync()
    {
        try
        {
            await _runtime.StopRewardsAsync(_state);
            _status.Text = "Status: Rewards run stopped";
        }
        catch (Exception ex)
        {
            _status.Text = $"Status: unable to stop • {ex.Message}";
        }
    }

    private async Task InitializeDashboardAsync()
    {
        try
        {
            await _dashboardView.EnsureCoreWebView2Async();
            _dashboardView.CoreWebView2.Navigate("http://127.0.0.1:8890/");
            _status.Text = "Status: dashboard embedded and ready";
        }
        catch (Exception ex)
        {
            _status.Text = $"Status: embedded dashboard unavailable • {ex.Message}";
        }
    }

    private async Task ShowDashboardAsync()
    {
        _tabs.SelectedIndex = 0;

        if (_dashboardView.CoreWebView2 is null)
            await InitializeDashboardAsync();
        else
            _dashboardView.CoreWebView2.Navigate("http://127.0.0.1:8890/");
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _monitor.Stop();
        _runtime.Dispose();
        base.OnFormClosing(e);
    }
}

internal static class WindowsStartup
{
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "MicrosoftRewardsApp";

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(RunKey);

            if (enabled)
            {
                var exe = Environment.ProcessPath!;
                key!.SetValue(ValueName, "\"" + exe + "\" --background");
            }
            else
            {
                key!.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Startup is a convenience; do not prevent the app from running.
        }
    }
}