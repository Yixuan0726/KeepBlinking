# L2CS-Net 仓库与模型来源审计

审计日期：2026-07-24

## 官方实现

- 仓库：`https://github.com/ahmednull/L2CS-Net`
- 已审计 commit：`a4d8f7fa5436a2b2b9f088471623b552a85811bd`
- 源码许可证：MIT，版权人 Ahmed Abdelrahman
- README 中的 Google Drive checkpoint 文件夹已确认不可用。

本轮不再访问该 Drive 链接，不使用 cookie/gdown，不下载任何第三方
`.pkl`，也不反序列化非官方 pickle。

## Ailia 第三方 ONNX 候选

- 仓库：`https://github.com/ailia-ai/ailia-models`
- 固定 commit：`e28ef95d123bc4e625fea40462d3cd33e0eef688`
- commit 时间：`2026-07-24T04:01:57Z`
- 样例目录：`face_recognition/l2cs_net`
- `l2cs_net.py` Git blob：`9ceaec771a3efec9c72042dd895dbb38794459b1`
- `l2cs_util.py` Git blob：`3959297a5c6b950fefb205461f2ddb944a1ca7a1`
- `README.md` Git blob：`4264d91eed884f356500a4abfe45a31f4203be4d`
- `LICENSE.txt` Git blob：`e5258bf47fa8726155cc800fa6705ec7be9b64e8`

Ailia README 明确：

- Reference 指向 Ahmednull/L2CS-Net；
- Framework 为 PyTorch 1.9；
- 模型格式为 ONNX opset 9；
- 第一次运行会从 Ailia 托管地址下载 ONNX 和 prototxt。

实际下载 URL 由固定 commit 中的 `l2cs_net.py` 直接声明：

- `https://storage.googleapis.com/ailia-models/l2cs_net/l2cs.onnx`
- `https://storage.googleapis.com/ailia-models/l2cs_net/l2cs.onnx.prototxt`

没有执行 Ailia 的 Python 样例、安装脚本或自动下载函数。审计阶段仅读取固定
commit 的源码并对上述两个 URL 发起 HEAD 请求。

## 下载前服务器元数据

`l2cs.onnx`：

- HTTP 200
- `Content-Length: 95414596`
- `Last-Modified: Mon, 29 Jan 2024 01:11:25 GMT`
- `ETag: e4f4d3ae884c8a2f3817b138b2af0d2e`
- GCS MD5（base64）：`5PTTrohMii84F7E4sq8NLg==`

`l2cs.onnx.prototxt`：

- HTTP 200
- `Content-Length: 52773`
- `Last-Modified: Mon, 29 Jan 2024 01:11:21 GMT`
- `ETag: 1a48a6cd02c6df494494cdc455cc8a98`
- GCS MD5（base64）：`GkimzQLG30lElM3EVcyKmA==`

下载后必须另外记录本机 SHA-256，并核对字节长度。

下载与校验结果：

- `l2cs.onnx`：`95414596` bytes，
  SHA-256 `3DF55DDB1AD6667496F394A635AB7DCA6947800C93E941B1E7669CF3BA30E2BF`
- `l2cs.onnx.prototxt`：`52773` bytes，
  SHA-256 `F7C0F52E2EACACA70F3F2AA48728463A8F29551C115D96A6C3B8A2819997A644`

## 许可证

Ailia 样例目录中的 `LICENSE.txt` 是 MIT License，版权人为
Ahmed Abdelrahman（2022）。Luxonis 模型卡也将该候选标为 MIT，并说明
“Shared by: Ailia models”。

这足以继续本地技术研究，但来源链仍有一个重要限制：Ailia 没有记录原始
PyTorch checkpoint 文件名、checkpoint SHA-256 或完整转换脚本。因此不能证明
该 ONNX 与官方原始 checkpoint 数值等价，也不把它称为“官方原始权重”。

本轮模型文件保持研究用途、被 Git 忽略、不重新分发；在原始 checkpoint
等价性和权重授权链进一步明确前，不进入正式发布包。

## 预处理与输出（来自固定 Ailia 源码）

- 完整人脸裁剪；
- OpenCV 输入先从 BGR 转 RGB；
- 人脸先缩放到 224×224，随后预处理函数缩放到 448×448；
- ImageNet normalization；
- NCHW；
- `model.run` 的 output 0 为 pitch，output 1 为 yaw；
- 输出值按“度”解释，Ailia 后处理乘 `pi / 180` 转为弧度；
- 绘制方向：
  `dx = -L * sin(pitch) * cos(yaw)`，
  `dy = -L * sin(yaw)`。

ONNX 下载后仍必须从模型图和固定测试输入独立验证输入尺寸、输出名/顺序、
单位、符号、镜像和坐标轴。

## ONNX 图审计结果

- `onnx.checker`：通过；
- producer：PyTorch 1.9；
- IR version：6；
- opset：9；
- 原始输入：`input.1`，FLOAT，`[6, 3, 448, 448]`；
- 原始输出 0：`516`，FLOAT，`[6]`，Ailia 后处理解释为 pitch（度）；
- 原始输出 1：`523`，FLOAT，`[6]`，Ailia 后处理解释为 yaw（度）。

这与 Luxonis 模型卡显示的 batch 1 不一致。为避免单脸推理重复计算六份，研究
工具只修改图输入/输出的 batch 元数据，生成本地 batch-1 派生 ONNX：

- SHA-256 `27554BBB92985510214CF23101A2EBC3E51ADAB8D6FCA19032E6AF914EE17BB1`
- 原模型六份相同输入的首项与派生模型输出最大差：`0`
- 原模型六份相同输入内部最大差：`1.52587890625e-05`

原下载文件没有被覆盖；派生模型与大型模型文件均被 Git 忽略。
