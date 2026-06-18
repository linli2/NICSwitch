/// 托盘图标和菜单管理

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace NICSwitch;

// ── Win11 原生窗口圆角 ──────────────────────────

internal static class NativeMethods
{
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_ROUND = 2;
}

/// <summary>带 Win11 圆角的 ContextMenuStrip</summary>
public class Win11ContextMenuStrip : ContextMenuStrip
{
    public Win11ContextMenuStrip()
    {
        // 默认字体
        Font = new Font("Segoe UI", 9F);
        // 右侧留白
        Padding = new Padding(0, 4, 0, 4);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Environment.OSVersion.Version.Major >= 10)
        {
            int preference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(
                Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference, sizeof(int));
        }
    }
}

// ── Win11 风格渲染器 ────────────────────────────

public class Win11Renderer : ToolStripProfessionalRenderer
{
    public Win11Renderer() : base(new Win11ColorTable()) { }
}

public class Win11ColorTable : ProfessionalColorTable
{
    // Win11 浅色主题色板
    public override Color MenuItemSelected => Color.FromArgb(0xE5, 0xF0, 0xFF);
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(0xE5, 0xF0, 0xFF);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(0xE5, 0xF0, 0xFF);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(0xE5, 0xF0, 0xFF);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(0xE5, 0xF0, 0xFF);
    public override Color ToolStripDropDownBackground => Color.White;
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
    public override Color SeparatorDark => Color.FromArgb(0xE0, 0xE0, 0xE0);
    public override Color SeparatorLight => Color.Transparent;

    // 标题行（禁用项）灰色
    public override Color ToolStripBorder => Color.Transparent;
}

public class TrayManager : IDisposable
{
    private readonly NotifyIcon _trayIcon;
    private readonly Win11ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private Config _config;
    private List<NetworkAdapter> _adapters = new();
    private bool _rebuilding;
    /// 右键点击网卡项时设此标志，跳过 ItemClicked 中的切换逻辑
    private bool _skipNextAdapterClick;

    // ── 菜单项 ID 常量 ────────────────────────────
    private const string CmdQuit = "__quit__";
    private const string PrefixToggle = "toggle:";
    private const string PrefixProfile = "profile:";

