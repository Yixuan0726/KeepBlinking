using System;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum CareCircuitSegment
  {
    Move,
    Focus,
    Rest,
    Release,
  }

  public readonly struct CareCircuitSummary
  {
    public CareCircuitSummary(int round, int raw, int focused, int rested, int collectedValue, bool completed)
    {
      Round = round;
      RawCount = raw;
      FocusedCount = focused;
      RestedCount = rested;
      CollectedValue = collectedValue;
      Completed = completed;
    }
    public int Round { get; }
    public int RawCount { get; }
    public int FocusedCount { get; }
    public int RestedCount { get; }
    public int CollectedValue { get; }
    public bool Completed { get; }
  }

  public sealed class CareCircuitController : MonoBehaviour
  {
    [SerializeField, Range(0.1f, 1f)] private float _focusConversionWaveSeconds = 0.55f;
    private EdgeOrbitHarvestMvp _gameplay;
    private CareCircuitView _view;
    private CareUpgradeController _upgrades;
    private int _round;
    private int _farRewardsGranted;
    private int _collectedValue;
    private int _releaseRaw;
    private int _releaseFocused;
    private int _releaseRested;
    private bool _move;
    private bool _focus;
    private bool _rest;
    private bool _release;
    private bool _releasePrepared;
    private bool _invalid;
    private readonly System.Collections.Generic.List<CareCircuitSummary> _history = new System.Collections.Generic.List<CareCircuitSummary>(4);

    public static CareCircuitController Instance { get; private set; }
    public static event Action<CareCircuitSummary> CareCircuitCompleted;

    public bool MoveCompleted => _move;
    public bool FocusCompleted => _focus;
    public bool RestCompleted => _rest;
    public bool ReleaseCompleted => _release;
    public System.Collections.Generic.IReadOnlyList<CareCircuitSummary> History => _history;

    public static CareCircuitController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null) Instance = FindFirstObjectByType<CareCircuitController>();
      if (Instance == null)
      {
        var owner = new GameObject("Care Circuit Controller");
        Instance = owner.AddComponent<CareCircuitController>();
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
      _view = gameObject.AddComponent<CareCircuitView>();
    }

    private void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      if (_gameplay == gameplay) return;
      if (_gameplay != null) _gameplay.CareExperienceReachedBar -= HandleExperienceReached;
      _gameplay = gameplay;
      _upgrades = CareUpgradeController.EnsureExists(gameplay);
      if (_gameplay != null) _gameplay.CareExperienceReachedBar += HandleExperienceReached;
    }

    public void BeginRound(int oneBasedRound)
    {
      _round = Mathf.Clamp(oneBasedRound, 1, 4);
      _farRewardsGranted = 0;
      _collectedValue = 0;
      _releaseRaw = 0;
      _releaseFocused = 0;
      _releaseRested = 0;
      _move = _focus = _rest = _release = false;
      _releasePrepared = false;
      _invalid = false;
      _view?.SetSegments(false, false, false, false);
    }

    public void CompleteMove(bool valid)
    {
      if (!valid || _move) return;
      _move = true;
      _view?.Light(CareCircuitSegment.Move);
    }

    public int CompleteFocus(bool valid)
    {
      if (!valid || _focus || !_move || _gameplay == null) return 0;
      var raw = _gameplay.CountPendingCareExperience(CareExperienceState.Raw);
      var fraction = _upgrades != null ? _upgrades.FocusRawConversionFraction : 0.25f;
      var requested = CareExperienceConversionLogic.ConvertedCount(raw, fraction, true);
      var converted = _gameplay.ConvertPendingCareExperience(CareExperienceState.Raw, CareExperienceState.Focused, requested);
      _focus = true;
      _view?.Light(CareCircuitSegment.Focus);
      _view?.PlayConversion(CareExperienceState.Focused, Mathf.Max(0.1f, _focusConversionWaveSeconds));
      if (_upgrades != null && fraction > 0.25f)
        _gameplay.NotifyCareUpgradeActivated(fraction >= 1f ? FirstLevelModuleId.FocusFullRefine : FirstLevelModuleId.FocusMintShift);
      return converted;
    }

    public void RegisterValidFarPoint()
    {
      if (_invalid || !_move || _upgrades == null || !_upgrades.FocusFarWaveEnabled || _farRewardsGranted >= 2) return;
      _farRewardsGranted++;
      CareExperienceRewardEmitter.EnsureExists(_gameplay)
        .EnqueueFragments(8, CareExperienceState.Focused, CareMovementDirection.Far, 1f);
      _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.FocusFarWave);
    }

    public int CompleteRest(bool valid, int validSeconds)
    {
      if (!valid || _rest || !_focus || _gameplay == null) return 0;
      var focused = _gameplay.CountPendingCareExperience(CareExperienceState.Focused);
      var fraction = _upgrades != null ? _upgrades.RestFocusedConversionFraction : 0.5f;
      var requested = CareExperienceConversionLogic.ConvertedCount(focused, fraction, false);
      var converted = _gameplay.ConvertPendingCareExperience(CareExperienceState.Focused, CareExperienceState.Rested, requested);
      var perSecond = _upgrades != null ? _upgrades.RestXpPerSecond : 1;
      var extra = Mathf.Clamp(validSeconds, 0, 8) * Mathf.Max(1, perSecond);
      if (extra > 0)
        CareExperienceRewardEmitter.EnsureExists(_gameplay).EnqueueRestGold(extra);
      _rest = true;
      _view?.Light(CareCircuitSegment.Rest);
      _view?.PlayConversion(CareExperienceState.Rested, 0.65f);
      if (_upgrades != null)
      {
        if (fraction >= 1f) _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.RestFullRest);
        if (perSecond > 1) _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.RestGoldenRest);
        _upgrades.ApplyQuietReturn();
      }
      return converted + extra;
    }

    public void PrepareReleaseBonuses()
    {
      if (_releasePrepared || _gameplay == null) return;
      _releasePrepared = true;
      var originalValue = _gameplay.GetPendingCareExperienceValue();
      if (_invalid || !_move || !_focus || !_rest)
      {
        _releaseRaw = _gameplay.CountPendingCareExperience(CareExperienceState.Raw);
        _releaseFocused = _gameplay.CountPendingCareExperience(CareExperienceState.Focused);
        _releaseRested = _gameplay.CountPendingCareExperience(CareExperienceState.Rested);
        return;
      }
      if (_upgrades != null && _upgrades.TwinPulseEnabled)
      {
        var bonusRaw = CareExperienceConversionLogic.TwinPulseRawBonus(originalValue);
        for (var i = 0; i < bonusRaw; i++)
        {
          var t = bonusRaw <= 1 ? 0.5f : i / (float)(bonusRaw - 1);
          var id = _gameplay.SpawnPendingCareExperienceFragment(CareExperienceState.Raw, new Vector2(Mathf.Lerp(0.18f, 0.82f, t), 0.66f));
          _gameplay.SetCareExperienceCollectionWave(id, 1);
        }
        if (bonusRaw > 0) _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.ReleaseTwinPulse);
      }
      if (_upgrades != null && _upgrades.ChainPulseEnabled)
      {
        var bonusGold = CareExperienceConversionLogic.ChainPulseGoldBonus(originalValue);
        for (var i = 0; i < bonusGold; i++)
        {
          var t = bonusGold <= 1 ? 0.5f : i / (float)(bonusGold - 1);
          var id = _gameplay.SpawnPendingCareExperienceFragment(CareExperienceState.Rested, new Vector2(Mathf.Lerp(0.24f, 0.76f, t), 0.72f));
          _gameplay.SetCareExperienceCollectionWave(id, 1);
        }
        if (bonusGold > 0) _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.ReleaseChainPulse);
      }
      _releaseRaw = _gameplay.CountPendingCareExperience(CareExperienceState.Raw);
      _releaseFocused = _gameplay.CountPendingCareExperience(CareExperienceState.Focused);
      _releaseRested = _gameplay.CountPendingCareExperience(CareExperienceState.Rested);
    }

    public void CompleteRelease(bool physicalPushAway)
    {
      if (_release || _gameplay == null) return;
      _release = physicalPushAway;
      if (physicalPushAway) _view?.Light(CareCircuitSegment.Release);
      var complete = !_invalid && _move && _focus && _rest && _release;
      if (complete)
      {
        _view?.PlayPulse();
        _upgrades?.ApplyFullRelease();
      }
      if (complete)
      {
        var summary = new CareCircuitSummary(
          _round, _releaseRaw, _releaseFocused, _releaseRested, _collectedValue, true);
        _history.Add(summary);
        CareCircuitCompleted?.Invoke(summary);
      }
    }

    public void InvalidateRound()
    {
      _invalid = true;
    }

    private void HandleExperienceReached(int id, CareExperienceState state, int value)
    {
      _collectedValue += Mathf.Max(0, value);
    }

    private void Update()
    {
      _view?.SetVisible(_gameplay != null && !_gameplay.IsFirstLevelBossMode && !_gameplay.IsModuleUpgradeOpen && !_gameplay.IsCalibrationActive);
    }

    private void OnDestroy()
    {
      if (_gameplay != null) _gameplay.CareExperienceReachedBar -= HandleExperienceReached;
      if (Instance == this) Instance = null;
    }
  }
}
