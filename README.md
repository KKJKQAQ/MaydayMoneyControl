# MaydayMoneyControl

MAYDAY AIRWAYS Steam Demo 的 Windows 房主控制面板与 BepInEx IL2CPP 插件。当前版本 1.0.9，适配时使用游戏版本 0.14.11。

## 功能

- 设置目标余额。
- 房主无限冲刺、1–10 倍冲刺速度和 1–20 倍推力。
- 选择成员后掉落物品，或向上传送 1–1000 米并眩晕 2 秒。
- 在房主脚下布置一次性地砖。其他成员进入半径 0.8 米、垂直差 0.6 米的范围后上移 0.6 米。120 秒自动失效，可以手动取消。

## 使用

便携包需要 Windows x64、已安装的 Steam Demo 和 .NET 8 Desktop Runtime x64。

1. 完整解压便携包，关闭游戏。
2. 首次使用运行 Install.cmd，升级运行 Upgrade.cmd，选择包含 MaydayAirways.exe 的目录。
3. 通过 LaunchGame.cmd 启动，进入单人飞行或自己的房间。
4. 通过 OpenPanel.cmd 打开面板。
5. 卸载前关闭游戏，再运行 Uninstall.cmd。

## 验证范围

1.0.9 已实测插件加载、面板连接、地砖布置和取消。其他成员踩中、单次触发、自动到期和未安装模组客户端的同步仍待实测。地砖使用传送模拟弹起。

源码和安装脚本位于仓库根目录。安装脚本运行需要便携包中配套的 payload 文件夹。插件编译依赖 BepInEx 6 IL2CPP、Harmony、Il2CppInterop 和本地游戏生成的 UnityEngine 互操作程序集。面板使用 .NET 8 Windows Forms。

第三方依赖遵循各自许可证，游戏及其商标归各自权利人所有。

