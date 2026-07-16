// Copyright (c) 2023 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using KeepBlinking.Gameplay;
using KeepBlinking.Input;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mediapipe.Unity.Sample.FaceLandmarkDetection
{
  public class FaceLandmarkerRunner : VisionTaskApiRunner<FaceLandmarker>
  {
    [SerializeField] private FaceLandmarkerResultAnnotationController _faceLandmarkerResultAnnotationController;
    [SerializeField] private bool _hideCameraPreviewForKeepBlinking = true;
    [SerializeField] private bool _autoCreateKeepBlinkingObjects;
    [SerializeField] private bool _logRunnerConfiguration;
    [SerializeField] private global::Mediapipe.Logger.LogLevel _runtimeLogLevel = global::Mediapipe.Logger.LogLevel.Warn;

    private Experimental.TextureFramePool _textureFramePool;

    public readonly FaceLandmarkDetectionConfig config = new FaceLandmarkDetectionConfig();

    public override void Stop()
    {
      base.Stop();
      _textureFramePool?.Dispose();
      _textureFramePool = null;
    }

    protected override IEnumerator Run()
    {
      if (_autoCreateKeepBlinkingObjects)
      {
        EyeInputDebugOverlay.EnsureExists();
        BlinkBootSequence.EnsureExists();
        EdgeOrbitHarvestMvp.EnsureExists();
      }

      global::Mediapipe.Logger.MinLogLevel = _runtimeLogLevel;
      if (_logRunnerConfiguration)
      {
        Debug.Log($"Delegate = {config.Delegate}");
        Debug.Log($"Image Read Mode = {config.ImageReadMode}");
        Debug.Log($"Running Mode = {config.RunningMode}");
        Debug.Log($"NumFaces = {config.NumFaces}");
        Debug.Log($"MinFaceDetectionConfidence = {config.MinFaceDetectionConfidence}");
        Debug.Log($"MinFacePresenceConfidence = {config.MinFacePresenceConfidence}");
        Debug.Log($"MinTrackingConfidence = {config.MinTrackingConfidence}");
        Debug.Log($"OutputFaceBlendshapes = {config.OutputFaceBlendshapes}");
        Debug.Log($"OutputFacialTransformationMatrixes = {config.OutputFacialTransformationMatrixes}");
      }

      yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

      var options = config.GetFaceLandmarkerOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnFaceLandmarkDetectionOutput : null);
      taskApi = FaceLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
      var imageSource = ImageSourceProvider.ImageSource;

      var playRoutine = imageSource.Play();
      while (true)
      {
        object current;
        try
        {
          if (!playRoutine.MoveNext())
          {
            break;
          }
          current = playRoutine.Current;
        }
        catch (System.Exception ex)
        {
          Debug.LogError($"Failed to start the camera feed: {ex.Message}. Close other apps using the camera, then try again.");
          yield break;
        }

        yield return current;
      }

      if (!imageSource.isPrepared)
      {
        Debug.LogError("Failed to start the camera feed. Check camera permission, close other apps using the camera, and try again.");
        yield break;
      }

      // Use RGBA32 as the input format.
      // TODO: When using GpuBuffer, MediaPipe assumes that the input format is BGRA, so maybe the following code needs to be fixed.
      _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      // NOTE: The screen will be resized later, keeping the aspect ratio.
      if (screen != null)
      {
        screen.Initialize(imageSource);
      }

      if (_faceLandmarkerResultAnnotationController != null)
      {
        SetupAnnotationController(_faceLandmarkerResultAnnotationController, imageSource);
      }
      HideKeepBlinkingCameraPreview();

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);
      var waitForEndOfFrame = new WaitForEndOfFrame();
      var result = FaceLandmarkerResult.Alloc(options.numFaces, config.OutputFaceBlendshapes, config.OutputFacialTransformationMatrixes);

      // NOTE: we can share the GL context of the render thread with MediaPipe (for now, only on Android)
      var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
      using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

      while (true)
      {
        if (isPaused)
        {
          yield return new WaitWhile(() => isPaused);
        }

        if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
        {
          yield return null;
          continue;
        }

        // Build the input Image
        Image image;
        switch (config.ImageReadMode)
        {
          case ImageReadMode.GPU:
            if (!canUseGpuImage)
            {
              throw new System.Exception("ImageReadMode.GPU is not supported");
            }
            textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildGPUImage(glContext);
            // TODO: Currently we wait here for one frame to make sure the texture is fully copied to the TextureFrame before sending it to MediaPipe.
            // This usually works but is not guaranteed. Find a proper way to do this. See: https://github.com/homuler/MediaPipeUnityPlugin/pull/1311
            yield return waitForEndOfFrame;
            break;
          case ImageReadMode.CPU:
            yield return waitForEndOfFrame;
            textureFrame.ReadTextureOnCPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
          case ImageReadMode.CPUAsync:
          default:
            req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            yield return waitUntilReqDone;

            if (req.hasError)
            {
              Debug.LogWarning($"Failed to read texture from the image source");
              continue;
            }
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
        }

        switch (taskApi.runningMode)
        {
          case Tasks.Vision.Core.RunningMode.IMAGE:
            if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
            {
              EyeInputDebugState.UpdateFrom(result);
              DrawFaceLandmarksNow(result);
            }
            else
            {
              EyeInputDebugState.Clear();
              DrawFaceLandmarksNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.VIDEO:
            if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
            {
              EyeInputDebugState.UpdateFrom(result);
              DrawFaceLandmarksNow(result);
            }
            else
            {
              EyeInputDebugState.Clear();
              DrawFaceLandmarksNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
            taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            break;
        }
      }
    }

    private void OnFaceLandmarkDetectionOutput(FaceLandmarkerResult result, Image image, long timestamp)
    {
      EyeInputDebugState.UpdateFrom(result);
      DrawFaceLandmarksLater(result);
    }

    private void HideKeepBlinkingCameraPreview()
    {
      if (!_hideCameraPreviewForKeepBlinking)
      {
        return;
      }

      if (screen != null)
      {
        screen.gameObject.SetActive(false);
      }

      if (_faceLandmarkerResultAnnotationController != null)
      {
        _faceLandmarkerResultAnnotationController.gameObject.SetActive(false);
      }
    }

    private void DrawFaceLandmarksNow(FaceLandmarkerResult result)
    {
      if (_hideCameraPreviewForKeepBlinking || _faceLandmarkerResultAnnotationController == null)
      {
        return;
      }

      _faceLandmarkerResultAnnotationController.DrawNow(result);
    }

    private void DrawFaceLandmarksLater(FaceLandmarkerResult result)
    {
      if (_hideCameraPreviewForKeepBlinking || _faceLandmarkerResultAnnotationController == null)
      {
        return;
      }

      _faceLandmarkerResultAnnotationController.DrawLater(result);
    }
  }
}
