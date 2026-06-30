// Attach this script to an Empty GameObject, then assign a real eye-tracking Transform to eyeGazeTransform.
// Eye gaze: cast a ray from eyeGazeTransform.forward. Blink: call TriggerBlink() from an eye-tracking SDK.

using System.Collections;
using System.Collections.Generic;
using KeepBlinking.Input;
using UnityEngine;
using UnityEngine.Rendering;

namespace KeepBlinking.Gameplay
{
  public class BasicObservationMvp : MonoBehaviour
  {
    [Header("Eye Tracking Hooks")]
    public Transform eyeGazeTransform;
    public bool isBlinkTriggered;

    [SerializeField] private bool _consumeMediaPipeBlinkInput = true;
    [SerializeField] private float _gazeRayDistance = 80f;
    [SerializeField] private float _minimumOpenEyeForBlinkBaseline = 0.2f;
    [SerializeField] private float _relativeBlinkCloseRatio = 0.72f;
    [SerializeField] private float _blinkCooldownSeconds = 0.35f;
    [SerializeField] private float _gazeAssistSphereRadius = 0.9f;
    [SerializeField] private float _fallbackScreenGazeRadiusPixels = 180f;

    [Header("Spawn")]
    [SerializeField] private float _spawnIntervalSeconds = 1.8f;
    [SerializeField] private int _maxActiveBlocks = 8;
    [SerializeField] private int _maxUnconvertedBlocks = 3;
    [SerializeField] private float _spawnPlaneDistance = 9f;
    [SerializeField] private Vector2 _spawnAreaSize = new Vector2(8f, 4.5f);
    [SerializeField] private Vector2 _fallbackCenteredSpawnAreaSize = new Vector2(1.6f, 1f);
    [SerializeField] private Vector2 _blockSizeRange = new Vector2(1.1f, 1.8f);

    [Header("Motion")]
    [SerializeField] private float _driftRadius = 0.75f;
    [SerializeField] private float _driftSpeed = 0.18f;
    [SerializeField] private float _gazeColorLerpSpeed = 5f;

    [Header("Conversion Feedback")]
    [SerializeField] private float _conversionDurationSeconds = 0.75f;
    [SerializeField] private float _convertedScaleRatio = 0.3f;

    private readonly List<DataBlock> _blocks = new List<DataBlock>();
    private Camera _camera;
    private Material _blockMaterial;
    private Material _gazedMaterial;
    private Material _convertedMaterial;
    private DataBlock _currentGazedBlock;
    private int _cleanedCount;
    private int _spawnedCount;
    private int _lastObservedBlinkCount = -1;
    private float _baselineLeftEyeOpen = -1f;
    private float _baselineRightEyeOpen = -1f;
    private float _lastBlinkAcceptedAt = -999f;
    private bool _lastRelativeBlinking;
    private bool _lastBlinkSignalConsumed;
    private float _nextSpawnAt;
    private GUIStyle _hudStyle;

    public static void EnsureExists()
    {
      if (FindFirstObjectByType<BasicObservationMvp>() != null)
      {
        return;
      }

      var observer = new GameObject("Basic Observation MVP");
      observer.AddComponent<BasicObservationMvp>();
    }

    public void TriggerBlink()
    {
      isBlinkTriggered = true;
    }

    private void Start()
    {
      EnsureSceneBasics();
      CreateRuntimeMaterials();
      _lastObservedBlinkCount = EyeInputDebugState.Latest.BlinkCount;
      _nextSpawnAt = Time.time + 0.2f;
    }

    private void Update()
    {
      RemoveMissingBlocks();
      SpawnOnTimer();
      UpdateGazeFocus();

      if (ConsumeBlinkSignal() && _currentGazedBlock != null)
      {
        _currentGazedBlock.ConvertToDataCluster(_convertedMaterial, _conversionDurationSeconds, _convertedScaleRatio);
        _currentGazedBlock = null;
        _cleanedCount++;
      }
    }

