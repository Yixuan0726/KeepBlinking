# KeepBlinking Mac 开发交接

生成日期：2026-08-04
源项目：`D:\111\KeepBlinking`
目标 Unity 版本：`6000.1.8f1 (2a5b1522e5ab)`

## 1. 项目目标与当前范围

KeepBlinking 是一个以视线、自然眨眼、持续闭眼和推远设备为核心交互的护眼游戏研究原型。当前第一关已经完成到可运行的端到端流程：舒适度检查、教程、普通区域目标、危机/闭眼休息、推远收集、三次强化选择、Dry Core Boss、关后检查与会话报告。

本迁移包只保存当前开发现场，不代表已完成产品发布验证。以下状态必须保持：

- Boss：Dry Core 已接入第一关流程；它不依赖视线坐标进行精确瞄准。
- 教程：现有第一关教程保持原样。
- 强化：现有第一关模块选择与应用规则保持原样。
- 存档：未改变现有格式。当前持久化包括 `PlayerPrefs` 中的 protocol day，以及 `Application.persistentDataPath` 下的会话报告、L2CS/Current 校准和诊断数值。
- 正式玩法默认视线 Provider：`Current`。
- 所有项目内玩家可见文字必须保持英文；开发报告可以使用中文。

Mac Codex 或后续开发者不得在没有用户明确授权时修改 Boss、教程、强化、存档、第一关玩法、目标尺寸、眨眼/闭眼/睁眼/追踪丢失/推远算法、玩家可见文本或默认 Provider，也不得删除 Current 回退路径。

## 2. 打包前 Git 基线

本节记录的是创建本交接文档之前的状态。本轮没有执行 commit、push、reset、checkout、clean 或其他破坏性 Git 操作。

- 分支：`ui-module-polish`
- HEAD：`9bc711c1aa050d42b2e435dfc936a5e5a40c8e0f`
- 上游：`origin/ui-module-polish`
- 工作区：有用户修改和未跟踪文件，必须保留。

已修改文件：

- `.gitignore`
- `Assets/KeepBlinking/Editor/KeepBlinking.Editor.asmdef`
- `Assets/KeepBlinking/KeepBlinking.Runtime.asmdef`
- `Assets/KeepBlinking/Scripts/Gameplay/EdgeOrbitHarvestMvp.cs`
- `Assets/Samples/MediaPipe Unity Plugin/0.16.3/Official Solutions/Scenes/Face Landmark Detection/FaceLandmarkerRunner.cs`
- `Packages/manifest.json`
- `Packages/packages-lock.json`

未跟踪的项目功能文件包括 L2CS Editor validator、Resources 模型目录、Current/L2CS/Compare Provider、校准映射器、FREE LOOK/A/B 控制器、预处理 Shader、编辑器测试及 `Tools/L2CSNetEvaluation`。此外，源目录中还有与 Unity 项目无关的未跟踪 `outputs/` 课件/文档生成内容；它不进入本迁移包。准确的逐文件 Git 输出保存在迁移包根目录的 `TRANSFER_BASELINE_GIT_STATUS.txt`。

`.git` 已执行只读 `git fsck --full --no-reflogs`：未发现损坏或缺失对象，仅报告若干非致命 dangling tree/blob。Git LFS 本地对象包含 MediaPipe tarball，SHA-256 与工作树文件一致。

## 3. Unity 项目入口与主要文件

- 主场景：`Assets/Scenes/SampleScene.unity`
- 备用/开发场景：`Assets/Scenes/EdgeOrbitHarvestMvp.unity`
- 主玩法：`Assets/KeepBlinking/Scripts/Gameplay/EdgeOrbitHarvestMvp.cs`
- 第一关状态编排：`Assets/KeepBlinking/Scripts/Gameplay/FirstLevelSessionController.cs`
- Boss：`Assets/KeepBlinking/Scripts/Gameplay/DryCoreBossController.cs`
- 教程：`Assets/KeepBlinking/Scripts/Tutorial/KeepBlinkingTutorialController.cs`
- 强化：`Assets/KeepBlinking/Scripts/Gameplay/FirstLevelUpgradeCatalog.cs` 与 `FirstLevelUpgradeView.cs`
- 输入状态：`Assets/KeepBlinking/Scripts/Input/EyeInputDebugState.cs`
- MediaPipe Runner：`Assets/Samples/MediaPipe Unity Plugin/0.16.3/Official Solutions/Scenes/Face Landmark Detection/FaceLandmarkerRunner.cs`
- Provider 比较工具：`Assets/KeepBlinking/Scripts/Input/GazeProviderComparisonController.cs`