    public TrayManager()
    {
        _config = ConfigManager.Load();
        _adapters = NicManager.ListAdapters();

        // Win11 风格渲染
        _menu.Renderer = new Win11Renderer();

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

    // ── 彩色状态圆点图标 ──────────────────────────

    /// <summary>绿色圆点 (启用)</summary>
    private static Bitmap GreenDot() => MakeDot(Color.FromArgb(0x00, 0xC8, 0x53));
    /// <summary>红色圆点 (禁用)</summary>
    private static Bitmap RedDot() => MakeDot(Color.FromArgb(0xFF, 0x33, 0x33));

    private static Bitmap MakeDot(Color fill)
    {
        var bmp = new Bitmap(14, 14);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        using var brush = new SolidBrush(fill);
        g.FillEllipse(brush, 1, 1, 12, 12);
        return bmp;
    }

    // ── 创建 Win11 风格弹出菜单 ──────────────────

    private Win11ContextMenuStrip CreatePopupMenu()
    {
        var popup = new Win11ContextMenuStrip
        {
            Renderer = new Win11Renderer(),
        };
        return popup;
    }

    // ── 鼠标点击 ──────────────────────────────────

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        // 每次点击刷新网卡状态并重建菜单
        _adapters = NicManager.ListAdapters();
        RebuildMenu();

        if (e.Button == MouseButtons.Left)
        {
            Logger.Info("左键单击 -> 弹出菜单");
            var method = typeof(NotifyIcon).GetMethod("ShowContextMenu",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(_trayIcon, null);
        }
    }

    // ── 菜单打开时刷新菜单内容 ────────────────────

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
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
            AddSectionHeader("网卡列表");

            var displayAdapters = _adapters;

            if (displayAdapters.Count == 0)
            {
                _menu.Items.Add(new ToolStripMenuItem("未发现网卡") { Enabled = false });
            }
            else
            {
                foreach (var adapter in displayAdapters)
                {
                    var name = adapter.Name; // capture for closure
                    var isEnabled = adapter.State == NicState.Enabled;
                    var item = new ToolStripMenuItem(" " + adapter.Name)
                    {
                        Tag = PrefixToggle + name,
                        Image = isEnabled ? GreenDot() : RedDot(),
                        ForeColor = isEnabled
                            ? SystemColors.ControlText
                            : Color.FromArgb(0xC0, 0x00, 0x00),  // 深红色
                        Font = isEnabled
                            ? new Font(_menu.Font, FontStyle.Bold)
                            : _menu.Font,
                        // 增加项目高度
                        Height = 26,
                    };

                    // 右键 -> 弹出操作菜单
                    item.MouseDown += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Right)
                        {
                            _skipNextAdapterClick = true;

                            var popup = CreatePopupMenu();
                            popup.Items.Add("启用/禁用", null, (_, _) =>
                            {
                                Logger.Info($"切换网卡: {name}");
                                if (NicManager.ToggleAdapter(name, adapter.State))
                                {
                                    Thread.Sleep(500);
                                    _adapters = NicManager.ListAdapters();
                                }
                            });
                            popup.Items.Add("状态", null, (_, _) => ShowAdapterStatus(name));
                            popup.Items.Add("属性", null, (_, _) => OpenAdapterProperties(name));

                            popup.Show(Cursor.Position);
                        }
                    };

                    _menu.Items.Add(item);
                }
            }

            _menu.Items.Add(new ToolStripSeparator());

            // ═ 快速切换 Profiles ═══
            if (_config.Profiles.Count > 0)
            {
                AddSectionHeader("快速切换");

                foreach (var profile in _config.Profiles)
                {
                    var item = new ToolStripMenuItem(profile.Name)
                    {
                        Tag = PrefixProfile + profile.Name,
                        Height = 26,
                    };
                    _menu.Items.Add(item);
                }

                _menu.Items.Add(new ToolStripSeparator());
            }

            // ═ 设置 ═══════════════
            AddSectionHeader("设置");

            AddActionItem("网卡设置", (_, _) => OpenNcpaCpl());
            AddActionItem("刷新状态", (_, _) =>
            {
                _adapters = NicManager.ListAdapters();
            });
            AddActionItem("编辑配置", (_, _) => OpenConfigFile());

            _menu.Items.Add(new ToolStripSeparator());

            // ═ 退出 ═══════════════
            _menu.Items.Add(new ToolStripMenuItem("退出") { Tag = CmdQuit, Height = 26 });
        }
        finally
        {
            _rebuilding = false;
        }
    }

    // ── 辅助：添加分组标题 ────────────────────────

    private void AddSectionHeader(string text)
    {
        _menu.Items.Add(new ToolStripMenuItem(text)
        {
            Enabled = false,
            Font = new Font(_menu.Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x70, 0x70, 0x70),
            Height = 22,
        });
    }

    // ── 辅助：添加操作项 ──────────────────────────

    private void AddActionItem(string text, EventHandler onClick)
    {
        _menu.Items.Add(new ToolStripMenuItem(text, null, onClick)
        {
            Height = 26,
        });
    }

    // ── 菜单项点击 ─────────────────────────────────

    private void OnMenuItemClicked(object? sender, ToolStripItemClickedEventArgs e)
    {
        var tag = e.ClickedItem?.Tag as string;
        if (string.IsNullOrEmpty(tag)) return;

        Logger.Info($"菜单点击: {tag}");

        switch (tag)
        {
            case CmdQuit:
                Logger.Info("用户退出");
                _trayIcon.Visible = false;
                Application.Exit();
                return;
        }

        if (tag.StartsWith(PrefixToggle))
        {
            // 右键触发的 MouseDown 已设标志，跳过此次点击
            if (_skipNextAdapterClick)
            {
                _skipNextAdapterClick = false;
                return;
            }

            var name = tag[PrefixToggle.Length..];
            var adapter = _adapters.FirstOrDefault(a => a.Name == name);
            if (adapter != null)
            {
                Logger.Info($"左键切换网卡: {name}");
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

    // ── 打开网卡设置 (ncpa.cpl) ────────────────────

    private static void OpenNcpaCpl()
    {
        try
        {
            Logger.Info("打开网卡设置 (ncpa.cpl)");
            Process.Start(new ProcessStartInfo("control", "ncpa.cpl")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"打开网卡设置失败: {ex.Message}");
        }
    }

    // ── 显示网卡状态 ──────────────────────────────

    private void ShowAdapterStatus(string name)
    {
        try
        {
            Logger.Info($"查看网卡状态: {name}");
            var adapter = _adapters.FirstOrDefault(a => a.Name == name);
            if (adapter == null)
            {
                MessageBox.Show($"未找到网卡: {name}", "NICSwitch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var (exitCode, stdout, stderr) = NicManager.RunNetsh($"interface ip show address \"{name}\"");
            var ipInfo = exitCode == 0 ? stdout.Trim() : $"获取失败: {stderr.Trim()}";

            var stateText = adapter.State switch
            {
                NicState.Enabled => "[已启用]",
                NicState.Disabled => "[已禁用]",
                _ => "[未知]",
            };
            var typeText = adapter.IsHardware ? "物理网卡" : "虚拟网卡";

            var sb = new StringBuilder();
            sb.AppendLine($"网卡: {adapter.Name}");
            sb.AppendLine($"状态: {stateText}");
            sb.AppendLine($"类型: {typeText}");
            sb.AppendLine();
            sb.AppendLine("-- IP 配置 --");
            sb.Append(ipInfo);

            MessageBox.Show(sb.ToString(), "NICSwitch - 网卡状态", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error($"显示网卡状态失败: {ex.Message}");
        }
    }

    // ── 打开网卡属性 ──────────────────────────────

    private static void OpenAdapterProperties(string name)
    {
        try
        {
            Logger.Info($"打开网卡属性: {name}");
            Process.Start(new ProcessStartInfo("control", "ncpa.cpl")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"打开网卡属性失败: {ex.Message}");
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
                Logger.Info($"  OK {action.Action} {action.Name}");
            else
                Logger.Error($"  FAIL {action.Action} {action.Name}");

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