    private void OnGUI()
    {
      EnsureHudStyle();

      GUILayout.BeginArea(new Rect(18f, 18f, 620f, 150f));
      GUILayout.Label("Basic Observation MVP // Eye Tracking Mode", _hudStyle);
      GUILayout.Label($"Gaze source: {(eyeGazeTransform == null ? "Main Camera center zone" : eyeGazeTransform.name)}", _hudStyle);
      GUILayout.Label("Blink input: external SDK calls TriggerBlink(); MediaPipe blink bridge is also enabled.", _hudStyle);
      GUILayout.Label(eyeGazeTransform == null ? $"Fallback mode: blocks inside a {Mathf.RoundToInt(_fallbackScreenGazeRadiusPixels)}px center gaze zone can be selected." : "Real gaze Transform is assigned.", _hudStyle);
      GUILayout.Label($"Gazed block: {(_currentGazedBlock == null ? "none" : _currentGazedBlock.name)}", _hudStyle);
      GUILayout.Label($"Cleaned data blocks: {_cleanedCount}   Active targets: {CountUnconvertedBlocks()}", _hudStyle);
      var snapshot = EyeInputDebugState.Latest;
      GUILayout.Label($"Eye L {snapshot.LeftEyeOpen:F2}/{_baselineLeftEyeOpen:F2}  R {snapshot.RightEyeOpen:F2}/{_baselineRightEyeOpen:F2}  BlinkCount {snapshot.BlinkCount}  Signal {(_lastBlinkSignalConsumed ? "YES" : "no")}", _hudStyle);
      GUILayout.EndArea();
    }

    private Ray GetGazeRay()
    {
      var source = eyeGazeTransform != null ? eyeGazeTransform : _camera.transform;
      return new Ray(source.position, source.forward);
    }

    private bool ConsumeBlinkSignal()
    {
      _lastBlinkSignalConsumed = false;

      if (Time.time - _lastBlinkAcceptedAt < _blinkCooldownSeconds)
      {
        return false;
      }

      if (isBlinkTriggered)
      {
        isBlinkTriggered = false;
        _lastBlinkAcceptedAt = Time.time;
        _lastBlinkSignalConsumed = true;
        return true;
      }

      if (!_consumeMediaPipeBlinkInput)
      {
        return false;
      }

      var snapshot = EyeInputDebugState.Latest;
      UpdateBlinkBaseline(snapshot);

      if (_lastObservedBlinkCount < 0)
      {
        _lastObservedBlinkCount = snapshot.BlinkCount;
        return false;
      }

      if (snapshot.BlinkCount > _lastObservedBlinkCount)
      {
        _lastObservedBlinkCount = snapshot.BlinkCount;
        _lastBlinkAcceptedAt = Time.time;
        _lastBlinkSignalConsumed = true;
        return true;
      }

      _lastObservedBlinkCount = snapshot.BlinkCount;
      if (ConsumeRelativeBlink(snapshot))
      {
        _lastBlinkAcceptedAt = Time.time;
        _lastBlinkSignalConsumed = true;
        return true;
      }

      return false;
    }

    private void UpdateBlinkBaseline(EyeInputDebugSnapshot snapshot)
    {
      if (!snapshot.FaceDetected)
      {
        _baselineLeftEyeOpen = -1f;
        _baselineRightEyeOpen = -1f;
        _lastRelativeBlinking = false;
        return;
      }

      var averageOpen = (snapshot.LeftEyeOpen + snapshot.RightEyeOpen) * 0.5f;
      if (averageOpen < _minimumOpenEyeForBlinkBaseline)
      {
        return;
      }

      _baselineLeftEyeOpen = Mathf.Max(_baselineLeftEyeOpen, snapshot.LeftEyeOpen);
      _baselineRightEyeOpen = Mathf.Max(_baselineRightEyeOpen, snapshot.RightEyeOpen);
    }

    private bool ConsumeRelativeBlink(EyeInputDebugSnapshot snapshot)
    {
      if (_baselineLeftEyeOpen <= 0f || _baselineRightEyeOpen <= 0f)
      {
        return false;
      }

      var currentAverage = (snapshot.LeftEyeOpen + snapshot.RightEyeOpen) * 0.5f;
      var baselineAverage = (_baselineLeftEyeOpen + _baselineRightEyeOpen) * 0.5f;
      var relativeBlinking = currentAverage <= baselineAverage * _relativeBlinkCloseRatio;
      var blinkStarted = relativeBlinking && !_lastRelativeBlinking;
      _lastRelativeBlinking = relativeBlinking;
      return blinkStarted;
    }

    private void SpawnOnTimer()
    {
      if (Time.time < _nextSpawnAt || _blocks.Count >= _maxActiveBlocks || CountUnconvertedBlocks() >= _maxUnconvertedBlocks)
      {
        return;
      }

      SpawnDataBlock();
      _nextSpawnAt = Time.time + _spawnIntervalSeconds;
    }

