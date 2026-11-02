using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class WindowsDpi
{
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int awareness);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    internal static void Initialize()
    {
        // The embedded manifest normally sets PMv2 before Main. API calls also
        // support rebuilt binaries and older Windows without a sidecar config.
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); return; }
        catch (EntryPointNotFoundException) { }
        try { SetProcessDpiAwareness(2); return; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        SetProcessDPIAware();
    }
    internal static int ForWindow(IntPtr window)
    {
        try { uint dpi = GetDpiForWindow(window); if (dpi > 0) return (int)dpi; }
        catch (EntryPointNotFoundException) { }
        using (Graphics graphics = Graphics.FromHwnd(window)) return (int)Math.Round(graphics.DpiX);
    }
    internal static bool PerMonitorV2()
    {
        try { return AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), new IntPtr(-4)); }
        catch (EntryPointNotFoundException) { return false; }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct SuggestedBounds { internal int Left, Top, Right, Bottom; }
}

internal static class OriginalSteamSdk
{
    // Verify the Authenticode digest as well as the signer; metadata alone can
    // label a modified/emulated DLL as Valve. Offline cache avoids network waits.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    { internal uint Size; [MarshalAs(UnmanagedType.LPWStr)] internal string Path; internal IntPtr Handle, KnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        internal uint Size; internal IntPtr Policy, Sip;
        internal uint UiChoice, RevocationChecks, UnionChoice; internal IntPtr File;
        internal uint StateAction; internal IntPtr StateData, UrlReference;
        internal uint ProviderFlags, UiContext; internal IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    internal static bool IsOriginal(string path)
    {
        if (!System.IO.File.Exists(path)) return false;
        IntPtr filePointer = IntPtr.Zero;
        try
        {
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
            if (version.CompanyName == null || version.CompanyName.IndexOf("Valve", StringComparison.OrdinalIgnoreCase) < 0 ||
                (version.OriginalFilename != "steam_api.dll" && version.OriginalFilename != "steam_api64.dll")) return false;
            TrustFile file = new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = System.IO.Path.GetFullPath(path) };
            filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            Marshal.StructureToPtr(file, filePointer, false);
            TrustData data = new TrustData { Size = (uint)Marshal.SizeOf(typeof(TrustData)), UiChoice = 2, UnionChoice = 1, File = filePointer, ProviderFlags = 4096 };
            Guid action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
            using (X509Certificate certificate = X509Certificate.CreateFromSignedFile(path))
                return Regex.IsMatch(certificate.Subject, @"(?:^|,\s*)O=Valve(?: Corp\.)?(?:,|$)", RegexOptions.IgnoreCase);
        }
        catch (CryptographicException) { return false; }
        catch (IOException) { return false; }
        finally
        {
            if (filePointer != IntPtr.Zero) { Marshal.DestroyStructure(filePointer, typeof(TrustFile)); Marshal.FreeHGlobal(filePointer); }
        }
    }
    internal static bool EnsureBackup(string root)
    {
        string backup = Path.Combine(root, "SeamlessCoop", "network", "steam_api64.steam.dll");
        if (File.Exists(backup)) return false; // Never overwrite an existing original.
        string current = Path.Combine(root, "steam_api64.dll");
        if (!IsOriginal(current)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        File.Copy(current, backup, false);
        if (!IsOriginal(backup)) throw new IOException("原 Steam DLL 自动备份校验失败，已停止启动。请检查文件。 ");
        return true;
    }
    internal static bool Restore(string root)
    {
        if (Process.GetProcessesByName("eldenring").Length > 0)
            throw new InvalidOperationException("请先退出《艾尔登法环》，再恢复原 Steam 接口。");
        string backup = Path.Combine(root, "SeamlessCoop", "network", "steam_api64.steam.dll");
        if (!File.Exists(backup)) throw new FileNotFoundException("没有找到原 Steam 接口备份。若之前已使用公网接口，请从自己的正版安装或原备份中找回原文件。 ");
        if (!IsOriginal(backup)) throw new InvalidOperationException("备份未通过原 Valve Steam 接口校验，未覆盖根目录文件。请使用未修改的原接口备份。");
        string target = Path.Combine(root, "steam_api64.dll");
        if (File.Exists(target) && SameFile(target, backup)) return false;
        File.Copy(backup, target, true);
        if (!SameFile(target, backup)) throw new IOException("恢复文件校验失败，请检查目录写入权限。");
        return true;
    }
    private static bool SameFile(string first, string second)
    {
        using (SHA256 sha = SHA256.Create())
        using (Stream a = File.OpenRead(first))
        using (Stream b = File.OpenRead(second))
            return Convert.ToBase64String(sha.ComputeHash(a)) == Convert.ToBase64String(sha.ComputeHash(b));
    }
}

