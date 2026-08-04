# 本机验证环境状态

更新日期：2026-07-24

## 已完成

- 项目局部虚拟环境：`Tools/L2CSNetEvaluation/.venv`
- Python：3.12.13
- ONNX：1.18.0
- ONNX Runtime：1.22.0（CPUExecutionProvider）
- OpenCV：4.11.0
- NumPy：2.2.6
- psutil：7.0.0
- 本机 GPU：NVIDIA GeForce RTX 4060 Laptop GPU，8 GB
- NVIDIA 驱动：560.94

虚拟环境中保留了之前安装的 PyTorch 2.7.1 CPU wheel，但新路线不再使用
PyTorch checkpoint、导出或数值对照。不会继续下载约 2.7 GB 的 CUDA PyTorch
wheel，也不会修改系统 CUDA 或驱动。

## 已取消路线

- 官方 Google Drive checkpoint 已不可用，不再重试；
- 不再使用 cookie 或 gdown；
- 不从 Hugging Face 或其他个人镜像下载 `.pkl`；
- 不反序列化任何非官方 pickle；
- 取消“PyTorch 官方权重与 ONNX 数值一致性”门槛。

## 当前路线

仅评估 Ailia 托管的第三方转换 `l2cs.onnx`。在 ONNX checker、CPU
smoke/benchmark、固定输入方向检查通过前：

- 不添加 Unity L2CS 正式输入；
- 不改变默认 Current Provider；
- 不声称模型适合实际玩家；
- 不修改 Boss、眨眼、闭眼、强化、教程、文本、存档或第一关玩法。

## ONNX Runtime CPU 结果

- checker：通过；
- 模型加载：约 `182 ms`；
- 固定真人样例输出：pitch `39.9037°`，yaw `-23.4410°`；
- 与 Ailia 参考箭头方向余弦：`0.99856`；
- 水平镜像后水平方向：正确反向；
- 相同输入重复推理最大差：`0`；
- 50 次推理平均：`260.1 ms`；
- P50：`398.3 ms`；
- P95：`502.5 ms`；
- 实际推理吞吐：约 `3.84 FPS`；
- 进程峰值 RSS：约 `654.2 MiB`；
- 运行期间约占 `13.74` 个等效逻辑核。

结论：数值、顺序、符号和镜像技术门槛通过；CPU 性能不满足 10–20 Hz 实时
目标，必须以 Unity Inference Engine GPU 后端实测作为接入门槛。

## Unity 包解析状态

`Packages/manifest.json` 已声明 Unity 官方
`com.unity.ai.inference` `2.4.1`。当前没有 Unity 进程或项目锁，但自动
BatchMode 审批因审批服务额度上限被拒绝；本机和项目缓存中也没有该包。
在用户让 Unity Package Manager 完成一次官方包解析前，不编写或声称通过
Inference Engine 接入。
