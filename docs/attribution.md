# 来源与致谢

## 原 Seamless Co-op

《艾尔登法环》Seamless Co-op 原 Mod 由 **LukeYui（Yui）** 开发。

- 原发布仓库：<https://github.com/LukeYui/EldenRingSeamlessCoopRelease>
- Nexus Mods：<https://www.nexusmods.com/eldenring/mods/510>

本项目是第三方启动器和辅助工具，不是官方 Mod 仓库，也未得到原作者背书。当前配套验证版本为 2.0.1，核心 `ersc.dll` 不作修改。工作版本还显示 `YuiKeyNexus Version 3` 标识；本项目不据此宣称其作者身份或取得该版本的分发授权。原 Mod 应由使用者从自己的来源另行获取。

## 网络组件

- WireGuard：<https://www.wireguard.com/>，提供实际虚拟网络和加密隧道。本仓库仅提供配置模板。
- GSE 兼容接口参考：<https://github.com/Detanup01/gbe_fork>。本仓库没有打包其 DLL，也不把兼容接口称为本项目自行实现的网络协议。
- 游戏、Steam API 及其他第三方内容的权利归各自权利人所有。

## 存档格式参考

本项目的 C# 存档管理实现参考公开格式资料，并独立实现固定容器读写和限定范围修改：

- [BenGrn/EldenRingSaveCopier](https://github.com/BenGrn/EldenRingSaveCopier)：角色槽、摘要和账号迁移方法。
- [ClayAmore/ER-Save-Editor](https://github.com/ClayAmore/ER-Save-Editor)：PC 存档容器、角色和物品结构。
- [Hapfel1/er-save-manager](https://github.com/Hapfel1/er-save-manager)：用于交叉核对公开的 Seamless 道具 ID；未打包其程序或源码实现。

原项目的许可证与授权范围不因引用而改变。本仓库不包含逆向工程产物、保护层分析、第三方角色头像素材或个人运行快照。
