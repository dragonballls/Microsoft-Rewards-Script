using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RewardsManager
{
    /// <summary>
    /// 首次启动的Environment Setup向导：展示 Node/依赖/dist/浏览器/配置状态，
    /// 提供「Install Dependencies and Build」「Install/Fix Node」一键按钮，全部就绪后Enter Main Interface。
    /// </summary>
    internal class EnvWizardForm : Form
    {
        private readonly TableLayoutPanel statusPanel;
        private readonly FlowLayoutPanel btnRow;
        private readonly Panel panelOut;
        private readonly RichTextBox txtOut;
        private readonly Button btnInstallDeps, btnInstallNode, btnEnter, btnCancel;
        private readonly bool _standalone;
        private readonly ToolTip toolTip = new ToolTip();
        private bool _busy;
        private CancellationTokenSource _cts;

        public EnvWizardForm(bool standalone = false)
        {
            _standalone = standalone;
            Text = "Environment Setup";
            Width = 840;
            Height = 640;
            MinimumSize = new Size(800, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            Padding = new Padding(10);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormClosing += (_, e) => { if (_busy && MessageBox.Show("Installation is in progress. Cancel and exit?", "Confirm Cancel", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true; };

            // 状态区：顶部，自动根据内容撑高
            statusPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                Margin = Padding.Empty,
                Padding = new Padding(0, 0, 0, 8)
            };
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320f));
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // 按钮行：底部，固定高度
            btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 6),
                Margin = Padding.Empty
            };
            btnInstallDeps = new Button { Text = "Install Dependencies and Build", AutoSize = true, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 10, 0) };
            btnInstallNode = new Button { Text = "Install/Fix Node", AutoSize = true, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 10, 0) };
            btnCancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 10, 0), Visible = false };
            btnEnter = new Button { Text = standalone ? "Enter Main Interface" : "Return to Main Interface", AutoSize = true, Padding = new Padding(8, 3, 8, 3), Visible = !standalone };
            btnInstallDeps.Click += (_, _) => _ = DoInstallDeps();
            btnInstallNode.Click += (_, _) => _ = DoInstallNode();
            btnCancel.Click += (_, _) => _cts?.Cancel();
            btnEnter.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            btnRow.Controls.Add(btnInstallDeps);
            btnRow.Controls.Add(btnInstallNode);
            btnRow.Controls.Add(btnCancel);
            btnRow.Controls.Add(btnEnter);

            // 输出区：Dock=Fill 占满状态区与按钮行之间的剩余空间
            panelOut = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 6, 0, 6),
                Margin = Padding.Empty
            };
            txtOut = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 9F),
                Margin = Padding.Empty,
                BackColor = Color.White,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.ForcedBoth
            };
            panelOut.Controls.Add(txtOut);

            Controls.Add(panelOut);
            Controls.Add(statusPanel);
            Controls.Add(btnRow);

            RefreshStatus();
        }

        private void AppendOut(string s) => txtOut.AppendText(s + Environment.NewLine);

        private void RefreshStatus()
        {
            statusPanel.Controls.Clear();
            var node = EnvCheck.CheckNode();
            bool hasModules = EnvCheck.HasNodeModules();
            bool hasDist = EnvCheck.HasDist();
            bool hasBrowser = hasModules && EnvCheck.HasBrowser();
            bool hasConfig = EnvCheck.HasConfig();

            string nodeText;
            if (node.ok)
                nodeText = $"{node.version} ✓ ({node.path})";
            else if (string.IsNullOrEmpty(node.version))
                nodeText = "Not installed";
            else
                nodeText = $"{node.version}（Version too old; ≥24 required）";
            AddStatus("Node.js (requires ≥24):", nodeText, node.ok);
            AddStatus("Dependencies (node_modules):", hasModules ? "Installed" : "Missing (install required)", hasModules);
            AddStatus("Build output (dist):", hasDist ? "Built" : "Missing (build required)", hasDist);
            AddStatus("Browser engine (Chromium):", hasBrowser ? "Installed" : (hasModules ? "Missing (download required)" : "Check after dependencies are installed"), hasBrowser);
            AddStatus("Config file (config.json):", hasConfig ? "Present" : "Will be generated from template", hasConfig);

            bool allOk = node.ok && hasModules && hasDist && hasBrowser && hasConfig;
            if (!_busy)
                btnEnter.Enabled = allOk;
            btnInstallNode.Visible = !node.ok;
            btnInstallNode.Enabled = !_busy && !node.ok;

            // Node 没装好时不能点「安装依赖」
            btnInstallDeps.Enabled = node.ok && !_busy;
            if (!node.ok)
                toolTip.SetToolTip(btnInstallDeps, "请先点击「Install/Fix Node」安装 Node.js（requires ≥24）");
            else
                toolTip.SetToolTip(btnInstallDeps, "Runs npm install + downloads the browser + npm run build");

            // 独立模式（启动前拦截）：全部就绪后自动Enter Main Interface
            if (_standalone && allOk && !_busy)
            {
                DialogResult = DialogResult.OK;
                BeginInvoke(new Action(Close));
            }
        }

        private void AddStatus(string label, string value, bool ok)
        {
            int row = statusPanel.RowCount++;
            statusPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lblName = new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 4, 10, 4)
            };
            var lblValue = new Label
            {
                Text = value,
                AutoSize = true,
                AutoEllipsis = true,
                ForeColor = ok ? Color.DarkGreen : Color.DarkRed,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Margin = new Padding(0, 4, 0, 4)
            };
            toolTip.SetToolTip(lblValue, value);
            statusPanel.Controls.Add(lblName, 0, row);
            statusPanel.Controls.Add(lblValue, 1, row);
        }

        private async Task DoInstallDeps()
        {
            if (_busy) return;
            var node = EnvCheck.CheckNode();
            if (!node.ok)
            {
                MessageBox.Show("请先安装 Node.js（requires ≥24）后再安装依赖。", "Node Installation Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _cts = new CancellationTokenSource();
            _busy = true;
            btnInstallDeps.Enabled = false;
            btnInstallNode.Enabled = false;
            btnEnter.Enabled = false;
            btnCancel.Visible = true;
            txtOut.Clear();
            try
            {
                int code = await EnvCheck.InstallDepsAsync(AppendOut, _cts.Token);
                if (_cts.IsCancellationRequested)
                    AppendOut("=== 用户Cancel ===");
                else
                    AppendOut(code == 0 ? "=== Dependencies installed and build completed ===" : "=== Install/build failed; see the output above ===");
            }
            catch (OperationCanceledException) { AppendOut("=== 用户Cancel ==="); }
            catch (Exception ex) { AppendOut("Exception: " + ex.Message); }
            finally
            {
                _busy = false;
                btnCancel.Visible = false;
                _cts?.Dispose();
                _cts = null;
                RefreshStatus();
            }
        }

        private async Task DoInstallNode()
        {
            if (_busy) return;
            if (MessageBox.Show("将尝试使用 winget 自动安装 Node.js（当前版本requires ≥24）。\n若系统无 winget 则会提示手动安装。继续？",
                "安装 Node.js", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _cts = new CancellationTokenSource();
            _busy = true;
            btnInstallNode.Enabled = false;
            btnInstallDeps.Enabled = false;
            btnEnter.Enabled = false;
            btnCancel.Visible = true;
            txtOut.Clear();
            try
            {
                bool ok = await EnvCheck.InstallNodeAsync(AppendOut);
                if (_cts.IsCancellationRequested)
                    AppendOut("=== 用户Cancel ===");
                else if (ok)
                    AppendOut("Installation complete. Restart this application to apply the new Node.js version, then install dependencies.");
            }
            catch (OperationCanceledException) { AppendOut("=== 用户Cancel ==="); }
            catch (Exception ex) { AppendOut("Exception: " + ex.Message); }
            finally
            {
                _busy = false;
                btnCancel.Visible = false;
                _cts?.Dispose();
                _cts = null;
                RefreshStatus();
            }
        }
    }
}
