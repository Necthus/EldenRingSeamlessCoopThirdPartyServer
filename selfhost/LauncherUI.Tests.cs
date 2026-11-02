using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;

internal static class LauncherUITests
{
    static int assertions;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    static void Rejected(Action action, string message)
    { bool failed = false; try { action(); } catch (ArgumentException) { failed = true; } catch (IOException) { failed = true; } Check(failed, message); }
    [STAThread]
    static int Main(string[] args)
    {
        WindowsDpi.Initialize();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.Combine(Path.GetTempPath(), "ersc-ui-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "SeamlessCoop"));
        Directory.CreateDirectory(Path.Combine(root, "steam_settings"));
        string mod = Path.Combine(root, "SeamlessCoop", "ersc_settings.ini"), user = Path.Combine(root, "steam_settings", "configs.user.ini");
        string save = Path.Combine(root, "test-storage");
        string originalMod = "; preserve comment\r\n[NETWORK]\r\nuse_steam_network = 0\r\nrelay_server = 39.96.9.72\r\nrelay_port = 10099\r\n[PASSWORD]\r\ncooppassword = oldtest\r\n[SAVE]\r\nsave_file_extension = co2\r\n[CUSTOM]\r\nspecial = keep\r\n";
        string originalUser = "; preserve user comment\r\n[user::general]\r\naccount_name = 原玩家\r\naccount_steamid = 76561198000001001\r\nlanguage = japanese\r\n[user::saves]\r\nlocal_save_path = " + save + "\r\n";
        File.WriteAllText(mod, originalMod, new UTF8Encoding(true)); File.WriteAllText(user, originalUser, new UTF8Encoding(false));
        try
        {
            Check(WindowsDpi.PerMonitorV2(), "Embedded manifest did not activate Per-Monitor V2 awareness");
            using (var form = new SettingsWindow(root))
            {
                Check(form.Relay.Checked, "Mode did not load"); Check(form.Password.Text == "oldtest", "Password did not load");
                Check(form.Username.Text == "原玩家", "Unicode name did not load"); Check(form.SteamId.Text == "76561198000001001", "ID did not load");
                Check(((LanguageChoice)form.Language.SelectedItem).Code == "japanese", "Language did not load");
                Check(form.BlockModUpdates.Checked, "Mod update guard should default on");
                Check(File.ReadAllText(mod) == originalMod, "Opening UI changed config"); Check(File.ReadAllText(user) == originalUser, "Opening UI changed user");
                form.Username.Text = "测试玩家"; form.Password.Text = "changedtest"; form.Language.SelectedIndex = 1;
                Check(form.SaveSettings(true), "Valid settings save failed");
                var ini = new IniFile(mod); var u = new IniFile(user);
                Check(ini.Get("NETWORK", "block_mod_update_checks", "") == "1", "Mod update guard preference not saved");
                Check(ini.Get("PASSWORD", "cooppassword", "") == "changedtest", "Password did not save");
                Check(u.Get("user::general", "account_name", "") == "测试玩家", "Unicode name did not save");
                Check(u.Get("user::general", "language", "") == "tchinese", "Traditional Chinese did not save");
                Check(u.Get("user::saves", "local_save_path", "") == save, "Save path changed");
                Check(ini.Get("SAVE", "save_file_extension", "") == "co2", "Save extension changed");
                Check(ini.Get("CUSTOM", "special", "") == "keep" && File.ReadAllText(mod).Contains("; preserve comment"), "Unknown key/comment lost");
                Check(File.ReadAllText(mod + ".launcher.bak") == originalMod, "First backup not original");
                byte[] saved = File.ReadAllBytes(mod); string savedUser = File.ReadAllText(user);
                form.Port.Text = "70000"; Check(!form.SaveSettings(true), "Invalid port accepted");
                Check(Convert.ToBase64String(saved) == Convert.ToBase64String(File.ReadAllBytes(mod)), "Invalid save changed file");
                form.Port.Text = "10099"; form.SteamId.Text = "123"; Check(!form.SaveSettings(true), "Invalid identity accepted");
                Check(File.ReadAllText(user) == savedUser, "Invalid identity save changed user");
                form.SteamId.Text = "76561198000001001"; form.Steam.Checked = true;
                form.Username.Text = "do not persist"; form.SteamId.Text = "123"; form.Password.Text = "steamtest";
                Check(form.SaveSettings(true), "Steam mode rejected hidden fields");
                Check(File.ReadAllText(user) == savedUser, "Steam mode rewrote user config");
                Check(new IniFile(mod).Get("NETWORK", "use_steam_network", "") == "1", "Steam selection not saved");
                form.Relay.Checked = true; form.SteamId.Text = ""; form.Username.Text = "";
                Check(form.SaveSettings(false), "Automatic identity save failed");
                u = new IniFile(user);
                Check(u.Get("user::general", "account_steamid", null) == null, "Blank ID left a key that would regenerate identity");
                Check(u.Get("user::saves", "local_save_path", "") == save, "Blank ID modified unrelated values");
                Check(File.ReadAllText(mod + ".launcher.bak") == originalMod, "Original backup overwritten");
                form.Password.Text = ""; Check(!form.SaveSettings(true), "Empty launch password accepted");
                Check(form.SaveSettings(false), "Empty draft password should be allowed to save");
            }
            using (var form = new SettingsWindow(root))
            { Check(form.Relay.Checked && form.Password.Text == "", "Reload did not match saved configuration"); form.BlockModUpdates.Checked = false; Check(form.SaveSettings(false), "Disabling update guard failed"); }
            using (var form = new SettingsWindow(root))
            { Check(!form.BlockModUpdates.Checked, "Disabled update guard preference not reloaded"); }
            // A new Steam-mode installation must not create compatibility settings.
            string fresh = Path.Combine(root, "fresh"); Directory.CreateDirectory(Path.Combine(fresh, "SeamlessCoop"));
            using (var form = new SettingsWindow(fresh))
            { form.Password.Text = "draft"; Check(form.Steam.Checked && form.SaveSettings(true), "Default Steam setup failed"); Check(!File.Exists(Path.Combine(fresh, "steam_settings", "configs.user.ini")), "Steam created user config"); }
            // Verify missing local ID/name reads a stored global identity, never new random IDs.
            Directory.CreateDirectory(Path.Combine(save, "settings"));
            File.WriteAllText(Path.Combine(save, "settings", "configs.user.ini"), "[user::general]\naccount_steamid=76561198000001002\naccount_name=GeneratedUser\n");
            using (var form = new SettingsWindow(root))
            { Check(form.SteamId.Text == "76561198000001002", "Persisted global identity not loaded"); Check(form.Username.Text == "GeneratedUser", "Global name not loaded"); }
            // Unknown languages are retained on unrelated edits.
            var file = new IniFile(user); file.Set("user::general", "language", "german"); file.Save();
            using (var form = new SettingsWindow(root))
            { form.Password.Text = "keep-language"; Check(form.SaveSettings(true), "Custom language save failed"); Check(new IniFile(user).Get("user::general", "language", "") == "german", "Existing unsupported language overwritten"); }
            using (var form = new SettingsWindow(root))
            {
                form.Username.Text = "AutoSaveTest";
                Stopwatch clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 950) { Application.DoEvents(); Thread.Sleep(15); }
                Check(new IniFile(user).Get("user::general", "account_name", "") == "AutoSaveTest", "Debounced automatic save did not fire");
                string[] codes = { "schinese", "tchinese", "english", "japanese" };
                for (int i = 0; i < 4; i++) { form.Language.SelectedIndex = i; Check(form.SaveSettings(false), "Language save failed"); Check(new IniFile(user).Get("user::general", "language", "") == codes[i], "Language mapping wrong"); }
                form.Size = new System.Drawing.Size(470, 430); form.PerformLayout();
                Check(form.Controls[0].Dock == DockStyle.Fill, "Root layout not responsive");
                var tabs = (TabControl)((TableLayoutPanel)form.Controls[0]).GetControlFromPosition(0, 1);
                Check(((Panel)tabs.TabPages[0].Controls[0]).AutoScroll, "Network options cannot scroll");
                Check(((Panel)tabs.TabPages[1].Controls[0]).AutoScroll, "Gameplay options cannot scroll");
                Check(tabs.TabPages.Count == 4 && tabs.TabPages[2].Text == "正版steamapi64恢复" && tabs.TabPages[3].Text == "存档管理", "Restore/save management tab missing");
                Check(tabs.TabPages[2].Controls.Count == 1 && tabs.TabPages[2].Controls[0].Text == "恢复原steamapi64", "Restore tab should contain only its action");
                var footer = (TableLayoutPanel)((TableLayoutPanel)form.Controls[0]).GetControlFromPosition(0, 3);
                Check(footer.GetControlFromPosition(2, 0).Text == "启动游戏", "Launch action not pinned in footer");
            }
            // Explicit discovery-list edits autosave without changing identities
            // or silently replacing files edited by another program.
            string discoveryRoot = Path.Combine(root, "discovery");
            Directory.CreateDirectory(Path.Combine(discoveryRoot, "SeamlessCoop")); Directory.CreateDirectory(Path.Combine(discoveryRoot, "steam_settings"));
            File.WriteAllText(Path.Combine(discoveryRoot, "SeamlessCoop", "ersc_settings.ini"), originalMod);
            string discoveryPath = Path.Combine(discoveryRoot, "steam_settings", "custom_broadcasts.txt");
            string originalDiscovery = "# player endpoints\r\n10.77.42.2:47584\r\n\r\n; keep this comment\r\n10.77.42.3:47584\r\n";
            File.WriteAllText(discoveryPath, originalDiscovery, new UTF8Encoding(true)); byte[] originalDiscoveryBytes = File.ReadAllBytes(discoveryPath);
            using (var form = new SettingsWindow(discoveryRoot))
            {
                Check(form.Discovery.Addresses.Items.Count == 2, "Discovery list did not read endpoints");
                Check(SaveContainer.Equal(File.ReadAllBytes(discoveryPath), originalDiscoveryBytes), "Opening UI rewrote discovery");
                form.Username.Text = "unrelated"; Check(form.SaveSettings(false), "Unrelated configuration save failed");
                Check(SaveContainer.Equal(File.ReadAllBytes(discoveryPath), originalDiscoveryBytes), "Unrelated edit rewrote discovery");
                form.Discovery.AddressInput.Text = "10.77.42.4"; form.Discovery.AddAddress();
                Stopwatch timer = Stopwatch.StartNew(); while (timer.ElapsedMilliseconds < 1000) { Application.DoEvents(); Thread.Sleep(15); }
                Check(File.ReadAllText(discoveryPath).Contains("10.77.42.4:47584"), "List addition did not autosave/default the game port");
                Check(SaveContainer.Equal(File.ReadAllBytes(discoveryPath + ".launcher.bak"), originalDiscoveryBytes), "Discovery first backup does not match input");
                Check(File.ReadAllText(discoveryPath).Contains("# player endpoints\r\n") && File.ReadAllText(discoveryPath).Contains("; keep this comment\r\n"), "Comments or newline style lost");
                Check(File.ReadAllBytes(discoveryPath)[0] == 239, "UTF-8 BOM not retained");
                form.Discovery.AddressInput.Text = "10.77.42.4:47584"; Rejected(delegate { form.Discovery.AddAddress(); }, "Duplicate discovery entry accepted");
                form.Discovery.AddressInput.Text = "999.77.42.5:47584"; Rejected(delegate { form.Discovery.AddAddress(); }, "Invalid IPv4 accepted");
                form.Discovery.AddressInput.Text = "10.77.42.5:0"; Rejected(delegate { form.Discovery.AddAddress(); }, "Invalid discovery port accepted");
                form.Discovery.AddressInput.Text = "http://10.77.42.5:47584"; Rejected(delegate { form.Discovery.AddAddress(); }, "URL accepted as discovery address");
                form.Discovery.Addresses.SelectedIndex = 1; form.Discovery.AddressInput.Text = "10.77.42.5:47585"; form.Discovery.UpdateAddress();
                Check(form.SaveSettings(false), "Discovery edit failed");
                Check(!File.ReadAllText(discoveryPath).Contains("10.77.42.3:47584") && File.ReadAllText(discoveryPath).Contains("10.77.42.5:47585"), "Selected entry not updated");
                form.Discovery.Addresses.SelectedIndex = 0; form.Discovery.RemoveAddress(); Check(form.SaveSettings(false), "Discovery remove failed");
                Check(!File.ReadAllText(discoveryPath).Contains("10.77.42.2:47584"), "Selected endpoint not removed");
                Check(SaveContainer.Equal(File.ReadAllBytes(discoveryPath + ".launcher.bak"), originalDiscoveryBytes), "First backup overwritten");
                form.Discovery.AddressInput.Text = "Player.Example.com:47584"; form.Discovery.AddAddress(); form.Steam.Checked = true;
                Check(form.SaveSettings(false) && File.ReadAllText(discoveryPath).Contains("player.example.com:47584"), "Switching modes dropped a pending explicit list edit");
                byte[] savedDiscovery = File.ReadAllBytes(discoveryPath); form.Password.Text = "steam-unrelated"; Check(form.SaveSettings(false), "Steam settings save failed");
                Check(SaveContainer.Equal(File.ReadAllBytes(discoveryPath), savedDiscovery), "Steam-only settings rewrote discovery");
                form.Relay.Checked = true; form.Discovery.AddressInput.Text = "10.77.42.6:47584"; form.Discovery.AddAddress();
                string external = "10.77.42.99:47584\n"; File.WriteAllText(discoveryPath, external);
                Check(!form.SaveSettings(false) && File.ReadAllText(discoveryPath) == external, "Stale editor overwrote external edits");
                form.Discovery.Reload(); Check(form.Discovery.Addresses.Items.Count == 1 && form.Discovery.Addresses.Items[0].ToString() == "10.77.42.99:47584", "Discovery reload failed");
                Check(form.SaveSettings(false), "Reload did not clear conflict");
                form.Discovery.Addresses.SelectedIndex = 0; form.Discovery.RemoveAddress(); Check(form.SaveSettings(false), "Empty list could not be saved");
                Check(new BroadcastAddressFile(discoveryPath).Entries.Count == 0, "Removing final entry did not save empty list");
            }
            string noDiscovery = Path.Combine(root, "discovery-missing"); Directory.CreateDirectory(Path.Combine(noDiscovery, "SeamlessCoop"));
            using (var form = new SettingsWindow(noDiscovery))
            { form.Password.Text = "steam"; Check(form.SaveSettings(false), "Fresh Steam setup failed"); Check(!File.Exists(Path.Combine(noDiscovery, "steam_settings", "custom_broadcasts.txt")), "Unedited missing discovery file was created"); }
            Check(BroadcastAddressFile.Normalize("10.77.42.2") == "10.77.42.2:47584", "Default discovery port wrong");
            Check(BroadcastAddressFile.Normalize(" example.com:65535 ") == "example.com:65535", "DNS endpoint parsing failed");
            foreach (string invalid in new string[] { "", "127.1", "010.1.2.3", "1.2.3.4:65536", "0.0.0.0", "[::1]:47584", "1.2.3.4\n5.6.7.8" })
                Rejected(delegate { BroadcastAddressFile.Normalize(invalid); }, "Invalid discovery endpoint accepted: " + invalid);
            if (args.Length == 2)
            {
                string official = args[0], emulator = args[1];
                Check(OriginalSteamSdk.IsOriginal(official), "Valid official Steam API was rejected");
                Check(!OriginalSteamSdk.IsOriginal(emulator), "Compatibility DLL was considered an original");
                string sdkRoot = Path.Combine(root, "sdk-test"); Directory.CreateDirectory(sdkRoot);
                string sdkCurrent = Path.Combine(sdkRoot, "steam_api64.dll");
                string sdkBackup = Path.Combine(sdkRoot, "SeamlessCoop", "network", "steam_api64.steam.dll");
                Check(!OriginalSteamSdk.EnsureBackup(sdkRoot), "Missing root SDK created a backup");
                File.Copy(emulator, sdkCurrent);
                Check(!OriginalSteamSdk.EnsureBackup(sdkRoot) && !File.Exists(sdkBackup), "Emulator was backed up as original");
                bool rejected = false;
                try { OriginalSteamSdk.Restore(sdkRoot); } catch (FileNotFoundException) { rejected = true; }
                Check(rejected, "Missing original backup was accepted");
                File.Copy(official, sdkCurrent, true);
                Check(OriginalSteamSdk.EnsureBackup(sdkRoot), "Official root DLL was not automatically backed up");
                string officialBytes = Convert.ToBase64String(File.ReadAllBytes(official));
                Check(Convert.ToBase64String(File.ReadAllBytes(sdkBackup)) == officialBytes, "Automatic backup changed bytes");
                File.Copy(emulator, sdkCurrent, true);
                Check(!OriginalSteamSdk.EnsureBackup(sdkRoot), "Existing original backup was overwritten");
                Check(Convert.ToBase64String(File.ReadAllBytes(sdkBackup)) == officialBytes, "Original backup was corrupted by repeated launch");
                Check(OriginalSteamSdk.Restore(sdkRoot), "Root SDK was not restored");
                Check(Convert.ToBase64String(File.ReadAllBytes(sdkCurrent)) == officialBytes, "Restore did not match original");
                Check(!OriginalSteamSdk.Restore(sdkRoot), "Already restored SDK was copied unnecessarily");
                byte[] damaged = File.ReadAllBytes(official); damaged[1024] ^= 1;
                File.WriteAllBytes(sdkBackup, damaged);
                Check(!OriginalSteamSdk.IsOriginal(sdkBackup), "Modified signed file passed validation");
                string currentBefore = Convert.ToBase64String(File.ReadAllBytes(sdkCurrent));
                rejected = false;
                try { OriginalSteamSdk.Restore(sdkRoot); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && currentBefore == Convert.ToBase64String(File.ReadAllBytes(sdkCurrent)), "Invalid backup changed the root SDK");
                Check(!File.Exists(Path.Combine(sdkRoot, "SeamlessCoop", "ersc_settings.ini")), "Restore created or changed networking settings");
            }
            Console.WriteLine("PASS: " + assertions + " assertions; persistence, preservation, validation, mode isolation, reload and identity fallback.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
