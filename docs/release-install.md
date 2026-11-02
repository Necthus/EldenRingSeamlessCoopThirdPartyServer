# 客户端 Release 安装与升级

本包为 Windows x64 的增强 Seamless Co-op 客户端，包含原 Mod 的配套运行文件和已编译启动器，无需编译、运行 BAT／PowerShell 或安装其他 UI 框架。原 Mod 由 LukeYui（Yui）开发；本项目不修改其核心同步代码，完整来源见包内 `第三方来源.md`。

## 安装

1. 完全退出游戏和原来的设置启动器。
2. 找到有 `eldenring.exe` 的实际 `Game` 目录。
3. 将压缩包中 **`Game` 文件夹里面的内容**复制进实际 `Game` 目录。不是在其中再创建一层 `Game`。
4. 双击新的 `ersc_launcher.exe`，填写联机密码并选择网络模式。原 Mod 启动器已经随包保留为 `ersc_launcher.original.exe`，无需再次改名。

本包针对当前已验证的 **游戏 1.17.1／Seamless Co-op 2.0.1** 环境准备。原核心含 `YuiKeyNexus Version 3` 标识，属于当前项目配套文件，不代表原作者官方原封发行包。其他版本需自行确认兼容性。

## 升级已有的本项目客户端

如果已安装本项目当前配套核心且只升级增强启动器，**只替换 `ersc_launcher.exe` 即可**。

如果合并完整包，**保留自己的以下配置，不要用包内默认值覆盖**：

- `SeamlessCoop/ersc_settings.ini`：密码、玩法、模式等。
- `SeamlessCoop/network/client.json`：已有个性化服务器／接口设置时保留。
- `steam_settings/configs.user.ini`：若存在，保留玩家身份；本包不提供此文件。
- `steam_settings/custom_broadcasts.txt`、`configs.main.ini`：若已配置其他虚拟网段或端口则保留。

压缩包不含 `Game/steam_api64.dll`，也不含 `steam_api64.steam.dll`，因此复制文件时不会直接覆盖自己的原接口或备份。接口切换由点击“启动游戏”时执行。

## Steam 模式

默认选择 Steam 通道。Steam 模式需要自己的正常 Steam 游戏环境。第一次启动时，若原接口备份不存在，启动器会核验根目录 `steam_api64.dll`，确认有效原 Valve 接口后备份为 `SeamlessCoop/network/steam_api64.steam.dll`；已有备份不会覆盖。

若根目录已是 GSE 等兼容接口，不能从中恢复出原 Valve 文件，需要使用自己的原备份或正版安装中的原接口。不要把兼容接口手动改名作为原 Steam 备份。

## 自建服务器模式

1. 安装 Windows 原生 [WireGuard](https://www.wireguard.com/install/)，导入并激活自己的个人 `.conf`。
2. 在启动器选择“自建服务器”，填写自己的服务器 IP／域名和 UDP 端口（默认 10099）、联机密码、用户名、稳定且各不相同的 SteamID64，选择语言。
3. WireGuard 的 Endpoint 应与自己的公网入口一致。启动器中的 IP／端口不会自动编辑 WireGuard 配置。
4. 包内发现列表默认给出 `10.77.42.2–10.77.42.6:47584` 示例；若自己使用其他玩家地址／网段，在自建模式下滚动到「玩家发现地址」，使用“添加”“更新选中”“删除选中”修改列表。地址可用玩家的虚拟 IPv4 或域名，省略端口补全 `47584`；列表修改自动保存到 `steam_settings/custom_broadcasts.txt`，下次启动生效。首次修改会备份原文件。每人使用不同的个人隧道密钥及 SteamID，所有人密码与游戏／Mod 版本一致。

包内 `SeamlessCoop/network/steam_api64.relay.dll` 是已测 GSE 兼容接口，不需要另找配套 DLL。公网服务器地址、联机密码、账号身份、WireGuard 私钥均未预填，Release 不提供服务器端程序或任何人的 `.conf`。

## 设置、存档与联网检查

- 设置界面自动保存；高 DPI 内嵌在 EXE，不需要 `.exe.config`。
- “禁止 Mod 启动联网检查”默认开启，阻止本次游戏向已确认 GitHub 检查域名的 HTTP/HTTPS 请求；游戏运行期间有一个无窗口守卫，游戏退出后自动结束。
- 存档管理支持查看角色、原版与无缝单角色转换及跨 SteamID 导入，默认保留源角色，写入前自动备份。转换结果仍需游戏内实际加载验证。
- 备份目录：`%APPDATA%/EldenRing/SeamlessLauncherBackups/`。可从存档管理页打开并恢复。
- 通过“正版steamapi64恢复”页恢复原接口；退出公网游戏不会自动恢复。

详细说明在包内 `说明/`。文件指纹在 `文件校验.json`，可检查包内所有核心文件及启动器。
