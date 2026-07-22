using System;
using System.Collections;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public sealed class SessionReportController : MonoBehaviour
  {
    [SerializeField] private StudyFeedbackMode _studyFeedbackMode = StudyFeedbackMode.PlayerFeedback;
    [SerializeField] private bool _enableResearchDataExport;
    [SerializeField] private string _subjectId = "S-021";
    [SerializeField, Min(1)] private int _sessionIndex = 1;
    [SerializeField, Min(1)] private int _totalProtocolSessions = 12;

    private EdgeOrbitHarvestMvp _gameplay;
    private SessionMetricsTracker _metrics;
    private SessionReportView _view;
    private Coroutine _closeRoutine;

    public bool IsOpen => _view != null && _view.IsOpen;
    public StudyFeedbackMode FeedbackMode => _studyFeedbackMode;
    public bool ResearchDataExportEnabled => _enableResearchDataExport;

    public event Action SessionReportClosed;

    public void Initialize(EdgeOrbitHarvestMvp gameplay, SessionMetricsTracker metrics)
    {
      _gameplay = gameplay;
      _metrics = metrics;
      _view = GetComponent<SessionReportView>();
      if (_view == null)
      {
        _view = gameObject.AddComponent<SessionReportView>();
      }
      _view.Initialize(gameplay);
      _view.ContinueRequested -= HandleContinueRequested;
      _view.ContinueRequested += HandleContinueRequested;
    }

    public SessionReportData ShowReport()
    {
      if (_metrics == null)
      {
        return null;
      }

      var data = _metrics.BuildReportData(
        _subjectId,
        Mathf.Max(1, _sessionIndex),
        Mathf.Max(1, _totalProtocolSessions));
      _view.Show(data, _studyFeedbackMode);
      if (_enableResearchDataExport)
      {
        SessionReportExporter.TryExport(data);
      }
      return data;
    }

    private void HandleContinueRequested()
    {
      if (_closeRoutine == null && _view != null && _view.IsOpen)
      {
        _closeRoutine = StartCoroutine(CloseRoutine());
      }
    }

    private IEnumerator CloseRoutine()
    {
      _view.BeginClose();
      const float duration = 0.28f;
      var elapsed = 0f;
      while (elapsed < duration)
      {
        elapsed += Time.unscaledDeltaTime;
        _view.SetCloseAlpha(1f - Mathf.Clamp01(elapsed / duration));
        yield return null;
      }
      _view.HideImmediate();
      _closeRoutine = null;
      if (SessionReportClosed == null)
      {
        yield break;
      }

      var handlers = SessionReportClosed.GetInvocationList();
      for (var i = 0; i < handlers.Length; i++)
      {
        try
        {
          ((Action)handlers[i]).Invoke();
        }
        catch (Exception exception)
        {
          Debug.LogError("KeepBlinking session-report observer failed.", this);
          Debug.LogException(exception, this);
        }
      }
    }

    private void OnDestroy()
    {
      if (_view != null)
      {
        _view.ContinueRequested -= HandleContinueRequested;
      }
    }
  }
}
