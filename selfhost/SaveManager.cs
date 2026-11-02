using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Independently implemented fixed-container reader and bounded slot edits.
// Format references and item-ID provenance are documented in README-save-manager.md.
internal sealed class SaveRole
{
    internal int Slot, Level;
    internal string Name;
    internal ulong PlayMilliseconds;
    internal bool Active;
}

internal sealed class SaveContainer
{
    internal const int Length = 28967888, SlotBytes = 0x280000, SummaryBase = 0x1901d0e, SummaryBytes = 0x24c, ActiveBase = 0x1901d04;
    internal readonly byte[] Data;
    internal readonly string PathName;
    internal readonly SaveRole[] Roles = new SaveRole[10];
    internal ulong Owner;
    internal uint Version;
    internal static int SectionStart(int i) { return i < 10 ? 0x300 + i * 0x280010 : i == 10 ? 0x19003a0 : 0x19603b0; }
    internal static int SectionSize(int i) { return i < 10 ? 0x280010 : i == 10 ? 0x60010 : 0x240020; }
    internal static int SlotStart(int i) { return SectionStart(i) + 16; }
    internal static uint U32(byte[] data, int pos) { return BitConverter.ToUInt32(data, pos); }
    internal static void Put32(byte[] data, int pos, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, data, pos, 4); }
    internal static void Put64(byte[] data, int pos, ulong value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, data, pos, 8); }
    internal static string Fingerprint(byte[] data) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    internal static bool Equal(byte[] left, byte[] right)
    { if (left.Length != right.Length) return false; for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false; return true; }
    internal static SaveContainer Read(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length != Length) throw new InvalidDataException("存档长度不受支持：" + path);
            byte[] bytes = new byte[Length]; int read = 0;
            while (read < bytes.Length) { int count = stream.Read(bytes, read, bytes.Length - read); if (count == 0) throw new EndOfStreamException(); read += count; }
            return new SaveContainer(bytes, path);
        }
    }
    internal SaveContainer(byte[] data, string path)
    {
        Data = data; PathName = path;
        if (data.Length != Length || Encoding.ASCII.GetString(data, 0, 4) != "BND4" || U32(data, 12) != 12 ||
            BitConverter.ToUInt64(data, 16) != 0x40 || BitConverter.ToUInt64(data, 32) != 0x20)
            throw new InvalidDataException("不是受支持的 PC 艾尔登法环存档。");
        using (var md5 = MD5.Create())
            for (int i = 0; i < 12; i++)
            {
                int pos = SectionStart(i), size = SectionSize(i), record = 0x40 + i * 0x20;
                if (BitConverter.ToUInt64(data, record + 8) != (ulong)size || U32(data, record + 16) != pos)
                    throw new InvalidDataException("存档区段布局不受支持。");
                byte[] checksum = md5.ComputeHash(data, pos + 16, size - 16);
                for (int k = 0; k < 16; k++) if (checksum[k] != data[pos + k]) throw new InvalidDataException("存档校验失败，数据区段 " + i + "。请使用有效备份。");
            }
        Owner = BitConverter.ToUInt64(data, 0x19003b4); Version = U32(data, 0x19003b0);
        for (int i = 0; i < 10; i++)
        {
            byte active = data[ActiveBase + i];
            if (active > 1) throw new InvalidDataException("角色槽状态异常。");
            int summary = SummaryBase + i * SummaryBytes;
            var role = new SaveRole { Slot = i, Active = active == 1, Name = "空槽", Level = 0 };
            if (role.Active)
            {
                role.Name = Encoding.Unicode.GetString(data, summary, 0x22).Split('\0')[0];
                role.Level = checked((int)U32(data, summary + 0x22));
                role.PlayMilliseconds = BitConverter.ToUInt64(data, SlotStart(i) + 8);
                if (role.Level < 1 || role.Level > 713 || U32(data, SlotStart(i)) == 0) throw new InvalidDataException("角色摘要异常，槽位 " + (i + 1));
            }
            Roles[i] = role;
        }
    }
    internal static void Checksum(byte[] data, int section)
    { using (var md5 = MD5.Create()) { int pos = SectionStart(section); Buffer.BlockCopy(md5.ComputeHash(data, pos + 16, SectionSize(section) - 16), 0, data, pos, 16); } }
}

