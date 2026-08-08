using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum CareRhythmPetal
  {
    Focus,
    Blink,
    Distance,
    Rest,
  }

  public sealed class CareRhythmController : MonoBehaviour
  {
    [SerializeField, Min(0.1f)] private float carePulsePresentationSeconds = 0.5f;

    private EdgeOrbitHarvestMvp _gameplay;
    private CareRhythmView _view;
    private bool _focusLit;
    private bool _blinkLit;
    private bool _distanceLit;
    private bool _restLit;
    private bool _pulsePending;
    private float _pulseStartedAt;

    public static CareRhythmController Instance { get; private set; }
    public bool FocusLit => _focusLit;
    public bool BlinkLit => _blinkLit;
    public bool DistanceLit => _distanceLit;
    public bool RestLit => _restLit;

    public static CareRhythmController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null) Instance = FindFirstObjectByType<CareRhythmController>();
      if (Instance == null)
      {
        var owner = new GameObject("Care Rhythm Controller");
        Instance = owner.AddComponent<CareRhythmController>();
      }
      Instance.Bind(gameplay);
      return Instance;
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }
      Instance = this;
      _view = gameObject.AddComponent<CareRhythmView>();
    }

    private void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      if (_gameplay == gameplay) return;
      Unsubscribe();
      _gameplay = gameplay;
      if (_gameplay == null) return;
      _gameplay.SoftFocusBatchCompleted += HandleSoftFocusBatch;
      _gameplay.SoftBlinkPerformed += HandleNaturalBlink;
      _gameplay.PushAwayReturnedNeutral += HandleDistanceCompleted;
      _gameplay.ReopenReleaseCompleted += HandleFormalRestCompleted;
      _gameplay.FirstLevelModuleInstalled += HandleModuleInstalled;
      ScreenDownRestController.ScreenDownRestCompleted += HandleScreenDownRestCompleted;
      GuidedEyeMovementController.GuidedEyeMovementCompleted += HandleGuidedEyeMovementCompleted;
      _view.SetPetals(_focusLit, _blinkLit, _distanceLit, _restLit);
    }

    private void Update()
    {
      _view?.SetVisible(_gameplay != null &&
                        !_gameplay.IsFirstLevelBossMode &&
                        !_gameplay.IsModuleUpgradeOpen &&
                        !_gameplay.IsGuidedEyeMovementActive &&
                        !_gameplay.IsScreenDownRestActive &&
                        !_gameplay.IsCalibrationActive);
      if (!_pulsePending || Time.unscaledTime - _pulseStartedAt < Mathf.Max(0.1f, carePulsePresentationSeconds)) return;
      _pulsePending = false;
      var upgrades = CareUpgradeController.Instance;
      var sampleCount = upgrades != null ? upgrades.GetCarePulseSampleCount() : 1;
      upgrades?.ApplyCarePulseEffects();
      if (_gameplay != null && FirstLevelCareFlowController.Instance != null && !_gameplay.IsFirstLevelBossMode)
      {
        CareExperienceRewardEmitter.EnsureExists(_gameplay)
          .EnqueueFragments(sampleCount, true, CareMovementDirection.Center, 1f);
      }
      else
      {
        _gameplay?.SpawnCareRewardSamples(sampleCount, true);
      }
      _focusLit = false;
      _blinkLit = false;
      _distanceLit = false;
      _restLit = false;
      _view.SetPetals(false, false, false, false);
      Debug.Log($"CARE PULSE completed through the formal sample-flight pipeline. Samples={sampleCount}.");
    }

    private void HandleSoftFocusBatch(int convertedCount)
    {
      if (convertedCount > 0 && IsValidCareSignal()) Light(CareRhythmPetal.Focus);
    }

    private void HandleNaturalBlink(int serial)
    {
      if (IsValidCareSignal() && _gameplay != null && !_gameplay.IsCareInteractionActive) Light(CareRhythmPetal.Blink);
    }

    private void HandleDistanceCompleted()
    {
      if (IsValidCareSignal()) Light(CareRhythmPetal.Distance);
    }

    private void HandleFormalRestCompleted(int convertedCount)
    {
      if (convertedCount > 0 && IsValidCareSignal()) Light(CareRhythmPetal.Rest);
    }

    private void HandleScreenDownRestCompleted()
    {
      if (IsValidCareSignal()) Light(CareRhythmPetal.Rest);
    }

    private void HandleGuidedEyeMovementCompleted()
    {
      if (IsValidCareSignal()) Light(CareRhythmPetal.Rest);
    }

    private void HandleModuleInstalled(FirstLevelModuleId moduleId)
    {
      var definition = FirstLevelUpgradeCatalog.Get(moduleId);
      _view?.FlashCategory(definition.Category);
    }

    private bool IsValidCareSignal()
    {
      return _gameplay != null &&
             !_gameplay.IsTutorialModeEnabled &&
             !_gameplay.IsFirstLevelBossMode &&
             !_gameplay.IsModuleUpgradeOpen &&
             _gameplay.IsTrackingAvailable;
    }

    private void Light(CareRhythmPetal petal)
    {
      if (_pulsePending) return;
      switch (petal)
      {
        case CareRhythmPetal.Focus:
          if (_focusLit) return;
          _focusLit = true;
          break;
        case CareRhythmPetal.Blink:
          if (_blinkLit) return;
          _blinkLit = true;
          break;
        case CareRhythmPetal.Distance:
          if (_distanceLit) return;
          _distanceLit = true;
          break;
        case CareRhythmPetal.Rest:
          if (_restLit) return;
          _restLit = true;
          break;
      }
      _view?.SetPetals(_focusLit, _blinkLit, _distanceLit, _restLit);
      _view?.FlashPetal(petal);
      if (_focusLit && _blinkLit && _distanceLit && _restLit)
      {
        _pulsePending = true;
        _pulseStartedAt = Time.unscaledTime;
        _view?.PlayCarePulse();
      }
    }

    private void Unsubscribe()
    {
      if (_gameplay != null)
      {
        _gameplay.SoftFocusBatchCompleted -= HandleSoftFocusBatch;
        _gameplay.SoftBlinkPerformed -= HandleNaturalBlink;
        _gameplay.PushAwayReturnedNeutral -= HandleDistanceCompleted;
        _gameplay.ReopenReleaseCompleted -= HandleFormalRestCompleted;
        _gameplay.FirstLevelModuleInstalled -= HandleModuleInstalled;
      }
      ScreenDownRestController.ScreenDownRestCompleted -= HandleScreenDownRestCompleted;
      GuidedEyeMovementController.GuidedEyeMovementCompleted -= HandleGuidedEyeMovementCompleted;
    }

    private void OnDestroy()
    {
      Unsubscribe();
      if (Instance == this) Instance = null;
    }
  }
}