// Native .NET Framework / WinForms. No scripts, tunnel installation or elevation.
internal sealed class IniFile
{
    private readonly string path;
    private readonly List<string> lines;
    private readonly string newline;
    private readonly bool bom;
    internal IniFile(string path)
    {
        this.path = path;
        byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : new byte[0];
        string text;
        if (bytes.Length >= 2 && ((bytes[0] == 255 && bytes[1] == 254) || (bytes[0] == 254 && bytes[1] == 255)))
            throw new IOException("配置文件采用 UTF-16，请先另存为 UTF-8：" + path);
        bom = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191;
        text = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        newline = text.Contains("\r\n") ? "\r\n" : (text.Contains("\n") ? "\n" : "\r\n");
        lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
        if (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
    }
    private static string Section(string line)
    {
        Match m = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*(?:[;#].*)?$");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
    private static Match Entry(string line)
    { return Regex.Match(line, @"^\s*([^=;#\s][^=]*?)\s*=\s*(.*)$"); }
    internal string Get(string section, string key, string fallback)
    {
        string current = "";
        foreach (string line in lines)
        {
            string next = Section(line);
            if (next != null) { current = next; continue; }
            Match m = Entry(line);
            if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && m.Success && m.Groups[1].Value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return Regex.Replace(m.Groups[2].Value, @"\s+[;#].*$", "").Trim();
        }
        return fallback;
    }
    internal void Set(string section, string key, string value)
    {
        if (value.IndexOfAny(new char[] { '\r', '\n', '\0' }) >= 0) throw new ArgumentException("配置值不能包含换行。");
        string current = "";
        int insert = -1;
        bool found = false;
        for (int i = 0; i < lines.Count; i++)
        {
            string next = Section(lines[i]);
            if (next != null)
            {
                if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && insert < 0) insert = i;
                current = next;
                continue;
            }
            Match m = Entry(lines[i]);
            if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && m.Success && m.Groups[1].Value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                if (found) { lines.RemoveAt(i--); if (insert >= 0) insert--; continue; }
                string previous = Get(section, key, "");
                if (previous != value)
                {
                    Match comment = Regex.Match(m.Groups[2].Value, @"\s+[;#].*$");
                    lines[i] = key + " = " + value + (comment.Success ? comment.Value : "");
                }
                found = true;
            }
        }
        if (found) return;
        if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && insert < 0) insert = lines.Count;
        if (insert < 0)
        {
            if (lines.Count > 0 && lines[lines.Count - 1] != "") lines.Add("");
            lines.Add("[" + section + "]");
            insert = lines.Count;
        }
        lines.Insert(insert, key + " = " + value);
    }
    internal bool Save()
    {
        string text = String.Join(newline, lines.ToArray()) + newline;
        UTF8Encoding encoding = new UTF8Encoding(bom);
        byte[] body = encoding.GetBytes(text), preamble = encoding.GetPreamble();
        byte[] data = new byte[body.Length + preamble.Length];
        Buffer.BlockCopy(preamble, 0, data, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, data, preamble.Length, body.Length);
        if (File.Exists(path))
        {
            byte[] old = File.ReadAllBytes(path);
            if (Convert.ToBase64String(old) == Convert.ToBase64String(data)) return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            if (File.Exists(path))
            {
                string backup = path + ".launcher.bak";
                if (!File.Exists(backup)) File.Copy(path, backup);
                File.Replace(temporary, path, null);
            }
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }
    internal void Remove(string section, string key)
    {
        string current = "";
        for (int i = 0; i < lines.Count; i++)
        {
            string next = Section(lines[i]);
            if (next != null) { current = next; continue; }
            Match m = Entry(lines[i]);
            if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && m.Success && m.Groups[1].Value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) lines.RemoveAt(i--);
        }
    }
}

internal sealed class LauncherConfig
{
    internal const string RelayHash = "723e13f5a2e1ec799f342612a3294229ca832d143fdea85c00025216b6696e88";
    internal readonly string Root, ModPath, UserPath, Network;
    internal readonly Dictionary<string, object> Client;
    internal LauncherConfig(string root)
    {
        Root = root; Network = Path.Combine(root, "SeamlessCoop", "network");
        ModPath = Path.Combine(root, "SeamlessCoop", "ersc_settings.ini");
        UserPath = Path.Combine(root, "steam_settings", "configs.user.ini");
        string json = Path.Combine(Network, "client.json");
        Client = File.Exists(json) ? new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(json)) : new Dictionary<string, object>();
    }
    internal string Default(string key, string fallback)
    { return Client.ContainsKey(key) ? Convert.ToString(Client[key]) : fallback; }
    internal static string ValidateEndpoint(string host, string port)
    {
        int number;
        UriHostNameType type = Uri.CheckHostName(host);
        if (String.IsNullOrWhiteSpace(host) || (type != UriHostNameType.IPv4 && type != UriHostNameType.Dns)) return "服务器地址请填写 IPv4 或域名，不要加 http:// 或端口。";
        if (!Int32.TryParse(port, out number) || number < 1 || number > 65535) return "端口必须是 1–65535 之间的整数。";
        return null;
    }
    internal static bool ValidSteamId(string id)
    {
        if (id == "") return true;
        ulong value;
        return Regex.IsMatch(id, @"^\d+$") && UInt64.TryParse(id, out value) &&
            (value >> 56) == 1 && ((value >> 52) & 15) == 1 && ((value >> 32) & 1048575) == 1 && (value & 4294967295UL) != 0;
    }
    internal IniFile UserWithFallback()
    { return new IniFile(UserPath); }
    internal string UserValue(IniFile user, string key, string fallback)
    {
        string local = user.Get("user::general", key, null);
        if (local != null) return local;
        string storage = user.Get("user::saves", "local_save_path", "");
        if (storage == "") storage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GSE Saves");
        else if (!Path.IsPathRooted(storage)) storage = Path.Combine(Root, storage);
        string global = Path.Combine(storage, "settings", "configs.user.ini");
        return File.Exists(global) ? new IniFile(global).Get("user::general", key, fallback) : fallback;
    }
    internal void Launch(bool relay, string host, string port, string password)
    {
        if (String.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("请先填写联机密码。所有队友需要使用相同密码。");
        if (Process.GetProcessesByName("eldenring").Length > 0) throw new InvalidOperationException("请先退出正在运行的《艾尔登法环》，再启动或切换网络模式。");
        if (!File.Exists(Path.Combine(Root, "eldenring.exe"))) throw new FileNotFoundException("请将启动器放到含有 eldenring.exe 的 Game 目录。");
        string original = Path.Combine(Root, "ersc_launcher.original.exe");
        if (!File.Exists(original)) throw new FileNotFoundException("缺少 ersc_launcher.original.exe，请复制完整 Mod 文件。");
        // Snapshot the genuine root DLL before any mode can replace it.
        // Already-emulated installs can still use relay mode without falsely
        // creating a Steam backup from their emulator.
        OriginalSteamSdk.EnsureBackup(Root);
        IniFile ini = new IniFile(ModPath);
        string tunnel = ini.Get("NETWORK", "wireguard_tunnel", Default("tunnel_name", ""));
        if (relay)
        {
            string error = ValidateEndpoint(host, port);
            if (error != null) throw new InvalidOperationException(error);
            string legacy = Default("legacy_hub_name", "");
            if (Regex.IsMatch(legacy, "^erhub[0-9a-f]{6}$") && Running(legacy))
                throw new InvalidOperationException("旧本地中继仍在运行。请在服务管理器停止 WireGuard Tunnel: " + legacy + "，再激活个人隧道。");
            if (tunnel != "" && !Running(tunnel)) throw new InvalidOperationException("请先在 WireGuard 激活隧道：" + tunnel);
            if (tunnel == "" && !AnyWireGuard()) throw new InvalidOperationException("请先在 WireGuard 导入并激活自己的 .conf，再点击启动游戏。");
        }
        else if (tunnel != "" && Running(tunnel)) throw new InvalidOperationException("已选择 Steam 通道，请先在 WireGuard 停用个人游戏隧道。 ");
        string selected = Path.Combine(Network, relay ? "steam_api64.relay.dll" : "steam_api64.steam.dll");
        if (!File.Exists(selected)) throw new FileNotFoundException(relay ? "缺少公网接口缓存 steam_api64.relay.dll，请复制完整 Mod 文件。" : "缺少原 Steam 接口备份。请将原 Valve DLL 复制到 SeamlessCoop/network/steam_api64.steam.dll。 ");
        if (relay && Hash(selected) != Default("gse_sha256", RelayHash)) throw new InvalidOperationException("公网接口 DLL 与已测试版本不一致，请使用配套 DLL。");
        if (!relay && !OriginalSteamSdk.IsOriginal(selected)) throw new InvalidOperationException("原 Steam 接口备份未通过 Valve 签名校验，请使用未修改的正版 DLL。");
        string target = Path.Combine(Root, "steam_api64.dll");
        if (!File.Exists(target) || Hash(target) != Hash(selected)) File.Copy(selected, target, true);
        if (ini.Get("NETWORK", "block_mod_update_checks", "1") != "0") ModUpdateGuard.Launch(Root);
        else Process.Start(new ProcessStartInfo(original) { WorkingDirectory = Root, UseShellExecute = true });
    }
    private static bool Running(string name)
    { try { using (ServiceController service = new ServiceController("WireGuardTunnel$" + name)) return service.Status == ServiceControllerStatus.Running; } catch (InvalidOperationException) { return false; } }
    private static bool AnyWireGuard()
    {
        ServiceController[] services = ServiceController.GetServices();
        bool found = false;
        foreach (ServiceController service in services)
        { try { if (service.ServiceName.StartsWith("WireGuardTunnel$", StringComparison.OrdinalIgnoreCase) && service.Status == ServiceControllerStatus.Running) found = true; } finally { service.Dispose(); } }
        return found;
    }
    private static string Hash(string path)
    { using (Stream file = File.OpenRead(path)) using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
}

internal sealed class LanguageChoice
{
    internal readonly string Code, Label;
    internal LanguageChoice(string label, string code) { Label = label; Code = code; }
    public override string ToString() { return Label; }
}

// Ignore table-cell width when measuring captions: a narrow column must not
// turn a setting name into two lines. Long status messages use an ellipsis.
internal sealed class SingleLineLabel : Label
{
    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
        return new Size(text.Width + Padding.Horizontal, text.Height + Padding.Vertical);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        if (AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Enabled ? ForeColor : SystemColors.GrayText, flags);
    }
}

internal sealed class SettingsWindow : Form
{
    private readonly LauncherConfig config;
    private bool loading = true, dirty = false;
    private readonly Timer autosave = new Timer();
    internal readonly RadioButton Steam = new RadioButton(), Relay = new RadioButton();
    internal readonly TextBox Server = new TextBox(), Port = new TextBox(), Password = new TextBox(), Username = new TextBox(), SteamId = new TextBox();
    internal readonly ComboBox Language = new ComboBox();
    internal readonly CheckBox BlockModUpdates = new CheckBox();
    private readonly GroupBox relayGroup = new GroupBox();
    private readonly Label status = new SingleLineLabel();
    private readonly ToolTip tips = new ToolTip();
    private Icon launcherIcon;
    private readonly Button launch = new Button();
    private readonly Dictionary<string, CheckBox> flags = new Dictionary<string, CheckBox>();
    private readonly Dictionary<string, NumericUpDown> scaling = new Dictionary<string, NumericUpDown>();
    private readonly ComboBox overhead = new ComboBox();
    private readonly NumericUpDown volume = new NumericUpDown();
    private readonly Color muted = SystemColors.GrayText;
    private int monitorDpi = 96;
    protected override void Dispose(bool disposing)
    {
        if (disposing && launcherIcon != null) { launcherIcon.Dispose(); launcherIcon = null; }
        base.Dispose(disposing);
    }
    protected override void OnHandleCreated(EventArgs e)
    { base.OnHandleCreated(e); monitorDpi = WindowsDpi.ForWindow(Handle); }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg != 0x02E0) { base.WndProc(ref message); return; }
        // Framework 4.x versions differ in whether they resize children without
        // app.config. Handle the monitor transition explicitly when needed.
        int nextDpi = (int)(message.WParam.ToInt64() & 65535);
        int previousDpi = monitorDpi;
        int beforePadding = Controls.Count > 0 ? Controls[0].Padding.Horizontal : -1;
        WindowsDpi.SuggestedBounds bounds = (WindowsDpi.SuggestedBounds)Marshal.PtrToStructure(message.LParam, typeof(WindowsDpi.SuggestedBounds));
        base.WndProc(ref message);
        monitorDpi = nextDpi;
        if (nextDpi > 0 && previousDpi > 0 && nextDpi != previousDpi)
        {
            SuspendLayout();
            try
            {
                if (Controls.Count > 0 && Controls[0].Padding.Horizontal == beforePadding)
                {
                    Scale(new SizeF((float)nextDpi / previousDpi, (float)nextDpi / previousDpi));
                    AutoScaleDimensions = new SizeF(nextDpi, nextDpi);
                }
                Bounds = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            }
            finally { ResumeLayout(true); }
        }
    }
    internal SettingsWindow(string root)
    {
        // Build the whole 96-DPI tree before WinForms applies its first scale.
        // Incremental layout otherwise scales the empty form and leaves controls
        // added later at 96-DPI sizes (notably footer rows and minimum widths).
        SuspendLayout();
        config = new LauncherConfig(root);
        Text = "Seamless Co-op 设置";
        using (Stream iconStream = typeof(SettingsWindow).Assembly.GetManifestResourceStream("SeamlessCoop.LauncherIcon"))
        {
            if (iconStream != null)
                using (Icon embeddedIcon = new Icon(iconStream))
                { launcherIcon = (Icon)embeddedIcon.Clone(); Icon = launcherIcon; }
        }
        Font = SystemFonts.MessageBoxFont;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(660, 680);
        MinimumSize = new Size(600, 400);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Control;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        Controls.Add(layout);
        var header = new SingleLineLabel { Text = "设置会自动保存。完成后点击“启动游戏”。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(header, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var networkTab = new TabPage("启动与联机") { BackColor = SystemColors.Control, Padding = new Padding(10) };
        var gameplayTab = new TabPage("玩法设置") { BackColor = SystemColors.Control, Padding = new Padding(10) };
        var restoreTab = new TabPage("正版steamapi64恢复") { BackColor = SystemColors.Control, Padding = new Padding(16) };
        var savesTab = new TabPage("存档管理") { BackColor = SystemColors.Control, Padding = new Padding(6) };
        tabs.TabPages.Add(networkTab); tabs.TabPages.Add(gameplayTab); tabs.TabPages.Add(restoreTab); tabs.TabPages.Add(savesTab);
        savesTab.Enter += delegate {
            if (savesTab.Controls.Count > 0) return;
            string id = SteamId.Text.Trim();
            if (id.Length != 17 || !LauncherConfig.ValidSteamId(id))
            {
                string saves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EldenRing");
                if (Directory.Exists(saves)) foreach (string folder in Directory.GetDirectories(saves))
                    if (Path.GetFileName(folder).Length == 17 && LauncherConfig.ValidSteamId(Path.GetFileName(folder))) { id = Path.GetFileName(folder); break; }
            }
            savesTab.Controls.Add(new SaveManagerPage(id));
        };
        var restoreButton = new Button { Text = "恢复原steamapi64", AutoSize = true, UseVisualStyleBackColor = true, Padding = new Padding(12, 4, 12, 4), Location = new Point(16, 16) };
        restoreButton.Click += delegate {
            try
            {
                bool restored = OriginalSteamSdk.Restore(config.Root);
                MessageBox.Show(this, restored ? "已将原 Steam 接口恢复到 Game\\steam_api64.dll。没有启动游戏，也没有更改网络模式配置。" : "根目录已经是备份中的原 Steam 接口，无需再次恢复。", "恢复完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error is UnauthorizedAccessException ? "无法写入游戏目录。请以管理员身份打开启动器，再恢复原 Steam 接口。" : error.Message, "无法恢复", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        restoreTab.Controls.Add(restoreButton);
        tabs.SelectedIndexChanged += delegate { launch.Visible = tabs.SelectedIndex < 2; };
        layout.Controls.Add(tabs, 0, 1);
        // A scroll container surrounds an auto-sized document. Fixed-height rows
        // used to clip controls when Windows scaled the font or window shrank.
        var networkScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Name = "NetworkScroll" };
        var networkLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3 };
        for (int i = 0; i < 3; i++) networkLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        networkScroll.Controls.Add(networkLayout); networkTab.Controls.Add(networkScroll);
        var modes = new GroupBox { Text = "连接方式", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 8, 12, 8) };
        var modeRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 0, 4) };
        Steam.Text = "Steam 通道"; Steam.AutoSize = true; Steam.Name = "SteamMode";
        Relay.Text = "自建服务器"; Relay.AutoSize = true; Relay.Name = "RelayMode";
        Steam.Margin = new Padding(0, 0, 40, 0);
        modeRow.Controls.Add(Steam); modeRow.Controls.Add(Relay); modes.Controls.Add(modeRow);
        networkLayout.Controls.Add(modes, 0, 0);
        var shared = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 4, Padding = new Padding(0, 8, 0, 8) };
        for (int i = 0; i < 4; i++) shared.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shared.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        shared.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shared.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        shared.Controls.Add(FieldLabel("联机密码"), 0, 0);
        Password.Name = "SessionPassword"; Password.AccessibleName = "联机密码"; Password.Dock = DockStyle.Fill; Password.UseSystemPasswordChar = true;
        shared.Controls.Add(Password, 1, 0);
        var show = new CheckBox { Text = "显示", AutoSize = true, Margin = new Padding(8, 5, 0, 0) };
        show.CheckedChanged += delegate { Password.UseSystemPasswordChar = !show.Checked; };
        shared.Controls.Add(show, 2, 0);
        var passwordHint = Hint("两种通道共用；所有队友填写同一密码。");
        shared.Controls.Add(passwordHint, 0, 1); shared.SetColumnSpan(passwordHint, 3);
        BlockModUpdates.Text = "禁止 Mod 启动联网检查"; BlockModUpdates.AutoSize = true; BlockModUpdates.Name = "BlockModUpdateChecks";
        BlockModUpdates.Margin = new Padding(0, 10, 0, 3);
        tips.SetToolTip(BlockModUpdates, "拦截本次游戏对 raw.githubusercontent.com 的 HTTP/HTTPS 请求。\n不修改系统代理或防火墙；Steam/WireGuard 联机通信保留。\n游戏运行期间有一个无窗口的启动器守卫，游戏退出后自动结束。\n此域名的其他 Mod 检查也会被阻止。关闭后恢复原有启动行为。");
        shared.Controls.Add(BlockModUpdates, 0, 2); shared.SetColumnSpan(BlockModUpdates, 3);
        var updateHint = Hint("拦截 GitHub 检查；联机通信保留。");
        shared.Controls.Add(updateHint, 0, 3); shared.SetColumnSpan(updateHint, 3);
        networkLayout.Controls.Add(shared, 0, 1);
        relayGroup.Text = "自建服务器与玩家身份"; relayGroup.Dock = DockStyle.Top; relayGroup.AutoSize = true; relayGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink; relayGroup.Padding = new Padding(10, 10, 10, 8);
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 9 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++) fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddField(fields, 0, "服务器地址", Server, "服务器地址");
        AddField(fields, 1, "端口", Port, "服务器端口");
        AddField(fields, 2, "用户名", Username, "用户名");
        AddField(fields, 3, "SteamID64", SteamId, "SteamID64");
        Language.DropDownStyle = ComboBoxStyle.DropDownList;
        Language.Items.Add(new LanguageChoice("简体中文", "schinese"));
        Language.Items.Add(new LanguageChoice("繁體中文", "tchinese"));
        Language.Items.Add(new LanguageChoice("English", "english"));
        Language.Items.Add(new LanguageChoice("日本語", "japanese"));
        AddField(fields, 4, "游戏语言", Language, "游戏语言");
        string[] hints = {
            "SteamID 留空：使用兼容层已有的自动身份。",
            "已有角色请保持原 ID；修改会影响存档识别。",
            "启动前请在 WireGuard 激活个人配置。",
            "修改地址或端口后，请同步 WireGuard 的 Endpoint。"
        };
        for (int i = 0; i < hints.Length; i++)
        { var hint = Hint(hints[i]); fields.Controls.Add(hint, 0, 5 + i); fields.SetColumnSpan(hint, 2); }
        relayGroup.Controls.Add(fields); networkLayout.Controls.Add(relayGroup, 0, 2);
        FitScrollDocument(networkScroll, networkLayout, 480);
        BuildGameplay(gameplayTab);
        status.Dock = DockStyle.Fill; status.ForeColor = muted; status.TextAlign = ContentAlignment.MiddleLeft; status.AccessibleName = "保存状态"; status.AutoEllipsis = true;
        status.TextChanged += delegate { tips.SetToolTip(status, status.Text); };
        layout.Controls.Add(status, 0, 2);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        var close = new Button { Text = "关闭", Dock = DockStyle.Fill, UseVisualStyleBackColor = true, Margin = new Padding(0, 0, 8, 0) };
        close.Click += delegate { Close(); };
        launch.Text = "启动游戏"; launch.Dock = DockStyle.Fill; launch.UseVisualStyleBackColor = true; launch.Margin = Padding.Empty;
        launch.Click += delegate { StartGame(); };
        buttons.Controls.Add(close, 1, 0); buttons.Controls.Add(launch, 2, 0); layout.Controls.Add(buttons, 0, 3);
        ReadConfiguration();
        foreach (TextBox box in new TextBox[] { Server, Port, Password, Username, SteamId }) box.TextChanged += Changed;
        Language.SelectedIndexChanged += Changed; Steam.CheckedChanged += ModeChanged; Relay.CheckedChanged += ModeChanged;
        BlockModUpdates.CheckedChanged += Changed;
        foreach (CheckBox box in flags.Values) box.CheckedChanged += Changed;
        foreach (NumericUpDown number in scaling.Values) number.ValueChanged += Changed;
        overhead.SelectedIndexChanged += Changed; volume.ValueChanged += Changed;
        autosave.Interval = 700; autosave.Tick += delegate { autosave.Stop(); SaveSettings(false); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (dirty && !SaveSettings(false)) { e.Cancel = MessageBox.Show(this, "设置尚未保存。是否放弃未保存的修改并关闭？", "无法保存", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes; } };
        FormClosed += delegate { autosave.Dispose(); tips.Dispose(); };
        Shown += delegate {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 32), Math.Min(MinimumSize.Height, area.Height - 32));
            Size = new Size(Math.Min(Width, area.Width - 32), Math.Min(Height, area.Height - 48));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        };
        loading = false;
        UpdateMode();
        status.Text = "已读取现有配置 · 修改后自动保存";
        ResumeLayout(true);
    }
    private Label FieldLabel(string text)
    { return new SingleLineLabel { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 16, 0) }; }
    private Label Hint(string text)
    { return new SingleLineLabel { Text = text, AutoSize = true, ForeColor = muted, Margin = new Padding(0, 6, 0, 4) }; }
    private void FitScrollDocument(Panel scroll, TableLayoutPanel document, int minimumWidth)
    {
        // Dock=Top forces a document into the viewport and can cut off wide
        // controls. Keep its own width and let AutoScroll expose all content.
        document.Dock = DockStyle.None;
        document.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        document.AutoSize = false;
        document.MinimumSize = new Size(minimumWidth, 0);
        bool fitting = false, ready = false, pending = false;
        EventHandler fit = delegate {
            if (fitting || !ready || IsDisposed) return;
            fitting = true;
            try {
                int width = Math.Max(document.MinimumSize.Width, scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 1);
                Size preferred = document.GetPreferredSize(new Size(width, 0));
                Size target = new Size(width, preferred.Height);
                if (document.Size != target) document.Size = target;
            }
            finally { fitting = false; }
        };
        EventHandler queue = delegate {
            if (!ready || pending || IsDisposed || !IsHandleCreated) return;
            pending = true;
            BeginInvoke((MethodInvoker)delegate { pending = false; fit(scroll, EventArgs.Empty); });
        };
        scroll.SizeChanged += queue; scroll.VisibleChanged += queue;
        document.Layout += delegate { if (!fitting) queue(document, EventArgs.Empty); };
        Shown += delegate { ready = true; fit(scroll, EventArgs.Empty); };
    }
    private void AddField(TableLayoutPanel panel, int row, string text, Control control, string name)
    { panel.Controls.Add(FieldLabel(text), 0, row); control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 3, 0, 3); control.Name = name; control.AccessibleName = name; panel.Controls.Add(control, 1, row); }
    private void BuildGameplay(TabPage tab)
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Name = "GameplayScroll" };
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 7 };
        for (int i = 0; i < 7; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        scroll.Controls.Add(panel); tab.Controls.Add(scroll);
        string[,] options = { { "allow_invaders", "允许玩家入侵" }, { "death_debuffs", "死亡减益（腐败精华）" }, { "allow_summons", "允许骨灰召唤" }, { "skip_splash_screens", "跳过启动 Logo" }, { "always_spectate_on_death", "死亡后始终观战" }, { "append_steam_id_to_players", "头顶显示玩家 ID" } };
        for (int i = 0; i < options.GetLength(0); i++)
        { var box = new CheckBox { Text = options[i, 1], AutoSize = true, Margin = new Padding(0, 6, 6, 6) }; flags.Add(options[i, 0], box); panel.Controls.Add(box, i % 2, i / 2); }
        var display = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 12, 0, 8) };
        display.RowStyles.Add(new RowStyle(SizeType.AutoSize)); display.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        display.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); display.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        overhead.DropDownStyle = ComboBoxStyle.DropDownList; overhead.Dock = DockStyle.Fill; overhead.Margin = new Padding(0, 3, 0, 3);
        overhead.Items.AddRange(new object[] { "默认", "不显示", "延迟", "等级", "死亡次数", "等级与延迟" });
        display.Controls.Add(FieldLabel("头顶玩家信息"), 0, 0); display.Controls.Add(overhead, 1, 0);
        // UpDownBase on .NET Framework can repeatedly scale nonzero margins
        // while measuring an auto-sized table. Put spacing on the row instead.
        volume.Minimum = 0; volume.Maximum = 10; volume.Dock = DockStyle.Fill; volume.Margin = Padding.Empty;
        display.Controls.Add(FieldLabel("启动音量 0–10"), 0, 1); display.Controls.Add(volume, 1, 1);
        panel.Controls.Add(display, 0, 3); panel.SetColumnSpan(display, 2);
        var title = new SingleLineLabel { Text = "多人难度增幅（每增加一名玩家，%）", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 12, 0, 8) };
        panel.Controls.Add(title, 0, 4); panel.SetColumnSpan(title, 2);
        string[,] stats = { { "enemy_health_scaling", "普通敌人：血量" }, { "boss_health_scaling", "Boss：血量" }, { "enemy_damage_scaling", "普通敌人：伤害" }, { "boss_damage_scaling", "Boss：伤害" }, { "enemy_posture_scaling", "普通敌人：架势" }, { "boss_posture_scaling", "Boss：架势" } };
        var statFields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 6, Margin = Padding.Empty };
        statFields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); statFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < stats.GetLength(0); i++)
        {
            statFields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var number = new NumericUpDown { Minimum = -1000000, Maximum = 1000000, Dock = DockStyle.Fill, Margin = Padding.Empty };
            var label = FieldLabel(stats[i, 1]); label.Margin = new Padding(0, 6, 16, 6);
            scaling.Add(stats[i, 0], number); statFields.Controls.Add(label, 0, i); statFields.Controls.Add(number, 1, i);
        }
        panel.Controls.Add(statFields, 0, 5); panel.SetColumnSpan(statFields, 2);
        var note = Hint("其他 INI 项目和存档设置保持原值。");
        panel.Controls.Add(note, 0, 6); panel.SetColumnSpan(note, 2);
        FitScrollDocument(scroll, panel, 480);
    }
    private decimal Number(IniFile ini, string section, string key, decimal fallback, NumericUpDown control)
    { decimal value; if (!Decimal.TryParse(ini.Get(section, key, ""), out value)) value = fallback; return Math.Max(control.Minimum, Math.Min(control.Maximum, value)); }
    private void ReadConfiguration()
    {
        IniFile ini = new IniFile(config.ModPath);
        Relay.Checked = ini.Get("NETWORK", "use_steam_network", "1") == "0"; Steam.Checked = !Relay.Checked;
        Server.Text = ini.Get("NETWORK", "relay_server", config.Default("public_ip", ""));
        Port.Text = ini.Get("NETWORK", "relay_port", config.Default("udp_port", "10099"));
        Password.Text = ini.Get("PASSWORD", "cooppassword", "");
        BlockModUpdates.Checked = ini.Get("NETWORK", "block_mod_update_checks", "1") != "0";
        IniFile user = config.UserWithFallback();
        Username.Text = config.UserValue(user, "account_name", "");
        SteamId.Text = config.UserValue(user, "account_steamid", "");
        string language = config.UserValue(user, "language", "schinese");
        if (language == "") language = "schinese";
        for (int i = 0; i < Language.Items.Count; i++) if (((LanguageChoice)Language.Items[i]).Code == language) Language.SelectedIndex = i;
        if (Language.SelectedIndex < 0) { Language.Items.Add(new LanguageChoice("保留现有语言（" + language + "）", language)); Language.SelectedIndex = Language.Items.Count - 1; }
        foreach (var entry in flags) entry.Value.Checked = ini.Get("GAMEPLAY", entry.Key, entry.Key == "allow_invaders" || entry.Key == "death_debuffs" || entry.Key == "allow_summons" ? "1" : "0") == "1";
        foreach (var entry in scaling)
        { decimal fallback = entry.Key == "enemy_health_scaling" ? 35 : entry.Key == "boss_health_scaling" ? 100 : entry.Key == "enemy_posture_scaling" ? 15 : entry.Key == "boss_posture_scaling" ? 20 : 0; entry.Value.Value = Number(ini, "SCALING", entry.Key, fallback, entry.Value); }
        int selected; if (!Int32.TryParse(ini.Get("GAMEPLAY", "overhead_player_display", "0"), out selected) || selected < 0 || selected > 5) selected = 0;
        overhead.SelectedIndex = selected; volume.Value = Number(ini, "GAMEPLAY", "default_boot_master_volume", 5, volume);
    }
    private void UpdateMode() { relayGroup.Visible = Relay.Checked; }
    private void ModeChanged(object sender, EventArgs e) { UpdateMode(); Changed(sender, e); }
    private void Changed(object sender, EventArgs e)
    {
        if (loading) return;
        dirty = true; status.ForeColor = muted; status.Text = "正在编辑…"; autosave.Stop(); autosave.Start();
    }
    internal bool SaveSettings(bool requirePassword)
    {
        autosave.Stop();
        try
        {
            if (requirePassword && String.IsNullOrWhiteSpace(Password.Text)) throw new InvalidOperationException("请填写联机密码，所有队友应使用相同密码。");
            if (Relay.Checked)
            {
                string error = LauncherConfig.ValidateEndpoint(Server.Text.Trim(), Port.Text.Trim());
                if (error != null) throw new InvalidOperationException(error);
                if (!LauncherConfig.ValidSteamId(SteamId.Text.Trim())) throw new InvalidOperationException("SteamID 请填写有效的 SteamID64，或留空使用兼容层身份。");
            }
            // Always reread before writing: preserve unrelated settings and outside edits.
            IniFile ini = new IniFile(config.ModPath);
            ini.Set("NETWORK", "use_steam_network", Relay.Checked ? "0" : "1");
            ini.Set("NETWORK", "block_mod_update_checks", BlockModUpdates.Checked ? "1" : "0");
            ini.Set("PASSWORD", "cooppassword", Password.Text.Trim());
            if (Relay.Checked)
            {
                ini.Set("NETWORK", "relay_server", Server.Text.Trim()); ini.Set("NETWORK", "relay_port", Port.Text.Trim());
            }
            foreach (var flag in flags) ini.Set("GAMEPLAY", flag.Key, flag.Value.Checked ? "1" : "0");
            foreach (var stat in scaling) ini.Set("SCALING", stat.Key, stat.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("GAMEPLAY", "overhead_player_display", overhead.SelectedIndex.ToString());
            ini.Set("GAMEPLAY", "default_boot_master_volume", volume.Value.ToString());
            // Steam mode never creates or rewrites the compatibility user config.
            if (Relay.Checked)
            {
                IniFile user = new IniFile(config.UserPath);
                if (Username.Text.Trim() == "") user.Remove("user::general", "account_name");
                else user.Set("user::general", "account_name", Username.Text.Trim());
                // A permanent blank local ID can force a fresh generated identity
                // on every run. Omit the key so the persisted global ID is reused.
                if (SteamId.Text.Trim() == "") user.Remove("user::general", "account_steamid");
                else user.Set("user::general", "account_steamid", SteamId.Text.Trim());
                user.Set("user::general", "language", ((LanguageChoice)Language.SelectedItem).Code);
                user.Save();
            }
            ini.Save();
            dirty = false; status.ForeColor = Color.FromArgb(40, 111, 69); status.Text = "已自动保存 · " + DateTime.Now.ToString("HH:mm:ss");
            return true;
        }
        catch (Exception error)
        {
            status.ForeColor = Color.FromArgb(170, 50, 40);
            status.Text = error is UnauthorizedAccessException ? "保存失败：目录无写入权限，请以管理员身份打开启动器。" : "未保存：" + error.Message;
            return false;
        }
    }
    private void StartGame()
    {
        if (!SaveSettings(true)) return;
        launch.Enabled = false;
        try { config.Launch(Relay.Checked, Server.Text.Trim(), Port.Text.Trim(), Password.Text.Trim()); Close(); }
        catch (Exception error)
        {
            MessageBox.Show(this, error is UnauthorizedAccessException ? "无法备份或切换接口 DLL：Game 目录没有写入权限。请以管理员身份运行启动器，或手动复制对应缓存 DLL。" : error.Message, "暂时无法启动", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { launch.Enabled = true; }
    }
}

internal static class NetworkLauncherUI
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--mod-http-guard") return ModUpdateGuard.Run(args[1], args[2]);
        WindowsDpi.Initialize();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (args.Length == 2 && args[0] == "--dpi-check")
            {
                using (SettingsWindow window = new SettingsWindow(AppDomain.CurrentDomain.BaseDirectory))
                {
                    File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new {
                        dpi = WindowsDpi.ForWindow(window.Handle), perMonitorV2 = WindowsDpi.PerMonitorV2(),
                        externalConfigPresent = File.Exists(Application.ExecutablePath + ".config")
                    }), new UTF8Encoding(false));
                }
                return 0;
            }
            if (args.Length == 2 && args[0] == "--inspect")
            {
                LauncherConfig config = new LauncherConfig(AppDomain.CurrentDomain.BaseDirectory);
                IniFile ini = new IniFile(config.ModPath), user = config.UserWithFallback();
                File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new {
                    mode = ini.Get("NETWORK", "use_steam_network", "1"), endpoint = ini.Get("NETWORK", "relay_server", config.Default("public_ip", "")) + ":" + ini.Get("NETWORK", "relay_port", config.Default("udp_port", "10099")),
                    language = config.UserValue(user, "language", "schinese"), username = config.UserValue(user, "account_name", ""), steamId = config.UserValue(user, "account_steamid", ""),
                    blockModUpdateChecks = ini.Get("NETWORK", "block_mod_update_checks", "1") != "0", gui = true, helperScripts = false
                }), new UTF8Encoding(false));
                return 0;
            }
            Application.Run(new SettingsWindow(AppDomain.CurrentDomain.BaseDirectory));
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "无法读取启动设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
