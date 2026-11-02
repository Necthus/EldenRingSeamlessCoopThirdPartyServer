using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal sealed class BroadcastAddressFile
{
    internal readonly string PathName;
    internal readonly List<string> Entries = new List<string>();
    private readonly List<string> lines;
    private byte[] original;
    private bool existed;
    private readonly bool bom;
    private readonly string newline;
    internal BroadcastAddressFile(string path)
    {
        PathName = path; existed = File.Exists(path); original = existed ? File.ReadAllBytes(path) : new byte[0];
        if (original.Length >= 2 && ((original[0] == 255 && original[1] == 254) || (original[0] == 254 && original[1] == 255))) throw new IOException("发现地址文件是 UTF-16，请另存为 UTF-8 后重新读取。");
        bom = original.Length >= 3 && original[0] == 239 && original[1] == 187 && original[2] == 191;
        string text = new UTF8Encoding(false, true).GetString(original, bom ? 3 : 0, original.Length - (bom ? 3 : 0));
        newline = text.Contains("\r\n") ? "\r\n" : text.Contains("\n") ? "\n" : "\r\n";
        lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
        if (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
        foreach (string line in lines) if (IsEntry(line)) Entries.Add(line.Trim());
    }
    private static bool IsEntry(string line)
    { string text = line.Trim(); return text.Length > 0 && !text.StartsWith("#") && !text.StartsWith(";"); }
    internal static string Normalize(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.IndexOfAny(new char[] { '/', '\\', '\r', '\n', '\0', ' ', '\t', '#', ';', '@' }) >= 0) throw new ArgumentException("填写玩家虚拟 IPv4 或域名，例如 10.77.42.3:47584。");
        int colon = text.IndexOf(':');
        if (colon != text.LastIndexOf(':')) throw new ArgumentException("当前兼容接口只支持 IPv4 或域名，不支持 IPv6。");
        string host = colon >= 0 ? text.Substring(0, colon) : text;
        string portText = colon >= 0 ? text.Substring(colon + 1) : "47584";
        int port;
        if (!Regex.IsMatch(portText, @"^\d{1,5}$") || !Int32.TryParse(portText, out port) || port < 1 || port > 65535) throw new ArgumentException("发现端口必须是 1–65535 的整数；通常填 47584。");
        if (Regex.IsMatch(host, @"^[\d.]+$"))
        {
            string[] parts = host.Split('.');
            if (parts.Length != 4) throw new ArgumentException("IPv4 必须填写完整的四段地址。");
            foreach (string part in parts)
            { int value; if (!Regex.IsMatch(part, @"^\d{1,3}$") || !Int32.TryParse(part, out value) || value > 255 || (part.Length > 1 && part[0] == '0')) throw new ArgumentException("IPv4 格式无效，请检查各段数字。"); }
            if (host == "0.0.0.0") throw new ArgumentException("0.0.0.0 不是可用的玩家发现目标。");
        }
        else if (Uri.CheckHostName(host) != UriHostNameType.Dns || host.Length > 253) throw new ArgumentException("请输入有效的玩家虚拟 IPv4 或域名。");
        return host.ToLowerInvariant() + ":" + port;
    }
    internal void Save(IList<string> entries)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in entries)
        { string normalized = Normalize(entry); if (!unique.Add(normalized)) throw new ArgumentException("玩家发现地址重复：" + normalized); }
        if (File.Exists(PathName) != existed || (existed && !SaveContainer.Equal(File.ReadAllBytes(PathName), original))) throw new IOException("发现地址文件已被外部修改，请点击“重新读取”后再编辑。");
        var outputLines = new List<string>(); int index = 0;
        foreach (string line in lines)
        { if (!IsEntry(line)) outputLines.Add(line); else if (index < entries.Count) outputLines.Add(entries[index++]); }
        while (index < entries.Count) outputLines.Add(entries[index++]);
        string text = outputLines.Count == 0 ? "" : String.Join(newline, outputLines) + newline;
        var encoding = new UTF8Encoding(bom); byte[] body = encoding.GetBytes(text), preamble = encoding.GetPreamble();
        byte[] data = new byte[preamble.Length + body.Length]; Buffer.BlockCopy(preamble, 0, data, 0, preamble.Length); Buffer.BlockCopy(body, 0, data, preamble.Length, body.Length);
        if (existed && SaveContainer.Equal(data, original)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(PathName)));
        string temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            if (existed)
            { string backup = PathName + ".launcher.bak"; if (!File.Exists(backup)) File.Copy(PathName, backup); File.Replace(temporary, PathName, null); }
            else File.Move(temporary, PathName);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        original = data; existed = true; lines.Clear(); lines.AddRange(outputLines); Entries.Clear(); foreach (string entry in entries) Entries.Add(entry);
    }
}