    private void SpawnDataBlock()
    {
      var cameraTransform = _camera.transform;
      var center = cameraTransform.position + cameraTransform.forward * _spawnPlaneDistance;
      var spawnArea = eyeGazeTransform == null ? _fallbackCenteredSpawnAreaSize : _spawnAreaSize;
      var rightOffset = _spawnedCount == 0 ? 0f : Random.Range(-spawnArea.x * 0.5f, spawnArea.x * 0.5f);
      var upOffset = _spawnedCount == 0 ? 0f : Random.Range(-spawnArea.y * 0.5f, spawnArea.y * 0.5f);
      var position = center + cameraTransform.right * rightOffset + cameraTransform.up * upOffset;
      var size = Random.Range(_blockSizeRange.x, _blockSizeRange.y);

      var blockObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
      blockObject.name = $"Redundant Data Block {_blocks.Count + 1}";
      blockObject.transform.position = position;
      blockObject.transform.rotation = Random.rotationUniform;
      blockObject.transform.localScale = Vector3.one * size;
      blockObject.GetComponent<Renderer>().material = _blockMaterial;

      if (blockObject.GetComponent<Collider>() == null)
      {
        blockObject.AddComponent<BoxCollider>();
      }

      var drift = new Vector3(
        Random.Range(-_driftRadius, _driftRadius),
        Random.Range(-_driftRadius * 0.55f, _driftRadius * 0.55f),
        Random.Range(-_driftRadius * 0.2f, _driftRadius * 0.2f));

      var block = blockObject.AddComponent<DataBlock>();
      block.Initialize(position, position + drift, _blockMaterial, _gazedMaterial, _gazeColorLerpSpeed, _driftSpeed);
      _blocks.Add(block);
      _spawnedCount++;
    }

    private void UpdateGazeFocus()
    {
      var previous = _currentGazedBlock;
      _currentGazedBlock = null;

      var gazeRay = GetGazeRay();
      Debug.DrawRay(gazeRay.origin, gazeRay.direction * _gazeRayDistance, new Color(0.55f, 1f, 0.7f));

      if (Physics.SphereCast(gazeRay, _gazeAssistSphereRadius, out var hit, _gazeRayDistance))
      {
        _currentGazedBlock = hit.collider.GetComponent<DataBlock>();
        if (_currentGazedBlock != null && _currentGazedBlock.IsConverted)
        {
          _currentGazedBlock = null;
        }
      }

      if (_currentGazedBlock == null && eyeGazeTransform == null)
      {
        _currentGazedBlock = FindFallbackCenterGazedBlock();
      }

      if (previous != null && previous != _currentGazedBlock)
      {
        previous.SetGazed(false);
      }

      if (_currentGazedBlock != null)
      {
        _currentGazedBlock.SetGazed(true);
      }
    }

    private DataBlock FindFallbackCenterGazedBlock()
    {
      var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
      DataBlock bestBlock = null;
      var bestDistance = float.PositiveInfinity;

      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block == null || block.IsConverted)
        {
          continue;
        }

        var screenPosition = _camera.WorldToScreenPoint(block.transform.position);
        if (screenPosition.z <= 0f)
        {
          continue;
        }

        var distance = Vector2.Distance(new Vector2(screenPosition.x, screenPosition.y), screenCenter);
        if (distance <= _fallbackScreenGazeRadiusPixels && distance < bestDistance)
        {
          bestDistance = distance;
          bestBlock = block;
        }
      }

