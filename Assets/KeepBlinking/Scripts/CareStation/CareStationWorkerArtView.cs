using KeepBlinking.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.CareStation
{
  /// <summary>
  /// Independent layered presentation for one approved droplet Worker. All
  /// movement and device feedback are visual only and never touch station data.
  /// </summary>
  public sealed class CareStationWorkerArtView : MonoBehaviour
  {
    private const float IdleCycle = 1.8f;
    private const float WalkCycle = 0.62f;
    private const float WorkCycle = 0.9f;
    private const float RestCycle = 2.4f;
    private const float CheerDuration = 0.62f;

    private RectTransform _rect;
    private RectTransform _visualRoot;
    private Image _body;
    private Image _leftEye;
    private Image _rightEye;
    private Image _brows;
    private Image _mouth;
    private Image _leftArm;
    private Image _rightArm;
    private Image _leftHand;
    private Image _rightHand;
    private Image _leftLeg;
    private Image _rightLeg;
    private Image _leftFoot;
    private Image _rightFoot;
    private Image _workGlow;
    private CareStationWorkerArtCatalog _catalog;
    private Vector2 _homePosition;
    private Vector2 _targetPosition;
    private CareCrewState _state = CareCrewState.Idle;
    private CareStationWorkerFacing _facing = CareStationWorkerFacing.Front;
    private CareStationWorkerExpression _expression = CareStationWorkerExpression.Focused;
    private float _phaseOffset;
    private float _stateStartedAt;
    private int _instanceIndex;

    public CareCrewState AnimationState => _state;
    public CareStationWorkerFacing Facing => _facing;
    public CareStationWorkerExpression Expression => _expression;
    public Vector2 HomePosition => _homePosition;
    public Vector2 TargetPosition => _targetPosition;
    public float AnimationPhase => _phaseOffset;
    public string WorkTarget { get; private set; } = string.Empty;
    public bool UsesFormalArt => _catalog != null && _catalog.IsComplete && _body != null && _body.sprite != null;

    public static CareStationWorkerArtView Create(RectTransform parent, int index, Vector2 normalizedPosition)
    {
      var root = FirstLevelUiFactory.CreateObject($"Care Crew {index + 1}", parent).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, normalizedPosition, normalizedPosition, new Vector2(0.5f, 0f),
        Vector2.zero, new Vector2(118f, 170f));
      var view = root.gameObject.AddComponent<CareStationWorkerArtView>();
      view.Initialize(index, CareStationWorkerArtCatalog.LoadFromResources());
      return view;
    }

    private void Initialize(int instanceIndex, CareStationWorkerArtCatalog catalog)
    {
      _instanceIndex = instanceIndex;
      _phaseOffset = Mathf.Repeat(instanceIndex * 0.317f, 1f);
      _catalog = catalog;
      _rect = (RectTransform)transform;
      _homePosition = _rect.anchoredPosition;
      _targetPosition = _homePosition;

      _visualRoot = FirstLevelUiFactory.CreateObject("Worker Visual Root", _rect).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_visualRoot, Vector2.zero, Vector2.one, new Vector2(0.5f, 0f),
        Vector2.zero, Vector2.zero);

      _leftLeg = CreateLayer("Left Leg", new Vector2(-17f, 29f), new Vector2(13f, 51f));
      _rightLeg = CreateLayer("Right Leg", new Vector2(17f, 29f), new Vector2(13f, 51f));
      _leftFoot = CreateLayer("Left Foot", new Vector2(-20f, 8f), new Vector2(31f, 20f));
      _rightFoot = CreateLayer("Right Foot", new Vector2(20f, 8f), new Vector2(31f, 20f));
      _leftArm = CreateLayer("Left Arm", new Vector2(-45f, 79f), new Vector2(14f, 57f));
      _rightArm = CreateLayer("Right Arm", new Vector2(45f, 79f), new Vector2(14f, 57f));
      _body = CreateLayer("Directional Body", new Vector2(0f, 98f), new Vector2(100f, 140f));
      _leftHand = CreateLayer("Left Hand", new Vector2(-48f, 54f), new Vector2(25f, 29f));
      _rightHand = CreateLayer("Right Hand", new Vector2(48f, 54f), new Vector2(25f, 29f));
      _leftEye = CreateLayer("Left Eye", new Vector2(-15f, 105f), new Vector2(22f, 29f));
      _rightEye = CreateLayer("Right Eye", new Vector2(15f, 105f), new Vector2(22f, 29f));
      _brows = CreateLayer("Eyebrows", new Vector2(0f, 123f), new Vector2(51f, 18f));
      _mouth = CreateLayer("Mouth", new Vector2(0f, 83f), new Vector2(29f, 13f));
      _workGlow = CreateLayer("Mint Work Feedback", new Vector2(0f, 29f), new Vector2(112f, 28f));
      _workGlow.sprite = FirstLevelUiFactory.CircleSprite;
      _workGlow.color = Color.clear;

      ApplyCatalog();
      SetExpression(CareStationWorkerExpression.Focused);
      SetFacing(CareStationWorkerFacing.Front);
      _stateStartedAt = Time.unscaledTime;
    }

    private Image CreateLayer(string name, Vector2 position, Vector2 size)
    {
      var image = FirstLevelUiFactory.CreateImage(name, _visualRoot, Color.white);
      FirstLevelUiFactory.SetRect(image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
        new Vector2(0.5f, 0.5f), position, size);
      image.preserveAspect = true;
      image.raycastTarget = false;
      return image;
    }

    private void ApplyCatalog()
    {
      if (_catalog == null || !_catalog.IsComplete)
      {
        Debug.LogError("Care Station Worker formal art is incomplete; retired graybox fallback is disabled.", this);
        _visualRoot.gameObject.SetActive(false);
        return;
      }
      _visualRoot.gameObject.SetActive(true);
      _leftArm.sprite = _catalog.LeftArm;
      _rightArm.sprite = _catalog.RightArm;
      _leftHand.sprite = _catalog.LeftHand;
      _rightHand.sprite = _catalog.RightHand;
      _leftLeg.sprite = _catalog.LeftLeg;
      _rightLeg.sprite = _catalog.RightLeg;
      _leftFoot.sprite = _catalog.LeftFoot;
      _rightFoot.sprite = _catalog.RightFoot;
    }

    public void SetState(CareCrewState state, string workTarget = "")
    {
      if (_state == state && WorkTarget == (workTarget ?? string.Empty)) return;
      _state = state;
      WorkTarget = workTarget ?? string.Empty;
      _stateStartedAt = Time.unscaledTime;
      if (state == CareCrewState.Work && !string.IsNullOrEmpty(WorkTarget))
      {
        if (WorkTarget.IndexOf("FILTER", System.StringComparison.OrdinalIgnoreCase) >= 0)
          SetFacing(CareStationWorkerFacing.BackLeft);
        else if (WorkTarget.IndexOf("FILLER", System.StringComparison.OrdinalIgnoreCase) >= 0)
          SetFacing(CareStationWorkerFacing.BackRight);
      }
    }

    public void SetExpression(CareStationWorkerExpression expression)
    {
      _expression = expression;
      RefreshFace();
    }

    public void SetFacing(CareStationWorkerFacing facing)
    {
      _facing = facing;
      if (_body != null && _catalog != null) _body.sprite = _catalog.Body(facing);
      RefreshDirectionalLayout();
      RefreshFace();
    }

    public void SetTargetPosition(Vector2 targetPosition)
    {
      _targetPosition = targetPosition;
      var movement = targetPosition - (_rect != null ? _rect.anchoredPosition : _homePosition);
      SetFacing(CareStationWorkerVisualRules.FacingForMovement(movement, _facing));
    }

    public void ReturnHome()
    {
      _targetPosition = _homePosition;
    }

    private void RefreshFace()
    {
      if (_catalog == null || _leftEye == null) return;
      var faceVisible = CareStationWorkerVisualRules.FaceVisible(_facing);
      var side = CareStationWorkerVisualRules.IsSide(_facing);
      var happy = _expression == CareStationWorkerExpression.Happy;
      _leftEye.gameObject.SetActive(faceVisible && !side);
      _rightEye.gameObject.SetActive(faceVisible);
      _brows.gameObject.SetActive(faceVisible && !happy && !side);
      _mouth.gameObject.SetActive(faceVisible && !side);
      var eyeSprite = happy ? _catalog.HappyEye : _catalog.OpenEye;
      _leftEye.sprite = eyeSprite;
      _rightEye.sprite = eyeSprite;
      _brows.sprite = _expression == CareStationWorkerExpression.Angry
        ? _catalog.AngryBrows
        : _catalog.FocusedBrows;
      _mouth.sprite = _expression == CareStationWorkerExpression.Angry
        ? _catalog.AngryMouth
        : _expression == CareStationWorkerExpression.Happy
          ? _catalog.HappyMouth
          : _catalog.FocusedMouth;
    }

    private void RefreshDirectionalLayout()
    {
      if (_body == null) return;
      var side = CareStationWorkerVisualRules.IsSide(_facing);
      var facingLeft = _facing == CareStationWorkerFacing.Left || _facing == CareStationWorkerFacing.FrontLeft ||
                       _facing == CareStationWorkerFacing.BackLeft;
      var faceShift = side ? (facingLeft ? -13f : 13f) :
        _facing == CareStationWorkerFacing.FrontLeft ? -6f :
        _facing == CareStationWorkerFacing.FrontRight ? 6f : 0f;
      _leftEye.rectTransform.anchoredPosition = new Vector2(faceShift - (side ? 0f : 15f), 105f);
      _rightEye.rectTransform.anchoredPosition = new Vector2(faceShift + (side ? 0f : 15f), 105f);
      _brows.rectTransform.anchoredPosition = new Vector2(faceShift, 123f);
      _mouth.rectTransform.anchoredPosition = new Vector2(faceShift, 83f);
      _leftArm.rectTransform.anchoredPosition = new Vector2(-45f, 79f);
      _rightArm.rectTransform.anchoredPosition = new Vector2(45f, 79f);
      _leftHand.rectTransform.anchoredPosition = new Vector2(-48f, 54f);
      _rightHand.rectTransform.anchoredPosition = new Vector2(48f, 54f);
      if (side)
      {
        var nearSign = facingLeft ? -1f : 1f;
        _leftArm.rectTransform.anchoredPosition = new Vector2(-nearSign * 21f, 76f);
        _rightArm.rectTransform.anchoredPosition = new Vector2(nearSign * 43f, 79f);
        _leftHand.rectTransform.anchoredPosition = new Vector2(-nearSign * 23f, 52f);
        _rightHand.rectTransform.anchoredPosition = new Vector2(nearSign * 47f, 54f);
      }
    }

    private void Update()
    {
      if (_rect == null || _visualRoot == null || !_visualRoot.gameObject.activeSelf) return;
      var elapsed = Time.unscaledTime - _stateStartedAt + _phaseOffset;
      var target = _state == CareCrewState.Walk || _state == CareCrewState.Carry
        ? _targetPosition
        : _homePosition;
      if ((_state == CareCrewState.Walk || _state == CareCrewState.Carry) &&
          Vector2.Distance(_rect.anchoredPosition, target) > 0.2f)
      {
        var previous = _rect.anchoredPosition;
        _rect.anchoredPosition = Vector2.MoveTowards(previous, target, 34f * Time.unscaledDeltaTime);
        SetFacing(CareStationWorkerVisualRules.FacingForMovement(_rect.anchoredPosition - previous, _facing));
      }

      var bob = 0f;
      var tilt = 0f;
      var armSwing = 0f;
      var legSwing = 0f;
      var bodyScale = 1f;
      var workAlpha = 0f;
      switch (_state)
      {
        case CareCrewState.Walk:
        case CareCrewState.Carry:
          var walk = Mathf.Sin(elapsed * Mathf.PI * 2f / WalkCycle);
          bob = Mathf.Abs(walk) * 3.2f;
          tilt = walk * 1.8f;
          armSwing = walk * 11f;
          legSwing = -walk * 8f;
          break;
        case CareCrewState.Work:
          var work = Mathf.Sin(elapsed * Mathf.PI * 2f / WorkCycle);
          bob = work * 1.1f;
          tilt = -1.2f + work * 1.4f;
          armSwing = 8f + work * 9f;
          workAlpha = 0.18f + (work * 0.5f + 0.5f) * 0.16f;
          break;
        case CareCrewState.Rest:
          bob = Mathf.Sin(elapsed * Mathf.PI * 2f / RestCycle) * 1.2f - 3f;
          bodyScale = 0.985f;
          armSwing = -8f;
          break;
        case CareCrewState.Cheer:
          var cheerT = Mathf.Clamp01(elapsed / CheerDuration);
          bob = Mathf.Sin(cheerT * Mathf.PI) * 11f;
          armSwing = -Mathf.Sin(cheerT * Mathf.PI) * 42f;
          workAlpha = Mathf.Sin(cheerT * Mathf.PI) * 0.28f;
          if (cheerT >= 1f) SetState(CareCrewState.Work, WorkTarget);
          break;
        default:
          bob = Mathf.Sin(elapsed * Mathf.PI * 2f / IdleCycle) * 1.4f;
          armSwing = Mathf.Sin(elapsed * Mathf.PI * 2f / IdleCycle + _instanceIndex) * 2.5f;
          break;
      }

      _visualRoot.anchoredPosition = new Vector2(0f, bob);
      _visualRoot.localRotation = Quaternion.Euler(0f, 0f, tilt);
      _visualRoot.localScale = Vector3.one * bodyScale;
      _leftArm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, armSwing);
      _rightArm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -armSwing);
      _leftLeg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, legSwing);
      _rightLeg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -legSwing);
      _workGlow.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, workAlpha);
    }
  }
}
