using UnityEngine;

namespace KeepBlinking.Input
{
  public sealed class CurrentGazeProvider : IGazePositionProvider
  {
    private GazeProviderSample _latest;
    private bool _hasLatest;

    public string ProviderName => "Current";
    public bool IsAvailable => true;
    public string FailureReason => string.Empty;
    public CalibratedScreenGazeMapper Mapper { get; } = new CalibratedScreenGazeMapper();

    public void Tick()
    {
      var snapshot = EyeInputDebugState.Latest;
      var valid = snapshot.FaceDetected && snapshot.HasGazeScreenPosition && Screen.width > 0 && Screen.height > 0;
      var normalized = valid
        ? new Vector2(
          Mathf.Clamp01(snapshot.GazeScreenPosition.x / Screen.width),
          Mathf.Clamp01(snapshot.GazeScreenPosition.y / Screen.height))
        : Vector2.zero;

      _latest = new GazeProviderSample(
        ProviderName,
        Time.unscaledTimeAsDouble,
        valid,
        normalized,
        valid,
        normalized,
        Vector2.zero,
        0f);
      _hasLatest = true;
    }

    public bool TryGetLatest(out GazeProviderSample sample)
    {
      sample = _latest;
      return _hasLatest;
    }

    public void Dispose()
    {
    }
  }
}
