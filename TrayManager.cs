/// 托盘图标和菜单管理

using System.Diagnostics;
using System.Reflection;

namespace NICSwitch;

public class TrayManager : IDisposable
{
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private Config _config;
    private List<NetworkAdapter> _adapters = new();
    private bool _rebuilding;

    // ── 菜单项 ID 常量 ────────────────────────────
    private const string CmdRefresh = "__refresh__";
    private const string CmdEditConfig = "__edit_config__";
    private const string CmdQuit = "__quit__";
    private const string PrefixToggle = "toggle:";
    private const string PrefixProfile = "profile:";

    public TrayManager()
    {
        _config = ConfigManager.Load();
        _adapters = NicManager.ListAdapters();

        // ── 美化菜单渲染 ──
        _menu.Renderer = new ModernMenuRenderer();
        _menu.Font = new Font("Microsoft YaHei UI", 9F);

        // 托盘图标（优先加载同目录下 app.ico）
        var icon = LoadTrayIcon();
        _trayIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "NICSwitch - 网卡切换",
            Visible = true,
        };

        // 右键：系统原生弹出，自动关闭
        _trayIcon.ContextMenuStrip = _menu;

        // 左键：通过反射调用 NotifyIcon.ShowContextMenu()
        // 这是 .NET 内部方法，行为与右键完全一致
        _trayIcon.MouseClick += OnTrayMouseClick;

        // 菜单打开时刷新数据
        _menu.Opening += OnMenuOpening;

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

    // ── 加载自定义图标 ────────────────────────────

    private static Icon LoadTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                var icon = new Icon(iconPath);
                Logger.Info($"已加载自定义图标: {iconPath}");
                return icon;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"加载图标失败: {ex.Message}");
        }
        return SystemIcons.Application;
    }

    // ── 鼠标点击 ──────────────────────────────────

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        // 每次点击刷新网卡状态并重建菜单
        _adapters = NicManager.ListAdapters();
        RebuildMenu();

        if (e.Button == MouseButtons.Left)
        {
            Logger.Info("左键单击 → 弹出菜单");
            // 调用 NotifyIcon 内部 ShowContextMenu()
            // 该方法触发 TrackPopupMenu，行为与右键完全一致（自动关闭）
            var method = typeof(NotifyIcon).GetMethod("ShowContextMenu",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(_trayIcon, null);
        }
    }

    // ── 菜单打开时刷新菜单内容 ────────────────────

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 右键打开菜单时刷新数据（左键在 OnTrayMouseClick 已刷新）
        _adapters = NicManager.ListAdapters();
        RebuildMenu();
    }

    // ── 构建菜单（全量重建） ──────────────────────

    private void RebuildMenu()
    {
        if (_rebuilding) return;
        _rebuilding = true;

        try
        {
            _menu.Items.Clear();

            // ═ 网卡列表 ═══════════
            _menu.Items.Add(new ToolStripMenuItem("📡 网卡列表") { Enabled = false });

            var displayAdapters = _adapters;

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
        // 菜单会自动关闭（原生 TrackPopupMenu），不需要手动 Close

        switch (tag)
        {
            case CmdRefresh:
                _adapters = NicManager.ListAdapters();
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
        _trayIcon?.Dispose();
        _menu?.Dispose();
    }
}

/// 现代风格菜单渲染器（美化菜单外观）
public class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    public ModernMenuRenderer() : base(new ModernColorTable()) { }
}

public class ModernColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => Color.FromArgb(0xE3, 0xF0, 0xFD);       // 悬停浅蓝
    public override Color MenuItemBorder => Color.FromArgb(0x99, 0xCF, 0xF8);          // 悬停边框
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(0xE3, 0xF0, 0xFD);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(0xE3, 0xF0, 0xFD);
    public override Color ToolStripDropDownBackground => Color.White;                   // 白底
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
}