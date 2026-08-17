using System;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  internal enum CareUpgradePreviewType
  {
    Legacy,
    TwinTrail,
    TripleTrail,
    GoldenStreak,
    MintShift,
    FarWave,
    FullRefine,
    GoldenRest,
    QuietReturn,
    FullRest,
    TwinPulse,
    ChainPulse,
    FullRelease,
    BossShardRain,
    BossMintCore,
    BossCoreEcho,
    BossGoldRelease,
  }

  internal enum CareUpgradeEffectType
  {
    Legacy,
    MoveTrailMultiplier,
    MoveGoldenStreak,
    FocusConversion,
    FocusFarReward,
    RestPerSecond,
    RestQuietReturn,
    RestConversion,
    ReleaseSecondWave,
    ReleaseChain,
    ReleaseFieldExpansion,
    BossMoveTrails,
    BossFocusedDrops,
    BossExtraCore,
    BossGoldRelease,
  }

  internal sealed class CareUpgradeDefinition
  {
    public FirstLevelModuleId Id { get; }
    public string StableId { get; }
    public FirstLevelModuleCategory Category { get; }
    public string CategoryLabel => Category.ToString().ToUpperInvariant();
    public int Tier { get; }
    public string CardName { get; }
    public string ShortDescription { get; }
    public string BeforeLabel { get; }
    public string AfterLabel { get; }
    public FirstLevelModuleId RequiredUpgradeId { get; }
    public FirstLevelModuleId[] IncompatibleUpgradeIds { get; }
    public CareUpgradePreviewType PreviewType { get; }
    public CareUpgradeEffectType EffectType { get; }
    public bool BossOnly { get; }
    public int MaxLevel { get; }
    public bool Legacy { get; }
    public bool QuantityOnly { get; }
    public Color AccentColor { get; }

    // Compatibility aliases used by the existing formal TMP card view.
    public string Title => CardName;
    public string Description => ShortDescription;
    public string BeforeValue => BeforeLabel;
    public string AfterValue => AfterLabel;
    public string Delta => $"{BeforeLabel}  →  {AfterLabel}";
    public int AvailableFromUpgrade => BossOnly ? 4 : Mathf.Clamp(Tier, 1, 3);

    // Every active card is a reward multiplier or visible consequence. None
    // changes the required health action.
    public bool ReducesBlinkRequirement => false;
    public bool ExtendsNoBlinkTime => false;
    public bool ReducesDirectionActions => false;
    public bool ReducesFocusShiftCycles => false;
    public bool ShortensRest => false;
    public bool AllowsCloserDistance => false;
    public bool AutomaticallyCompletesAction => false;
    public bool RewardsRapidBlinking => false;
    public bool SkipsPushAway => false;
    public bool DirectlyGrantsExperience => false;
    public bool AllowsBossEarlyReopen => false;
    public bool ExtraExperienceUsesFormalFlight => true;
    public bool SkipCanRewardRest => false;
    public bool TrackingLossCanReward => false;
    public bool RewardCanTriggerMoreThanOnce => false;
    public bool RewardsCompletedCareAction => !Legacy;
    public bool CreatesVisibleDifference => !Legacy;

    public CareUpgradeDefinition(
      FirstLevelModuleId id,
      string stableId,
      FirstLevelModuleCategory category,
      int tier,
      string cardName,
      string shortDescription,
      string beforeLabel,
      string afterLabel,
      FirstLevelModuleId requiredUpgradeId,
      CareUpgradePreviewType previewType,
      CareUpgradeEffectType effectType,
      bool bossOnly,
      bool quantityOnly,
      Color accentColor,
      bool legacy = false,
      FirstLevelModuleId[] incompatibleUpgradeIds = null,
      int maxLevel = 1)
    {
      Id = id;
      StableId = stableId ?? id.ToString();
      Category = category;
      Tier = Mathf.Max(0, tier);
      CardName = cardName ?? string.Empty;
      ShortDescription = shortDescription ?? string.Empty;
      BeforeLabel = beforeLabel ?? string.Empty;
      AfterLabel = afterLabel ?? string.Empty;
      RequiredUpgradeId = requiredUpgradeId;
      PreviewType = previewType;
      EffectType = effectType;
      BossOnly = bossOnly;
      QuantityOnly = quantityOnly;
      AccentColor = accentColor;
      Legacy = legacy;
      IncompatibleUpgradeIds = incompatibleUpgradeIds ?? Array.Empty<FirstLevelModuleId>();
      MaxLevel = Mathf.Max(1, maxLevel);
    }

    public bool PassesHealthInvariantAudit()
    {
      return !Legacy &&
             !ReducesBlinkRequirement &&
             !ExtendsNoBlinkTime &&
             !ReducesDirectionActions &&
             !ReducesFocusShiftCycles &&
             !ShortensRest &&
             !AllowsCloserDistance &&
             !AutomaticallyCompletesAction &&
             !RewardsRapidBlinking &&
             !SkipsPushAway &&
             !DirectlyGrantsExperience &&
             !AllowsBossEarlyReopen &&
             ExtraExperienceUsesFormalFlight &&
             !SkipCanRewardRest &&
             !TrackingLossCanReward &&
             !RewardCanTriggerMoreThanOnce &&
             RewardsCompletedCareAction &&
             CreatesVisibleDifference;
    }
  }
}
