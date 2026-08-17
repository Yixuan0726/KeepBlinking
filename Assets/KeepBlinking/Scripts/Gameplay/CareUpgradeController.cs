using System;
using System.Collections.Generic;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public sealed class CareUpgradeController : MonoBehaviour
  {
    private static readonly Color RawColor = KeepBlinkingTheme.TextPrimary;
    private static readonly Color FocusColor = KeepBlinkingTheme.AccentPrimary;
    private static readonly Color RestColor = KeepBlinkingTheme.AccentWarm;
    private static readonly Color ReleaseColor = new Color32(0xA8, 0xD8, 0xC9, 0xFF);

    internal static readonly CareUpgradeDefinition[] Definitions =
    {
      Define(FirstLevelModuleId.MoveTwinTrail, "move_twin_trail", FirstLevelModuleCategory.Move, 1, "TWIN TRAIL", "Move drops two XP trails.", "1", "2", FirstLevelModuleId.None, CareUpgradePreviewType.TwinTrail, CareUpgradeEffectType.MoveTrailMultiplier, false, RawColor),
      Define(FirstLevelModuleId.MoveTripleTrail, "move_triple_trail", FirstLevelModuleCategory.Move, 2, "TRIPLE TRAIL", "Move drops three XP trails.", "2", "3", FirstLevelModuleId.MoveTwinTrail, CareUpgradePreviewType.TripleTrail, CareUpgradeEffectType.MoveTrailMultiplier, false, RawColor),
      Define(FirstLevelModuleId.MoveGoldenStreak, "move_golden_streak", FirstLevelModuleCategory.Move, 3, "GOLDEN STREAK", "Six move nodes drop Gold XP.", "0", "1", FirstLevelModuleId.MoveTripleTrail, CareUpgradePreviewType.GoldenStreak, CareUpgradeEffectType.MoveGoldenStreak, false, RestColor),

      Define(FirstLevelModuleId.FocusMintShift, "focus_mint_shift", FirstLevelModuleCategory.Focus, 1, "MINT SHIFT", "Focus turns half Raw XP mint.", "25%", "50%", FirstLevelModuleId.None, CareUpgradePreviewType.MintShift, CareUpgradeEffectType.FocusConversion, false, FocusColor),
      Define(FirstLevelModuleId.FocusFarWave, "focus_far_wave", FirstLevelModuleCategory.Focus, 2, "FAR WAVE", "Each Far point drops eight Mint XP.", "0", "8", FirstLevelModuleId.FocusMintShift, CareUpgradePreviewType.FarWave, CareUpgradeEffectType.FocusFarReward, false, FocusColor),
      Define(FirstLevelModuleId.FocusFullRefine, "focus_full_refine", FirstLevelModuleCategory.Focus, 3, "FULL REFINE", "Two cycles turn all XP mint.", "50%", "ALL", FirstLevelModuleId.FocusFarWave, CareUpgradePreviewType.FullRefine, CareUpgradeEffectType.FocusConversion, false, FocusColor),

      Define(FirstLevelModuleId.RestGoldenRest, "rest_golden_rest", FirstLevelModuleCategory.Rest, 1, "GOLDEN REST", "Rest drops two Gold XP each second.", "1", "2", FirstLevelModuleId.None, CareUpgradePreviewType.GoldenRest, CareUpgradeEffectType.RestPerSecond, true, RestColor),
      Define(FirstLevelModuleId.RestCircuitQuietReturn, "rest_quiet_return", FirstLevelModuleCategory.Rest, 2, "QUIET RETURN", "Reopen pauses spawns for six seconds.", "0s", "6s", FirstLevelModuleId.RestGoldenRest, CareUpgradePreviewType.QuietReturn, CareUpgradeEffectType.RestQuietReturn, false, RestColor),
      Define(FirstLevelModuleId.RestFullRest, "rest_full_rest", FirstLevelModuleCategory.Rest, 3, "FULL REST", "Full rest turns all Mint XP gold.", "50%", "ALL", FirstLevelModuleId.RestCircuitQuietReturn, CareUpgradePreviewType.FullRest, CareUpgradeEffectType.RestConversion, false, RestColor),

      Define(FirstLevelModuleId.ReleaseTwinPulse, "release_twin_pulse", FirstLevelModuleCategory.Release, 1, "TWIN PULSE", "Push sends a bonus second wave.", "1", "2", FirstLevelModuleId.None, CareUpgradePreviewType.TwinPulse, CareUpgradeEffectType.ReleaseSecondWave, false, ReleaseColor),
      Define(FirstLevelModuleId.ReleaseChainPulse, "release_chain_pulse", FirstLevelModuleCategory.Release, 2, "CHAIN PULSE", "Ten collected XP drop one Gold XP.", "0", "1 / 10", FirstLevelModuleId.ReleaseTwinPulse, CareUpgradePreviewType.ChainPulse, CareUpgradeEffectType.ReleaseChain, false, ReleaseColor),
      Define(FirstLevelModuleId.ReleaseFullRelease, "release_full_release", FirstLevelModuleCategory.Release, 3, "FULL RELEASE", "Final pulse expands the Focus Field.", "55%", "80% · 12s", FirstLevelModuleId.ReleaseChainPulse, CareUpgradePreviewType.FullRelease, CareUpgradeEffectType.ReleaseFieldExpansion, false, ReleaseColor),

      DefineBoss(FirstLevelModuleId.BossShardRain, "boss_shard_rain", FirstLevelModuleCategory.Move, "SHARD RAIN", "Boss hits drop two XP trails.", "1", "2", CareUpgradePreviewType.BossShardRain, CareUpgradeEffectType.BossMoveTrails, RawColor),
      DefineBoss(FirstLevelModuleId.BossMintCore, "boss_mint_core", FirstLevelModuleCategory.Focus, "MINT CORE", "Boss drops become Focused XP.", "PART", "ALL", CareUpgradePreviewType.BossMintCore, CareUpgradeEffectType.BossFocusedDrops, FocusColor),
      DefineBoss(FirstLevelModuleId.BossCoreEcho, "boss_core_echo", FirstLevelModuleCategory.Rest, "CORE ECHO", "Correct rest breaks two cores.", "1", "2", CareUpgradePreviewType.BossCoreEcho, CareUpgradeEffectType.BossExtraCore, RestColor),
      DefineBoss(FirstLevelModuleId.BossGoldRelease, "boss_gold_release", FirstLevelModuleCategory.Release, "GOLD RELEASE", "Final Push drops eight Gold XP.", "0", "8", CareUpgradePreviewType.BossGoldRelease, CareUpgradeEffectType.BossGoldRelease, ReleaseColor),
    };

    private static readonly Dictionary<FirstLevelModuleId, CareUpgradeDefinition> LegacyCache =
      new Dictionary<FirstLevelModuleId, CareUpgradeDefinition>();

    private EdgeOrbitHarvestMvp _gameplay;
    private bool _quietReturnPending;
    public static CareUpgradeController Instance { get; private set; }

    public static CareUpgradeController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null) Instance = FindFirstObjectByType<CareUpgradeController>();
      if (Instance == null)
      {
        var owner = new GameObject("Care Upgrade Controller");
        Instance = owner.AddComponent<CareUpgradeController>();
      }
      Instance._gameplay = gameplay;
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
    }

    private void OnDestroy()
    {
      if (Instance == this) Instance = null;
    }

    internal static CareUpgradeDefinition Get(FirstLevelModuleId id)
    {
      for (var i = 0; i < Definitions.Length; i++)
      {
        if (Definitions[i].Id == id) return Definitions[i];
      }
      if (!LegacyCache.TryGetValue(id, out var legacy))
      {
        legacy = new CareUpgradeDefinition(
          id,
          $"legacy_{id.ToString().ToLowerInvariant()}",
          FirstLevelModuleCategory.Combo,
          0,
          SplitEnumName(id.ToString()).ToUpperInvariant(),
          "Legacy module.",
          "—",
          "—",
          FirstLevelModuleId.None,
          CareUpgradePreviewType.Legacy,
          CareUpgradeEffectType.Legacy,
          false,
          false,
          KeepBlinkingTheme.TextMuted,
          true);
        LegacyCache[id] = legacy;
      }
      return legacy;
    }

    internal static List<FirstLevelModuleId> BuildOffer(int upgradeNumber, HashSet<FirstLevelModuleId> installed)
    {
      installed = installed ?? new HashSet<FirstLevelModuleId>();
      return upgradeNumber >= 4 ? BuildBossOffer(installed) : BuildCareOffer(installed);
    }

    private static List<FirstLevelModuleId> BuildCareOffer(HashSet<FirstLevelModuleId> installed)
    {
      var eligible = new List<CareUpgradeDefinition>();
      for (var i = 0; i < Definitions.Length; i++)
      {
        var definition = Definitions[i];
        if (definition.BossOnly || definition.Legacy || installed.Contains(definition.Id)) continue;
        if (definition.RequiredUpgradeId != FirstLevelModuleId.None && !installed.Contains(definition.RequiredUpgradeId)) continue;
        if (HasIncompatibility(definition, installed)) continue;
        eligible.Add(definition);
      }

      var offer = new List<FirstLevelModuleId>(3);
      // Evolution is always represented when a selected route can advance.
      AddFirst(offer, eligible, d => d.Tier > 1);

      // Until all four routes have Tier 1, at least one untouched route appears.
      AddFirst(offer, eligible, d => d.Tier == 1 && !HasCategory(installed, d.Category));

      // Fill by rotating the stable catalog start so repeated runs do not always
      // present the same visual ordering.
      var start = GetStableVariant(installed) % Mathf.Max(1, eligible.Count);
      for (var offset = 0; offset < eligible.Count && offer.Count < 3; offset++)
      {
        var definition = eligible[(start + offset) % eligible.Count];
        if (!offer.Contains(definition.Id)) offer.Add(definition.Id);
      }

      EnsureCategoryVariety(offer, eligible);
      EnsureProcessCard(offer, eligible);
      return offer;
    }

    private static List<FirstLevelModuleId> BuildBossOffer(HashSet<FirstLevelModuleId> installed)
    {
      var route = HighestTierCategory(installed);
      var boss = new List<CareUpgradeDefinition>(4);
      for (var i = 0; i < Definitions.Length; i++) if (Definitions[i].BossOnly) boss.Add(Definitions[i]);
      var offer = new List<FirstLevelModuleId>(3);
      AddFirst(offer, boss, d => d.Category == route);
      var start = GetStableVariant(installed) % Mathf.Max(1, boss.Count);
      for (var i = 0; i < boss.Count && offer.Count < 3; i++)
      {
        var definition = boss[(start + i) % boss.Count];
        if (!offer.Contains(definition.Id)) offer.Add(definition.Id);
      }
      return offer;
    }

    private static void AddFirst(List<FirstLevelModuleId> offer, List<CareUpgradeDefinition> pool, Predicate<CareUpgradeDefinition> predicate)
    {
      for (var i = 0; i < pool.Count; i++)
      {
        if (!predicate(pool[i]) || offer.Contains(pool[i].Id)) continue;
        offer.Add(pool[i].Id);
        return;
      }
    }

    private static void EnsureCategoryVariety(List<FirstLevelModuleId> offer, List<CareUpgradeDefinition> eligible)
    {
      if (offer.Count < 2) return;
      var first = Get(offer[0]).Category;
      for (var i = 1; i < offer.Count; i++) if (Get(offer[i]).Category != first) return;
      for (var i = 0; i < eligible.Count; i++)
      {
        if (eligible[i].Category == first || offer.Contains(eligible[i].Id)) continue;
        offer[offer.Count - 1] = eligible[i].Id;
        return;
      }
    }

    private static void EnsureProcessCard(List<FirstLevelModuleId> offer, List<CareUpgradeDefinition> eligible)
    {
      for (var i = 0; i < offer.Count; i++) if (!Get(offer[i]).QuantityOnly) return;
      for (var i = 0; i < eligible.Count; i++)
      {
        if (eligible[i].QuantityOnly || offer.Contains(eligible[i].Id)) continue;
        offer[offer.Count - 1] = eligible[i].Id;
        EnsureCategoryVariety(offer, eligible);
        return;
      }
    }

    private static bool HasCategory(HashSet<FirstLevelModuleId> installed, FirstLevelModuleCategory category)
    {
      foreach (var id in installed)
      {
        var definition = Get(id);
        if (!definition.Legacy && !definition.BossOnly && definition.Category == category) return true;
      }
      return false;
    }

    private static FirstLevelModuleCategory HighestTierCategory(HashSet<FirstLevelModuleId> installed)
    {
      var categories = new[] { FirstLevelModuleCategory.Move, FirstLevelModuleCategory.Focus, FirstLevelModuleCategory.Rest, FirstLevelModuleCategory.Release };
      var best = categories[0];
      var bestTier = -1;
      for (var i = 0; i < categories.Length; i++)
      {
        var tier = 0;
        foreach (var id in installed)
        {
          var definition = Get(id);
          if (!definition.Legacy && !definition.BossOnly && definition.Category == categories[i]) tier = Mathf.Max(tier, definition.Tier);
        }
        if (tier > bestTier)
        {
          bestTier = tier;
          best = categories[i];
        }
      }
      return best;
    }

    private static bool HasIncompatibility(CareUpgradeDefinition definition, HashSet<FirstLevelModuleId> installed)
    {
      for (var i = 0; i < definition.IncompatibleUpgradeIds.Length; i++)
        if (installed.Contains(definition.IncompatibleUpgradeIds[i])) return true;
      return false;
    }

    private static int GetStableVariant(HashSet<FirstLevelModuleId> installed)
    {
      var ids = new List<int>();
      foreach (var id in installed) ids.Add((int)id);
      ids.Sort();
      unchecked
      {
        var hash = 17;
        for (var i = 0; i < ids.Count; i++) hash = hash * 31 + ids[i];
        return Mathf.Abs(hash == int.MinValue ? 0 : hash);
      }
    }

    public int MoveTrailCount => Has(FirstLevelModuleId.MoveTripleTrail) ? 3 : Has(FirstLevelModuleId.MoveTwinTrail) ? 2 : 1;
    public bool MoveGoldenStreakEnabled => Has(FirstLevelModuleId.MoveGoldenStreak);
    public float FocusRawConversionFraction => Has(FirstLevelModuleId.FocusFullRefine) ? 1f : Has(FirstLevelModuleId.FocusMintShift) ? 0.5f : 0.25f;
    public bool FocusFarWaveEnabled => Has(FirstLevelModuleId.FocusFarWave);
    public int RestXpPerSecond => Has(FirstLevelModuleId.RestGoldenRest) ? 2 : 1;
    public float RestFocusedConversionFraction => Has(FirstLevelModuleId.RestFullRest) ? 1f : 0.5f;
    public bool QuietReturnEnabled => Has(FirstLevelModuleId.RestCircuitQuietReturn);
    public bool TwinPulseEnabled => Has(FirstLevelModuleId.ReleaseTwinPulse);
    public bool ChainPulseEnabled => Has(FirstLevelModuleId.ReleaseChainPulse);
    public bool FullReleaseEnabled => Has(FirstLevelModuleId.ReleaseFullRelease);
    public bool BossShardRainEnabled => Has(FirstLevelModuleId.BossShardRain);
    public bool BossMintCoreEnabled => Has(FirstLevelModuleId.BossMintCore);
    public bool BossCoreEchoEnabled => Has(FirstLevelModuleId.BossCoreEcho);
    public bool BossGoldReleaseEnabled => Has(FirstLevelModuleId.BossGoldRelease);

    public void ApplyQuietReturn()
    {
      if (!QuietReturnEnabled || _gameplay == null) return;
      _quietReturnPending = true;
      _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.RestCircuitQuietReturn);
    }

    public void ApplyPendingQuietReturn()
    {
      if (!_quietReturnPending || _gameplay == null) return;
      _quietReturnPending = false;
      _gameplay.BeginQuietReturnVisual(6f);
    }

    public void ApplyFullRelease()
    {
      if (!FullReleaseEnabled || _gameplay == null) return;
      SoftFocusFieldController.Instance?.GrantTemporaryExpansion(80f / 55f, 12f);
      _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.ReleaseFullRelease);
    }

    public int GetCarePulseSampleCount() => 0;
    public void ApplyCarePulseEffects() { }
    public int GetPendingPushAwayBonusSampleCount() => 0;

    private bool Has(FirstLevelModuleId id)
    {
      return _gameplay != null && _gameplay.HasFirstLevelModule(id);
    }

    private static CareUpgradeDefinition Define(
      FirstLevelModuleId id, string stableId, FirstLevelModuleCategory category, int tier,
      string title, string description, string before, string after,
      FirstLevelModuleId required, CareUpgradePreviewType preview, CareUpgradeEffectType effect,
      bool quantityOnly, Color color)
    {
      return new CareUpgradeDefinition(id, stableId, category, tier, title, description, before, after,
        required, preview, effect, false, quantityOnly, color);
    }

    private static CareUpgradeDefinition DefineBoss(
      FirstLevelModuleId id, string stableId, FirstLevelModuleCategory category,
      string title, string description, string before, string after,
      CareUpgradePreviewType preview, CareUpgradeEffectType effect, Color color)
    {
      return new CareUpgradeDefinition(id, stableId, category, 4, title, description, before, after,
        FirstLevelModuleId.None, preview, effect, true, false, color);
    }

    private static string SplitEnumName(string value)
    {
      if (string.IsNullOrEmpty(value)) return string.Empty;
      var result = value[0].ToString();
      for (var i = 1; i < value.Length; i++) result += char.IsUpper(value[i]) ? $" {value[i]}" : value[i].ToString();
      return result;
    }
  }
}
