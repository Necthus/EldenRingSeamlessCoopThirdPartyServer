# 架构和文件职责

原游戏与 Mod 的运行关系：

```text
增强启动器 ersc_launcher.exe
  ├─ 配置界面、存档管理、Steam 接口备份与切换
  ├─ 可选：本次游戏的 Mod HTTP 检查守卫
  └─ 原 Mod 启动器 ersc_launcher.original.exe
       └─ eldenring.exe + 原 SeamlessCoop/ersc.dll
            └─ 选中的 steam_api64.dll
```

Steam 模式使用原 Valve 接口和正常 Steam 环境。自建模式使用配套 GSE 接口，并将玩家发现及传输流量通过 WireGuard 虚拟网络连接起来。

```text
Host 游戏 ↔ Host WireGuard ↔ Linux WireGuard 中转 ↔ Client WireGuard ↔ Client 游戏
```

服务器只转发网络包；Host 仍运行自己的世界，Client 参与联机，每位玩家使用自己的存档。原 Mod 核心的同步逻辑保持原样。

## 配置

增强启动器使用 `SeamlessCoop/ersc_settings.ini` 的额外字段：

```ini
[NETWORK]
use_steam_network = 0
relay_server = YOUR_SERVER_IP_OR_DOMAIN
relay_port = 10099
block_mod_update_checks = 1
```

`use_steam_network=1` 为 Steam，`0` 为自建服务器。公网模式的用户名、SteamID 与语言保存到 `steam_settings/configs.user.ini`。配置共用原 Mod 的联机密码、玩法选项及存档扩展名。

可选的 `SeamlessCoop/network/client.json` 可覆盖默认 endpoint 参数与 `gse_sha256`，一般无需提供。`wireguard_tunnel` 可指定需要检查的个人隧道；留空时检查是否有运行中的 WireGuard 隧道，不保证它连接了正确的服务器。

自建模式所需缓存路径：`SeamlessCoop/network/steam_api64.relay.dll`；原 Steam 接口备份路径：`SeamlessCoop/network/steam_api64.steam.dll`。这两个文件不在本仓库内。

「玩家发现地址」列表读取 `steam_settings/custom_broadcasts.txt`。选择条目后在输入框编辑并点击“更新选中”；新增点击“添加”，删除点击“删除选中”。输入可为 `10.77.42.3:47584` 或域名，省略端口补全 `47584`。提交列表修改后约 700 ms 自动保存，下次启动游戏生效。首次写入原文件时保留 `.launcher.bak`，保留原 UTF-8 BOM、换行和注释；外部修改冲突时拒绝覆盖，可“重新读取”。未编辑列表时不会改写该文件，Steam 模式也不会自动创建发现列表。

## 核心限制

- 地址／端口输入不自动管理 WireGuard 隧道，玩家通过 GUI 手动导入、激活和编辑。
- 核心 DLL 不读取本项目新增字段；新增行为由增强启动器实现。
- 不包含游戏或原 Mod 源码，不修补 Boss／NPC／多人剧情同步逻辑。
- 公网模式与 Steam 模式的身份可能不同；存档管理中改变目标身份不会同时改变启动身份。
