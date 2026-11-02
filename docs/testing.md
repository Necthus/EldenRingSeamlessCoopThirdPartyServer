# 构建与测试

在 Windows 上运行 `selfhost/build-launcher-ui.ps1 -BuildTests`，会构建启动器与四组测试。测试程序为开发用途，不需要分发给玩家。

```powershell
.\selfhost\build-launcher-ui.ps1 -BuildTests
.\selfhost\LauncherUI.tests.exe
.\selfhost\HttpGuard.tests.exe
.\selfhost\LauncherLayout.tests.exe 'C:\Temp\ersc-layout-previews'
```

- `LauncherUI`：INI 读取、保存、无关键保留、输入校验、身份隔离和联网检查开关。可选的两个参数为自行提供的原 Valve DLL 和配套 GSE DLL，提供时额外测试接口识别、备份和恢复；测试在临时目录操作。
- `HttpGuard`：仅使用本机回环测试服务器验证拦截、透传、上游代理和日志。可选的完整流程测试夹具未在本仓库打包。
- `LauncherLayout`：为本程序创建一个屏幕外测试窗口，检查常规／最小尺寸下的控件边界，输出页面图片，不向其他应用发送输入。图标素材为可选，不提供时使用默认图标。
- `SaveManager`：两个必需参数分别为原版和无缝存档路径，需绑定同一账号，槽 1 有角色，原版有空槽，无缝样本中包含已知道具。可选第三个参数为另一账号的无缝样本。它按输入读取账号与角色，跨账号测试使用合成目标 ID。界面断言还要求该账号在运行者当前 `%APPDATA%/EldenRing` 下有默认命名的 `.sl2` 和 `.co2`；不满足这些夹具条件的其他样本需调整测试。真实输入只读取，转换及恢复在临时目录中进行。

不要把真实存档、接口 DLL、配置私钥或测试转储提交到仓库。游戏内多人同步与转换后实际角色加载应单独验证；单元测试和格式校验不能替代这些检查。

此前本地验证包括配置／接口回归 70 项、175% DPI 布局 399 项、HTTP 守卫 43 项，以及存档条目保留与备份恢复测试。带额外样本或夹具的运行与基础测试计数会不同。