internal sealed class InventoryBlock
{
    internal int CountOffset, RecordsOffset, Capacity;
    internal string Location;
}
internal sealed class ModItemHit
{
    public uint Id, Quantity;
    public string Name, Location;
    internal int Record, QuickIndex = -1;
}
internal sealed class SlotInventory
{
    internal static readonly string[] ModNames = { "袖珍大壶", "玛莲妮亚雕像", "挑战者之针", "离别迷雾", "判官法典", "卢恩之酒", "腐败精华：憔悴", "腐败精华：绝望", "腐败精华：衰败", "腐败精华：脆弱", "腐败精华：攻击削弱", "干缩手指" };
    internal readonly List<InventoryBlock> Blocks = new List<InventoryBlock>();
    internal readonly List<ModItemHit> Hits = new List<ModItemHit>();
    internal readonly List<uint> UnknownModIds = new List<uint>();
    internal int Player, Quick, Equipped;
    private readonly byte[] bytes;
    private int position, limit;
    internal static uint GoodsId(uint value)
    {
        uint kind = value >> 28;
        return kind == 11 ? value & 0xffffff : kind == 4 ? value & 0xfffffff : 0;
    }
    internal static bool Known(uint id) { return id >= 8380001 && id <= 8380012; }
    private void Skip(int count)
    { if (count < 0 || position > limit - count) throw new InvalidDataException("角色物品区布局不受支持，已停止转换。"); position += count; }
    private void Inventory(string place, int common, int key)
    {
        InventoryList(place + "／普通", common); InventoryList(place + "／重要", key); Skip(8);
    }
    private void InventoryList(string location, int capacity)
    {
        int countAt = position; uint count = SaveContainer.U32(bytes, position); Skip(4);
        if (count > capacity) throw new InvalidDataException("物品数量字段超出容量，已停止转换。");
        var block = new InventoryBlock { CountOffset = countAt, RecordsOffset = position, Capacity = capacity, Location = location };
        Blocks.Add(block); int occupied = 0;
        for (int i = 0; i < capacity; i++)
        {
            int record = position; uint handle = SaveContainer.U32(bytes, record), quantity = SaveContainer.U32(bytes, record + 4); Skip(12);
            // Quantity-zero key items can retain valid handles (rot essence is
            // one example); they still count toward the distinct-entry count.
            if (handle == 0 || handle == uint.MaxValue) continue;
            occupied++; uint id = GoodsId(handle);
            if (Known(id)) Hits.Add(new ModItemHit { Id = id, Name = ModNames[id - 8380001], Quantity = quantity, Location = location, Record = record });
            else if (id >= 8380000 && id < 8390000 && !UnknownModIds.Contains(id)) UnknownModIds.Add(id);
        }
        if (count != occupied) throw new InvalidDataException("背包计数与实际条目不一致（" + location + "：" + count + "/" + occupied + "），已停止物品转换。");
    }
    internal SlotInventory(SaveContainer save, int slot)
    {
        if (!save.Roles[slot].Active) throw new InvalidOperationException("请选择有角色的源槽位。");
        bytes = save.Data; position = SaveContainer.SlotStart(slot); limit = position + SaveContainer.SlotBytes;
        uint version = SaveContainer.U32(bytes, position);
        // This release deliberately limits writes to the verified game layout.
        if (version != 261 || save.Version != 261) throw new InvalidDataException("本试用版只支持内部格式 261；角色仍可浏览，请先用匹配的游戏版本保存一次。");
        Skip(32);
        for (int i = 0; i < 0x1400; i++)
        {
            uint item = SaveContainer.U32(bytes, position + 4); Skip(8);
            if (item != 0 && item >> 28 == 0) Skip(13);
            else if (item != 0 && item >> 28 == 1) Skip(8);
        }
        Player = position;
        if (SaveContainer.U32(bytes, Player + 0x60) != save.Roles[slot].Level || Encoding.Unicode.GetString(bytes, Player + 0x94, 32).Split('\0')[0] != save.Roles[slot].Name)
            throw new InvalidDataException("角色数据与摘要不一致，已停止转换。");
        Skip(0x1b0 + 0xd0 + 0x58 + 0x74 + 0x58);
        Inventory("背包", 0xa80, 0x180);
        Skip(0x74); Quick = position; Skip(0x8c + 0x18);
        uint projectiles = SaveContainer.U32(bytes, position); Skip(4);
        if (projectiles > 4096) throw new InvalidDataException("投射物区布局不受支持。");
        Skip(checked((int)projectiles * 8)); Equipped = position;
        Skip(0x9c + 8 + 4 + 0x12f); Inventory("仓库", 0x780, 0x80);
        for (int i = 0; i < 16; i++)
        {
            int pos = Quick + (i < 10 ? i * 8 : 84 + (i - 10) * 8);
            uint id = GoodsId(SaveContainer.U32(bytes, pos));
            if (Known(id)) Hits.Add(new ModItemHit { Id = id, Name = ModNames[id - 8380001], Quantity = 0, Location = i < 10 ? "快捷栏" : "随身包", Record = pos, QuickIndex = i });
            else if (id >= 8380000 && id < 8390000 && !UnknownModIds.Contains(id)) UnknownModIds.Add(id);
        }
    }
    internal int Clean(byte[] output, int delta, List<string> removed)
    {
        if (UnknownModIds.Count > 0) throw new InvalidDataException("发现当前规则未识别的无缝道具 ID：" + String.Join(", ", UnknownModIds) + "。不会猜测删除。");
        int count = 0;
        foreach (var hit in Hits)
        {
            if (hit.QuickIndex >= 0)
            {
                SaveContainer.Put32(output, hit.Record + delta, 0); SaveContainer.Put32(output, hit.Record + delta + 4, 0);
            }
            else
            {
                Array.Clear(output, hit.Record + delta, 12);
                foreach (var block in Blocks)
                    if (hit.Record >= block.RecordsOffset && hit.Record < block.RecordsOffset + 12 * block.Capacity)
                        SaveContainer.Put32(output, block.CountOffset + delta, SaveContainer.U32(output, block.CountOffset + delta) - 1);
                count++;
            }
            removed.Add(hit.Name + " [" + hit.Id + "] " + hit.Location + (hit.QuickIndex < 0 ? " ×" + hit.Quantity : ""));
        }
        // A second equipment mirror stores item IDs rather than handles.
        for (int i = 22; i < 38; i++)
        {
            int pos = Equipped + delta + i * 4; uint id = GoodsId(SaveContainer.U32(output, pos));
            if (Known(id)) SaveContainer.Put32(output, pos, uint.MaxValue);
        }
        return count;
    }
}

