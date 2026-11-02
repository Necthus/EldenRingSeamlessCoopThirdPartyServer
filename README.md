# Elden Ring Seamless Co-op Third-Party Server

《艾尔登法环》Seamless Co-op 的第三方增强启动器与自建网络辅助项目。提供 Windows 原生设置界面、Steam／自建网络模式选择、存档管理，以及可自行部署的 Linux WireGuard 中转配置模板。

**原 Seamless Co-op 由 LukeYui（Yui）开发。本项目不是原 Mod 的官方仓库，没有修改或重新实现其 `ersc.dll` 核心。** 游戏内的联机、玩法和世界同步逻辑仍由原 Mod 完成。

- 原 Mod 发布仓库：[LukeYui/EldenRingSeamlessCoopRelease](https://github.com/LukeYui/EldenRingSeamlessCoopRelease)
- 原 Mod 下载页：[Seamless Co-op · Nexus Mods](https://www.nexusmods.com/eldenring/mods/510)
- 本项目：[Necthus/EldenRingSeamlessCoopThirdPartyServer](https://github.com/Necthus/EldenRingSeamlessCoopThirdPartyServer)

## 下载直接使用

前往 [Releases](https://github.com/Necthus/EldenRingSeamlessCoopThirdPartyServer/releases/latest)，下载最新的 `EldenRingSeamlessCoop-Enhanced-…-Windows-x64.zip`。这是包含原 Mod 文件与增强启动器的客户端包，将其中的 `Game` 内容合并到实际游戏目录即可，无需自己编译。

Release 只提供客户端包，不包含服务器端部署文件、真实玩家配置、密钥或逆向资料。自建模式仍需玩家自行安装 WireGuard 并导入个人配置；原作者、兼容接口与素材来源附在包内。安装或升级前请阅读 [使用说明](docs/release-install.md)，保留自己的 INI 设置。

## 功能

- **Windows 原生启动器**：WinForms、系统字体、高 DPI；编辑 INI 并自动保存。
- **网络模式切换**：选择 Steam 接口或配套兼容接口；启动前校验、备份与切换接口，提供原 Steam 接口恢复按钮。
- **玩家发现地址列表**：自建模式下添加、编辑、删除虚拟 IPv4／域名与游戏端口，自动保存 `custom_broadcasts.txt`，首次修改备份原文件。
- **自建中转网络**：玩家手动导入 WireGuard 配置，通过 Linux 公网服务器转发玩家间的数据；一个玩家仍担任 Host，其余为 Client。
- **存档管理（试用）**：显示角色名、等级、时长和槽位；支持 `.sl2`／`.co2` 单角色转换、跨 SteamID 导入、已知无缝道具清理、自动备份及恢复。
- **Mod 联网检查开关**：可阻止本次启动的游戏对已确认 GitHub 检查域名的 HTTP/HTTPS 请求；不更改系统代理、hosts 或防火墙。

服务器是 WireGuard 网络中转节点，不运行游戏世界逻辑。兼容接口与 WireGuard 共同提供玩家间通信环境；启动器没有为原 `ersc.dll` 增加一套新的游戏同步协议。

## 仓库内容

| 目录 | 内容 |
|---|---|
| `selfhost/` | 当前增强启动器、存档管理、HTTP 守卫源码、构建脚本与测试 |
| `server/` | 不含密钥的 Linux／Windows WireGuard 配置模板及 Linux 安装脚本 |
| `docs/` | 架构、存档管理、联网检查、测试与来源说明 |

Git 源码仓库不跟踪游戏本体、第三方 DLL／启动器、存档、密钥、逆向资料或头像原图。Release 客户端包另行包含原 Mod 运行文件、已测 GSE 兼容 DLL、内嵌头像与来源说明；不含游戏本体或任何人的原 Valve 接口、存档、身份及真实隧道配置。

## 构建启动器

在 Windows 上使用 .NET Framework 4.x C# 编译器：

```powershell
.\selfhost\build-launcher-ui.ps1
```

输出：`selfhost/ersc_launcher.ui.exe`。构建不需要额外 NuGet UI 库，不需要 WebView。用户运行时无需 Python、PowerShell 脚本或额外 UI 安装程序。

可选图标由使用者自行提供：

```powershell
.\selfhost\build-launcher-ui.ps1 -IconPath 'C:\Assets\launcher.ico'
```

没有图标文件时使用 WinForms 默认窗口图标。测试程序可用 `-BuildTests` 一起构建，运行方法见 [测试说明](docs/testing.md)。

## 放入自己的游戏目录

1. 从原作者渠道安装匹配版本的 Seamless Co-op。当前项目基于 **Seamless Co-op 2.0.1** 验证，其他版本需另行确认。
2. 关闭游戏，将原 Mod 的 `ersc_launcher.exe` 保留为 `ersc_launcher.original.exe`；将本项目输出改名为 `ersc_launcher.exe` 放在同一个 `Game` 目录。
3. 保留原 `SeamlessCoop/ersc.dll`、语言文件、配置及其配套目录。
4. Steam 模式需要正常的 Steam 游戏环境及自己的原 Valve 接口。自建模式需要自行提供与本项目已测版本匹配的 GSE 兼容接口，放到 `SeamlessCoop/network/steam_api64.relay.dll`。兼容接口不随此仓库提供。
5. 自建模式先在 Windows WireGuard 中导入并激活自己的 `.conf`，再通过启动器进入游戏。每个人使用不同私钥和稳定、互不重复的 SteamID，游戏与 Mod 版本保持一致，联机密码相同。

已测兼容 DLL 的 SHA-256：

```text
723e13f5a2e1ec799f342612a3294229ca832d143fdea85c00025216b6696e88
```

启动器会拒绝不匹配的兼容 DLL；仅使用不同构建时修改配置中的哈希不等于验证兼容性。

初次启动前缺少 `SeamlessCoop/network/steam_api64.steam.dll` 时，启动器会尝试核验并备份根目录原 Valve 接口；已有备份不覆盖。切换模式会复制选中的接口到根目录，游戏退出后不会自动恢复；可使用“正版steamapi64恢复”页恢复。

## 配置自建服务器

按 [server/README.md](server/README.md) 生成自己服务器与玩家的密钥、填入公网地址，并部署 Linux 中转。玩家端只需安装 WireGuard，通过其界面导入个人配置。

启动器中填写的服务器地址／端口会保存到 INI，**不会自动修改 WireGuard 的 Endpoint**；改变公网入口时，需要在 WireGuard 界面同步修改。玩家发现列表可在自建模式下直接管理，填写实际玩家的虚拟 IP 和通信端口（通常 `47584`），保存到 `steam_settings/custom_broadcasts.txt`。

公开包不预填公网服务器地址，使用者必须填入自己的地址。不要把自己的密钥或联机密码提交到仓库。

## 当前验证范围

已验证配置读写、接口校验与恢复、存档格式与备份恢复、175% DPI 布局，以及隔离 GSE 进程之间的房间／消息收发。存档转换属于试用功能，文件校验通过不代表所有游戏版本、剧情或 Mod 内容均可加载；原 Mod 的 Bug 不会因使用该启动器自动修复。

进一步阅读：[架构](docs/architecture.md) · [存档管理](docs/save-manager.md) · [Mod 联网检查](docs/mod-network-checks.md) · [来源与致谢](docs/attribution.md)
