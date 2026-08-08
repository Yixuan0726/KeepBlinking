using System;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  [Obsolete("The gaze-evaluated eye break was removed. Screen-Down Rest owns the opening rest flow.")]
  public sealed class OffScreenEyeBreakController : MonoBehaviour
  {
    public const string ReportDisplayName = ScreenDownRestController.ReportDisplayName;
    public static OffScreenEyeBreakController Instance { get; private set; }
    public static event Action OffScreenGazeBreakCompleted;

    public static OffScreenEyeBreakController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null) Instance = FindFirstObjectByType<OffScreenEyeBreakController>();
      if (Instance == null)
      {
        var owner = new GameObject("Screen-Down Rest Compatibility");
        Instance = owner.AddComponent<OffScreenEyeBreakController>();
      }
      ScreenDownRestController.EnsureExists(gameplay);
      return Instance;
    }

    public void StartEyeMovementBreak()
    {
      ScreenDownRestController.Instance?.BeginOpeningRest();
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }
      Instance = this;
      ScreenDownRestController.ScreenDownRestCompleted += RelayCompleted;
    }

    private void OnDestroy()
    {
      ScreenDownRestController.ScreenDownRestCompleted -= RelayCompleted;
      if (Instance == this) Instance = null;
    }

    private static void RelayCompleted()
    {
      OffScreenGazeBreakCompleted?.Invoke();
    }
  }
}
