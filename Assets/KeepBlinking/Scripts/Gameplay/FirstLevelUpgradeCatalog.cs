using System;
using System.Collections.Generic;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum FirstLevelModuleId
  {
    None,
    ChainBlink,
    WideBlink,
    LockHold,
    WideChain,
    QuietWake,
    QuietField,
    CoreEcho,
    DeepRecovery,
    BonusSample,
    XpDiscount,
    XpReserve,
    LoopBonus,
    WakeEcho,
    RestCache,
    PreciseHarvest,
    FullLoop,

    // CARE RHYTHM modules. Legacy ids above remain stable for compatibility
    // with reports and scene references, but are no longer offered.
    WiderField,
    MoreTargets,
    LookAwayHold,
    BlinkBloom,
    TearWave,
    QuietBlink,
    ExtraSamples,
    ReturnBloom,
    ShiftReward,
    RestBloom,
    QuietReturn,
    RestSample,
    DoublePulse,
    FieldPulse,
    FullRecovery,

    // CARE CIRCUIT. Values above are retained for serialized compatibility and
    // are treated as Legacy by the first-level catalog.
    MoveTwinTrail,
    MoveTripleTrail,
    MoveGoldenStreak,
    FocusMintShift,
    FocusFarWave,
    FocusFullRefine,
    RestGoldenRest,
    RestCircuitQuietReturn,
    RestFullRest,
    ReleaseTwinPulse,
    ReleaseChainPulse,
    ReleaseFullRelease,
    BossShardRain,
    BossMintCore,
    BossCoreEcho,
    BossGoldRelease,
  }

  public enum FirstLevelModuleCategory
  {
    Focus,
    Blink,
    Rest,
    Distance,
    Rhythm,
    Combo,
    Move,
    Release,
  }

  [Flags]
  internal enum FirstLevelCategoryMask
  {
    None = 0,
    Blink = 1,
    Rest = 2,
    Distance = 4,
  }

  // Compatibility facade retained for existing first-level callers.
  internal static class FirstLevelUpgradeCatalog
  {
    internal static CareUpgradeDefinition[] Definitions => CareUpgradeController.Definitions;

    internal static CareUpgradeDefinition Get(FirstLevelModuleId id) => CareUpgradeController.Get(id);

    internal static List<FirstLevelModuleId> BuildOffer(int upgradeNumber, HashSet<FirstLevelModuleId> installed)
    {
      return CareUpgradeController.BuildOffer(upgradeNumber, installed);
    }
  }
}
