using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KeepBlinking.Gameplay
{
  public sealed class DistanceCameraFeedback : MonoBehaviour
  {
    private const string BackgroundShaderResourcePath = "DistanceFeedback/DistanceBackgroundFeedback";

    private readonly struct LayerRestoreEntry
    {
      public LayerRestoreEntry(GameObject gameObject, int layer)
      {
        GameObject = gameObject;
        Layer = layer;
      }

      public GameObject GameObject { get; }
      public int Layer { get; }
    }

    [Header("Distance Curve")]
    [SerializeField, Min(1f)] private float _nearStartRatio = 1.03f;
    [SerializeField, Min(1.01f)] private float _nearFullRatio = 1.30f;
    [SerializeField, Range(0.5f, 1f)] private float _farStartRatio = 0.95f;
    [SerializeField, Range(0.1f, 0.99f)] private float _farFullRatio = 0.82f;
    [SerializeField, Range(0.25f, 0.4f)] private float _responseSeconds = 0.30f;
    [SerializeField, Range(0.1f, 0.35f)] private float _trackingLostRecoverySeconds = 0.35f;

    [Header("World Scale")]
    [SerializeField, Range(0f, 0.25f)] private float _maximumNearWorldMagnification = 0.18f;
    [SerializeField, Range(0f, 0.12f)] private float _maximumFarWorldReduction = 0.06f;

    [Header("World Fisheye")]
    [SerializeField, Range(0f, 0.45f)] private float _maximumLensDistortion = 0.30f;
    [SerializeField, Range(1f, 1.3f)] private float _maximumOverscanScale = 1.15f;
    [SerializeField, Range(0f, 0.24f)] private float _maximumGridBend = 0.14f;
    [SerializeField, Range(0f, 0.4f)] private float _maximumVignetteStrength = 0.30f;
    [SerializeField, Range(0f, 4f)] private float _maximumEdgeBlurPixels = 2f;
    [SerializeField, Range(0f, 0.2f)] private float _maximumEdgeDesaturation = 0.08f;

    [Header("Too Close Status")]
    [SerializeField] private bool _showTooCloseStatus = true;
    [SerializeField, Range(0.35f, 1f)] private float _tooCloseStatusAlpha = 0.76f;

#if UNITY_EDITOR
    [Header("Development Preview")]
    [SerializeField] private bool _developmentPreviewEnabled;
    [SerializeField, Range(1f, 1.3f)] private float _developmentPreviewDistanceRatio = 1f;
#endif

    private readonly List<LayerRestoreEntry> _hudLayerRestore = new List<LayerRestoreEntry>();
    private Camera _worldCamera;
    private Camera _hudCamera;
    private UniversalAdditionalCameraData _worldCameraData;
    private UniversalAdditionalCameraData _hudCameraData;
    private SpriteRenderer _backgroundRenderer;
    private Material _originalBackgroundMaterial;
    private Material _backgroundFeedbackMaterial;
    private Volume _worldVolume;
    private VolumeProfile _worldVolumeProfile;
    private LensDistortion _lensDistortion;
    private Vignette _vignette;
    private float _defaultOrthographicSize;
    private float _targetNearAmount;
    private float _targetFarAmount;
    private float _nearVelocity;
    private float _farVelocity;
    private int _originalWorldCullingMask;
    private bool _originalRenderPostProcessing;
    private bool _inputValid;
    private bool _tooClose;
    private GUIStyle _tooCloseStyle;

    public float NearAmount { get; private set; }
    public float FarAmount { get; private set; }
    public float CameraFeedbackAmount => NearAmount - FarAmount;
    public float CurrentDistortionStrength { get; private set; }
    public float CurrentOverscanScale { get; private set; } = 1f;
    public float HudWorldScaleCompensation =>
      _worldCamera != null && _worldCamera.orthographic && _defaultOrthographicSize > 0f
        ? _worldCamera.orthographicSize / _defaultOrthographicSize
        : 1f;

    public void SetTooCloseStatusVisible(bool visible)
    {
      _showTooCloseStatus = visible;
    }

    public void Configure(Camera worldCamera, SpriteRenderer backgroundRenderer)
    {
      if (_worldCamera != worldCamera)
      {
        RestoreCameraSetup();
        _worldCamera = worldCamera;
        if (_worldCamera != null && _worldCamera.orthographic)
        {
          _defaultOrthographicSize = Mathf.Max(0.01f, _worldCamera.orthographicSize);
          _originalWorldCullingMask = _worldCamera.cullingMask;
          EnsureWorldPostProcessing();
          EnsureHudCamera();
        }
      }

      if (_backgroundRenderer == backgroundRenderer && _backgroundFeedbackMaterial != null)
      {
        return;
      }

      RestoreBackgroundMaterial();
      _backgroundRenderer = backgroundRenderer;
      if (_backgroundRenderer == null)
      {
        return;
      }

      _originalBackgroundMaterial = _backgroundRenderer.sharedMaterial;
      var shader = Resources.Load<Shader>(BackgroundShaderResourcePath);
      if (shader == null)
      {
        shader = Shader.Find("KeepBlinking/DistanceBackgroundFeedback");
      }
      if (shader == null)
      {
        Debug.LogWarning("KeepBlinking distance background shader is unavailable. URP world fisheye feedback will remain active.", this);
        return;
      }

      _backgroundFeedbackMaterial = new Material(shader)
      {
        name = "Distance Background Feedback (Runtime)",
        hideFlags = HideFlags.HideAndDontSave,
      };
      if (_backgroundRenderer.sprite != null)
      {
        _backgroundFeedbackMaterial.mainTexture = _backgroundRenderer.sprite.texture;
      }
      _backgroundRenderer.sharedMaterial = _backgroundFeedbackMaterial;
      ApplyVisualFeedback();
    }

    public void RegisterHudRoot(GameObject hudRoot)
    {
      if (hudRoot == null)
      {
        return;
      }

      var hudLayer = LayerMask.NameToLayer("UI");
      if (hudLayer < 0)
      {
        Debug.LogWarning("KeepBlinking could not isolate world-space HUD because the UI layer is unavailable.", this);
        return;
      }

      RegisterHudTransform(hudRoot.transform, hudLayer);
      EnsureHudCamera();
    }

    public void SetInput(float distanceRatio, bool trackingValid, bool feedbackAllowed, bool tooClose)
    {
#if UNITY_EDITOR
      if (_developmentPreviewEnabled)
      {
        distanceRatio = _developmentPreviewDistanceRatio;
        trackingValid = true;
        feedbackAllowed = true;
        tooClose = distanceRatio >= 1.18f;
      }
#endif
      _inputValid = trackingValid && feedbackAllowed &&
                    distanceRatio > 0f && !float.IsNaN(distanceRatio) && !float.IsInfinity(distanceRatio);
      if (!_inputValid)
      {
        _targetNearAmount = 0f;
        _targetFarAmount = 0f;
        _tooClose = false;
        return;
      }

      _targetNearAmount = EvaluateNearAmount(distanceRatio);
      var farRange = Mathf.Max(0.001f, _farStartRatio - _farFullRatio);
      _targetFarAmount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_farStartRatio - distanceRatio) / farRange));
      _tooClose = tooClose;
    }

    public void Tick(float unscaledDeltaTime)
    {
      var deltaTime = Mathf.Max(0f, unscaledDeltaTime);
      if (_inputValid)
      {
        var response = Mathf.Clamp(_responseSeconds, 0.25f, 0.4f);
        NearAmount = Mathf.SmoothDamp(NearAmount, _targetNearAmount, ref _nearVelocity, response, Mathf.Infinity, deltaTime);
        FarAmount = Mathf.SmoothDamp(FarAmount, _targetFarAmount, ref _farVelocity, response, Mathf.Infinity, deltaTime);
      }
      else
      {
        var recovery = Mathf.Max(0.01f, _trackingLostRecoverySeconds);
        NearAmount = Mathf.MoveTowards(NearAmount, 0f, deltaTime / recovery);
        FarAmount = Mathf.MoveTowards(FarAmount, 0f, deltaTime / recovery);
        _nearVelocity = 0f;
        _farVelocity = 0f;
      }

      ApplyCameraFeedback();
      ApplyVisualFeedback();
      SyncHudCamera();
    }

    public Vector2 OutputScreenToWorldSourceScreen(Vector2 outputScreenPosition)
    {
      if (Screen.width <= 0 || Screen.height <= 0 || CurrentDistortionStrength <= 0.00001f)
      {
        return outputScreenPosition;
      }

      var outputUv = new Vector2(outputScreenPosition.x / Screen.width, outputScreenPosition.y / Screen.height);
      var sourceUv = DistortOutputUvToSourceUv(outputUv);
      return new Vector2(sourceUv.x * Screen.width, sourceUv.y * Screen.height);
    }

    public Vector2 WorldSourceScreenToOutputScreen(Vector2 sourceScreenPosition)
    {
      if (Screen.width <= 0 || Screen.height <= 0 || CurrentDistortionStrength <= 0.00001f)
      {
        return sourceScreenPosition;
      }

      var sourceUv = new Vector2(sourceScreenPosition.x / Screen.width, sourceScreenPosition.y / Screen.height);
      var outputUv = sourceUv;
      for (var i = 0; i < 8; i++)
      {
        var error = sourceUv - DistortOutputUvToSourceUv(outputUv);
        outputUv += error;
      }
      return new Vector2(outputUv.x * Screen.width, outputUv.y * Screen.height);
    }

    private float EvaluateNearAmount(float distanceRatio)
    {
      var nearRange = Mathf.Max(0.001f, _nearFullRatio - _nearStartRatio);
      return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distanceRatio - _nearStartRatio) / nearRange));
    }

    private void EnsureWorldPostProcessing()
    {
      if (_worldCamera == null)
      {
        return;
      }

      _worldCameraData = _worldCamera.GetUniversalAdditionalCameraData();
      _originalRenderPostProcessing = _worldCameraData.renderPostProcessing;
      _worldCameraData.renderType = CameraRenderType.Base;
      _worldCameraData.renderPostProcessing = true;

      if (_worldVolume == null)
      {
        var volumeObject = new GameObject("Distance World Fisheye Volume")
        {
          hideFlags = HideFlags.HideAndDontSave,
        };
        volumeObject.transform.SetParent(transform, false);
        _worldVolume = volumeObject.AddComponent<Volume>();
        _worldVolume.isGlobal = true;
        _worldVolume.priority = 10000f;
        _worldVolume.weight = 1f;
      }

      if (_worldVolumeProfile == null)
      {
        _worldVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _worldVolumeProfile.name = "Distance World Fisheye Profile (Runtime)";
        _worldVolumeProfile.hideFlags = HideFlags.HideAndDontSave;
        _lensDistortion = _worldVolumeProfile.Add<LensDistortion>(true);
        _vignette = _worldVolumeProfile.Add<Vignette>(true);
        _worldVolume.sharedProfile = _worldVolumeProfile;

        _lensDistortion.intensity.Override(0f);
        _lensDistortion.xMultiplier.Override(1f);
        _lensDistortion.yMultiplier.Override(1f);
        _lensDistortion.center.Override(new Vector2(0.5f, 0.5f));
        _lensDistortion.scale.Override(1f);
        _vignette.color.Override(Color.black);
        _vignette.center.Override(new Vector2(0.5f, 0.5f));
        _vignette.intensity.Override(0f);
        _vignette.smoothness.Override(0.48f);
        _vignette.rounded.Override(false);
      }
    }

    private void EnsureHudCamera()
    {
      if (_worldCamera == null || _hudCamera != null)
      {
        return;
      }

      var hudLayer = LayerMask.NameToLayer("UI");
      if (hudLayer < 0)
      {
        return;
      }

      _worldCamera.cullingMask = _originalWorldCullingMask & ~(1 << hudLayer);
      var hudCameraObject = new GameObject("Distance Feedback HUD Camera")
      {
        hideFlags = HideFlags.HideAndDontSave,
      };
      hudCameraObject.transform.SetParent(_worldCamera.transform, false);
      _hudCamera = hudCameraObject.AddComponent<Camera>();
      _hudCamera.CopyFrom(_worldCamera);
      _hudCamera.clearFlags = CameraClearFlags.Nothing;
      _hudCamera.cullingMask = 1 << hudLayer;
      _hudCamera.depth = _worldCamera.depth + 1f;

      _hudCameraData = _hudCamera.GetUniversalAdditionalCameraData();
      _hudCameraData.renderType = CameraRenderType.Overlay;
      _hudCameraData.renderPostProcessing = false;
      var cameraStack = _worldCameraData != null ? _worldCameraData.cameraStack : null;
      if (cameraStack != null && !cameraStack.Contains(_hudCamera))
      {
        cameraStack.Add(_hudCamera);
      }
      SyncHudCamera();
    }

    private void SyncHudCamera()
    {
      if (_worldCamera == null || _hudCamera == null)
      {
        return;
      }

      _hudCamera.transform.SetPositionAndRotation(_worldCamera.transform.position, _worldCamera.transform.rotation);
      _hudCamera.orthographic = _worldCamera.orthographic;
      _hudCamera.orthographicSize = _worldCamera.orthographicSize;
      _hudCamera.fieldOfView = _worldCamera.fieldOfView;
      _hudCamera.nearClipPlane = _worldCamera.nearClipPlane;
      _hudCamera.farClipPlane = _worldCamera.farClipPlane;
      _hudCamera.rect = _worldCamera.rect;
      _hudCamera.aspect = _worldCamera.aspect;
    }

    private void RegisterHudTransform(Transform root, int hudLayer)
    {
      _hudLayerRestore.Add(new LayerRestoreEntry(root.gameObject, root.gameObject.layer));
      root.gameObject.layer = hudLayer;
      for (var i = 0; i < root.childCount; i++)
      {
        RegisterHudTransform(root.GetChild(i), hudLayer);
      }
    }

    private void ApplyCameraFeedback()
    {
      if (_worldCamera == null || !_worldCamera.orthographic || _defaultOrthographicSize <= 0f)
      {
        return;
      }

      var visualScale = 1f + NearAmount * Mathf.Clamp(_maximumNearWorldMagnification, 0f, 0.25f) -
                        FarAmount * Mathf.Clamp(_maximumFarWorldReduction, 0f, 0.12f);
      _worldCamera.orthographicSize = _defaultOrthographicSize / Mathf.Max(0.68f, visualScale);
    }

    private void ApplyVisualFeedback()
    {
      CurrentDistortionStrength = NearAmount * Mathf.Clamp(_maximumLensDistortion, 0f, 0.45f);
      CurrentOverscanScale = Mathf.Lerp(1f, Mathf.Clamp(_maximumOverscanScale, 1f, 1.3f), NearAmount);

      if (_lensDistortion != null)
      {
        _lensDistortion.intensity.value = -CurrentDistortionStrength;
        _lensDistortion.scale.value = CurrentOverscanScale;
      }
      if (_vignette != null)
      {
        _vignette.intensity.value = NearAmount * Mathf.Clamp(_maximumVignetteStrength, 0f, 0.4f);
      }
      if (_backgroundFeedbackMaterial != null)
      {
        _backgroundFeedbackMaterial.SetFloat("_NearAmount", NearAmount);
        _backgroundFeedbackMaterial.SetFloat("_BarrelStrength", NearAmount * Mathf.Min(0.12f, _maximumLensDistortion * 0.4f));
        _backgroundFeedbackMaterial.SetFloat("_GridBend", NearAmount * Mathf.Clamp(_maximumGridBend, 0f, 0.24f));
        _backgroundFeedbackMaterial.SetFloat("_VignetteStrength", 0f);
        _backgroundFeedbackMaterial.SetFloat("_EdgeBlurPixels", NearAmount * Mathf.Clamp(_maximumEdgeBlurPixels, 0f, 4f));
        _backgroundFeedbackMaterial.SetFloat("_EdgeDesaturation", NearAmount * Mathf.Clamp(_maximumEdgeDesaturation, 0f, 0.2f));
      }
    }

    private Vector2 DistortOutputUvToSourceUv(Vector2 outputUv)
    {
      if (CurrentDistortionStrength <= 0.00001f)
      {
        return outputUv;
      }

      var uv = (outputUv - Vector2.one * 0.5f) / Mathf.Max(1f, CurrentOverscanScale) + Vector2.one * 0.5f;
      var radial = uv - Vector2.one * 0.5f;
      var radius = radial.magnitude;
      if (radius <= 0.000001f)
      {
        return uv;
      }

      var amountDegrees = Mathf.Min(160f, 1.6f * Mathf.Max(CurrentDistortionStrength * 100f, 1f));
      var theta = amountDegrees * Mathf.Deg2Rad;
      var sigma = 2f * Mathf.Tan(theta * 0.5f);
      var radialScale = (1f / radius) * (1f / theta) * Mathf.Atan(radius * sigma);
      return uv + radial * (radialScale - 1f);
    }

    private void OnGUI()
    {
      if (!_showTooCloseStatus || !_tooClose)
      {
        return;
      }

      if (_tooCloseStyle == null)
      {
        _tooCloseStyle = new GUIStyle(GUI.skin.label)
        {
          alignment = TextAnchor.MiddleCenter,
          fontStyle = FontStyle.Bold,
          fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.022f), 14, 24),
          normal = { textColor = new Color(0.92f, 0.91f, 0.82f, _tooCloseStatusAlpha) },
        };
      }

      var safeArea = Screen.safeArea;
      var width = Mathf.Min(240f, safeArea.width * 0.5f);
      GUI.Label(new Rect(safeArea.center.x - width * 0.5f, Screen.height - safeArea.yMax + 14f, width, 30f), "TOO CLOSE", _tooCloseStyle);
    }

    private void OnDisable()
    {
      _targetNearAmount = 0f;
      _targetFarAmount = 0f;
      NearAmount = 0f;
      FarAmount = 0f;
      CurrentDistortionStrength = 0f;
      CurrentOverscanScale = 1f;
      _inputValid = false;
      _tooClose = false;
      RestoreCameraScale();
      ApplyVisualFeedback();
    }

    private void OnDestroy()
    {
      RestoreCameraSetup();
      RestoreBackgroundMaterial();
    }

    private void RestoreCameraScale()
    {
      if (_worldCamera != null && _worldCamera.orthographic && _defaultOrthographicSize > 0f)
      {
        _worldCamera.orthographicSize = _defaultOrthographicSize;
      }
    }

    private void RestoreCameraSetup()
    {
      RestoreCameraScale();
      if (_worldCameraData != null)
      {
        if (_hudCamera != null)
        {
          _worldCameraData.cameraStack?.Remove(_hudCamera);
        }
        _worldCameraData.renderPostProcessing = _originalRenderPostProcessing;
      }
      if (_worldCamera != null)
      {
        _worldCamera.cullingMask = _originalWorldCullingMask;
      }
      for (var i = 0; i < _hudLayerRestore.Count; i++)
      {
        var entry = _hudLayerRestore[i];
        if (entry.GameObject != null)
        {
          entry.GameObject.layer = entry.Layer;
        }
      }
      _hudLayerRestore.Clear();

      if (_hudCamera != null)
      {
        Destroy(_hudCamera.gameObject);
      }
      if (_worldVolume != null)
      {
        Destroy(_worldVolume.gameObject);
      }
      if (_worldVolumeProfile != null)
      {
        Destroy(_worldVolumeProfile);
      }
      _hudCamera = null;
      _hudCameraData = null;
      _worldCameraData = null;
      _worldVolume = null;
      _worldVolumeProfile = null;
      _lensDistortion = null;
      _vignette = null;
    }

    private void RestoreBackgroundMaterial()
    {
      if (_backgroundRenderer != null && _backgroundRenderer.sharedMaterial == _backgroundFeedbackMaterial)
      {
        _backgroundRenderer.sharedMaterial = _originalBackgroundMaterial;
      }
      if (_backgroundFeedbackMaterial != null)
      {
        Destroy(_backgroundFeedbackMaterial);
      }
      _backgroundFeedbackMaterial = null;
      _originalBackgroundMaterial = null;
    }
  }
}