internal sealed class SaveConversion
{
    internal byte[] Bytes;
    internal readonly List<string> Removed = new List<string>();
    internal int SourceSlot, TargetSlot;
    internal string Description;
    internal static SaveConversion Create(SaveContainer source, int sourceSlot, SaveContainer target, int targetSlot, ulong targetId, bool toVanilla)
    {
        if (sourceSlot < 0 || sourceSlot > 9 || targetSlot < 0 || targetSlot > 9 || !source.Roles[sourceSlot].Active) throw new InvalidOperationException("请选择有效源角色和目标槽位。");
        if (!LauncherConfig.ValidSteamId(targetId.ToString())) throw new InvalidDataException("目标 SteamID64 无效。");
        var inventory = new SlotInventory(source, sourceSlot);
        if (target != null && (target.Owner != targetId || target.Version != source.Version)) throw new InvalidDataException("目标存档账号或内部版本不匹配，请检查目标 ID／先在匹配的游戏中保存。");
        byte[] output = (byte[])(target == null ? source.Data : target.Data).Clone();
        if (target == null)
        {
            for (int i = 0; i < 10; i++)
            { Array.Clear(output, SaveContainer.SlotStart(i), SaveContainer.SlotBytes); Array.Clear(output, SaveContainer.SummaryBase + i * SaveContainer.SummaryBytes, SaveContainer.SummaryBytes); output[SaveContainer.ActiveBase + i] = 0; SaveContainer.Checksum(output, i); }
            SaveContainer.Put64(output, 0x19003b4, targetId);
        }
        int from = SaveContainer.SlotStart(sourceSlot), to = SaveContainer.SlotStart(targetSlot);
        Buffer.BlockCopy(source.Data, from, output, to, SaveContainer.SlotBytes);
        byte[] oldId = BitConverter.GetBytes(source.Owner); int matches = 0;
        for (int pos = from; pos <= from + SaveContainer.SlotBytes - 8; pos++)
        {
            bool match = true; for (int k = 0; k < 8; k++) if (source.Data[pos + k] != oldId[k]) { match = false; break; }
            if (match) { SaveContainer.Put64(output, to + pos - from, targetId); matches++; }
        }
        if (matches == 0 || matches > 8) throw new InvalidDataException("未能可靠识别角色账号绑定，已停止转换。");
        Buffer.BlockCopy(source.Data, SaveContainer.SummaryBase + sourceSlot * SaveContainer.SummaryBytes, output, SaveContainer.SummaryBase + targetSlot * SaveContainer.SummaryBytes, SaveContainer.SummaryBytes);
        output[SaveContainer.ActiveBase + targetSlot] = 1;
        var result = new SaveConversion { Bytes = output, SourceSlot = sourceSlot, TargetSlot = targetSlot,
            Description = (toVanilla ? "无缝 → 原版" : "原版 → 无缝") + "：" + source.Roles[sourceSlot].Name + "，目标槽 " + (targetSlot + 1) };
        if (toVanilla) inventory.Clean(output, to - from, result.Removed);
        SaveContainer.Checksum(output, targetSlot); SaveContainer.Checksum(output, 10);
        var verified = new SaveContainer(output, "转换预览");
        new SlotInventory(verified, targetSlot);
        if (verified.Owner != targetId || verified.Roles[targetSlot].Name != source.Roles[sourceSlot].Name || verified.Roles[targetSlot].Level != source.Roles[sourceSlot].Level) throw new InvalidDataException("转换后验证失败。");
        if (toVanilla && new SlotInventory(verified, targetSlot).Hits.Count > 0) throw new InvalidDataException("转换后仍有已知无缝道具，已停止写入。");
        return result;
    }
}

