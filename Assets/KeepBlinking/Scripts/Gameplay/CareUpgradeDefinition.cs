using UnityEngine;

namespace KeepBlinking.Gameplay
{
  internal sealed class CareUpgradeDefinition
  {
    public FirstLevelModuleId Id { get; }
    public FirstLevelModuleCategory Category { get; }
    public string CategoryLabel { get; }
    public string Title { get; }
    public string Description { get; }
    public string BeforeValue { get; }
    public string AfterValue { get; }
    public string Delta => $"{BeforeValue}  →  {AfterValue}";
    public Color AccentColor { get; }
    public int Tier { get; }
    public int AvailableFromUpgrade { get; }

    // Health Invariant Audit: action requirements are immutable for every card.
    public bool ReducesBlinkRequirement => false;
    public bool ExtendsNoBlinkTime => false;
    public bool ReducesFocusShiftCycles => false;
    public bool ShortensRest => false;
    public bool AllowsCloserDistance => false;
    public bool AutomaticallyCompletesAction => false;
    public bool RewardsRapidBlinking => false;
    public bool DirectlyGrantsExperience => false;
    public bool RewardsCompletedCareAction => true;
    public bool CreatesVisibleDifference => true;

    public CareUpgradeDefinition(
      FirstLevelModuleId id,
      FirstLevelModuleCategory category,
      string title,
      string effect,
      string beforeValue,
      string afterValue,
      Color accentColor,
      int availableFromUpgrade)
    {
      Id = id;
      Category = category;
      CategoryLabel = category.ToString().ToUpperInvariant();
      Title = title;
      Description = effect;
      BeforeValue = beforeValue;
      AfterValue = afterValue;
      AccentColor = accentColor;
      AvailableFromUpgrade = Mathf.Clamp(availableFromUpgrade, 1, 4);
      Tier = AvailableFromUpgrade;
    }

    public bool PassesHealthInvariantAudit()
    {
      return !ReducesBlinkRequirement &&
             !ExtendsNoBlinkTime &&
             !ReducesFocusShiftCycles &&
             !ShortensRest &&
             !AllowsCloserDistance &&
             !AutomaticallyCompletesAction &&
             !RewardsRapidBlinking &&
             !DirectlyGrantsExperience &&
             RewardsCompletedCareAction &&
             CreatesVisibleDifference;
    }
  }
}