internal sealed class BroadcastAddressEditor : GroupBox
{
    internal readonly ListBox Addresses = new ListBox();
    internal readonly TextBox AddressInput = new TextBox();
    private readonly Label notice = new SingleLineLabel();
    private readonly ToolTip tips = new ToolTip();
    private readonly string path;
    private BroadcastAddressFile file;
    private bool dirty;
    internal event EventHandler Edited;
    internal bool HasChanges { get { return dirty; } }
    internal BroadcastAddressEditor(string gameRoot)
    {
        SuspendLayout();
        path = Path.Combine(gameRoot, "steam_settings", "custom_broadcasts.txt");
        Text = "玩家发现地址"; Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(8);
        Name = "BroadcastAddresses";
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var hint = new SingleLineLabel { Text = "填写玩家虚拟地址；游戏端口通常为 47584。", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 3, 0, 5) };
        layout.Controls.Add(hint, 0, 0);
        Addresses.Dock = DockStyle.Fill; Addresses.IntegralHeight = false; Addresses.HorizontalScrollbar = true; Addresses.Name = "PlayerDiscoveryList"; Addresses.AccessibleName = "玩家发现地址列表";
        Addresses.SelectedIndexChanged += delegate { if (Addresses.SelectedIndex >= 0) AddressInput.Text = Addresses.SelectedItem.ToString(); };
        Addresses.DoubleClick += delegate { AddressInput.Focus(); AddressInput.SelectAll(); };
        layout.Controls.Add(Addresses, 0, 1);
        AddressInput.Dock = DockStyle.Fill; AddressInput.Name = "PlayerDiscoveryInput"; AddressInput.AccessibleName = "待添加或修改的发现地址";
        tips.SetToolTip(AddressInput, "例如 10.77.42.3:47584；省略端口时补全 47584。\n这是玩家的 WireGuard 虚拟地址，不是公网服务器入口。\n修改输入后点击“添加”或“更新选中”，约 0.7 秒后自动保存。");
        AddressInput.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TryEdit(delegate { if (Addresses.SelectedIndex < 0) AddAddress(); else UpdateAddress(); }); } };
        layout.Controls.Add(AddressInput, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        Button(actions, "添加", delegate { AddAddress(); }); Button(actions, "更新选中", delegate { UpdateAddress(); }); Button(actions, "删除选中", delegate { RemoveAddress(); });
        Button(actions, "重新读取", delegate {
            if (dirty && MessageBox.Show(this, "放弃尚未保存的发现地址修改，重新读取文件？", "重新读取", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Reload();
        });
        layout.Controls.Add(actions, 0, 3);
        notice.AutoSize = false; notice.Height = 24; notice.Dock = DockStyle.Top; notice.AutoEllipsis = true; notice.ForeColor = SystemColors.GrayText;
        notice.TextChanged += delegate { tips.SetToolTip(notice, notice.Text); };
        layout.Controls.Add(notice, 0, 4); Controls.Add(layout); Reload(); ResumeLayout(true);
    }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    private void Button(FlowLayoutPanel row, string title, Action action)
    { var button = new Button { Text = title, AutoSize = true, UseVisualStyleBackColor = true }; button.Click += delegate { TryEdit(action); }; row.Controls.Add(button); }
    private void TryEdit(Action action)
    { try { action(); } catch (Exception error) { notice.ForeColor = Color.DarkRed; notice.Text = error.Message; } }
    internal void Reload()
    {
        try
        {
            var loaded = new BroadcastAddressFile(path); Addresses.Items.Clear(); foreach (string entry in loaded.Entries) Addresses.Items.Add(entry);
            file = loaded; dirty = false; AddressInput.Clear(); notice.ForeColor = SystemColors.GrayText;
            notice.Text = Addresses.Items.Count == 0 ? "列表为空；可添加地址。" : "已读取 " + Addresses.Items.Count + " 个地址。";
        }
        catch (Exception error) { file = null; notice.ForeColor = Color.DarkRed; notice.Text = "读取失败：" + error.Message; }
    }
    private string ValidInput(int exclude)
    {
        if (file == null) throw new IOException("请修正文件读取问题，再点击“重新读取”。");
        string value = BroadcastAddressFile.Normalize(AddressInput.Text);
        for (int i = 0; i < Addresses.Items.Count; i++) if (i != exclude)
        {
            string existing = Addresses.Items[i].ToString();
            try { existing = BroadcastAddressFile.Normalize(existing); } catch (ArgumentException) { }
            if (existing.Equals(value, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("该发现地址已在列表中。");
        }
        return value;
    }
    private void Changed()
    { dirty = true; notice.ForeColor = SystemColors.GrayText; notice.Text = "修改待保存；下次启动游戏生效。"; if (Edited != null) Edited(this, EventArgs.Empty); }
    internal void AddAddress()
    { string value = ValidInput(-1); Addresses.Items.Add(value); Addresses.ClearSelected(); AddressInput.Clear(); Changed(); }
    internal void UpdateAddress()
    { int selected = Addresses.SelectedIndex; if (selected < 0) throw new InvalidOperationException("请先选择要修改的地址。"); string value = ValidInput(selected); if (Addresses.Items[selected].ToString() == value) return; Addresses.Items[selected] = value; Changed(); }
    internal void RemoveAddress()
    { if (file == null) throw new IOException("请先修正文件读取问题。"); int selected = Addresses.SelectedIndex; if (selected < 0) throw new InvalidOperationException("请先选择要删除的地址。"); Addresses.Items.RemoveAt(selected); AddressInput.Clear(); Changed(); }
    internal void ValidateChanges()
    {
        if (!dirty) return;
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in Addresses.Items) if (!unique.Add(BroadcastAddressFile.Normalize(value))) throw new ArgumentException("发现地址有重复项，请修改后保存。");
    }
    internal void SaveChanges()
    {
        if (!dirty) return; ValidateChanges(); var entries = new List<string>(); foreach (string value in Addresses.Items) entries.Add(value);
        try { file.Save(entries); dirty = false; notice.ForeColor = SystemColors.GrayText; notice.Text = "已保存 " + entries.Count + " 个地址；下次启动生效。"; }
        catch (Exception error) { notice.ForeColor = Color.DarkRed; notice.Text = error.Message; throw; }
    }
}
