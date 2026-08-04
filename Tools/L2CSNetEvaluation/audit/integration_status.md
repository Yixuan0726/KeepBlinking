# L2CS-Net 技术验证与条件接入状态

更新日期：2026-08-03

## 结论边界

- Official Google Drive checkpoint unavailable.
- Third-party converted ONNX evaluated.
- Original PyTorch checkpoint equivalence not verified.
- Ailia 转换 ONNX 已通过 ONNX checker、ONNX Runtime CPU、固定输入方向/镜像检查，以及 Unity Inference Engine CPU/GPU 数值一致性门槛。
- 尚未完成真人摄像头 A/B，因此不能声称 L2CS 比 Current 更可靠，也不能把默认 Provider 切换为 L2CS。
- Current 仍是默认。Compare 模式仍由 Current 驱动玩法。L2CS 无输出、未校准、输出过期或 GPU 不可用时自动使用 Current。

## Unity 固定输入一致性

环境：Unity 6000.1.8f1、Inference Engine 2.4.1、Direct3D 12、NVIDIA GeForce RTX 4060 Laptop GPU。

- 模型加载：80.36 ms
- ONNX Runtime：pitch 39.903656°，yaw -23.440979°
- Unity CPU 最大差异：0.0000305°
- Unity GPU 最大差异：0.0000153°
- Unity CPU：平均 118.69 ms，P50 118.92 ms，P95 124.99 ms，约 8.43 FPS
- Unity GPU：平均 31.75 ms，P50 10.17 ms，P95 102.82 ms，约 31.50 FPS

固定输入数值门槛通过。GPU P95 有明显长尾，必须由正常 Play Mode A/B 同时记录 Unity FPS 和卡顿峰值后再判断可用性。

## 接入结构

- `IGazePositionProvider`：统一 Provider 样本接口。
- `CurrentGazeProvider`：复用当前 MediaPipe 虹膜坐标。
- `L2CSGazeProvider`：复用 FaceLandmarker 的同一摄像头纹理和人脸框；使用 448×448、RGB、ImageNet mean/std、NCHW；GPU 异步回读并限制为 12 Hz。
- `CalibratedScreenGazeMapper`：Current 与 L2CS 各自独立的校准参数。Ailia 把输出 516/523 标记为 pitch/yaw，但 Ahmednull 原模型的 `model.py` 先返回 yaw head、再返回 pitch head；固定输入水平镜像也显示 516 是主要水平轴。因此校准显式按实测输出轴 `(516 horizontal, 523 vertical)` 使用，并由真人测试再次检查符号和方向。
- `GazeProviderComparisonController`：Current、L2CS、Compare 三种 Development 模式，A/B 流程、指标和数值输出。
- `L2CSPreprocess.shader`：显式处理完整方形人脸裁剪、边界、水平/垂直翻转和 90° 旋转。
- `FaceLandmarkerRunner`：只注入现有共享纹理及变换状态，不打开第二个摄像头，也不运行 RetinaFace。
- `EdgeOrbitHarvestMvp`：只在 gaze 坐标入口调用可选 Provider，并公开只读的实际目标区域尺寸；眨眼、闭眼、睁眼、丢失追踪、推远、Boss、教程、强化、文本和存档逻辑未改变。

## A/B 流程与输出

- F8 显示 `GAZE PROVIDER TEST`。
- F9 或 `START A/B TEST` 开始，不用凝视或眨眼确认。
- Current 和 L2CS 使用相同的 5 个校准点，校准参数分别保存到诊断目录。
- 随后显示 9 个非校准点，每点 2 秒，前 0.5 秒不计入，共 3 轮。
- 同一 MediaPipe 摄像头纹理同时供两套 Provider 使用；CSV 同时保存 Unity 时间戳和 Provider 时间戳。
- 只保存数值，不保存图片或视频。

输出目录：

`Application.persistentDataPath/KeepBlinking/Diagnostics/`

文件：

- `gaze_provider_calibrations.json`
- `gaze_provider_ab_YYYYMMDD_HHMMSS.csv`
- `gaze_provider_ab_YYYYMMDD_HHMMSS_summary.json`

摘要自动计算中位/P90 屏幕误差、归一化对角线误差、静止抖动、丢失比例、使用当前实际目标尺寸的区域命中率、错误锁定率、连续稳定 0.25 秒的锁定时间、平均/P95 推理延迟、Unity FPS、P95/最大帧耗时和超过 50 ms 的卡顿帧数。

## Free Look Test

- `START FREE LOOK` 不依赖活动怪物或正式游戏目标尺寸，可在空白测试画面直接运行。
- L2CS 未校准时先执行独立的五点大区域校准；Current 与 L2CS 仍使用各自的持久化参数。
- `1 / 2 / 3` 切换 Current、L2CS、Compare；Compare 同时显示两套独立结果，正式玩法在测试期间仍由 Current 驱动。
- Current 使用 warm white 空心环；L2CS 使用 mint green 中心点和空心环；两者之间用低透明度细线表示偏差。
- `R` 切换原始十字与平滑圆环，`Left/Right` 切换九个参考区，`A` 切换每四秒自动前进，`M` 切换 Free Movement，`Esc` 退出。
- 追踪丢失时标记快速淡出并显示 `TRACKING LOST`，不会把丢失状态传入闭眼逻辑。

## 真人通过条件

只有真人 A/B 满足以下条件后，才建议继续讨论默认切换：

- 区域命中率至少提高 5 个百分点，或中位误差降低至少 15%；
- P90 不能明显恶化；
- 静止抖动恶化不超过 10%；
- 追踪有效率不下降；
- 没有镜像、轴交换或方向反转；
- 没有明显游戏卡顿；
- 眨眼与持续闭眼行为完全不受影响。

当前结论：尚未验证真人优势，默认保持 Current。
