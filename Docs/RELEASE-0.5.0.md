# Weather Manager 0.5.0 · PICO Low-poly

发布日期：2026-09-08

发布标签：`v0.5.0-pico-lowpoly`

[GitHub Release](https://github.com/suanlaofan/weather-manager-unity/releases/tag/v0.5.0-pico-lowpoly) · [APK 下载](https://github.com/suanlaofan/weather-manager-unity/releases/download/v0.5.0-pico-lowpoly/WeatherManager-0.5.0-LowPoly-20260908.apk)

## 改动

本版针对 UI 随注视点移动、边缘内容截断、场景距离过远、模型悬空和天气表现不足的问题，更新空间交互与整座 low-poly 沙盘。

### 空间面板与观察

面板采用独立世界锚点、圆角布局、中文字体和 Nano Banana 图标。可拖动标题，左手 X 召回；UI 点击不会穿透为天气施法。默认模拟器沙盘在 XR 视角稳定后放置，距初始视点前方约 1.42 米、下方约 0.62 米。

放大镜功能已取消，改为六个观察按钮及“视角复位”：

| 控制 | 每次点击 | 范围／行为 |
| --- | --- | --- |
| 缩小／放大 | 10% | 初始大小的 50%–160% |
| 左转／右转 | 15° | 可连续旋转 |
| 拉近／推远 | 0.1 米 | 相对初始玩家位置 0.85–3 米 |
| 视角复位 | 恢复初始姿态 | 保留天气和游玩进度 |

观察调整平滑过渡，暂停时仍有效。沙盘变换保持 UI 锚点和头显位姿／投影独立；重置城镇保留观察姿态。用户手动调整后，晚到的平面识别不会再次移动沙盘。

### 场景、天气与居民

- 42 栋建筑采用新的房屋组合，补地基、步道和花园；22 块农田移入地形内部并增加三类作物。
- 185 个可受灾对象处理贴地和天气响应；道路面片按地形三角形裁切，修复穿草；矩形底座补完整裙边和底部。
- 桥梁加宽并补支撑；风车塔身固定、叶片独立转动；水轮围绕正确轮轴旋转，保留固定木支架。
- 更新 12 层沙盘空间粒子、火烟／蒸汽／连续龙卷风生命周期，以及积雪、积水和退水表现。
- 84 名居民共享 Idle、Walk、Run、Guard、Limp、Fall、Recover 七状态动画；射线和点光照适配沙盘缩放。

### 应用图标

使用游戏原有正式图标 `Assets/WeatherManager/Art/Branding/WeatherManagerIcon.png`。Android 构建入口会配置默认与 Android 图标槽，自适应前景增加安全留白；原 PNG 保留。图标来源记录位于同目录的 `icon-source.json`。

## 验收依据与边界

| 层级 | 已有证据 |
| --- | --- |
| 结构／输入 | 150 项检查通过，包括场景引用、UI 边界、七个观察动作及无放大镜残留 |
| Unity Editor 运行 | 298 项检查通过，包括七按钮真实 Input System 点击、上下限、完整旋转、暂停、变换后射线／碰撞、UI／HMD 独立及 20 次重置 |
| Editor 视觉 | Game View 检查新面板、村庄、农田／风车、桥梁／水轮、底部封口及天气表现 |
| 既有本地 0.5.0 APK | 独立 PICO 模拟器已启动；实际观察到放大、左转、右转、拉近及天气施法 |
| 最终发布附件 | 图标接入后 Android ARM64/IL2CPP 构建成功；18 个 Android 图标槽及24张 APK PNG 验证通过；APK v2签名通过；独立模拟器重新安装启动，安装文件 hash 与附件一致 |
| 实体 PICO 头显 | 尚未验收双眼观感、真实透视、物理桌面识别、控制器追踪及持续帧率 |

模拟器剩余三个观察按钮已有 Editor 实际点击证据，不扩写为全部模拟器按钮已逐项验证。此前模拟器启动出现可选 Google Play AssetPackManager 类缺失提示，未阻断该次游戏运行。

## 源码与构建依赖

- Unity `6000.5.4f1`，Android ARM64／IL2CPP，最低 API 29。
- 包名 `com.weathermanager.picomr`，Activity `com.unity3d.player.UnityPlayerActivity`。
- 从 PICO 官方渠道自行获取 Unity Integration `3.4.0` 并安装到 `Packages/com.unity.xr.picoxr/`。专有 SDK 与自动复制的 debugger prefab 不随源码发布。
- 场景、材质、网格、动画及 prefab 可继续在 Editor 编辑；SDK 复制品、Unity 缓存、原始日志与 APK 不纳入 Git 源码。
- embedded MCP for Unity `10.1.0` 随附 MIT LICENSE。项目配置连接本机 `127.0.0.1:8097`；将 `WeatherManagerLocalMcp.json` 的 `autoConnect` 改为 `false` 并重开 Editor 可关闭自动连接，保留文件以维持项目隔离 guards。
- 构建入口 `WeatherManager.Editor.PicoAndroidBuildPipeline.BuildRevisionApk`；输出 `Builds/PICO/WeatherManager-0.5.0-LowPoly-20260908.apk`。

安装步骤与操作说明见 [README](../README.md)。

## 安装包校验

文件：`WeatherManager-0.5.0-LowPoly-20260908.apk`，96,494,866 bytes。

SHA-256：`7406af55f345c1393d4e1c62f219c557231a43a78dae05cffd8bb89543d816df`。
