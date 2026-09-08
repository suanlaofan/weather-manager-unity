# 天气管理员 · Weather Manager PICO MR

可编辑的 Unity 6 URP low-poly 天气沙盘游戏。选择风、雨、雷、雪或热浪，观察居民、建筑、农田和水面随天气变化。

## 0.5.0 发布

本版重做场景细节、空间面板和天气表现，并用按钮直接调整沙盘的大小、旋转和远近。

- [GitHub Release](https://github.com/suanlaofan/weather-manager-unity/releases/tag/v0.5.0-pico-lowpoly)
- [下载 APK：WeatherManager-0.5.0-LowPoly-20260908.apk](https://github.com/suanlaofan/weather-manager-unity/releases/download/v0.5.0-pico-lowpoly/WeatherManager-0.5.0-LowPoly-20260908.apk)
- [完整修改与验收范围](Docs/RELEASE-0.5.0.md)

包名：`com.weathermanager.picomr`；启动 Activity：`com.unity3d.player.UnityPlayerActivity`。构建配置为 Android ARM64、IL2CPP、最低 API 29。

## 本版变化

- **独立空间面板**：面板锚定在世界中，转头不会拖动面板。标题可拖动，左手 X 可召回；点击 UI 会拦截天气施法。面板采用 Nano Banana 生成图标和中文字体。
- **沙盘观察按钮**：缩小／放大、左转／右转、拉近／推远，加“视角复位”。调整仅作用于沙盘，面板和头显视角保持独立。放大镜已取消。
- **精细 low-poly 场景**：重新布置村庄和农田，修复模型悬空、道路穿地和底座露底；增加地基、步道、花园、桥梁支撑及独立转动的风车叶片与水轮。
- **天气与动画**：更新雨雪、火烟、蒸汽、龙卷风与地面积雪／积水表现，居民采用共享的七状态动画。
- **默认观察距离**：模拟器等待 XR 视角就绪后，将沙盘放在初始视点前方约 1.42 米、下方约 0.62 米。
- **游戏图标**：构建入口会将 `Assets/WeatherManager/Art/Branding/WeatherManagerIcon.png` 应用于默认图标和 Android 图标槽；自适应前景保留安全留白。

## 操作

1. 用右手控制器射线选天气，在有效沙盘位置按住扳机蓄力并施法，松开结束。
2. 点击大小按钮每次调整 10%，旋转按钮每次调整 15°，远近按钮每次移动 0.1 米。
3. “视角复位”恢复初始沙盘姿态并保留游玩进度；“重置城镇”重置模拟状态并保留观察姿态。暂停时仍可调整沙盘。
4. 拖动面板标题调整面板位置，左手 X 召回面板。模拟器另有鼠标／触摸操作路径。

缩放限制为初始大小的 50%–160%；前后距离相对初始玩家位置限制在 0.85–3 米。沙盘调整不会修改头显位姿或投影。

## 验证范围

本版场景与交互通过 **150 项结构／输入检查、298 项 Unity Editor 运行检查**。Editor 检查包含七个观察按钮的真实 Input System 点击、范围边界、暂停、变换后的射线／碰撞、面板独立性及 20 次重置。

正式图标接入后的发布 APK 已重新构建、安装并在独立 PICO 模拟器启动，安装文件 SHA-256 与发布附件一致。18 个 Android 图标槽已绑定；从 APK 提取的 24 张各密度图标确认使用正式天气岛图、透明边缘及正确自适应层顺序。APK v2 签名验证通过。

此前同版玩法在模拟器实际观察到放大、左转、右转、拉近和天气施法；七个观察按钮全部具有 Editor 真实点击证据，未扩写为全部模拟器按钮已逐项验收。

APK：96,494,866 bytes；SHA-256：`7406af55f345c1393d4e1c62f219c557231a43a78dae05cffd8bb89543d816df`。

尚未进行实体 PICO 头显验收：双眼观感、真实透视、桌面识别、控制器追踪和持续帧率仍需在设备上确认。模拟器使用不透明背景的稳定沙盘回退场景。

## 打开与构建

1. 安装 Unity **6000.5.4f1** 及 Android Build Support。
2. 从 PICO 官方开发者渠道获取 **PICO Unity Integration 3.4.0**，自行恢复到 `Packages/com.unity.xr.picoxr/`。仓库不分发该专有 SDK；参见 [SDK 安装说明](Packages/PICO_SDK_REQUIRED.md)。SDK 自动复制的调试器 prefab 也不纳入版本控制。
3. 用 Unity Hub 打开项目，加载 `Assets/WeatherManager/Scenes/WeatherManager.unity`。
4. Editor 队列构建入口为 `WeatherManager.Editor.PicoAndroidBuildPipeline.QueueRevisionBuild`（自动切换 Android），输出 `Builds/PICO/WeatherManager-0.5.0-LowPoly-20260908.apk`。该入口在构建前配置 PICO Loader、游戏图标和 ARM64／IL2CPP。

场景、网格、材质、动画、prefab 和玩法源码均保留为可编辑资产。Unity 缓存、SDK 副本、原始运行日志与构建产物不提交到源码仓库。

## 开发工具

项目包含 **MCP for Unity 10.1.0** 的 embedded 源码、项目隔离修改及 [MIT LICENSE](Packages/com.coplaydev.unity-mcp/LICENSE)。项目专用 Editor 工具依赖此包。

`Packages/com.coplaydev.unity-mcp/WeatherManagerLocalMcp.json` 配置本机 `http://127.0.0.1:8097`。当前 `autoConnect:true` 会连接自行启动的本地 MCP 服务；无需 MCP 时可将其改为 `false`，再重新打开 Editor。保留该配置文件可以继续启用隔离 guards，避免修改其他项目的全局客户端配置。详见 [项目隔离说明](Packages/com.coplaydev.unity-mcp/SKYBRIDGE-ISOLATION.md)。
