using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;

internal static class SaveManagerTests
{
    static int assertions;
    static void Check(bool condition, string text) { assertions++; if (!condition) throw new Exception(text); }
    static void Throws(Action action, string text)
    { bool failed = false; try { action(); } catch { failed = true; } Check(failed, text); }
    static bool Region(byte[] a, byte[] b, int start, int size)
    { for (int i = start; i < start + size; i++) if (a[i] != b[i]) return false; return true; }
    [STAThread]
    static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "ersc-save-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var vanilla = SaveContainer.Read(args[0]); var coop = SaveContainer.Read(args[1]);
            Check(LauncherConfig.ValidSteamId(vanilla.Owner.ToString()) && coop.Owner == vanilla.Owner, "Provide two saves bound to the same valid account");
            Check(!String.IsNullOrEmpty(coop.Roles[0].Name), "Name parse failed");
            var scan = new SlotInventory(coop, 0);
            Console.WriteLine("Local role: " + coop.Roles[0].Name + " level " + coop.Roles[0].Level + "; known Mod entries " + scan.Hits.Count);
            foreach (var item in scan.Hits) Console.WriteLine(item.Name + " " + item.Id + " " + item.Location);
            Check(scan.Hits.Count >= 7, "Known co-op goods not found");
            int empty = -1; for (int i = 0; i < 10; i++) if (!vanilla.Roles[i].Active) { empty = i; break; }
            Check(empty >= 0, "No empty vanilla test slot");
            var clean = SaveConversion.Create(coop, 0, vanilla, empty, vanilla.Owner, true);
            var cleanSave = new SaveContainer(clean.Bytes, "test");
            Check(new SlotInventory(cleanSave, empty).Hits.Count == 0, "Cleaning missed known goods or shortcuts");
            Check(cleanSave.Roles[empty].Level == coop.Roles[0].Level && cleanSave.Roles[empty].Name == coop.Roles[0].Name, "Role summary lost");
            Check(clean.Removed.Count == scan.Hits.Count, "Removal report incomplete");
            var afterScan = new SlotInventory(cleanSave, empty);
            int delta = SaveContainer.SlotStart(empty) - SaveContainer.SlotStart(0);
            for (int b = 0; b < scan.Blocks.Count; b++)
            {
                var beforeBlock = scan.Blocks[b]; var afterBlock = afterScan.Blocks[b];
                for (int i = 0; i < beforeBlock.Capacity; i++)
                {
                    int beforeRecord = beforeBlock.RecordsOffset + i * 12, afterRecord = afterBlock.RecordsOffset + i * 12;
                    uint id = SlotInventory.GoodsId(SaveContainer.U32(coop.Data, beforeRecord));
                    if (!SlotInventory.Known(id))
                        Check(RegionBetween(coop.Data, beforeRecord, clean.Bytes, afterRecord, 12), "An ordinary/DLC inventory record changed");
                }
            }
            for (int i = 0; i < 12; i++) if (i != empty && i != 10)
                Check(Region(vanilla.Data, clean.Bytes, SaveContainer.SectionStart(i), SaveContainer.SectionSize(i)), "Unrelated target section changed " + i);
            for (int i = 0; i < 10; i++) if (i != empty)
                Check(Region(vanilla.Data, clean.Bytes, SaveContainer.SummaryBase + i * SaveContainer.SummaryBytes, SaveContainer.SummaryBytes), "Other role summary changed");
            Check(Region(vanilla.Data, clean.Bytes, 0, 0x300), "Container header changed");
            var imported = SaveConversion.Create(vanilla, 0, coop, 1, coop.Owner, false);
            Check(new SaveContainer(imported.Bytes, "test").Roles[1].Name == vanilla.Roles[0].Name && imported.Removed.Count == 0, "Vanilla to co-op import failed");
            ulong testTarget = coop.Owner == 76561198000001002UL ? 76561198000001003UL : 76561198000001002UL;
            var fresh = SaveConversion.Create(coop, 0, null, 3, testTarget, true);
            var freshSave = new SaveContainer(fresh.Bytes, "fresh");
            Check(freshSave.Owner == testTarget && freshSave.Roles[3].Active && !freshSave.Roles[0].Active, "New container or rebinding failed");
            Check(!ContainsOwner(fresh.Bytes, coop.Owner), "Old account remains in fresh imported role");
            Check(new SlotInventory(freshSave, 3).Hits.Count == 0, "New-file vanilla cleaning failed");
            Throws(delegate { SaveConversion.Create(coop, 0, vanilla, empty, testTarget, true); }, "Mismatched target account accepted");
            byte[] damaged = (byte[])coop.Data.Clone(); damaged[0x500] ^= 1;
            Throws(delegate { new SaveContainer(damaged, "bad"); }, "Checksum corruption accepted");
            // Unknown IDs in the reserved Seamless range must be blocked, never guessed.
            byte[] unknown = (byte[])coop.Data.Clone(); SaveContainer.Put32(unknown, scan.Hits[0].Record, 0xb0000000U | 8380013U); SaveContainer.Checksum(unknown, 0);
            var unknownSave = new SaveContainer(unknown, "unknown");
            Throws(delegate { SaveConversion.Create(unknownSave, 0, vanilla, empty, vanilla.Owner, true); }, "Unknown reserved Mod ID silently removed/retained");
            string source = Path.Combine(root, "ER0000.co2"), target = Path.Combine(root, "ER0000.sl2"), backups = Path.Combine(root, "backups");
            File.WriteAllBytes(source, coop.Data); File.WriteAllBytes(source + ".bak", coop.Data);
            File.WriteAllBytes(target, vanilla.Data); File.WriteAllBytes(target + ".bak", vanilla.Data);
            string backupFolder = SaveTransactions.Install(source, target, coop, vanilla, clean, backups);
            Check(SaveContainer.Equal(File.ReadAllBytes(source), coop.Data), "Source changed during installation");
            Check(SaveContainer.Equal(File.ReadAllBytes(target), clean.Bytes) && SaveContainer.Equal(File.ReadAllBytes(target + ".bak"), clean.Bytes), "Main and bak installation mismatch");
            var op = SaveTransactions.ReadOperation(Path.Combine(backupFolder, "operation.json"));
            Check(op.Files.Count == 4 && op.State == "completed", "Automatic backup incomplete");
            Check(op.SourceSteamId == coop.Owner.ToString() && op.TargetSteamId == vanilla.Owner.ToString(), "Backup lacks SteamID provenance");
            foreach (var item in op.Files) Check(item.Existed && SaveContainer.Fingerprint(File.ReadAllBytes(Path.Combine(backupFolder, item.BackupFile))) == item.Sha256, "Snapshot fingerprint invalid");
            SaveTransactions.Restore(Path.Combine(backupFolder, "operation.json"), backups);
            Check(SaveContainer.Equal(File.ReadAllBytes(target), vanilla.Data) && SaveContainer.Equal(File.ReadAllBytes(target + ".bak"), vanilla.Data), "Restore not byte-exact");
            Check(Directory.GetDirectories(backups).Length == 2, "Restore did not back up current files");
            var snapshot = op.Files.Find(delegate(SaveSnapshot file) { return file.IsTarget && file.Existed; });
            string snapshotPath = Path.Combine(backupFolder, snapshot.BackupFile); byte[] snapshotOriginal = File.ReadAllBytes(snapshotPath);
            byte[] snapshotBad = (byte[])snapshotOriginal.Clone(); snapshotBad[5] ^= 1; File.WriteAllBytes(snapshotPath, snapshotBad);
            Throws(delegate { SaveTransactions.Restore(Path.Combine(backupFolder, "operation.json"), backups); }, "Corrupted backup accepted");
            Check(SaveContainer.Equal(File.ReadAllBytes(target), vanilla.Data), "Failed restore changed live target");
            File.WriteAllBytes(snapshotPath, snapshotOriginal);
            byte[] changed = (byte[])vanilla.Data.Clone(); changed[0x500] ^= 1; File.WriteAllBytes(target, changed);
            Throws(delegate { SaveTransactions.Install(source, target, coop, vanilla, clean, backups); }, "Stale preview overwrote modified target");
            Check(SaveContainer.Equal(File.ReadAllBytes(target), changed), "Rejected write altered file");
            string newTarget = Path.Combine(root, "fresh", "ER0000.sl2");
            string newBackup = SaveTransactions.Install(source, newTarget, coop, null, fresh, backups);
            SaveTransactions.Restore(Path.Combine(newBackup, "operation.json"), backups);
            Check(!File.Exists(newTarget) && !File.Exists(newTarget + ".bak"), "Restore did not recover absent target state");
            if (args.Length > 2)
            {
                var friend = SaveContainer.Read(args[2]); var friendScan = new SlotInventory(friend, 0);
                Check(friendScan.Hits.Count >= 7, "Friend Mod items not recognized");
                var friendClean = SaveConversion.Create(friend, 0, null, 0, testTarget, true);
                Check(new SlotInventory(new SaveContainer(friendClean.Bytes, "test"), 0).Hits.Count == 0, "Friend conversion missed items");
            }
            WindowsDpi.Initialize(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var page = new SaveManagerPage(coop.Owner.ToString()))
            {
                Check(page.Source.Save != null && page.Target.Save != null, "Default SteamID saves not loaded in UI");
                Check(page.Source.Characters.Items.Count == 10 && page.Target.Characters.Items.Count == 10, "UI role slots missing");
                page.Target.Account.Text = testTarget.ToString(); page.Target.Format.SelectedIndex = 0; page.Target.DefaultPath();
                Check(page.Target.FilePath.Text.EndsWith(Path.Combine(testTarget.ToString(), "ER0000.sl2")), "Cross-account destination path not selected");
                Check(page.Source.Account.Text == coop.Owner.ToString(), "Changing target account changed source identity");
            }
            Console.WriteLine("PASS: " + assertions + " save-manager checks. Real save files were read only."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(root, true); }
    }
    static bool RegionBetween(byte[] a, int from, byte[] b, int to, int size)
    { for (int i = 0; i < size; i++) if (a[from + i] != b[to + i]) return false; return true; }
    static bool ContainsOwner(byte[] bytes, ulong owner)
    {
        byte[] value = BitConverter.GetBytes(owner);
        for (int i = 0; i <= bytes.Length - 8; i++)
        { bool same = true; for (int k = 0; k < 8; k++) if (bytes[i + k] != value[k]) { same = false; break; } if (same) return true; }
        return false;
    }
}
