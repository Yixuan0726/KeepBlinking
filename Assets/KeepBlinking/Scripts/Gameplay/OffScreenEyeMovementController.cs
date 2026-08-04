using System;

namespace KeepBlinking.Gameplay
{
  [Obsolete("Use OffScreenEyeBreakController. This compatibility shim does not implement gameplay logic.")]
  public static class OffScreenEyeMovementController
  {
    public const string ReportDisplayName = OffScreenEyeBreakController.ReportDisplayName;

    public static OffScreenEyeBreakController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      return OffScreenEyeBreakController.EnsureExists(gameplay);
    }
  }
}