internal sealed class SaveSnapshot
{
    public string OriginalPath, BackupFile, Sha256;
    public bool Existed, IsTarget;
}
internal sealed class SaveOperation
{
    public string Format = "SeamlessLauncherSaveBackupV1";
    public string Time, Action, TargetPath, OutputSha256, State;
    public int SourceSlot, TargetSlot;
    public string SourceSteamId, TargetSteamId;
    public List<string> Removed = new List<string>();
    public List<SaveSnapshot> Files = new List<SaveSnapshot>();
}
internal static class SaveTransactions
{
    internal static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EldenRing", "SeamlessLauncherBackups"); } }
    internal static void GameClosed()
    {
        Process[] processes = Process.GetProcessesByName("eldenring");
        try { if (processes.Length > 0) throw new InvalidOperationException("请先完全退出游戏，再转换或恢复存档。"); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    private static void Json(string folder, SaveOperation operation)
    { File.WriteAllText(Path.Combine(folder, "operation.json"), new JavaScriptSerializer().Serialize(operation), new UTF8Encoding(true)); }
    internal static string Snapshot(string source, string target, SaveOperation operation, string backupRoot)
    {
        string folder = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(folder); operation.Time = DateTime.Now.ToString("o"); operation.TargetPath = Path.GetFullPath(target); operation.State = "backed_up";
        var paths = new List<string>(); if (source != null) { paths.Add(source); paths.Add(source + ".bak"); } paths.Add(target); paths.Add(target + ".bak");
        foreach (string name in paths)
        {
            string full = Path.GetFullPath(name); bool duplicate = false;
            foreach (var saved in operation.Files) if (String.Equals(saved.OriginalPath, full, StringComparison.OrdinalIgnoreCase)) duplicate = true;
            if (duplicate) continue;
            var item = new SaveSnapshot { OriginalPath = full, Existed = File.Exists(full), IsTarget = String.Equals(full, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) || String.Equals(full, Path.GetFullPath(target + ".bak"), StringComparison.OrdinalIgnoreCase) };
            item.BackupFile = (item.IsTarget ? "target-" : "source-") + Path.GetFileName(full);
            if (item.Existed)
            { byte[] data = File.ReadAllBytes(full); item.Sha256 = SaveContainer.Fingerprint(data); string copy = Path.Combine(folder, item.BackupFile); File.WriteAllBytes(copy, data); if (SaveContainer.Fingerprint(File.ReadAllBytes(copy)) != item.Sha256) throw new IOException("自动备份校验失败，未改写存档。"); }
            operation.Files.Add(item);
        }
        Json(folder, operation); return folder;
    }
    internal static void Atomic(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + ".launcher-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(data, 0, data.Length); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Unchanged(SaveOperation operation)
    {
        foreach (var saved in operation.Files)
            if (File.Exists(saved.OriginalPath) != saved.Existed || (saved.Existed && SaveContainer.Fingerprint(File.ReadAllBytes(saved.OriginalPath)) != saved.Sha256)) throw new IOException("存档在预览或备份期间发生变化，请刷新后重试。");
    }
    private static void ApplySnapshots(string folder, SaveOperation operation)
    {
        foreach (var saved in operation.Files) if (saved.IsTarget)
        {
            if (saved.Existed)
            { string file = Path.Combine(folder, saved.BackupFile); byte[] data = File.ReadAllBytes(file); if (SaveContainer.Fingerprint(data) != saved.Sha256) throw new IOException("备份指纹不符，无法恢复。"); Atomic(saved.OriginalPath, data); }
            else if (File.Exists(saved.OriginalPath)) File.Delete(saved.OriginalPath);
        }
    }
    internal static string Install(string source, string target, SaveContainer sourceRead, SaveContainer targetRead, SaveConversion result, string backupRoot)
    {
        GameClosed();
        if (String.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("源文件与目标文件不能相同。");
        if (!SaveContainer.Equal(File.ReadAllBytes(source), sourceRead.Data) || (targetRead == null ? File.Exists(target) : !File.Exists(target) || !SaveContainer.Equal(File.ReadAllBytes(target), targetRead.Data))) throw new IOException("预览后存档有变化，请刷新重试。");
        new SaveContainer(result.Bytes, target);
        var operation = new SaveOperation { Action = result.Description, SourceSlot = result.SourceSlot + 1, TargetSlot = result.TargetSlot + 1, SourceSteamId = sourceRead.Owner.ToString(), TargetSteamId = new SaveContainer(result.Bytes, target).Owner.ToString(), OutputSha256 = SaveContainer.Fingerprint(result.Bytes), Removed = result.Removed };
        string folder = Snapshot(source, target, operation, backupRoot); GameClosed(); Unchanged(operation);
        // Snapshot and preview must describe the same generation of the files.
        if (!SaveContainer.Equal(File.ReadAllBytes(source), sourceRead.Data) || (targetRead == null ? File.Exists(target) : !File.Exists(target) || !SaveContainer.Equal(File.ReadAllBytes(target), targetRead.Data))) throw new IOException("备份期间存档改变，未写入，请刷新重试。");
        try
        {
            Atomic(target, result.Bytes); Atomic(target + ".bak", result.Bytes);
            if (SaveContainer.Fingerprint(File.ReadAllBytes(target)) != operation.OutputSha256 || SaveContainer.Fingerprint(File.ReadAllBytes(target + ".bak")) != operation.OutputSha256) throw new IOException("写入后校验失败。");
            operation.State = "completed"; Json(folder, operation);
        }
        catch { ApplySnapshots(folder, operation); operation.State = "rolled_back"; Json(folder, operation); throw; }
        return folder;
    }
    internal static SaveOperation ReadOperation(string path)
    {
        var op = new JavaScriptSerializer().Deserialize<SaveOperation>(File.ReadAllText(path));
        if (op == null || op.Format != "SeamlessLauncherSaveBackupV1" || op.Files == null || op.Files.Count == 0) throw new InvalidDataException("不是本启动器生成的备份记录。");
        string target = Path.GetFullPath(op.TargetPath); string extension = Path.GetExtension(target).ToLowerInvariant();
        if (extension != ".sl2" && extension != ".co2") throw new InvalidDataException("备份目标不是支持的存档文件。");
        foreach (var file in op.Files)
        {
            if (Path.GetFileName(file.BackupFile) != file.BackupFile) throw new InvalidDataException("备份路径异常。");
            if (file.IsTarget && !String.Equals(Path.GetFullPath(file.OriginalPath), target, StringComparison.OrdinalIgnoreCase) && !String.Equals(Path.GetFullPath(file.OriginalPath), target + ".bak", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份目标不一致。");
        }
        return op;
    }
    internal static string Restore(string manifest, string backupRoot)
    {
        GameClosed(); var operation = ReadOperation(manifest); string folder = Path.GetDirectoryName(manifest);
        foreach (var file in operation.Files) if (file.IsTarget && file.Existed)
            if (SaveContainer.Fingerprint(File.ReadAllBytes(Path.Combine(folder, file.BackupFile))) != file.Sha256) throw new IOException("备份文件校验失败，未恢复。");
        var recovery = new SaveOperation { Action = "恢复前自动备份" };
        string recoveryFolder = Snapshot(null, operation.TargetPath, recovery, backupRoot); GameClosed(); Unchanged(recovery);
        try { ApplySnapshots(folder, operation); recovery.State = "completed"; Json(recoveryFolder, recovery); }
        catch { ApplySnapshots(recoveryFolder, recovery); throw; }
        return recoveryFolder;
    }
}
