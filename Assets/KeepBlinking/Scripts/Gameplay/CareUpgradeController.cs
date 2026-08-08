using System;
using System.Collections.Generic;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public sealed class CareUpgradeController : MonoBehaviour
  {
    private static readonly Color RestColor = new Color32(0x91, 0xB8, 0xD0, 0xFF);
    private static readonly Color BlinkColor = new Color32(0x8B, 0xD7, 0xCF, 0xFF);

    internal static readonly CareUpgradeDefinition[] Definitions =
    {
      new CareUpgradeDefinition(FirstLevelModuleId.WiderField, FirstLevelModuleCategory.Focus, "WIDER FIELD", "Covers more screen.", "55%", "75%", KeepBlinkingTheme.AccentPrimary, 1),
      new CareUpgradeDefinition(FirstLevelModuleId.MoreTargets, FirstLevelModuleCategory.Focus, "MORE TARGETS", "Cleanses four at once.", "2", "4", KeepBlinkingTheme.AccentPrimary, 2),
      new CareUpgradeDefinition(FirstLevelModuleId.LookAwayHold, FirstLevelModuleCategory.Focus, "LOOK-AWAY HOLD", "Look away. Progress stays.", "1s", "4s", KeepBlinkingTheme.AccentPrimary, 3),

      new CareUpgradeDefinition(FirstLevelModuleId.BlinkBloom, FirstLevelModuleCategory.Blink, "BLINK BLOOM", "Blink expands the field.", "100%", "135% · 6s", BlinkColor, 1),
      new CareUpgradeDefinition(FirstLevelModuleId.TearWave, FirstLevelModuleCategory.Blink, "TEAR WAVE", "Blink boosts field progress.", "+0%", "+25%", BlinkColor, 2),
      new CareUpgradeDefinition(FirstLevelModuleId.QuietBlink, FirstLevelModuleCategory.Blink, "QUIET BLINK", "Blink pauses new spawns.", "0s", "4s", BlinkColor, 3),

      new CareUpgradeDefinition(FirstLevelModuleId.ExtraSamples, FirstLevelModuleCategory.Distance, "EXTRA SAMPLES", "Full push earns two more.", "+0", "+2", KeepBlinkingTheme.AccentWarm, 1),
      new CareUpgradeDefinition(FirstLevelModuleId.ReturnBloom, FirstLevelModuleCategory.Distance, "RETURN BLOOM", "Return neutral. Field expands.", "100%", "140% · 10s", KeepBlinkingTheme.AccentWarm, 2),

      new CareUpgradeDefinition(FirstLevelModuleId.RestBloom, FirstLevelModuleCategory.Rest, "REST BLOOM", "Full rest expands the field.", "100%", "150% · 12s", RestColor, 3),
      new CareUpgradeDefinition(FirstLevelModuleId.QuietReturn, FirstLevelModuleCategory.Rest, "QUIET RETURN", "Rest pauses new spawns.", "0s", "6s", RestColor, 3),
      new CareUpgradeDefinition(FirstLevelModuleId.RestSample, FirstLevelModuleCategory.Rest, "REST SAMPLE", "Full rest drops gold.", "+0", "+1", RestColor, 3),

      new CareUpgradeDefinition(FirstLevelModuleId.DoublePulse, FirstLevelModuleCategory.Rhythm, "DOUBLE PULSE", "Full cycle drops two.", "1", "2", KeepBlinkingTheme.TextPrimary, 4),
      new CareUpgradeDefinition(FirstLevelModuleId.FieldPulse, FirstLevelModuleCategory.Rhythm, "FIELD PULSE", "Pulse expands the field.", "100%", "160% · 12s", KeepBlinkingTheme.TextPrimary, 4),
      new CareUpgradeDefinition(FirstLevelModuleId.FullRecovery, FirstLevelModuleCategory.Rhythm, "FULL RECOVERY", "Pulse clears all dryness.", "Dry", "Fresh", KeepBlinkingTheme.TextPrimary, 4),
    };

    [SerializeField, Min(10f)] private float _naturalBlinkUpgradeCooldownSeconds = 10f;

    private EdgeOrbitHarvestMvp _gameplay;
    private float _nextNaturalBlinkUpgradeAt = -1f;

    public static CareUpgradeController Instance { get; private set; }

    public static CareUpgradeController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null)
      {
        Instance = FindFirstObjectByType<CareUpgradeController>();
      }
      if (Instance == null)
      {
        var owner = new GameObject("Care Upgrade Controller");
        Instance = owner.AddComponent<CareUpgradeController>();
      }
      Instance.Bind(gameplay);
      return Instance;
    }

    internal static CareUpgradeDefinition Get(FirstLevelModuleId id)
    {
      for (var i = 0; i < Definitions.Length; i++)
      {
        if (Definitions[i].Id == id) return Definitions[i];
      }
      return Definitions[0];
    }

    internal static List<FirstLevelModuleId> BuildOffer(int upgradeNumber, HashSet<FirstLevelModuleId> installed)
    {
      var offer = new List<FirstLevelModuleId>(3);
      AddPreferred(offer, BuildPreferredOffer(upgradeNumber, installed), installed, upgradeNumber);

      for (var i = 0; i < Definitions.Length && offer.Count < 3; i++)
      {
        var definition = Definitions[i];
        if (!installed.Contains(definition.Id) &&
            definition.AvailableFromUpgrade <= upgradeNumber &&
            definition.PassesHealthInvariantAudit() &&
            !offer.Contains(definition.Id))
        {
          offer.Add(definition.Id);
        }
      }

      EnsureCategoryVariety(offer, installed, upgradeNumber);
      return offer;
    }

    private static FirstLevelModuleId[] BuildPreferredOffer(int upgradeNumber, HashSet<FirstLevelModuleId> installed)
    {
      var variant = GetStableOfferVariant(upgradeNumber, installed);
      switch (Mathf.Clamp(upgradeNumber, 1, 4))
      {
        case 1:
          return new[] { FirstLevelModuleId.WiderField, FirstLevelModuleId.BlinkBloom, FirstLevelModuleId.ExtraSamples };
        case 2:
          return new[]
          {
            FirstLevelModuleId.MoreTargets,
            FirstLevelModuleId.TearWave,
            FirstLevelModuleId.ReturnBloom,
          };
        case 3:
          var restCards = new[] { FirstLevelModuleId.RestBloom, FirstLevelModuleId.QuietReturn, FirstLevelModuleId.RestSample };
          return new[]
          {
            FirstLevelModuleId.LookAwayHold,
            restCards[variant % restCards.Length],
            FirstLevelModuleId.QuietBlink,
          };
        default:
          var rhythmCards = new[] { FirstLevelModuleId.DoublePulse, FirstLevelModuleId.FieldPulse, FirstLevelModuleId.FullRecovery };
          var firstRhythm = variant % rhythmCards.Length;
          return new[]
          {
            rhythmCards[firstRhythm],
            rhythmCards[(firstRhythm + 1) % rhythmCards.Length],
            variant % 2 == 0 ? FirstLevelModuleId.RestSample : FirstLevelModuleId.ExtraSamples,
          };
      }
    }

    private static int GetStableOfferVariant(int upgradeNumber, HashSet<FirstLevelModuleId> installed)
    {
      unchecked
      {
        var value = upgradeNumber * 31;
        foreach (var id in installed) value += (int)id * 17;
        return Mathf.Abs(value == int.MinValue ? 0 : value);
      }
    }

    public int GetCarePulseSampleCount()
    {
      return _gameplay != null && _gameplay.HasFirstLevelModule(FirstLevelModuleId.DoublePulse) ? 2 : 1;
    }

    public void ApplyCarePulseEffects()
    {
      if (_gameplay == null) return;
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.FieldPulse))
      {
        SoftFocusFieldController.Instance?.GrantTemporaryExpansion(1.60f, 12f);
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.FieldPulse);
      }
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.FullRecovery))
      {
        SoftFocusFieldController.Instance?.ClearCurrentDryness();
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.FullRecovery);
      }
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.DoublePulse))
      {
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.DoublePulse);
      }
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }
      Instance = this;
    }

    private void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      if (_gameplay == gameplay) return;
      Unsubscribe();
      _gameplay = gameplay;
      if (_gameplay == null) return;
      _gameplay.SoftBlinkPerformed += HandleNaturalBlink;
      _gameplay.PushAwayTriggered += HandlePushAwayTriggered;
      _gameplay.PushAwayReturnedNeutral += HandlePushAwayReturnedNeutral;
      _gameplay.ReopenReleaseCompleted += HandleFormalRestCompleted;
      ScreenDownRestController.ScreenDownRestCompleted += HandleScreenDownRestCompleted;
      GuidedEyeMovementController.GuidedEyeMovementCompleted += HandleGuidedEyeMovementCompleted;
    }

    private void Unsubscribe()
    {
      if (_gameplay != null)
      {
        _gameplay.SoftBlinkPerformed -= HandleNaturalBlink;
        _gameplay.PushAwayTriggered -= HandlePushAwayTriggered;
        _gameplay.PushAwayReturnedNeutral -= HandlePushAwayReturnedNeutral;
        _gameplay.ReopenReleaseCompleted -= HandleFormalRestCompleted;
      }
      ScreenDownRestController.ScreenDownRestCompleted -= HandleScreenDownRestCompleted;
      GuidedEyeMovementController.GuidedEyeMovementCompleted -= HandleGuidedEyeMovementCompleted;
    }

    private void OnDestroy()
    {
      Unsubscribe();
      if (Instance == this) Instance = null;
    }

    private void HandleNaturalBlink(int serial)
    {
      if (_gameplay == null ||
          !_gameplay.IsTrackingAvailable ||
          !_gameplay.IsSoftFocusNormalGameplayActive ||
          Time.unscaledTime < _nextNaturalBlinkUpgradeAt)
      {
        return;
      }
      var activated = false;
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.BlinkBloom))
      {
        var field = SoftFocusFieldController.EnsureExists(_gameplay);
        if (field != null)
        {
          field.GrantTemporaryExpansion(1.35f, 6f);
          _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.BlinkBloom);
          activated = true;
        }
      }
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.TearWave))
      {
        _gameplay.AdvanceActiveSoftFocusTargets(0.25f);
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.TearWave);
        activated = true;
      }
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.QuietBlink))
      {
        _gameplay.PauseNormalSpawns(4f);
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.QuietBlink);
        activated = true;
      }
      if (activated)
      {
        _nextNaturalBlinkUpgradeAt = Time.unscaledTime + Mathf.Max(10f, _naturalBlinkUpgradeCooldownSeconds);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log(
          $"KeepBlinking blink upgrade activated by completed natural blink {serial}. " +
          $"Cooldown={Mathf.Max(10f, _naturalBlinkUpgradeCooldownSeconds):F1}s.",
          this);
#endif
      }
    }

    private void HandlePushAwayReturnedNeutral()
    {
      if (_gameplay == null) return;
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.ReturnBloom))
      {
        SoftFocusFieldController.Instance?.GrantTemporaryExpansion(1.40f, 10f);
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.ReturnBloom);
      }
    }

    public int GetPendingPushAwayBonusSampleCount()
    {
      return _gameplay != null && _gameplay.HasFirstLevelModule(FirstLevelModuleId.ExtraSamples) ? 2 : 0;
    }

    private void HandlePushAwayTriggered()
    {
      if (_gameplay == null || !_gameplay.HasFirstLevelModule(FirstLevelModuleId.ExtraSamples)) return;
      // Push collection starts synchronously after this signal. Emit immediately
      // into the real Converted pool so both bonus samples join the same batch.
      _gameplay.SpawnPendingCareExperienceFragment(false, new Vector2(0.42f, 0.58f));
      _gameplay.SpawnPendingCareExperienceFragment(false, new Vector2(0.58f, 0.58f));
      _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.ExtraSamples);
    }

    private void HandleScreenDownRestCompleted()
    {
      ApplyCompletedCareFlowRestEffects();
    }

    private void HandleGuidedEyeMovementCompleted()
    {
      ApplyCompletedCareFlowRestEffects();
    }

    private void ApplyCompletedCareFlowRestEffects()
    {
      if (_gameplay == null) return;
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.RestBloom))
      {
        SoftFocusFieldController.Instance?.GrantTemporaryExpansion(1.50f, 12f);
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.RestBloom);
      }
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.QuietReturn))
      {
        _gameplay.PauseNormalSpawns(6f);
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.QuietReturn);
      }
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.RestSample))
      {
        CareExperienceRewardEmitter.EnsureExists(_gameplay)
          .EnqueueFragments(1, true, CareMovementDirection.Center, 1f);
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.RestSample);
      }
    }

    private void HandleFormalRestCompleted(int convertedCount)
    {
      if (_gameplay == null || convertedCount <= 0) return;
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.RestBloom))
      {
        SoftFocusFieldController.Instance?.GrantTemporaryExpansion(1.50f, 12f);
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.RestBloom);
      }
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.QuietReturn))
      {
        _gameplay.PauseNormalSpawns(6f);
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.QuietReturn);
      }
      if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.RestSample))
      {
        _gameplay.SpawnCareRewardSamples(1, true);
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.RestSample);
      }
    }

    private static void AddPreferred(List<FirstLevelModuleId> offer, FirstLevelModuleId[] preferred, HashSet<FirstLevelModuleId> installed, int upgradeNumber)
    {
      for (var i = 0; i < preferred.Length && offer.Count < 3; i++)
      {
        var definition = Get(preferred[i]);
        if (!installed.Contains(definition.Id) && definition.AvailableFromUpgrade <= upgradeNumber && definition.PassesHealthInvariantAudit())
        {
          offer.Add(definition.Id);
        }
      }
    }

    private static void EnsureCategoryVariety(List<FirstLevelModuleId> offer, HashSet<FirstLevelModuleId> installed, int upgradeNumber)
    {
      if (offer.Count < 3) return;
      var firstCategory = Get(offer[0]).Category;
      var hasDifferent = false;
      for (var i = 1; i < offer.Count; i++) hasDifferent |= Get(offer[i]).Category != firstCategory;
      if (hasDifferent) return;

      for (var i = 0; i < Definitions.Length; i++)
      {
        var candidate = Definitions[i];
        if (candidate.Category != firstCategory && candidate.AvailableFromUpgrade <= upgradeNumber && !installed.Contains(candidate.Id))
        {
          offer[offer.Count - 1] = candidate.Id;
          return;
        }
      }
    }
  }
}
