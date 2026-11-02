"""Package a game-client-only Release from explicitly supplied local components.

Runs on a developer machine; players only extract Game/ and run the launcher.
Never copies arbitrary directory trees, player settings, saves or server tools.
"""
import argparse
import configparser
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
MOD_SHA256 = "fcd11a1849fd37851bb449be30f1525cf498f63fd1fa568c4e15030ebe7cb251"
RELAY_SHA256 = "723e13f5a2e1ec799f342612a3294229ca832d143fdea85c00025216b6696e88"


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", default="0.1.0")
    parser.add_argument("--mod-game", type=Path, required=True)
    parser.add_argument("--launcher", type=Path, required=True)
    parser.add_argument("--gse-source", type=Path, required=True, help="Upstream checkout for license and credits only")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--icon-notice", type=Path)
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[a-z0-9.]+)?", args.version):
        raise ValueError("Invalid version")
    mod = args.mod_game.resolve()
    if sha(mod / "SeamlessCoop/ersc.dll") != MOD_SHA256:
        raise ValueError("Unexpected Mod core; this package requires the validated version")
    if sha(mod / "SeamlessCoop/network/steam_api64.relay.dll") != RELAY_SHA256:
        raise ValueError("Unexpected compatibility interface")
    output = args.output.resolve()
    package_name = f"EldenRingSeamlessCoop-Enhanced-v{args.version}-Windows-x64"
    folder, archive = output / package_name, output / (package_name + ".zip")
    if folder.exists() or archive.exists():
        raise FileExistsError("Release output exists; refusing to overwrite")
    folder.mkdir(parents=True)

    def copy(source, relative):
        target = folder / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        if sha(source) != sha(target):
            raise IOError("Copy verification failed: " + relative)

    def write(relative, value):
        target = folder / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(value, encoding="utf-8-sig" if relative.endswith((".md", ".ini")) else "utf-8", newline="\n")

    copy(args.launcher, "Game/ersc_launcher.exe")
    for relative in (
        "ersc_launcher.original.exe", "SeamlessCoop/ersc.dll",
        "SeamlessCoop/crashpad/crashpad_handler.exe",
        "SeamlessCoop/locale/english.json", "SeamlessCoop/locale/schinese.json",
        "SeamlessCoop/network/steam_api64.relay.dll",
    ):
        copy(mod / relative, "Game/" + relative)
    # Explicit, fresh defaults: never copy a real player's configuration.
    write("Game/SeamlessCoop/ersc_settings.ini", """[GAMEPLAY]
allow_invaders = 1
death_debuffs = 1
allow_summons = 1
overhead_player_display = 0
skip_splash_screens = 0
append_steam_id_to_players = 0
always_spectate_on_death = 0
default_boot_master_volume = 5

[SCALING]
enemy_health_scaling = 35
enemy_damage_scaling = 0
enemy_posture_scaling = 15
boss_health_scaling = 100
boss_damage_scaling = 0
boss_posture_scaling = 20

[PASSWORD]
cooppassword =

[SAVE]
save_file_extension = co2

[LANGUAGE]
mod_language_override =

[NETWORK]
use_steam_network = 1
relay_server =
relay_port = 10099
block_mod_update_checks = 1
""")
    write("Game/SeamlessCoop/network/client.json", json.dumps({
        "tunnel_name": "", "public_ip": "", "udp_port": 10099, "gse_sha256": RELAY_SHA256,
    }, indent=2) + "\n")
    write("Game/steam_settings/configs.main.ini", "[main::connectivity]\ndisable_networking=0\noffline=0\nlisten_port=47584\n")
    write("Game/steam_settings/custom_broadcasts.txt", "".join(f"10.77.42.{i}:47584\n" for i in range(2, 7)))
    copy(ROOT / "docs/release-install.md", "安装说明.md")
    copy(ROOT / "docs/attribution.md", "第三方来源.md")
    copy(ROOT / "docs/save-manager.md", "说明/存档管理.md")
    copy(ROOT / "docs/mod-network-checks.md", "说明/禁止Mod联网检查.md")
    copy(args.gse_source / "LICENSE", "许可证/GSE-LGPL-3.0.txt")
    copy(args.gse_source / "CREDITS.md", "许可证/GSE-上游第三方致谢.md")
    license_text = ""
    for url in ("https://www.gnu.org/licenses/gpl-3.0.txt", "https://raw.githubusercontent.com/gcc-mirror/gcc/master/COPYING3"):
        try:
            with urllib.request.urlopen(url, timeout=12) as response:
                license_text = response.read().decode("utf-8")
            break
        except (OSError, urllib.error.URLError):
            continue
    if "GNU GENERAL PUBLIC LICENSE" not in license_text or len(license_text) < 30000:
        raise ValueError("GPL license download was not valid")
    write("许可证/GPL-3.0.txt", license_text)
    if args.icon_notice:
        copy(args.icon_notice, "许可证/启动器头像来源.md")

    files = sorted(p for p in folder.rglob("*") if p.is_file())
    forbidden_parts = ("server", "源码", "逆向", "investigation", "crashdumps", "runtime", "backups")
    for path in files:
        relative = path.relative_to(folder).as_posix()
        if any(part in relative.lower() for part in forbidden_parts) or path.suffix.lower() in (".conf", ".py", ".ps1", ".bat", ".cs", ".sl2", ".co2", ".dmp"):
            raise ValueError("Unexpected artifact in client-only package: " + relative)
        if path.suffix.lower() in (".ini", ".json", ".txt", ".md"):
            text = path.read_text(encoding="utf-8-sig")
            if re.search(r"(?im)^\s*(?:PrivateKey|PresharedKey)\s*=\s*[A-Za-z0-9+/]{40,}=", text):
                raise ValueError("Key material found")
            if re.search(r"C:[/\\]Users[/\\]|\b765611\d{11}\b", text):
                raise ValueError("Personal configuration found: " + relative)
    if (folder / "Game/steam_api64.dll").exists() or (folder / "Game/steam_settings/configs.user.ini").exists():
        raise ValueError("Must not ship root Steam API or fixed player identity")
    settings = configparser.ConfigParser()
    settings.read(folder / "Game/SeamlessCoop/ersc_settings.ini", encoding="utf-8-sig")
    assert settings["PASSWORD"]["cooppassword"] == "" and settings["NETWORK"]["relay_server"] == ""
    assert settings["NETWORK"]["use_steam_network"] == "1" and settings["NETWORK"]["block_mod_update_checks"] == "1"
    manifest = {
        "version": args.version, "original_mod_version": "2.0.1", "target_platform": "Windows x64",
        "client_only": True, "core_dll_modified": False,
        "files": [{"path": p.relative_to(folder).as_posix(), "bytes": p.stat().st_size, "sha256": sha(p)} for p in files],
    }
    write("文件校验.json", json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as package:
        for path in sorted(folder.rglob("*")):
            if path.is_file():
                package.write(path, package_name + "/" + path.relative_to(folder).as_posix())
    with zipfile.ZipFile(archive) as package:
        assert package.testzip() is None
        assert len(package.infolist()) == len(manifest["files"]) + 1
        for item in manifest["files"]:
            assert hashlib.sha256(package.read(package_name + "/" + item["path"])).hexdigest() == item["sha256"]
    report = {"folder": str(folder), "archive": str(archive), "bytes": archive.stat().st_size,
              "sha256": sha(archive), "file_count": len(manifest["files"]) + 1, "client_only": True}
    (output / "release-build.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
