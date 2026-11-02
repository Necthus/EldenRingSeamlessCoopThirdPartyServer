using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal sealed class SaveSide : GroupBox
{
    internal readonly ComboBox Account = new ComboBox(), Format = new ComboBox();
    internal readonly TextBox FilePath = new TextBox();
    internal readonly ListView Characters = new ListView();
    internal readonly Label Info = new SingleLineLabel();
    internal SaveContainer Save;
    internal string ReadError;
    internal bool Loading;
    private readonly bool destination;
    private readonly ToolTip tooltips = new ToolTip();
    private readonly string saveRoot;
    internal SaveSide(bool target, string root, string defaultId)
    {
        SuspendLayout(); Font = SystemFonts.MessageBoxFont;
        destination = target; saveRoot = root;
        Text = target ? "目标账号与存档" : "源账号与存档"; Dock = DockStyle.Fill; Padding = new Padding(8);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize)); fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Account.Dock = DockStyle.Fill; Account.DropDownStyle = ComboBoxStyle.DropDown; Account.AccessibleName = target ? "目标 SteamID64" : "源 SteamID64";
        Format.Dock = DockStyle.Fill; Format.DropDownStyle = ComboBoxStyle.DropDownList; Format.Items.AddRange(new object[] { "原版 .sl2", "无缝 .co2" });
        fields.Controls.Add(new SingleLineLabel { Text = "SteamID", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0); fields.Controls.Add(Account, 1, 0);
        fields.Controls.Add(new SingleLineLabel { Text = "类型", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1); fields.Controls.Add(Format, 1, 1);
        table.Controls.Add(fields, 0, 0);
        var fileRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); fileRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        FilePath.ReadOnly = true; FilePath.Dock = DockStyle.Fill; FilePath.AccessibleName = target ? "目标文件路径" : "源文件路径";
        FilePath.TextChanged += delegate { tooltips.SetToolTip(FilePath, FilePath.Text); FilePath.SelectionStart = FilePath.Text.Length; FilePath.ScrollToCaret(); };
        Info.TextChanged += delegate { tooltips.SetToolTip(Info, Info.Text); };
        var browse = new Button { Text = "浏览…", AutoSize = true, UseVisualStyleBackColor = true };
        browse.Click += delegate { Browse(); }; fileRow.Controls.Add(FilePath, 0, 0); fileRow.Controls.Add(browse, 1, 0); table.Controls.Add(fileRow, 0, 1);
        Info.AutoSize = true; Info.Margin = new Padding(0, 5, 0, 5); table.Controls.Add(Info, 0, 2);
        table.Controls.Add(new SingleLineLabel { Text = target ? "选择目标槽位（默认选空槽）" : "选择要复制的角色", AutoSize = true }, 0, 3);
        Characters.Dock = DockStyle.Fill; Characters.View = View.Details; Characters.FullRowSelect = true; Characters.MultiSelect = false; Characters.HideSelection = false;
        Characters.Columns.Add("槽", 30); Characters.Columns.Add("角色", 95); Characters.Columns.Add("等级", 45); Characters.Columns.Add("时长", 60);
        Characters.SizeChanged += delegate {
            int width = Math.Max(150, Characters.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 5);
            Characters.Columns[0].Width = width * 12 / 100; Characters.Columns[1].Width = width * 40 / 100; Characters.Columns[2].Width = width * 18 / 100; Characters.Columns[3].Width = width * 30 / 100;
        };
        table.Controls.Add(Characters, 0, 4); Controls.Add(table);
        Loading = true;
        if (Directory.Exists(root)) foreach (string folder in Directory.GetDirectories(root))
        { string name = Path.GetFileName(folder); if (name.Length == 17 && LauncherConfig.ValidSteamId(name)) Account.Items.Add(name); }
        Account.Text = defaultId; Format.SelectedIndex = target ? 1 : 0;
        Loading = false;
        Account.SelectedIndexChanged += delegate { DefaultPath(); };
        Account.Leave += delegate { DefaultPath(); };
        Format.SelectedIndexChanged += delegate { DefaultPath(); };
        DefaultPath();
        ResumeLayout(true);
    }
    protected override void Dispose(bool disposing)
    { if (disposing) tooltips.Dispose(); base.Dispose(disposing); }
    internal int SelectedSlot { get { return Characters.SelectedItems.Count == 0 ? -1 : (int)Characters.SelectedItems[0].Tag; } }
    internal void DefaultPath()
    {
        if (Loading) return;
        string id = Account.Text.Trim();
        if (id.Length != 17 || !LauncherConfig.ValidSteamId(id)) { ReadError = "请填写完整 SteamID64"; Info.Text = ReadError; return; }
        string path = Path.Combine(saveRoot, id, Format.SelectedIndex == 0 ? "ER0000.sl2" : "ER0000.co2");
        if (!String.Equals(path, FilePath.Text, StringComparison.OrdinalIgnoreCase)) FilePath.Text = path;
        Reload();
    }
    internal void Reload()
    {
        Save = null; ReadError = null; Characters.Items.Clear();
        try
        {
            if (File.Exists(FilePath.Text)) Save = SaveContainer.Read(FilePath.Text);
            else if (!destination) throw new FileNotFoundException("该账号没有此类存档");
            int defaultSlot = -1;
            for (int i = 0; i < 10; i++)
            {
                SaveRole role = Save == null ? null : Save.Roles[i]; bool active = role != null && role.Active;
                var item = new ListViewItem((i + 1).ToString()) { Tag = i };
                item.SubItems.Add(active ? role.Name : "空槽"); item.SubItems.Add(active ? role.Level.ToString() : "—");
                item.SubItems.Add(active ? ((ulong)(role.PlayMilliseconds / 3600000)).ToString() + "h " + ((role.PlayMilliseconds / 60000) % 60) + "m" : "—");
                Characters.Items.Add(item);
                if (defaultSlot == -1 && (destination ? !active : active)) defaultSlot = i;
            }
            if (defaultSlot >= 0) Characters.Items[defaultSlot].Selected = true;
            string account = Account.Text.Trim();
            if (Save == null) Info.Text = "目标不存在：将新建存档";
            else if (Save.Owner.ToString() != account) { Info.Text = "内部 ID 不同：" + Save.Owner; Info.ForeColor = Color.DarkRed; }
            else { Info.Text = "校验通过 · 角色已读取"; Info.ForeColor = SystemColors.GrayText; }
        }
        catch (Exception error) { ReadError = error.Message; Info.Text = "无法读取：" + error.Message; Info.ForeColor = Color.DarkRed; }
    }
    private void Browse()
    {
        using (var dialog = new OpenFileDialog { Filter = "艾尔登法环存档|*.sl2;*.co2|所有文件|*.*", CheckFileExists = true, InitialDirectory = saveRoot })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var file = SaveContainer.Read(dialog.FileName); Loading = true;
                Account.Text = file.Owner.ToString(); Format.SelectedIndex = Path.GetExtension(dialog.FileName).Equals(".co2", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                FilePath.Text = dialog.FileName; Loading = false; Reload();
            }
            catch (Exception error) { Loading = false; MessageBox.Show(this, error.Message, "无法读取存档", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }
}

internal sealed class SaveManagerPage : UserControl
{
    internal readonly SaveSide Source, Target;
    internal readonly Label Result = new SingleLineLabel();
    internal SaveManagerPage(string defaultId)
    {
        SuspendLayout(); Font = SystemFonts.MessageBoxFont;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EldenRing");
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = new TableLayoutPanel { Anchor = AnchorStyles.Top | AnchorStyles.Left, ColumnCount = 1, RowCount = 6, Padding = new Padding(4), Margin = Padding.Empty, MinimumSize = new Size(470, 450) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new SingleLineLabel { Text = "原版／无缝 · 跨账号复制角色 · 转换前自动备份", AutoSize = true, Margin = new Padding(0, 0, 0, 5) }, 0, 0);
        layout.Controls.Add(new SingleLineLabel { Text = "转原版会清理已知无缝道具；保留源角色。", AutoSize = true, ForeColor = SystemColors.GrayText }, 0, 1);
        var pair = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); pair.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Source = new SaveSide(false, root, defaultId); Target = new SaveSide(true, root, defaultId);
        pair.Controls.Add(Source, 0, 0); pair.Controls.Add(Target, 1, 0); layout.Controls.Add(pair, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 5, 0, 0) };
        AddButton(actions, "刷新", delegate { Source.Reload(); Target.Reload(); });
        AddButton(actions, "检查道具", delegate { InspectItems(); });
        AddButton(actions, "转换 →", delegate { ConvertRole(false); });
        AddButton(actions, "导出副本…", delegate { ConvertRole(true); });
        layout.Controls.Add(actions, 0, 3);
        var recovery = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        AddButton(recovery, "备份目标", delegate {
            SaveTransactions.GameClosed();
            if (Target.Save == null || Target.ReadError != null) throw new InvalidOperationException("请选择已有、可读取的目标存档。");
            string folder = SaveTransactions.Snapshot(null, Target.FilePath.Text,
                new SaveOperation { Action = "手动备份目标存档", TargetSteamId = Target.Save.Owner.ToString() }, SaveTransactions.Root);
            Result.Text = "已备份 · " + Path.GetFileName(folder);
        });
        AddButton(recovery, "打开备份目录", delegate { Directory.CreateDirectory(SaveTransactions.Root); Process.Start(new ProcessStartInfo(SaveTransactions.Root) { UseShellExecute = true }); });
        AddButton(recovery, "恢复备份…", delegate { Restore(); });
        layout.Controls.Add(recovery, 0, 4);
        Result.Text = "试用版：请退出游戏后操作。"; Result.AutoSize = true; Result.ForeColor = SystemColors.GrayText;
        layout.Controls.Add(Result, 0, 5); viewport.Controls.Add(layout); Controls.Add(viewport);
        bool fitting = false;
        EventHandler fit = delegate {
            if (fitting) return; fitting = true;
            try {
                layout.Size = new Size(Math.Max(layout.MinimumSize.Width, viewport.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 1), Math.Max(layout.MinimumSize.Height, viewport.ClientSize.Height));
            } finally { fitting = false; }
        };
        viewport.SizeChanged += fit; VisibleChanged += fit;
        ResumeLayout(true);
        fit(this, EventArgs.Empty);
    }
    private void AddButton(FlowLayoutPanel row, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, UseVisualStyleBackColor = true, Padding = new Padding(4, 2, 4, 2) };
        button.Click += delegate { try { action(); } catch (Exception error) { Result.Text = "未完成：" + error.Message; MessageBox.Show(this, error.Message, "存档管理", MessageBoxButtons.OK, MessageBoxIcon.Information); } };
        row.Controls.Add(button);
    }
    private void InspectItems()
    {
        if (Source.Save == null || Source.SelectedSlot < 0) throw new InvalidOperationException("请选择可读取的源角色。");
        var inventory = new SlotInventory(Source.Save, Source.SelectedSlot); var lines = new List<string>();
        foreach (var hit in inventory.Hits) lines.Add(hit.Name + " [" + hit.Id + "]  " + hit.Location + (hit.QuickIndex < 0 ? " ×" + hit.Quantity : ""));
        if (inventory.UnknownModIds.Count > 0) lines.Add("未知无缝保留区 ID：" + String.Join(", ", inventory.UnknownModIds));
        if (lines.Count == 0) lines.Add("未检出规则内的无缝道具。");
        ShowReport("源角色道具检查", "识别范围：Seamless Co-op 已知道具 8380001–8380012。", String.Join(Environment.NewLine, lines), false);
    }
    internal void ConvertRole(bool export)
    {
        SaveTransactions.GameClosed();
        if (Source.Save == null || Source.ReadError != null || Source.SelectedSlot < 0 || !Source.Save.Roles[Source.SelectedSlot].Active) throw new InvalidOperationException("请选择有效的源角色。");
        ulong targetId;
        if (!UInt64.TryParse(Target.Account.Text.Trim(), out targetId) || !LauncherConfig.ValidSteamId(targetId.ToString())) throw new InvalidOperationException("请填写完整、有效的目标 SteamID64。");
        string targetPath = Target.FilePath.Text;
        SaveContainer target = Target.Save; int targetSlot = Target.SelectedSlot;
        if (export)
        {
            using (var dialog = new SaveFileDialog { Filter = Target.Format.SelectedIndex == 0 ? "原版存档|*.sl2" : "无缝存档|*.co2", FileName = Target.Format.SelectedIndex == 0 ? "ER0000.sl2" : "ER0000.co2", OverwritePrompt = false })
            { if (dialog.ShowDialog(this) != DialogResult.OK) return; targetPath = dialog.FileName; }
            target = File.Exists(targetPath) ? SaveContainer.Read(targetPath) : null;
            if (targetSlot < 0) targetSlot = 0;
        }
        else if (Target.ReadError != null) throw new InvalidOperationException("目标存档无法读取：" + Target.ReadError);
        if (targetSlot < 0) throw new InvalidOperationException("请选择目标槽位。");
        bool vanilla = Target.Format.SelectedIndex == 0;
        if (String.Equals(Path.GetFullPath(Source.FilePath.Text), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("源文件与目标文件相同，请选择其他账号、类型或导出路径。");
        var conversion = SaveConversion.Create(Source.Save, Source.SelectedSlot, target, targetSlot, targetId, vanilla);
        conversion.Description = (Source.Format.SelectedIndex == 0 ? "原版" : "无缝") + " → " + (vanilla ? "原版" : "无缝") + "：" + Source.Save.Roles[Source.SelectedSlot].Name;
        string title = target != null && target.Roles[targetSlot].Active ? "将覆盖目标角色：" + target.Roles[targetSlot].Name : "将写入目标空槽 " + (targetSlot + 1);
        var body = new StringBuilder();
        body.AppendLine("源账号：" + Source.Save.Owner + "，槽 " + (Source.SelectedSlot + 1)); body.AppendLine("目标账号：" + targetId + "，槽 " + (targetSlot + 1));
        body.AppendLine("目标文件：" + targetPath); body.AppendLine();
        body.AppendLine("确认后自动备份源、目标及各自 .bak；来源角色保留。");
        body.AppendLine("目标不存在时新建单角色存档；存在时保留其他角色。");
        body.AppendLine(); body.AppendLine(vanilla ? "将清理以下已知无缝物品／快捷栏引用：" : "此方向保留源角色物品。");
        if (vanilla) body.AppendLine(conversion.Removed.Count == 0 ? "未检出已知无缝物品。" : String.Join(Environment.NewLine, conversion.Removed));
        if (!ShowReport(title, "预览转换内容，确认后写入。", body.ToString(), true)) return;
        string folder = SaveTransactions.Install(Source.FilePath.Text, targetPath, Source.Save, target, conversion, SaveTransactions.Root);
        Result.Text = "已完成 · 自动备份 " + Path.GetFileName(folder); Source.Reload(); Target.Reload();
        MessageBox.Show(this, "转换完成，文件校验通过。\n\n" + targetPath + "\n\n自动备份：\n" + folder + "\n\n请用对应模式、匹配的账号与游戏版本测试角色加载。", "转换完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    private void Restore()
    {
        using (var dialog = new OpenFileDialog { Filter = "启动器备份记录|operation.json", InitialDirectory = SaveTransactions.Root })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var op = SaveTransactions.ReadOperation(dialog.FileName);
            if (!ShowReport("恢复备份", "恢复前也会自动备份当前文件。", op.Time + "\r\n" + op.Action + "\r\n\r\n恢复目标：\r\n" + op.TargetPath + "\r\n\r\n本次只恢复该操作的目标文件及 .bak，不回退源账号的游戏进度。", true)) return;
            SaveTransactions.Restore(dialog.FileName, SaveTransactions.Root); Source.Reload(); Target.Reload(); Result.Text = "备份恢复完成";
        }
    }
    private bool ShowReport(string title, string subtitle, string body, bool confirm)
    {
        using (var form = new Form())
        {
            form.SuspendLayout(); form.Text = title; form.Font = Font; form.StartPosition = FormStartPosition.CenterParent;
            form.AutoScaleDimensions = new SizeF(96, 96); form.AutoScaleMode = AutoScaleMode.Dpi;
            form.ClientSize = new Size(580, 440); form.MinimizeBox = false; form.ShowIcon = false;
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new SingleLineLabel { Text = subtitle, AutoSize = true }, 0, 0);
            table.Controls.Add(new TextBox { Text = body, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill }, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var close = new Button { Text = confirm ? "取消" : "关闭", AutoSize = true, DialogResult = DialogResult.Cancel }; actions.Controls.Add(close); form.CancelButton = close;
            if (confirm) { var ok = new Button { Text = "确认执行", AutoSize = true, DialogResult = DialogResult.OK }; actions.Controls.Add(ok); }
            table.Controls.Add(actions, 0, 2); form.Controls.Add(table);
            form.Shown += delegate { Rectangle area = Screen.FromControl(this).WorkingArea; form.Size = new Size(Math.Min(form.Width, area.Width - 32), Math.Min(form.Height, area.Height - 48)); };
            form.ResumeLayout(true);
            return form.ShowDialog(this) == DialogResult.OK;
        }
    }
}