      return bestBlock;
    }

    private void RemoveMissingBlocks()
    {
      for (var i = _blocks.Count - 1; i >= 0; i--)
      {
        if (_blocks[i] == null)
        {
          _blocks.RemoveAt(i);
        }
      }
    }

    private int CountUnconvertedBlocks()
    {
      var count = 0;
      for (var i = 0; i < _blocks.Count; i++)
      {
        if (_blocks[i] != null && !_blocks[i].IsConverted)
        {
          count++;
        }
      }

      return count;
    }

    private void EnsureSceneBasics()
    {
      _camera = Camera.main;
      if (_camera == null)
      {
        var cameraObject = new GameObject("MVP Camera");
        _camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.transform.rotation = Quaternion.identity;
      }

      _camera.clearFlags = CameraClearFlags.SolidColor;
      _camera.backgroundColor = new Color(0.015f, 0.035f, 0.03f);

      if (FindFirstObjectByType<Light>() == null)
      {
        var lightObject = new GameObject("MVP Soft Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.color = new Color(0.72f, 1f, 0.82f);
        lightObject.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
      }
    }

    private void CreateRuntimeMaterials()
    {
      _blockMaterial = CreateMaterial("KB Redundant Block", new Color(0.28f, 0.08f, 0.06f, 1f), false);
      _gazedMaterial = CreateMaterial("KB Gazed Block", new Color(0.95f, 0.42f, 0.16f, 1f), false);
      _convertedMaterial = CreateMaterial("KB Data Cluster", new Color(0.28f, 0.86f, 0.48f, 0.48f), true);
    }

    private Material CreateMaterial(string materialName, Color color, bool transparent)
    {
      var shader = Shader.Find("Universal Render Pipeline/Lit");
      if (shader == null)
      {
        shader = Shader.Find("Standard");
      }

      if (shader == null)
      {
        shader = Shader.Find("Unlit/Color");
      }

      var material = new Material(shader)
      {
        name = materialName,
        color = color,
      };

      material.EnableKeyword("_EMISSION");
      material.SetColor("_EmissionColor", color * 0.15f);

      if (transparent)
      {
        ConfigureTransparentMaterial(material);
      }

      return material;
    }

    private void ConfigureTransparentMaterial(Material material)
    {
      material.SetFloat("_Surface", 1f);
      material.SetFloat("_Mode", 3f);
      material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
      material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
      material.SetFloat("_ZWrite", 0f);
      material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
      material.EnableKeyword("_ALPHABLEND_ON");
      material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void EnsureHudStyle()
    {
      if (_hudStyle != null)
      {
        return;
      }

      _hudStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 18,
        normal = { textColor = new Color(0.66f, 1f, 0.78f) },
      };
    }

    private class DataBlock : MonoBehaviour
    {
      private Renderer _renderer;
      private Material _runtimeMaterial;
      private Material _normalMaterial;
      private Material _gazedMaterial;
      private Vector3 _driftStart;
      private Vector3 _driftEnd;
      private float _gazeColorLerpSpeed;
      private float _driftSpeed;
      private bool _isGazed;
      private bool _isConverted;
      private Coroutine _conversionRoutine;

      public bool IsConverted => _isConverted;

      public void Initialize(
        Vector3 driftStart,
        Vector3 driftEnd,
        Material normalMaterial,
        Material gazedMaterial,
        float gazeColorLerpSpeed,
        float driftSpeed)
      {
        _renderer = GetComponent<Renderer>();
        _runtimeMaterial = new Material(normalMaterial);
        _normalMaterial = normalMaterial;
        _gazedMaterial = gazedMaterial;
        _gazeColorLerpSpeed = gazeColorLerpSpeed;
        _driftSpeed = driftSpeed;
        _driftStart = driftStart;
        _driftEnd = driftEnd;
        _renderer.material = _runtimeMaterial;
      }

      public void SetGazed(bool isGazed)
      {
        if (_isConverted)
        {
          return;
        }

        _isGazed = isGazed;
      }

      public void ConvertToDataCluster(Material convertedMaterial, float durationSeconds, float scaleRatio)
      {
        if (_isConverted)
        {
          return;
        }

        _isConverted = true;
        _isGazed = false;

        if (_conversionRoutine != null)
        {
          StopCoroutine(_conversionRoutine);
        }

        _conversionRoutine = StartCoroutine(ConvertRoutine(convertedMaterial, durationSeconds, scaleRatio));
      }

      private void Update()
      {
        if (!_isConverted)
        {
          var t = (Mathf.Sin(Time.time * _driftSpeed) + 1f) * 0.5f;
          transform.position = Vector3.Lerp(_driftStart, _driftEnd, t);
        }

        var targetColor = _isGazed ? _gazedMaterial.color : _normalMaterial.color;
        _runtimeMaterial.color = Color.Lerp(_runtimeMaterial.color, targetColor, Time.deltaTime * _gazeColorLerpSpeed);
        _runtimeMaterial.SetColor("_EmissionColor", _runtimeMaterial.color * (_isGazed ? 0.25f : 0.12f));
      }

      private IEnumerator ConvertRoutine(Material convertedMaterial, float durationSeconds, float scaleRatio)
      {
        var startScale = transform.localScale;
        var targetScale = startScale * scaleRatio;
        var startColor = _runtimeMaterial.color;
        var targetColor = convertedMaterial.color;
        var collider = GetComponent<Collider>();

        if (collider != null)
        {
          collider.enabled = false;
        }

        gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

        var elapsed = 0f;
        while (elapsed < durationSeconds)
        {
          elapsed += Time.deltaTime;
          var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / durationSeconds));
          transform.localScale = Vector3.Lerp(startScale, targetScale, t);
          _runtimeMaterial.color = Color.Lerp(startColor, targetColor, t);
          _runtimeMaterial.SetColor("_EmissionColor", _runtimeMaterial.color * 0.18f);
          yield return null;
        }

        transform.localScale = targetScale;
        _runtimeMaterial.color = targetColor;
        _runtimeMaterial.SetColor("_EmissionColor", targetColor * 0.18f);
      }
    }
  }
}