## 4. 当前 MediaPipe 输入结构

MediaPipe 示例框架的 `WebCamSource` 拥有并播放唯一的 `WebCamTexture`。`FaceLandmarkerRunner` 从 `ImageSourceProvider.ImageSource` 复用该纹理，应用水平/垂直翻转和旋转后送入 FaceLandmarker。项目没有为 L2CS 打开第二个摄像头，也没有同时运行 RetinaFace。

`EyeInputDebugState.UpdateFrom` 从第一张脸的 landmarks 产生当前输入：

- 双虹膜中心相对双眼与人脸跨度形成粗略 gaze，映射为 Unity 屏幕坐标；缺少虹膜时回退到人脸框中心。
- EAR 与可用的 `eyeBlinkLeft/Right` blendshape 产生眨眼、睁眼和持续闭眼状态。
- FaceLandmarker 无结果时清空状态；`FaceDetected` 是追踪有效性的基础。
- 推远设备使用平滑后的人脸面积及距离基线，不依赖 gaze 坐标。
- 第一关对 gaze 做指数平滑、五点校准、区域命中 padding、边缘 soft lock 和方向死区。

直接依赖者包括 `EdgeOrbitHarvestMvp`、`BlinkBootSequence`、`BasicObservationMvp`、`EyeInputDebugOverlay` 和通过玩法公开坐标工作的 `SessionReportView`。L2CS 只提供视线方向/校准后的屏幕位置，不接管眨眼、闭眼、睁眼、追踪丢失或推远判断。

更完整的输入审计见 `Tools/L2CSNetEvaluation/audit/current_implementation.md`。

## 5. L2CS 研究与 FREE LOOK 状态

结论边界必须原样保留：

- Official Google Drive checkpoint unavailable.
- Third-party converted ONNX evaluated.
- Original PyTorch checkpoint equivalence not verified.
- 尚未完成真人摄像头 A/B，不能声称 L2CS 比 Current 更可靠。

研究来源：

- 官方实现仓库：`https://github.com/ahmednull/L2CS-Net`
- 已审计官方实现 commit：`a4d8f7fa5436a2b2b9f088471623b552a85811bd`
- Ailia 转换实现：`https://github.com/ailia-ai/ailia-models`
- 固定 Ailia commit：`e28ef95d123bc4e625fea40462d3cd33e0eef688`
- Luxonis 模型卡：`https://models.luxonis.com/luxonis/l2cs-net/7051c9d2-78a4-420b-91a8-2d40ecf958dd`
- Ailia 代码/样例声明 MIT；但 Ailia 没有提供原始 PyTorch checkpoint 文件名、hash 或完整转换配方，因此不能证明与官方 checkpoint 数值等价。

迁移包中的 Unity 模型：

- 路径：`Assets/KeepBlinking/Resources/L2CSExperimental/l2cs_batch1.onnx`
- 大小：`95,414,596` bytes
- SHA-256：`27554BBB92985510214CF23101A2EBC3E51ADAB8D6FCA19032E6AF914EE17BB1`
- 来源父模型 SHA-256：`3DF55DDB1AD6667496F394A635AB7DCA6947800C93E941B1E7669CF3BA30E2BF`
- 派生方式：只把 ONNX 图的输入/输出 batch 元数据从 6 改为 1；固定输入首项输出最大差为 0。
- **Local research asset. Do not publish or redistribute.**

ONNX 输入为 `input.1`、`FLOAT [1,3,448,448]`，完整人脸方形裁剪、RGB、NCHW、`uint8 / 255`、ImageNet mean/std。输出名为 `516` 与 `523`。Ailia 文档把它们称作 pitch/yaw，但原模型 head 顺序与固定镜像试验显示 516 是主要水平轴；Unity 校准按实测 `(516 horizontal, 523 vertical)` 使用，符号和轴仍必须由真人测试复核。模型没有可靠置信度输出，追踪有效性继续使用 MediaPipe。

技术验证：

