# 增强无缝联机客户端 v0.1.0

下载附件 **`EldenRingSeamlessCoop-Enhanced-v0.1.0-Windows-x64.zip`**。该包包含原 Mod 配套运行文件和已编译增强启动器，只提供 Windows 游戏客户端，不含服务器端部署工具、源码、逆向资料或玩家密钥。

## 包含

- Windows 原生设置界面，Steam／自建服务器模式选择、INI 自动保存、接口备份和恢复。
- 存档角色查看、原版与无缝单角色转换、跨 SteamID 导入、隐式备份与恢复（转换为试用功能）。
- 默认开启的 Mod 启动联网检查拦截。
- 当前配套原 `ersc.dll`、`ersc_launcher.original.exe`、崩溃处理组件、英文与简体中文语言文件，以及 GSE 兼容接口。
- 千早爱音图标、来源说明和第三方许可证。

## 使用

关闭游戏，将包内 `Game` 的内容合并到包含 `eldenring.exe` 的实际游戏目录，再运行 `ersc_launcher.exe`。原 Mod 启动器已随包改名保留。

**升级已有本项目客户端，只更新启动器时替换 `ersc_launcher.exe` 即可；完整合并时保留自己的 INI、玩家身份和发现地址配置。**

Steam 模式使用自己的原 Valve 接口和正常 Steam 环境；自建模式还需通过 WireGuard GUI 激活自己的个人配置，再填写服务器、密码与 SteamID。包内不含任何人的公网地址、账号身份、密码、私钥或存档。

完整步骤见压缩包 `安装说明.md` 或仓库 [安装说明](https://github.com/Necthus/EldenRingSeamlessCoopThirdPartyServer/blob/main/docs/release-install.md)。

## 原 Mod 与验证范围

原 Seamless Co-op 由 **LukeYui（Yui）** 开发，下载页：[Nexus Mods #510](https://www.nexusmods.com/eldenring/mods/510)，发布仓库：[LukeYui/EldenRingSeamlessCoopRelease](https://github.com/LukeYui/EldenRingSeamlessCoopRelease)。本项目为第三方增强，未修改核心 DLL；当前配套核心含 `YuiKeyNexus Version 3` 标识，不将其称为原作者官方原封发行包。

基于本机游戏 1.17.1／配套 Seamless Co-op 2.0.1 验证。配置与接口检查、备份恢复、HTTP 拦截及高 DPI 布局测试通过；存档格式校验不替代实际角色加载，不保证修复原 Mod 的游戏内 Bug。第三方内容的来源和许可证随包提供。
