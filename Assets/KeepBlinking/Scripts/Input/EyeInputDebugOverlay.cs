using UnityEngine;

namespace KeepBlinking.Input
{
  public class EyeInputDebugOverlay : MonoBehaviour
  {
    [SerializeField] private bool _showOverlay = true;

    private GUIStyle _titleStyle;
    private GUIStyle _lineStyle;
    private GUIStyle _goodStyle;
    private GUIStyle _warningStyle;

    public static void EnsureExists()
    {
      if (FindFirstObjectByType<EyeInputDebugOverlay>() != null)
      {
        return;
      }

      var overlay = new GameObject("KeepBlinking Eye Input Debug Overlay");
      DontDestroyOnLoad(overlay);
      overlay.AddComponent<EyeInputDebugOverlay>();
    }

    private void OnGUI()
    {
      if (!_showOverlay)
      {
        return;
      }

      EnsureStyles();

      var snapshot = EyeInputDebugState.Latest;
      var panelWidth = Mathf.Min(440f, Screen.width - 24f);
      var panelHeight = snapshot.FaceDetected ? 306f : 104f;
      GUILayout.BeginArea(new Rect(12f, 12f, panelWidth, panelHeight), GUI.skin.box);
      GUILayout.Label("KeepBlinking Eye Input Debug", _titleStyle);

      if (!snapshot.FaceDetected)
      {
        GUILayout.Label("Face: Not detected", _warningStyle);
        GUILayout.Label("Tip: keep your full face inside the webcam frame.", _lineStyle);
        GUILayout.EndArea();
        return;
      }

      GUILayout.Label($"Face: Detected   Faces: {snapshot.FaceCount}   Landmarks: {snapshot.LandmarkCount}", _goodStyle);
      GUILayout.Label($"Face Rect: x {snapshot.FaceRect.x:F3}, y {snapshot.FaceRect.y:F3}, w {snapshot.FaceRect.width:F3}, h {snapshot.FaceRect.height:F3}", _lineStyle);
      GUILayout.Label($"Face Area: {snapshot.FaceArea:F4}   Smooth: {snapshot.SmoothedFaceArea:F4}   Delta: {snapshot.FaceAreaDelta:+0.0000;-0.0000;0.0000}", _lineStyle);
      GUILayout.Label($"Push Away Signal: {(snapshot.FaceMovingAway ? "YES" : "no")}   (face area shrinking)", snapshot.FaceMovingAway ? _goodStyle : _lineStyle);
      GUILayout.Space(6f);

      GUILayout.Label($"Left Eye Open:  {snapshot.LeftEyeOpen:F2}   BlinkScore: {snapshot.LeftBlinkScore:F2}   EAR: {snapshot.LeftEyeAspectRatio:F3}", _lineStyle);
      GUILayout.Label($"Right Eye Open: {snapshot.RightEyeOpen:F2}   BlinkScore: {snapshot.RightBlinkScore:F2}   EAR: {snapshot.RightEyeAspectRatio:F3}", _lineStyle);
      GUILayout.Label($"Blink: {(snapshot.IsBlinking ? "YES" : "no")}   Blink Started: {(snapshot.BlinkStarted ? "YES" : "no")}", snapshot.IsBlinking ? _warningStyle : _lineStyle);
      GUILayout.Label($"Hard Squeeze Draft: {(snapshot.IsHardSqueeze ? "YES" : "no")}", snapshot.IsHardSqueeze ? _warningStyle : _lineStyle);
      GUILayout.Space(6f);

      GUILayout.Label("Prototype mapping: blink = mine meteor, low eye open = charge, shrinking face area = push phone away.", _lineStyle);
      GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
      if (_titleStyle != null)
      {
        return;
      }

      _titleStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 18,
        fontStyle = FontStyle.Bold,
        normal = { textColor = new Color(0.72f, 0.96f, 1f) },
      };

      _lineStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 14,
        normal = { textColor = Color.white },
      };

      _goodStyle = new GUIStyle(_lineStyle)
      {
        normal = { textColor = new Color(0.5f, 1f, 0.55f) },
      };

      _warningStyle = new GUIStyle(_lineStyle)
      {
        normal = { textColor = new Color(1f, 0.78f, 0.28f) },
      };
    }
  }
}
