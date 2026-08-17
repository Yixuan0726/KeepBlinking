using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  public sealed class CareExperienceRewardEmitter : MonoBehaviour
  {
    private readonly struct Emission
    {
      public Emission(CareExperienceState experienceState, CareMovementDirection direction, float progress)
      {
        ExperienceState = experienceState;
        Direction = direction;
        Progress = progress;
      }
      public CareExperienceState ExperienceState { get; }
      public CareMovementDirection Direction { get; }
      public float Progress { get; }
    }

    private sealed class FloatingFeedback
    {
      public RectTransform Root;
      public TextMeshProUGUI Label;
      public Image Ripple;
      public float StartedAt = -1f;
    }

    [SerializeField, Range(0.03f, 0.12f)] private float _emissionInterval = 0.055f;
    [SerializeField, Range(0.08f, 0.12f)] private float _restEmissionInterval = 0.10f;
    [SerializeField, Range(3, 4)] private int _floatingTextPoolCapacity = 4;

    private readonly Queue<Emission> _queue = new Queue<Emission>(128);
    private readonly List<FloatingFeedback> _feedback = new List<FloatingFeedback>(4);
    private EdgeOrbitHarvestMvp _gameplay;
    private CanvasGroup _canvasGroup;
    private TextMeshProUGUI _pendingLabel;
    private float _nextEmissionAt;
    private int _emissionSerial;
    private int _waitingPlacementSerial;
    private bool _emissionPaused;
    private int _moveNodeSerial;

    public static CareExperienceRewardEmitter Instance { get; private set; }
    public event Action<int, bool> FragmentEmitted;
    public event Action<int, CareExperienceState> TypedFragmentEmitted;
    public event Action<CareMovementDirection, float> FragmentTrackFeedbackShown;
    public int QueuedCount => _queue.Count;
    public int QueuedValue
    {
      get
      {
        var value = 0;
        foreach (var emission in _queue) value += CareExperienceStateInfo.Value(emission.ExperienceState);
        return value;
      }
    }

    public int QueuedCountForState(CareExperienceState state)
    {
      var count = 0;
      foreach (var emission in _queue) if (emission.ExperienceState == state) count++;
      return count;
    }

    public void BeginCareRound()
    {
      _moveNodeSerial = 0;
    }

    public void SetEmissionPaused(bool paused)
    {
      _emissionPaused = paused;
      if (!paused && _queue.Count > 0) _nextEmissionAt = Mathf.Max(_nextEmissionAt, Time.unscaledTime);
    }

    public static CareExperienceRewardEmitter EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null) Instance = FindFirstObjectByType<CareExperienceRewardEmitter>();
      if (Instance == null)
      {
        var owner = new GameObject("Care Experience Reward Emitter");
        Instance = owner.AddComponent<CareExperienceRewardEmitter>();
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
      BuildView();
    }

    public void EnqueueFragments(int count, bool gold, CareMovementDirection direction, float progress)
    {
      EnqueueFragments(count, gold ? CareExperienceState.Rested : CareExperienceState.Raw, direction, progress);
    }

    public void EnqueueFragments(int count, CareExperienceState experienceState, CareMovementDirection direction, float progress)
    {
      count = Mathf.Max(0, count);
      var directionalRaw = experienceState == CareExperienceState.Raw &&
                           (direction == CareMovementDirection.Left || direction == CareMovementDirection.Right ||
                            direction == CareMovementDirection.Up || direction == CareMovementDirection.Down);
      var upgrades = CareUpgradeController.Instance;
      var trails = directionalRaw && upgrades != null ? upgrades.MoveTrailCount : 1;
      var wasEmpty = _queue.Count == 0;
      for (var node = 0; node < count; node++)
      {
        for (var trail = 0; trail < trails && _queue.Count < 256; trail++)
          _queue.Enqueue(new Emission(experienceState, direction, progress));
        if (!directionalRaw) continue;
        _moveNodeSerial++;
        if (upgrades != null && upgrades.MoveGoldenStreakEnabled && _moveNodeSerial % 6 == 0 && _queue.Count < 256)
        {
          _queue.Enqueue(new Emission(CareExperienceState.Rested, direction, progress));
          CareAudioFeedbackController.EnsureExists().PlaySweepEnd();
        }
      }
      if (wasEmpty && _queue.Count > 0) _nextEmissionAt = Time.unscaledTime;
    }

    public void EnqueueRestGold(int count)
    {
      count = Mathf.Clamp(count, 0, 128 - _queue.Count);
      for (var i = 0; i < count; i++) _queue.Enqueue(new Emission(CareExperienceState.Rested, CareMovementDirection.Center, i / Mathf.Max(1f, count - 1f)));
      if (_queue.Count == count) _nextEmissionAt = Time.unscaledTime;
    }

    public void FlushQueuedImmediately()
    {
      if (_gameplay == null) return;
      while (_queue.Count > 0)
      {
        Emit(_queue.Dequeue());
      }
    }

    private void Update()
    {
      UpdatePendingLabel();
      UpdateFloatingFeedback();
      if (_gameplay == null || _queue.Count == 0 || _emissionPaused || _gameplay.IsModuleUpgradeOpen) return;
      if (Time.unscaledTime < _nextEmissionAt) return;

      var emission = _queue.Dequeue();
      Emit(emission);
      _nextEmissionAt = Time.unscaledTime + (emission.ExperienceState == CareExperienceState.Rested ? _restEmissionInterval : _emissionInterval);
    }

    private void Emit(Emission emission)
    {
      var waitingViewport = GetWaitingViewport();
      var id = _gameplay.SpawnPendingCareExperienceFragment(emission.ExperienceState, waitingViewport);
      if (id != EdgeOrbitHarvestMvp.NoTargetId)
      {
        _emissionSerial++;
        var trackViewport = GetTrackFeedbackViewport(emission.Direction, emission.Progress);
        ShowFloatingFeedback(trackViewport, emission.ExperienceState);
        CareAudioFeedbackController.EnsureExists().PlayFragment(emission.Progress);
        FragmentTrackFeedbackShown?.Invoke(emission.Direction, Mathf.Clamp01(emission.Progress));
        FragmentEmitted?.Invoke(id, emission.ExperienceState == CareExperienceState.Rested);
        TypedFragmentEmitted?.Invoke(id, emission.ExperienceState);
      }
    }

    private Vector2 GetWaitingViewport()
    {
      // The gameplay owner subsequently reflows every Converted sample, including
      // base Soft Focus samples, into the same rails. Spawning at a rail anchor
      // prevents even a one-frame pop in the central interaction area.
      var placement = _waitingPlacementSerial++;
      var right = (placement & 1) != 0;
      var row = (placement / 2) % 10;
      var t = row / 9f;
      var arc = Mathf.Sin(t * Mathf.PI) * 0.025f;
      return new Vector2(right ? 0.88f - arc : 0.12f + arc, Mathf.Lerp(0.28f, 0.76f, t));
    }

    public static Vector2 GetTrackFeedbackViewport(CareMovementDirection direction, float progress)
    {
      progress = Mathf.Clamp01(progress);
      switch (direction)
      {
        case CareMovementDirection.Left:
          return new Vector2(Mathf.Lerp(0.5f, 0.16f, progress), 0.56f);
        case CareMovementDirection.Right:
          return new Vector2(Mathf.Lerp(0.16f, 0.84f, progress), 0.56f);
        case CareMovementDirection.Down:
          return new Vector2(0.5f, Mathf.Lerp(0.72f, 0.30f, progress));
        case CareMovementDirection.Up:
          return new Vector2(0.5f, Mathf.Lerp(0.30f, 0.74f, progress));
        case CareMovementDirection.Near:
          return new Vector2(Mathf.Lerp(0.38f, 0.62f, progress), 0.56f);
        case CareMovementDirection.Far:
          return new Vector2(Mathf.Lerp(0.62f, 0.38f, progress), 0.56f);
        default:
          return new Vector2(Mathf.Lerp(0.24f, 0.76f, progress), 0.56f);
      }
    }

    private void BuildView()
    {
      // Directional presentation uses sorting order 330. Keep track feedback and
      // XP READY above it without changing the formal HUD canvas.
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Pending Experience Canvas", 335, out _, out _canvasGroup);
      _pendingLabel = FirstLevelUiFactory.CreateText(
        "XP Ready",
        safe,
        string.Empty,
        27f,
        FontStyles.Bold,
        TextAlignmentOptions.TopRight,
        KeepBlinkingTheme.AccentWarm);
      FirstLevelUiFactory.SetRect(
        _pendingLabel.rectTransform,
        new Vector2(1f, 1f),
        new Vector2(1f, 1f),
        Vector2.one,
        new Vector2(-32f, -104f),
        new Vector2(390f, 92f));

      var count = Mathf.Clamp(_floatingTextPoolCapacity, 3, 4);
      for (var i = 0; i < count; i++)
      {
        var root = FirstLevelUiFactory.CreateObject($"Pooled +1 Feedback {i + 1}", safe).GetComponent<RectTransform>();
        FirstLevelUiFactory.SetRect(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 72f));
        var ripple = FirstLevelUiFactory.CreateImage("Ripple", root, Color.clear, FirstLevelUiFactory.RingSprite);
        FirstLevelUiFactory.Stretch(ripple.rectTransform, new Vector2(22f, 0f), new Vector2(-22f, -44f));
        var label = FirstLevelUiFactory.CreateText("Value", root, "+1", 28f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentPrimary);
        FirstLevelUiFactory.Stretch(label.rectTransform);
        root.gameObject.SetActive(false);
        _feedback.Add(new FloatingFeedback { Root = root, Label = label, Ripple = ripple });
      }
    }

    private void UpdatePendingLabel()
    {
      if (_pendingLabel == null || _gameplay == null) return;
      if (_gameplay.IsCareStationMode)
      {
        _pendingLabel.text = string.Empty;
        _canvasGroup.alpha = 0f;
        return;
      }
      var value = _gameplay.PendingUnsettledExperienceValue + QueuedValue;
      if (value <= 0)
      {
        _pendingLabel.text = string.Empty;
      }
      else
      {
        var raw = _gameplay.CountPendingCareExperience(CareExperienceState.Raw, true) + QueuedCountForState(CareExperienceState.Raw);
        var focused = _gameplay.CountPendingCareExperience(CareExperienceState.Focused, true) + QueuedCountForState(CareExperienceState.Focused);
        var rested = _gameplay.CountPendingCareExperience(CareExperienceState.Rested, true) + QueuedCountForState(CareExperienceState.Rested);
        _pendingLabel.text = $"XP READY {value}\n<size=65%>RAW XP {raw}  FOCUSED XP {focused}  RESTED XP {rested}</size>";
      }
      _canvasGroup.alpha = _gameplay.IsFirstLevelBossMode ? 0f : 1f;
    }

    private void ShowFloatingFeedback(Vector2 viewport, CareExperienceState experienceState)
    {
      FloatingFeedback item = null;
      for (var i = 0; i < _feedback.Count; i++)
      {
        if (!_feedback[i].Root.gameObject.activeSelf)
        {
          item = _feedback[i];
          break;
        }
      }
      if (item == null) item = _feedback[_emissionSerial % _feedback.Count];
      item.Root.anchorMin = viewport;
      item.Root.anchorMax = viewport;
      item.Root.anchoredPosition = Vector2.zero;
      item.Root.localScale = Vector3.one * 0.8f;
      item.Label.text = $"+{CareExperienceStateInfo.Value(experienceState)}";
      item.Label.color = CareExperienceStateInfo.Color(experienceState);
      item.Ripple.color = KeepBlinkingTheme.WithAlpha(item.Label.color, 0.45f);
      item.StartedAt = Time.unscaledTime;
      item.Root.gameObject.SetActive(true);
    }

    private void UpdateFloatingFeedback()
    {
      for (var i = 0; i < _feedback.Count; i++)
      {
        var item = _feedback[i];
        if (!item.Root.gameObject.activeSelf) continue;
        var progress = Mathf.Clamp01((Time.unscaledTime - item.StartedAt) / 0.55f);
        item.Root.anchoredPosition = Vector2.up * Mathf.Lerp(0f, 58f, progress);
        item.Root.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.12f, Mathf.Sin(progress * Mathf.PI));
        var alpha = 1f - progress;
        item.Label.color = KeepBlinkingTheme.WithAlpha(item.Label.color, alpha);
        item.Ripple.color = KeepBlinkingTheme.WithAlpha(item.Ripple.color, alpha * 0.5f);
        item.Ripple.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.65f, 1.8f, progress);
        if (progress >= 1f) item.Root.gameObject.SetActive(false);
      }
    }

    private void OnDestroy()
    {
      if (Instance == this) Instance = null;
    }
  }
}