- ONNX checker、ONNX Runtime CPU smoke/benchmark 和固定输入方向/镜像门槛通过。
- ONNX Runtime CPU：加载约 182 ms；平均 260.1 ms；P50 398.3 ms；P95 502.5 ms；约 3.84 FPS；峰值 RSS 约 654 MiB。
- Unity Inference Engine 2.4.1 固定输入：CPU 最大差 0.0000305°，GPU 最大差 0.0000153°。
- Windows RTX 4060 Laptop GPU：平均 31.75 ms，P50 10.17 ms，P95 102.82 ms，约 31.5 FPS。P95 长尾仍需 Play Mode 实测。

Provider 架构：`CurrentGazeProvider`、`L2CSGazeProvider`、`CalibratedScreenGazeMapper`、`GazeProviderComparisonController`。模式为 Current、L2CS Experimental、Compare；默认 Current，L2CS 失败自动回退 Current，Compare 不改变正式玩法驱动。

F8 打开 `GAZE PROVIDER TEST`。`START FREE LOOK` 可在无活动怪物时运行；Current 为 warm white 圆环，L2CS 为 mint green 点/圆环，Compare 同时显示。`R` 切 raw，左右键切九个区域，`A` 切四秒自动前进，`M` 切 Free Movement，`Esc` 退出。自动 measured test 与数值输出仍保留。诊断目录为 `Application.persistentDataPath/KeepBlinking/Diagnostics/`，不保存摄像头图片或视频。

## 6. Packages 与本地依赖

`Packages/manifest.json` 与 `Packages/packages-lock.json` 均包含在包内。关键依赖：

- Unity Inference Engine：`com.unity.ai.inference` `2.4.1`，官方 Unity Registry。
- MediaPipe Unity Plugin：`com.github.homuler.mediapipe`，本地 tarball `Packages/com.github.homuler.mediapipe-0.16.3.tgz`。
- MediaPipe tarball 大小：`290,414,365` bytes。
- MediaPipe tarball SHA-256：`CC3E77A219E0B99618AE3BE64C31A566197DEEDC69C1E136ACF52D65D7CF2E79`。
- 其他 manifest 直接依赖包括 URP 17.1.0、Input System 1.14.0、Test Framework 1.5.1、uGUI 2.0.0 等；准确列表以 manifest/lock 为准。

MediaPipe tarball 内存在 Windows `mediapipe_c.dll`、macOS `libmediapipe_c.dylib`、Linux `.so`、Android `.aar` 与 iOS `MediaPipeUnity.framework`，Unity PluginImporter 元数据按平台选择。iOS framework 的 Info.plist 显示来自 iPhoneOS 17.5 SDK、最低 iOS 12.0，并声明 OpenGLES framework 依赖。存在 iOS 支持文件不等于已经通过当前 Unity/Xcode/iPhone 真机验证。

## 7. Mac 所需软件与第一次打开

建议在 Mac 安装：

1. Unity Hub。
2. Unity Editor `6000.1.8f1`，同时安装 iOS Build Support（本轮不执行 iOS 构建）。
3. 与目标 macOS 兼容的 Git 和 Git LFS。
4. Xcode（仅为之后经授权的 iOS 真机工作准备；不要在文档或仓库保存 Apple ID、证书或 profile）。
5. 可选：如果需要重跑 L2CS 研究脚本，在 Mac 重新创建项目局部 Python venv；不要迁移或使用 Windows `.venv`。

第一次打开步骤：

1. 在 Mac 用 `shasum -a 256 KeepBlinking_MacTransfer_20260804.zip` 对照 `TRANSFER_SHA256.txt`。
2. 解压到本地短路径，保留所有隐藏文件和 `.meta`；不要只通过云盘“按需下载”。
3. 用 Unity Hub 添加解压后的项目根目录，并明确使用 `6000.1.8f1`。
4. 让 Unity 从官方 Registry 解析 packages。若 MediaPipe 报错，先确认相对路径 `Packages/com.github.homuler.mediapipe-0.16.3.tgz` 存在，不要改成 Windows 绝对路径。
5. 等待 Library 在 Mac 本地重建并完成脚本/Shader/ONNX 导入；不要从 Windows 复制 Library。
6. 打开 `Assets/Scenes/SampleScene.unity`，先查看 Console，不要在 BatchMode 做摄像头测试。
7. 正常 Play Mode 先验证 Current、眨眼、持续闭眼、追踪丢失和推远；再按 F8 验证 FREE LOOK。保持 Current 为默认。
8. 只有用户明确要求时，才继续 Xcode/iPhone 构建、签名和真机验证。

