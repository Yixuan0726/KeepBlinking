using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public static class SoftFocusFieldLogic
  {
    public static bool IsInsideEllipse(Vector2 point, Vector2 center, Vector2 size)
    {
      var halfSize = size * 0.5f;
      if (halfSize.x <= 0.001f || halfSize.y <= 0.001f)
      {
        return false;
      }

      var offset = point - center;
      return offset.x * offset.x / (halfSize.x * halfSize.x) +
             offset.y * offset.y / (halfSize.y * halfSize.y) <= 1f;
    }

    public static float AdvancePurification(
      float progress,
      float deltaTime,
      float durationSeconds,
      float speedMultiplier,
      bool canAccumulate,
      bool canComplete)
    {
      progress = Mathf.Clamp01(progress);
      if (!canAccumulate)
      {
        return progress;
      }

      progress += Mathf.Max(0f, deltaTime) * Mathf.Max(0f, speedMultiplier) /
                  Mathf.Max(0.05f, durationSeconds);
      return canComplete ? Mathf.Clamp01(progress) : Mathf.Min(progress, 0.995f);
    }
  }
}
