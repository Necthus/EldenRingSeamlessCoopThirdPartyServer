# 增强无缝联机客户端 v0.1.1

新增 **玩家发现地址列表**。下载 `EldenRingSeamlessCoop-Enhanced-v0.1.1-Windows-x64.zip`，仍为包含原 Mod 配套文件的完整 Windows 客户端包，不含服务器端部署工具、源码、逆向资料或个人配置。

## 更新内容

- 启动器选择“自建服务器”后，向下滚动可看到“玩家发现地址”。
- 自动读取 `steam_settings/custom_broadcasts.txt`，支持添加、更新选中、删除选中和重新读取。
- 支持玩家虚拟 IPv4／域名和端口；省略端口补全游戏通信端口 `47584`，检查重复项和非法地址。
- 提交列表修改后自动保存，下次启动游戏生效；首次更改自动备份原文件。
- 保留原文件注释、UTF-8 BOM 与换行；外部编辑冲突时拒绝覆盖并提示重新读取。
- 保留现有存档管理、Steam 接口切换恢复和 Mod 启动联网检查开关，原核心 DLL 不变。

## 升级

已有 v0.1.0 或相同配套原 Mod 的增强客户端，**只替换 `Game/ersc_launcher.exe` 即可**。这样保留密码、玩家身份、服务器和发现地址列表。

首次安装则将压缩包 `Game` 中的内容合并到有 `eldenring.exe` 的目录。完整合并升级时保留自己的 INI、`client.json`、`configs.user.ini` 及发现列表配置。自建模式仍需要在 WireGuard GUI 激活个人配置。

列表里的地址是其他玩家的虚拟网络地址，例如 `10.77.42.3:47584`；公网中转地址和 UDP 10099 是另一项配置。编辑列表不会修改 WireGuard Endpoint，也不会联网查询域名。

## 验证与来源

配置／列表读写与接口回归 106 项、当前 150% DPI 常规和最小窗口布局 422 项、HTTP 守卫 35 项测试通过。测试只在临时目录修改配置，没有改写本机发现列表或存档，也未启动游戏。

原 Seamless Co-op 由 **LukeYui（Yui）** 开发：[原发布仓库](https://github.com/LukeYui/EldenRingSeamlessCoopRelease) · [Nexus Mods #510](https://www.nexusmods.com/eldenring/mods/510)。当前配套核心版本 2.0.1，含 `YuiKeyNexus Version 3` 标识；本项目为第三方增强。配套原文件、GSE 和图标来源及许可证随包提供。