## 8. 跨平台审计结果与风险

已通过的静态检查：

- Assets/Packages/ProjectSettings/UserSettings/Tools/.git 内未发现大小写冲突。
- Assets 内未发现缺失 `.meta`。
- `Packages/manifest.json` 没有本地绝对路径；MediaPipe 使用项目内相对 tarball。
- Assets 运行时代码未发现 `D:\` 或 `C:\Users\` 硬编码。
- 选定文本文件均可严格按 UTF-8 解码；未发现超过 240 字符的包含路径。
- 未发现 `.env`、API key、Apple 证书、Provisioning Profile、Apple ID 数据、浏览器 cookie 或账号凭据。
- `UserSettings` 只含 Unity 编辑器设置、空搜索设置和布局，未发现敏感信息，因此按要求包含。

已知风险和注意事项：

- `UserSettings/Layouts/default-6000.dwlt` 记录旧路径 `D:\111\KeepBlinking`。这是编辑器布局历史，不是运行时引用；Mac 可忽略/重置布局。
- L2CS JSON/审计报告保留 Windows 测试机绝对路径作为可追溯记录；运行时代码通过 Unity Resources 相对路径加载模型。
- `L2CSGazeProvider` 使用 `Resources.Load<ModelAsset>("L2CSExperimental/l2cs_batch1")`，模型位置正确，但 Mac Metal、Apple Silicon 和 iOS Inference Engine 性能/兼容性尚未验证。
- MediaPipe 包虽然包含 macOS/iOS 原生库，仍需在目标 Apple Silicon Mac 和 iPhone 上验证架构、摄像头权限、方向/镜像、OpenGLES 依赖和实际 FPS。
- 四个文本文件存在混合 LF/CRLF：`Assets/KeepBlinking/KeepBlinking.Runtime.asmdef`、其 `.meta`、`BlinkBootSequence.cs` 和 `Packages/manifest.json`。当前均为有效 UTF-8；迁移时不要进行全项目自动换行重写。
- `.git` 约 554 MB，并包含约 290 MB MediaPipe LFS 对象；这是为完整开发交接而保留的重复体积。`git fsck` 仅有 dangling 对象提示，不是仓库损坏。
- 根目录未跟踪 `outputs/` 是与 Unity 项目无关的课件/文档输出，未包含。Windows `Library`、`Temp`、`Logs`、`.venv`、PackageCache、研究中间模型/图片等可重建内容也未包含。
- `Tools/unzip.exe`/`.cmd` 是已有 Windows 辅助工具，会随 Tools 保留，但 Mac 不应执行 `.exe`；它不是 Unity 运行依赖。
- 当前项目尚未完成 iPhone 真机的摄像头权限、横竖屏变换、Safe Area、Metal 推理、内存/热量、FPS、签名和安装验证。

## 9. Mac Codex 禁止擅自修改的范围

- 不改玩法、场景、Boss、教程、强化、文本或存档规则。
- 不改 MediaPipe 的眨眼、持续闭眼、睁眼、追踪有效性、丢失判断或推远检测。
- 不缩小普通目标，不让 Dry Core 使用 gaze 精确瞄准。
- 不把 L2CS 设为默认，不删除 Current 或自动回退。
- 不发布、提交、上传或重新分发 L2CS ONNX；它仅限私人本地研究。
- 不上传摄像头画面，不保存图片/视频，不接入云端推理。
- 不自动 commit、push、reset、checkout、clean 或覆盖当前脏工作区。
- 不执行 iOS 构建、修改 Apple 签名设置或写入任何账号/证书资料，除非用户明确授权。

技术细节和历史验证报告位于 `Tools/L2CSNetEvaluation/audit/` 与 `Tools/L2CSNetEvaluation/results/`。其中 `environment_status.md` 是较早阶段快照；后续 `integration_status.md` 和 `unity_parity_report.json` 记录了 Inference Engine 解析及固定输入一致性已通过的更新状态。
