# KeepBlinking 当前眼动输入审计

审计基线：

- 分支：`ui-module-polish`
- 初始工作区：clean
- Unity：6000.1.8f1
- MediaPipe Unity Plugin：0.16.3 本地 tarball

## 摄像头和 FaceLandmarker

`FaceLandmarkerRunner` 通过 `ImageSourceProvider.ImageSource` 取得摄像头
纹理。实际拥有并播放 `WebCamTexture` 的组件是 MediaPipe 示例框架的
`WebCamSource`。Runner 复用该纹理构建 MediaPipe Image，没有第二个摄像头。

Runner 读取 `ImageTransformationOptions`，显式使用水平翻转、垂直翻转和
rotationDegrees。任何 L2CS 裁剪必须复用同一纹理和这些变换。

## Current 视线坐标

`EyeInputDebugState.UpdateFrom` 读取第一张脸的 landmarks：

1. 计算完整 landmarks 的归一化人脸 Rect；
2. 对 landmarks 468–477 求双虹膜中心；
3. 对眼角和眼睑 landmarks 求双眼中心；
4. 用双眼跨度和脸部垂直跨度归一化虹膜偏移；
5. 使用固定倍率 X=1.8、Y=3.2 映射到归一化屏幕；
6. 转换为 Unity 屏幕像素，Y 轴翻转。

若缺少虹膜 landmarks，则回退为人脸 Rect 中心。

## 眨眼、睁眼、持续闭眼

这些信号均来自 MediaPipe，L2CS 不得接管：

- 每眼 EAR 由眼角和上下眼睑 landmarks 计算；
- 有 blendshape 时优先使用 `eyeBlinkLeft/Right`；
- `LeftEyeOpen/RightEyeOpen = 1 - blinkScore`；
- 双眼低于绝对阈值时形成基础 `IsBlinking`；
- `BlinkCount` 只在闭眼状态上升沿增加；
- 第一关另有基于用户自然睁眼基线的相对轻眨状态机；
- 持续闭眼由绝对阈值、相对基线下降和最短保持时间共同识别；
- 重新睁眼使用绝对或相对释放阈值。

## 追踪丢失和推远

- FaceLandmarker 无结果时 `EyeInputDebugState.Clear()`；
- 第一关以 `FaceDetected` 判断追踪有效性；
- 危机状态允许在短暂追踪丢失期间维持闭眼状态；
- 推远设备使用平滑后的人脸面积变化和第一关的距离基线，不依赖视线坐标。

## 平滑、有效性、死区和区域锁定

- 原始 gaze 在第一关使用指数平滑；
- gaze 有效性为 `FaceDetected && HasGazeScreenPosition`；
- 启动校准使用中心加四角，共五点；
- 当前校准只计算独立 X/Y scale 和 offset；
- 普通目标使用实际屏幕 Rect 加 `_gazePaddingPixels`；
- 未直接命中时可使用基于屏幕中心方向的 edge soft lock；
- 水平方向意图有独立左右死区；
- 危机前眨眼选择使用眨眼前短时间窗平均 gaze。

## 直接依赖者

- `EdgeOrbitHarvestMvp`：直接读取完整 `EyeInputDebugSnapshot`，包括 gaze、
  blink、open/closed、tracking 和 face area；
- `BlinkBootSequence`：直接读取 face、open 和 blink；
- `BasicObservationMvp`：直接读取 blink，gaze 主要仍是 Transform/ray；
- `EyeInputDebugOverlay`：直接显示 snapshot；
- `SessionReportView`：通过 `EdgeOrbitHarvestMvp.CurrentGazeScreenPosition`
  检查 Continue 区域。

教程、Boss、强化和存档没有被本轮输入审计修改。

