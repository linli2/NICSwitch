/// 托盘图标和菜单管理

using System.Diagnostics;

namespace NICSwitch;

public class TrayManager : IDisposable
{
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly Form _menuOwner; // 隐藏窗口，作为菜单的所有者
    private Config _config;
    private List<NetworkAdapter> _adapters = new();
    private bool _rebuilding;

    // ── 菜单项 ID 常量 ────────────────────────────
    private const string CmdRefresh = "__refresh__";
    private const string CmdTogglePhysical = "__toggle_physical__";
    private const string CmdEditConfig = "__edit_config__";
    private const string CmdQuit = "__quit__";
    private const string PrefixToggle = "toggle:";
    private const string PrefixProfile = "profile:";

    public TrayManager()
    {
        _config = ConfigManager.Load();
        _adapters = NicManager.ListAdapters();

        // 隐藏所有者窗口（让菜单有父窗口，才能正常弹出/关闭）
        _menuOwner = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            WindowState = FormWindowState.Minimized,
            Size = new Size(0, 0),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
        };
        // 要 Show 后才能作为菜单所有者
        _menuOwner.Show();

        // 托盘图标
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "NICSwitch - 网卡切换",
            Visible = true,
        };

        // 鼠标点击（左/右键统一处理）
        _trayIcon.MouseClick += OnTrayMouseClick;

        // 菜单项点击
        _menu.ItemClicked += OnMenuItemClicked;

        // 自动刷新
        var interval = _config.AutoRefreshInterval > 0 ? _config.AutoRefreshInterval : 5;
        _refreshTimer.Interval = interval * 1000;
        _refreshTimer.Tick += (_, _) => AutoRefresh();
        _refreshTimer.Start();

        // 首次构建菜单
        RebuildMenu();

        Logger.Info("托盘图标已创建");
    }

    // ── 鼠标点击处理 ──────────────────────────────

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        Logger.Info($"托盘: {(e.Button == MouseButtons.Left ? "左键" : "右键")}单击");

        // 点击时刷新网卡状态
        _adapters = NicManager.ListAdapters();
        RebuildMenu();

        // 在鼠标位置弹出菜单（通过 _menuOwner 作为所有者，确保自动关闭）
        _menu.Show(_menuOwner, _menuOwner.PointToClient(Cursor.Position));
    }

    // ── 构建菜单 ──────────────────────────────────

    private void RebuildMenu()
    {
        if (_rebuilding) return;
        _rebuilding = true;

        try
        {
            _menu.Items.Clear();

            // ═ 网卡列表 ═══════════
            _menu.Items.Add(new ToolStripMenuItem("📡 网卡列表") { Enabled = false });

            var displayAdapters = _config.ShowPhysicalOnly
                ? _adapters.Where(a => a.IsHardware).ToList()
                : _adapters;

            if (displayAdapters.Count == 0)
            {
                _menu.Items.Add(new ToolStripMenuItem("  未发现网卡") { Enabled = false });
            }
            else
            {
                foreach (var adapter in displayAdapters)
                {
                    var isEnabled = adapter.State == NicState.Enabled;
                    var item = new ToolStripMenuItem(adapter.Name)
                    {
                        Tag = PrefixToggle + adapter.Name,
                        Checked = isEnabled,
                        ForeColor = isEnabled
                            ? SystemColors.ControlText
                            : SystemColors.GrayText,
                    };
                    if (adapter.State == NicState.Unknown)
                        item.Enabled = false;
                    _menu.Items.Add(item);
                }
            }

            _menu.Items.Add(new ToolStripSeparator());

            // ═ 快速切换 Profiles ═══
            Logger.Debug($"Profiles 数量: {_config.Profiles.Count}");
            if (_config.Profiles.Count > 0)
            {
                _menu.Items.Add(new ToolStripMenuItem("⚡ 快速切换") { Enabled = false });

                foreach (var profile in _config.Profiles)
                {
                    var item = new ToolStripMenuItem(profile.Name)
                    {
                        Tag = PrefixProfile + profile.Name,
                    };
                    _menu.Items.Add(item);
                }

                _menu.Items.Add(new ToolStripSeparator());
            }

            // ═ 设置 ═══════════════
            _menu.Items.Add(new ToolStripMenuItem("⚙ 设置") { Enabled = false });

            var physItem = new ToolStripMenuItem("只显示物理网卡")
            {
                Checked = _config.ShowPhysicalOnly,
                Tag = CmdTogglePhysical,
            };
            _menu.Items.Add(physItem);

            _menu.Items.Add(new ToolStripMenuItem("🔄 刷新状态") { Tag = CmdRefresh });
            _menu.Items.Add(new ToolStripMenuItem("✏️ 编辑配置") { Tag = CmdEditConfig });

            _menu.Items.Add(new ToolStripSeparator());

            // ═ 退出 ═══════════════
            _menu.Items.Add(new ToolStripMenuItem("❌ 退出") { Tag = CmdQuit });
        }
        finally
        {
            _rebuilding = false;
        }
    }

    // ── 菜单项点击 ─────────────────────────────────

    private void OnMenuItemClicked(object? sender, ToolStripItemClickedEventArgs e)
    {
        var tag = e.ClickedItem?.Tag as string;
        if (string.IsNullOrEmpty(tag)) return;

        Logger.Info($"菜单点击: {tag}");
        _menu.Close();

        switch (tag)
        {
            case CmdRefresh:
                _adapters = NicManager.ListAdapters();
                return;

            case CmdTogglePhysical:
                _config.ShowPhysicalOnly = !_config.ShowPhysicalOnly;
                ConfigManager.Save(_config);
                return;

            case CmdEditConfig:
                OpenConfigFile();
                return;

            case CmdQuit:
                Logger.Info("用户退出");
                _trayIcon.Visible = false;
                Application.Exit();
                return;
        }

        if (tag.StartsWith(PrefixToggle))
        {
            var name = tag[PrefixToggle.Length..];
            var adapter = _adapters.FirstOrDefault(a => a.Name == name);
            if (adapter != null)
            {
                Logger.Info($"切换网卡: {name}");
                if (NicManager.ToggleAdapter(name, adapter.State))
                {
                    Thread.Sleep(500);
                    _adapters = NicManager.ListAdapters();
                }
            }
            return;
        }

        if (tag.StartsWith(PrefixProfile))
        {
            var profileName = tag[PrefixProfile.Length..];
            var profile = _config.Profiles.FirstOrDefault(p => p.Name == profileName);
            if (profile != null)
            {
                ExecuteProfile(profile);
            }
            return;
        }
    }

    // ── 执行 Profile ──────────────────────────────

    private void ExecuteProfile(Profile profile)
    {
        Logger.Info($"执行 Profile: {profile.Name}");

        foreach (var action in profile.Actions)
        {
            var success = action.Action.ToLowerInvariant() switch
            {
                "enable" => NicManager.EnableAdapter(action.Name),
                "disable" => NicManager.DisableAdapter(action.Name),
                _ => false,
            };

            if (success)
                Logger.Info($"  ✓ {action.Action} {action.Name}");
            else
                Logger.Error($"  ✗ {action.Action} {action.Name}");

            Thread.Sleep(300);
        }

        Thread.Sleep(500);
        _adapters = NicManager.ListAdapters();
    }

    // ── 编辑配置 ──────────────────────────────────

    private void OpenConfigFile()
    {
        var configPath = Path.Combine(ConfigManager.ConfigDirectory, "config.json");
        Logger.Info($"打开配置: {configPath}");

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", configPath)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"打开配置失败: {ex.Message}");
        }
    }

    // ── 自动刷新 ──────────────────────────────────

    private void AutoRefresh()
    {
        try
        {
            var current = NicManager.ListAdapters();
            var changed = current.Count != _adapters.Count
                       || current.Zip(_adapters).Any(p => p.First.Name != p.Second.Name || p.First.State != p.Second.State);

            if (changed)
            {
                Logger.Info("自动刷新: 网卡状态变更");
                _adapters = current;
                RebuildMenu();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"自动刷新异常: {ex.Message}");
        }
    }

    // ── 释放资源 ──────────────────────────────────

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _menuOwner?.Close();
        _menuOwner?.Dispose();
        _trayIcon?.Dispose();
        _menu?.Dispose();
    }
}