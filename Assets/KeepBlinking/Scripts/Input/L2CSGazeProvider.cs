using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace KeepBlinking.Input
{
  public sealed class L2CSGazeProvider : IGazePositionProvider
  {
    private const string ModelResourcePath = "L2CSExperimental/l2cs_batch1";
    private const string PitchOutputName = "516";
    private const string YawOutputName = "523";
    private const float InferenceIntervalSeconds = 1f / 12f;
    private const float MaximumSampleAgeSeconds = 0.45f;

    private readonly int _cropRectId = Shader.PropertyToID("_CropRect");
    private readonly int _flipHorizontalId = Shader.PropertyToID("_FlipHorizontal");
    private readonly int _flipVerticalId = Shader.PropertyToID("_FlipVertical");
    private readonly int _rotationQuarterTurnsId = Shader.PropertyToID("_RotationQuarterTurns");

    private Func<Texture> _getCurrentTexture;
    private Func<bool> _isFrameSourceReady;
    private Func<bool> _isFlippedHorizontally;
    private Func<bool> _isFlippedVertically;
    private Func<int> _getRotationQuarterTurns;
    private Worker _worker;
    private Tensor<float> _input;
    private Tensor<float> _pitchOutput;
    private Tensor<float> _yawOutput;
    private RenderTexture _preprocessedFace;
    private Material _preprocessMaterial;
    private bool _inFlight;
    private bool _disposed;
    private double _scheduledAt;
    private double _nextInferenceAt;
    private double _nextInitializeAttemptAt;
    private Vector2 _smoothedDirectionDegrees;
    private bool _hasSmoothedDirection;
    private GazeProviderSample _latest;
    private bool _hasLatest;

    public string ProviderName => "L2CS";
    public bool IsAvailable => _worker != null && _preprocessMaterial != null && !_disposed;
    public string FailureReason { get; private set; } = "Model is not initialized.";
    public CalibratedScreenGazeMapper Mapper { get; } = new CalibratedScreenGazeMapper();

    public void BindFrameSource(
      Func<Texture> getCurrentTexture,
      Func<bool> isFrameSourceReady,
      Func<bool> isFlippedHorizontally,
      Func<bool> isFlippedVertically,
      Func<int> getRotationQuarterTurns)
    {
      _getCurrentTexture = getCurrentTexture;
      _isFrameSourceReady = isFrameSourceReady;
      _isFlippedHorizontally = isFlippedHorizontally;
      _isFlippedVertically = isFlippedVertically;
      _getRotationQuarterTurns = getRotationQuarterTurns;
    }

    public void Tick()
    {
      if (_disposed)
      {
        return;
      }

      var now = Time.unscaledTimeAsDouble;
      if (!IsAvailable && now >= _nextInitializeAttemptAt)
      {
        _nextInitializeAttemptAt = now + 2.0;
        TryInitialize();
      }

      if (!IsAvailable)
      {
        return;
      }

      PollCompletedInference(now);
      if (_inFlight || now < _nextInferenceAt)
      {
        return;
      }

      var snapshot = EyeInputDebugState.Latest;
      if (!snapshot.FaceDetected || _isFrameSourceReady == null || !_isFrameSourceReady())
      {
        return;
      }

      var sourceTexture = _getCurrentTexture?.Invoke();
      if (sourceTexture == null)
      {
        FailureReason = "The shared MediaPipe camera texture is unavailable.";
        return;
      }

      var crop = MakeSquareFaceCrop(snapshot.FaceRect, 0.16f);
      _preprocessMaterial.SetVector(_cropRectId, new Vector4(crop.xMin, crop.yMin, crop.xMax, crop.yMax));
      _preprocessMaterial.SetFloat(_flipHorizontalId, _isFlippedHorizontally != null && _isFlippedHorizontally() ? 1f : 0f);
      _preprocessMaterial.SetFloat(_flipVerticalId, _isFlippedVertically != null && _isFlippedVertically() ? 1f : 0f);
      _preprocessMaterial.SetFloat(_rotationQuarterTurnsId, _getRotationQuarterTurns != null ? _getRotationQuarterTurns() : 0f);

      Graphics.Blit(sourceTexture, _preprocessedFace, _preprocessMaterial);
      TextureConverter.ToTensor(
        _preprocessedFace,
        _input,
        new TextureTransform().SetTensorLayout(TensorLayout.NCHW));

      try
      {
        _worker.Schedule(_input);
        _pitchOutput = _worker.PeekOutput(PitchOutputName) as Tensor<float>;
        _yawOutput = _worker.PeekOutput(YawOutputName) as Tensor<float>;
        if (_pitchOutput == null || _yawOutput == null)
        {
          FailureReason = "The model outputs 516/523 were not found.";
          return;
        }

        _pitchOutput.ReadbackRequest();
        _yawOutput.ReadbackRequest();
        _scheduledAt = now;
        _nextInferenceAt = now + InferenceIntervalSeconds;
        _inFlight = true;
        FailureReason = string.Empty;
      }
      catch (Exception exception)
      {
        FailureReason = $"Inference scheduling failed: {exception.Message}";
        _inFlight = false;
      }
    }

    public bool TryGetLatest(out GazeProviderSample sample)
    {
      sample = _latest;
      if (!_hasLatest)
      {
        return false;
      }

      var snapshot = EyeInputDebugState.Latest;
      var fresh = Time.unscaledTimeAsDouble - sample.TimestampSeconds <= MaximumSampleAgeSeconds;
      return sample.TrackingValid && snapshot.FaceDetected && fresh;
    }

    public void Dispose()
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
      _inFlight = false;
      _worker?.Dispose();
      _worker = null;
      _input?.Dispose();
      _input = null;

      if (_preprocessedFace != null)
      {
        _preprocessedFace.Release();
        UnityEngine.Object.Destroy(_preprocessedFace);
        _preprocessedFace = null;
      }

      if (_preprocessMaterial != null)
      {
        UnityEngine.Object.Destroy(_preprocessMaterial);
        _preprocessMaterial = null;
      }
    }

    private void TryInitialize()
    {
      if (_worker != null || _disposed)
      {
        return;
      }

      if (!SystemInfo.supportsComputeShaders)
      {
        FailureReason = "GPU compute shaders are unavailable; CPU is too slow for the realtime gate.";
        return;
      }

      var modelAsset = Resources.Load<ModelAsset>(ModelResourcePath);
      if (modelAsset == null)
      {
        FailureReason = "Local research model asset is missing or still importing.";
        return;
      }

      var shader = Shader.Find("Hidden/KeepBlinking/L2CSPreprocess");
      if (shader == null)
      {
        FailureReason = "L2CS preprocessing shader is unavailable.";
        return;
      }

      try
      {
        var model = ModelLoader.Load(modelAsset);
        _worker = new Worker(model, BackendType.GPUCompute);
        _input = new Tensor<float>(new TensorShape(1, 3, 448, 448));
        _preprocessedFace = new RenderTexture(448, 448, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
          name = "L2CS Preprocessed Face",
          filterMode = FilterMode.Bilinear,
          wrapMode = TextureWrapMode.Clamp,
        };
        _preprocessedFace.Create();
        _preprocessMaterial = new Material(shader)
        {
          name = "L2CS Preprocess Material",
          hideFlags = HideFlags.HideAndDontSave,
        };
        FailureReason = string.Empty;
      }
      catch (Exception exception)
      {
        FailureReason = $"Model initialization failed: {exception.Message}";
        _worker?.Dispose();
        _worker = null;
        _input?.Dispose();
        _input = null;
      }
    }

    private void PollCompletedInference(double now)
    {
      if (!_inFlight || _pitchOutput == null || _yawOutput == null ||
          !_pitchOutput.IsReadbackRequestDone() || !_yawOutput.IsReadbackRequestDone())
      {
        return;
      }

      try
      {
        using var pitchCpu = _pitchOutput.ReadbackAndClone();
        using var yawCpu = _yawOutput.ReadbackAndClone();
        var pitch = pitchCpu[0];
        var yaw = yawCpu[0];
        if (!IsFinite(pitch) || !IsFinite(yaw) || Mathf.Abs(pitch) > 180f || Mathf.Abs(yaw) > 180f)
        {
          FailureReason = "The model returned non-finite or out-of-range angles.";
          return;
        }

        var direction = new Vector2(pitch, yaw);
        var elapsed = Mathf.Max(0.0001f, (float)(now - _scheduledAt));
        var smoothing = 1f - Mathf.Exp(-10f * elapsed);
        _smoothedDirectionDegrees = _hasSmoothedDirection
          ? Vector2.Lerp(_smoothedDirectionDegrees, direction, smoothing)
          : direction;
        _hasSmoothedDirection = true;

        // Ailia labels outputs 516/523 as pitch/yaw, while the upstream network returns
        // its yaw head first. The fixed horizontal-mirror test confirms output 516 is
        // the horizontal calibration axis. Preserve tested output order here.
        var rawCalibrationAxes = direction;
        var smoothedCalibrationAxes = _smoothedDirectionDegrees;
        var hasScreenPosition = Mapper.TryMap(smoothedCalibrationAxes, out var normalizedScreenPosition);
        _latest = new GazeProviderSample(
          ProviderName,
          now,
          EyeInputDebugState.Latest.FaceDetected,
          rawCalibrationAxes,
          hasScreenPosition,
          normalizedScreenPosition,
          _smoothedDirectionDegrees,
          (float)((now - _scheduledAt) * 1000.0));
        _hasLatest = true;
        FailureReason = string.Empty;
      }
      catch (Exception exception)
      {
        FailureReason = $"Output readback failed: {exception.Message}";
      }
      finally
      {
        _inFlight = false;
      }
    }

    private static Rect MakeSquareFaceCrop(Rect faceRect, float paddingRatio)
    {
      var size = Mathf.Max(faceRect.width, faceRect.height) * (1f + paddingRatio * 2f);
      size = Mathf.Clamp(size, 0.05f, 1f);
      var center = faceRect.center;
      var minX = Mathf.Clamp(center.x - size * 0.5f, 0f, Mathf.Max(0f, 1f - size));
      var minY = Mathf.Clamp(center.y - size * 0.5f, 0f, Mathf.Max(0f, 1f - size));
      return new Rect(minX, minY, size, size);
    }

    private static bool IsFinite(float value)
    {
      return !float.IsNaN(value) && !float.IsInfinity(value);
    }
  }
}
