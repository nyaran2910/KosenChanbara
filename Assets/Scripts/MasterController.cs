using SchoolFestival.Combat;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class MasterController : MonoBehaviour
{
    public const float radsX = 90f;
    public const float radsZ = 90f;

    [SerializeField] private GameObject Sword1;
    [SerializeField] private GameObject Sword2;
    [SerializeField] private GameObject PlayerObject1;
    [SerializeField] private GameObject PlayerObject2;
    [SerializeField] private SpriteRenderer arena;
    [SerializeField] private GameObject[] characterBodies;
    [SerializeField] private GameObject[] characterBlades;
    [SerializeField] private string[] characterNames;
    [SerializeField] private Vector3[] characterChestCenters;
    [SerializeField] private GameObject colosseumStage;
    [SerializeField] private GameObject cricketStadiumStage;
    [SerializeField] private GameObject chambaraStage;
    [SerializeField] private GameObject chambaraSuddenDeathStage;
    [SerializeField] private GameObject circularStadiumStage;
    [SerializeField, Range(0.1f, 1f)] private float suddenDeathRadiusRatio = 0.75f;
    [SerializeField, Min(1f)] private float cricketBoundaryRadius = 30f;
    public static readonly string[] ColorNames = { "RED", "BLUE", "GREEN", "YELLOW", "PURPLE", "BLACK" };
    public static readonly string[] StageNames = { "PLATFORM", "COLOSSEUM", "CIRCULAR STADIUM" };
    [SerializeField] private Vector3 chestCenter = new Vector3(0f, 0.023497807f, 0f);
    [SerializeField, Min(0f)] private float gripRadius = 0.55f;
    [SerializeField, Min(0f)] private float guardPlaneOffset = 0.55f;
    [SerializeField, Min(0f)] private float guardHeight = 0.55f;
    [SerializeField, Min(0f)] private float guardRadius = 0.6f;

    private sealed class PlayerSword
    {
        public Transform Sword;
        public Transform Player;
        public MotionController Motion;
        public Quaternion TargetRotation = Quaternion.Euler(-90f, 0f, 0f);
        public SwordPose TargetPose;
        public readonly GuardPoseTransition GuardTransition = new GuardPoseTransition();
        public int GuardPoseGeneration = -1;
        public MotionController PoseMotion;
        public ControllerInputSource PoseSource;
        public int MotionRecenterVersion;
        public bool RecenterPending;
        public SwordPose RecenterFrom;
        public double RecenterPoseUntil = double.NegativeInfinity;
        public Vector3 LastGuardDirection = Vector3.up;
        public float Pitch = -90f;
        public float Roll;
        public int InputGeneration;
        public int MotionGeneration = -1;
        public bool UsingPhone, GuardHeld;
        public Vector3 PreviousDirection, CutVelocity;
        public int DirectionGeneration = -1;
        public Vector3 InitialPosition;
        public Quaternion InitialRotation;
        public float TurnVelocity;
        public Vector3 CombatGrip, CombatBlade, CombatPreviousGrip, CombatPreviousBlade;
        public Quaternion CombatFacing;
        public int CombatGeneration = -1;
        public bool CombatBaseline, CombatGuard;
        public double ThrustAt = -100d;
        public SwordPose ThrustFrom;
    }

    private sealed class SingleNpc
    {
        public GameObject Root;
        public Transform Sword;
        public DuelAvatar Avatar;
        public Vector3 GuardDirection;
        public Vector3 AttackDirection;
        public float AttackTimer = -1f;
        public float WindupSeconds, SwingSeconds, TurnVelocity;
        public Vector3 AttackStart, AttackEnd, AttackEndGrip, AttackAim, AttackReadyDirection, PreviousGrip, PreviousBlade;
        public readonly AttackStroke Stroke = new AttackStroke();
        public Vector3 CutVelocity;
        public Quaternion WindupRotation, AttackStartRotation;
        public Vector3 WindupGrip, AttackTarget, ChestCenter, GuardPoseDirection = Vector3.up;
        public Vector3 WaitingAnchor;
        public float WaitingPhase;
        public SwordPose VisualBasePose;
        public bool ContactResolved, CombatGuardHeld;
        public bool OpeningAttack, ApproachAttack, AdvanceDuringWindup, ContinuousAttack;
        public SingleEnemyStatus Status;
        // -1 is live continuous motion. 0..7 is an explicit legacy geometry
        // fixture override and is never selected by normal NPC generation.
        public int GuardOctant = -1;
        public float GuardInitialAngle, GuardMotionPhase, GuardBias, GuardBiasTarget;
        public double GuardMotionStarted;
        public float AttackReadyAngle, AttackReadyPhase;
        public Vector3 LastGeneratedReadyDirection;
        public bool AttackReadyOverride, AttackReadyCommitted;
        public int Hearts, MaximumHearts;
        public double StunnedUntil, RecoveryUntil;
        public double NextAttack, AttackNotBefore;
        public double NextGuardChange; // Legacy fixture timing data; never drives live guard direction.
        public bool Engaged;
        public double FacingSince = double.PositiveInfinity;
        public bool Defeated;
        public double DefeatedAt;
    }

    private readonly DuelMatch match = new DuelMatch();
    private readonly DuelSeries series = new DuelSeries();
    private readonly ShowdownProgress showdown = new ShowdownProgress();
    private ShowdownCourse singleCourse;
    private GameFlowController gameFlow;
    private double singleStarted;
    private double singleDefeatAt = double.NaN;
    private const double SingleDefeatResultDelay = 2d;
    private bool singlePushActive, singleCrowdInitialized;
    private double singlePushStarted;
    private float singlePushHitStop;
    private SingleNpc singlePushNpc;
    private Vector3 singlePlayerPushStart, singlePlayerPushTarget, singleNpcPushStart, singleNpcPushTarget;
    private Vector3 singleCrowdCenter, singleCrowdOffset;

    private readonly List<SingleNpc> singleNpcs = new List<SingleNpc>();
    private PlayerSword first;
    private PlayerSword second;
    private DuelPresentation presentation;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector2 arenaCenter;
    private float arenaRadius;
    private Vector2 fieldCenter;
    private float fieldRadius;
    private Vector2 FieldCenter => StageIndex == 2 && fieldRadius > 0f ? fieldCenter : arenaCenter;
    private float FieldRadius => StageIndex == 2 && fieldRadius > 0f ? fieldRadius
        : IsFreeMode && StageIndex != 2 ? arenaRadius : cricketBoundaryRadius;
    private bool suddenDeathStageActive;
    private float CurrentArenaRadius => arenaRadius * (suddenDeathStageActive ? suddenDeathRadiusRatio : 1f);
    private bool reactionActive;
    private double reactionStart;
    private float hitStopSeconds;
    private Vector3 pushStart;
    private Vector3 pushTarget;
    private PlayerSword pushedSword;
    private Vector3 lateralStart, lateralTarget;
    private SwordPose frozenFirstPose;
    private SwordPose frozenSecondPose;
    private Transform singleFocusAnchor;
    private float singleSpawnTimer;
    private readonly AttackStroke singleStroke = new AttackStroke();
    private double singleResolvingUntil, singleStunnedUntil;
    private SingleNpc singleActiveNpc;
    private double singleNextOpponentAt;
    private double singleTransitionStart = double.NegativeInfinity;
    private bool singleGameOver;
    private MotionController singleMotion;
    private readonly List<Vector3> singleObstaclePositions = new List<Vector3>();
    private readonly List<Renderer> hiddenSingleRenderers = new List<Renderer>();
    private readonly List<Collider> hiddenSingleColliders = new List<Collider>();

    private PhoneControllerHub phoneHub;
    private double matchTime;
    public bool IsPaused { get; private set; }
    public double MatchTime => matchTime;
    public MatchWinner Winner => series.Winner;
    public int DisplayMode { get; private set; } = 1;
    public int ViewedPlayer { get; private set; }
    public bool IsSingleMode { get; private set; }
    public bool IsEasySingleDifficulty { get; private set; }
    private ShowdownDifficulty SingleDifficulty => new ShowdownDifficulty(SingleSection, IsEasySingleDifficulty);
    public bool IsFreeMode { get; private set; }
    public int SingleSection => singleCourse != null ? singleCourse.SectionNumber : 1;
    public bool SingleAdvancing => singleCourse != null && singleCourse.Phase == ShowdownCoursePhase.Travelling;
    public float SingleAdvanceProgress => singleCourse != null ? singleCourse.TravelProgress : 0f;
    private readonly int[] characters = { 0, 0 };
    private readonly int[] colors = { 0, 1 };
    public int CharacterIndex(int playerIndex) => characters[playerIndex];
    public int ColorIndex(int playerIndex) => colors[playerIndex];
    public int CharacterCount => characterNames == null ? 0 : characterNames.Length;
    public string CharacterName(int index) => characterNames[index];
    public int StageIndex { get; private set; }
    public bool RingOutEnabled => StageIndex != 2;

    private void Start()
    {
        if (Sword1 == null || Sword2 == null || PlayerObject1 == null || PlayerObject2 == null || arena == null)
        {
            Debug.LogError("MasterController requires both swords, both players and the arena SpriteRenderer.", this);
            enabled = false;
            return;
        }

        Bounds bounds = arena.bounds;
        arenaCenter = new Vector2(bounds.center.x, bounds.center.z);
        arenaRadius = Mathf.Min(bounds.extents.x, bounds.extents.z);
        if (arenaRadius <= 0f)
        {
            Debug.LogError("The arena must be a horizontal circle with a non-zero radius.", this);
            enabled = false;
            return;
        }

        initialPosition = transform.position;
        initialRotation = transform.rotation;
        first = CreatePlayerSword(Sword1, PlayerObject1);
        second = CreatePlayerSword(Sword2, PlayerObject2);
        singleCourse = new ShowdownCourse(showdown);
        presentation = gameObject.AddComponent<DuelPresentation>();
        presentation.Initialize(PlayerObject1, PlayerObject2, Sword1, Sword2, arena, ResetMatch, ResumeFromPresentation, this);
        RefreshStage();
        singleFocusAnchor = new GameObject("Single mode focus").transform;
        singleFocusAnchor.SetParent(PlayerObject1.transform, false);
        singleFocusAnchor.localPosition = new Vector3(0f, 0.2f, 3f);
        phoneHub = PhoneControllerHub.EnsureInstance();
        ResetSword(first, true);
        ResetSword(second, false);
        presentation.SetLayout(DisplayMode, ViewedPlayer);
        SetGameplaySuspended(true);
        gameFlow = gameObject.AddComponent<GameFlowController>();
        gameFlow.Initialize(this, PlayerObject1, PlayerObject2, Sword1, Sword2);
    }

    private static PlayerSword CreatePlayerSword(GameObject sword, GameObject player)
    {
        return new PlayerSword
        {
            Sword = sword.transform,
            Player = player.transform,
            Motion = sword.GetComponent<MotionController>(),
            InitialPosition = player.transform.localPosition,
            InitialRotation = player.transform.localRotation
        };
    }

    // MotionController samples input in Update. This component owns the entire
    // visible pose and reads both players afterwards, regardless of script order.
    private void LateUpdate()
    {
        if (first == null || second == null)
            return;

        if (IsCharacterPreview)
        {
            TickCharacterPreview();
            return;
        }

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame && gameFlow != null && gameFlow.IsPlaying)
        {
            ResetMatch();
            return;
        }

        if (IsPaused)
        {
            ReadInput(first, true, false); ReadInput(second, false, false);
            return;
        }
        matchTime += Time.unscaledDeltaTime;
        double now = matchTime;
        if (IsSingleMode)
        {
            TickSingleMode(now, Time.unscaledDeltaTime);
            return;
        }
        RoundPhase previousPhase = series.Phase;
        if (!IsFreeMode && series.Tick(now))
        {
            if (series.Phase == RoundPhase.Round && previousPhase == RoundPhase.Result) ResetRound();
            if (series.Phase == RoundPhase.Result) presentation.BeginRoundResult(series.RoundWinner, now, false);
            if (series.Phase == RoundPhase.Fight) { first.InputGeneration++; second.InputGeneration++; }
            if (series.Phase == RoundPhase.Complete) presentation.ShowWinner(series.Winner, now);
        }
        if (!IsFreeMode) presentation.UpdateDuelFlow(series, now);
        if (!IsFreeMode && series.Phase != RoundPhase.Fight)
        {
            CombatInput previewFirst = ReadInput(first, true), previewSecond = ReadInput(second, false);
            presentation.PoseCharacters(now, Time.unscaledDeltaTime, first.TargetRotation, second.TargetRotation,
                previewFirst.Valid && previewFirst.GuardHeld, previewSecond.Valid && previewSecond.GuardHeld,
                previewFirst.Valid ? previewFirst.AngularSpeed : 0f, previewSecond.Valid ? previewSecond.AngularSpeed : 0f, false);
            UpdatePose(first, previewFirst.Valid && previewFirst.GuardHeld);
            UpdatePose(second, previewSecond.Valid && previewSecond.GuardHeld);
            ApplyPose(first, first.TargetPose); ApplyPose(second, second.TargetPose);
            presentation.Tick(now, false);
            return;
        }
        AdvanceReaction(now);
        if (!IsFreeMode) CheckWinner();
        bool alreadyFrozen = reactionActive && now < reactionStart + hitStopSeconds;
        if (!alreadyFrozen && match.Winner == MatchWinner.None)
        {
            RestoreDuelSpacing(Time.unscaledDeltaTime);
            FaceOpponent(Time.unscaledDeltaTime);
        }
        CombatInput firstInput = ReadInput(first, true);
        CombatInput secondInput = ReadInput(second, false);
        bool firstGuard = firstInput.Valid && firstInput.GuardHeld;
        bool secondGuard = secondInput.Valid && secondInput.GuardHeld;
        presentation.PoseCharacters(now, Time.unscaledDeltaTime, first.TargetRotation, second.TargetRotation,
            firstGuard, secondGuard,
            firstInput.Valid ? firstInput.AngularSpeed : 0f, secondInput.Valid ? secondInput.AngularSpeed : 0f, alreadyFrozen);
        UpdatePose(first, firstGuard);
        UpdatePose(second, secondGuard);

        CombatPose firstGeometry = PlayerCombatPose(first, firstInput, false);
        CombatPose secondGeometry = PlayerCombatPose(second, secondInput, false);
        CombatResult result = IsFreeMode || series.Phase == RoundPhase.Fight
            ? match.Step(firstInput, secondInput, firstGeometry, secondGeometry, now) : default;
        if (result.Outcome != CombatOutcome.None)
            BeginReaction(result, now);

        // Hold the visible swords during hit stop without stopping WebRTC,
        // sensor sampling, UI or the clock used to detect stale input.
        bool frozen = reactionActive && now < reactionStart + hitStopSeconds;
        ApplyPose(first, frozen && !firstGuard ? frozenFirstPose : first.TargetPose);
        ApplyPose(second, frozen && !secondGuard ? frozenSecondPose : second.TargetPose);
        presentation.Tick(now, frozen);
    }

    private MotionController SelectSingleMotion()
    {
        MotionController a = first.Motion, b = second.Motion;
        MotionController selected = singleMotion;
        // A reset briefly invalidates orientation until MotionController runs.
        // Keep the selected hand through that frame rather than swapping swords.
        if (selected != null && selected == first.PoseMotion && !selected.IsStale
            && selected.ActiveSource == first.PoseSource
            && (first.RecenterPending || selected.RecenterVersion != first.MotionRecenterVersion)) return selected;
        bool aValid = a != null && a.IsPoseValid, bValid = b != null && b.IsPoseValid;
        double thrustAge = SwordPoseTime - first.ThrustAt;
        if (selected != null && selected.IsPoseValid && thrustAge >= 0d && thrustAge < ThrustPose.DurationSeconds) return selected;
        if (!aValid && !bValid) selected = a != null && a.IsConnected ? a : b;
        else if (!aValid) selected = b;
        else if (!bValid) selected = a;
        else if (selected != a && selected != b) selected = a;
        else
        {
            MotionController other = selected == a ? b : a;
            if (other.AngularSpeedDegrees > 25f && other.AngularSpeedDegrees > selected.AngularSpeedDegrees + 15f)
                selected = other;
        }
        if (singleMotion != selected)
        {
            singleMotion = selected;
            first.InputGeneration++;
            first.MotionGeneration = -1;
            first.CombatBaseline = false;
            first.ThrustAt = -100d;
        }
        return selected;
    }

    private void SetSingleCharacterHidden(bool hidden)
    {
        if (!hidden)
        {
            foreach (Renderer renderer in hiddenSingleRenderers) if (renderer != null) renderer.enabled = true;
            foreach (Collider collider in hiddenSingleColliders) if (collider != null) collider.enabled = true;
            hiddenSingleRenderers.Clear(); hiddenSingleColliders.Clear();
            return;
        }
        foreach (Renderer renderer in PlayerObject2.GetComponentsInChildren<Renderer>(true))
            if (renderer.enabled) { hiddenSingleRenderers.Add(renderer); renderer.enabled = false; }
        foreach (Collider collider in PlayerObject2.GetComponentsInChildren<Collider>(true))
            if (collider.enabled) { hiddenSingleColliders.Add(collider); collider.enabled = false; }
    }

    private CombatInput ReadInput(PlayerSword player, bool isFirst, bool integrateKeyboard = true)
    {
        bool acceptThrust = integrateKeyboard && (IsCharacterPreview || !IsPaused && (IsSingleMode
            ? !singleGameOver && !SingleAdvancing && matchTime - singleStarted >= 2.5
            : IsFreeMode || series.Phase == RoundPhase.Fight));
        if (!IsCharacterPreview && (IsSingleMode ? SinglePlayerBlocked
            : match.IsResolving(matchTime) || match.IsStunned(player == first ? 0 : 1, matchTime))) acceptThrust = false;
        bool thrust = false;
        MotionController thrustSource = null;
        if (IsSingleMode && player == first)
        {
            // Drain both slots even during menus, travel, and introductions.
            bool a = first.Motion != null && first.Motion.TryConsumeThrust();
            bool b = second.Motion != null && second.Motion.TryConsumeThrust();
            if (acceptThrust)
            {
                if (b && second.Motion.IsPoseValid && singleMotion == second.Motion) thrustSource = second.Motion;
                else if (a && first.Motion.IsPoseValid) thrustSource = first.Motion;
                else if (b && second.Motion.IsPoseValid) thrustSource = second.Motion;
            }
            if (thrustSource != null && singleMotion != thrustSource)
            {
                singleMotion = thrustSource; first.InputGeneration++; first.MotionGeneration = -1; first.CombatBaseline = false;
            }
            thrust = thrustSource != null;
        }
        MotionController motion = thrustSource != null ? thrustSource : IsSingleMode && player == first ? SelectSingleMotion() : player.Motion;
        ControllerInputSource poseSource = motion != null ? motion.ActiveSource : ControllerInputSource.None;
        int recenterVersion = motion != null ? motion.RecenterVersion : 0;
        if (player.PoseMotion == motion && player.PoseSource == poseSource)
        {
            if (player.MotionRecenterVersion != recenterVersion)
            {
                player.RecenterFrom = player.TargetPose;
                player.RecenterPending = true;
                player.ThrustAt = -100d;
            }
        }
        else player.RecenterPending = false;
        player.PoseMotion = motion;
        player.PoseSource = poseSource;
        player.MotionRecenterVersion = recenterVersion;
        if (!(IsSingleMode && player == first) && motion != null)
            thrust = motion.TryConsumeThrust() && acceptThrust && motion.IsPoseValid;
        bool usingPhone = motion != null && motion.IsConnected;
        if (usingPhone != player.UsingPhone)
        {
            player.UsingPhone = usingPhone;
            player.InputGeneration++;
            presentation.ClearMotionHistory();
            player.Pitch = -90f;
            player.Roll = 0f;
        }

        Keyboard keyboard = Keyboard.current;
        float inputDelta = integrateKeyboard ? Time.unscaledDeltaTime : 0f;
        bool forward = keyboard != null && (IsSingleMode ? keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed : isFirst ? keyboard.wKey.isPressed : keyboard.upArrowKey.isPressed);
        bool upright = keyboard != null && (IsSingleMode ? keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed : isFirst ? keyboard.sKey.isPressed : keyboard.downArrowKey.isPressed);
        bool left = keyboard != null && (IsSingleMode ? keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed : isFirst ? keyboard.aKey.isPressed : keyboard.leftArrowKey.isPressed);
        bool right = keyboard != null && (IsSingleMode ? keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed : isFirst ? keyboard.dKey.isPressed : keyboard.rightArrowKey.isPressed);
        bool keyboardGuard = keyboard != null && (IsSingleMode ? keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed : isFirst ? keyboard.leftShiftKey.isPressed : keyboard.rightShiftKey.isPressed);
        float pitchSpeed = ((forward ? 1f : 0f) - (upright ? 1f : 0f)) * radsX;
        float rollSpeed = ((left ? 1f : 0f) - (right ? 1f : 0f)) * radsZ;
        float previousPitch = player.Pitch;
        player.Pitch = Mathf.Clamp(player.Pitch + pitchSpeed * inputDelta, -90f, 0f);
        if (player.Pitch == previousPitch) pitchSpeed = 0f;
        player.Roll += rollSpeed * inputDelta;
        float speed = Mathf.Sqrt(pitchSpeed * pitchSpeed + rollSpeed * rollSpeed);
        bool motionValid = usingPhone && motion.IsPoseValid;
        bool valid = !usingPhone || motionValid || forward || upright || left || right || keyboardGuard;
        bool guard = keyboardGuard || (motionValid && motion.GuardHeld)
            || (IsSingleMode && ((first.Motion != null && first.Motion.IsPoseValid && first.Motion.GuardHeld)
                || (second.Motion != null && second.Motion.IsPoseValid && second.Motion.GuardHeld)));
        if (usingPhone && player.MotionGeneration != motion.PoseGeneration)
        {
            player.MotionGeneration = motion.PoseGeneration;
            player.InputGeneration++;
            player.ThrustAt = -100d;
            presentation.ClearMotionHistory();
        }
        // Keyboard remains an independent input even with a connected controller.
        // Its offset composes with fresh sensor orientation; stale sensors cannot
        // suppress keyboard guard or supply angular speed for a synthetic strike.
        Quaternion sensorRotation = motionValid ? motion.CurrentLocalRotation : Quaternion.identity;
        if (motionValid && ShouldMirror(isFirst)) sensorRotation = SwordPoseMath.MirrorHorizontal(sensorRotation);
        // Keyboard offsets keep their own axes; the opposite player's sensor
        // correction must not reverse arrows or A/D.
        player.TargetRotation = Quaternion.AngleAxis(player.Roll, Vector3.forward)
            * Quaternion.Euler(motionValid ? player.Pitch + 90f : player.Pitch, 0f, 0f)
            * sensorRotation;
        if (motionValid) speed = Mathf.Max(speed, motion.AngularSpeedDegrees);

        // Measure the swing in the player's frame, so turning to face the
        // opponent never generates a sideways cut or a synthetic attack.
        Vector3 direction = player.TargetRotation * Vector3.forward;
        if (!valid || !integrateKeyboard || player.DirectionGeneration != player.InputGeneration)
        {
            player.CutVelocity = Vector3.zero;
            player.DirectionGeneration = player.InputGeneration;
        }
        else
        {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            player.CutVelocity = CombatContact.CutVelocity(player.CutVelocity, player.PreviousDirection, direction, dt);
        }
        player.PreviousDirection = direction;
        UpdatePose(player, valid && guard);
        // Guard moves the rendered sword across the body. Attack gates and
        // speed must still follow the sensor/keyboard input, not that movement.
        float forwardAngle = Vector3.Angle(WorldSwordDirection(player, player.TargetRotation), player.Player.forward);
        int playerIndex = player == first ? 0 : 1;
        bool stunned = !IsCharacterPreview && (IsSingleMode ? SinglePlayerBlocked
            : match.IsStunned(playerIndex, SwordPoseTime) || match.IsResolving(SwordPoseTime));
        thrust = thrust && motionValid && !guard && !stunned && forwardAngle <= AttackStroke.ThrustAngle;
        if (!valid) player.ThrustAt = -100d;
        if (thrust)
        {
            // Chamber briefly into the player's exact forward direction,
            // regardless of a small sensor angle or the opposite viewport.
            player.ThrustFrom = player.TargetPose;
            player.GuardTransition.Reset();
            player.ThrustAt = SwordPoseTime;
            presentation.ThrustCharacter(playerIndex, player.Player.forward,
                SwordPoseTime, ThrustPose.DurationSeconds);
            UpdatePose(player, false);
        }
        double thrustAge = SwordPoseTime - player.ThrustAt;
        if (!guard && thrustAge >= 0d && thrustAge < ThrustPose.DurationSeconds)
        {
            // Pose alignment and recovery are animation, never new cut input.
            forwardAngle = 0f;
            speed = 0f;
        }
        return new CombatInput(forwardAngle, speed, guard, valid, player.InputGeneration, thrust);
    }

    private void UpdatePose(PlayerSword player, bool guard)
    {
        bool wasGuarding = player.GuardHeld;
        player.GuardHeld = guard;
        if (player.RecenterPending)
        {
            if (player.PoseMotion != null && !player.PoseMotion.IsPoseValid)
            {
                player.TargetPose = player.RecenterFrom;
                return;
            }
            player.RecenterPending = false;
            player.GuardTransition.BeginFrom(player.RecenterFrom, guard, SwordPoseTime);
            player.RecenterPoseUntil = SwordPoseTime + GuardPoseTransition.DurationSeconds;
            player.GuardPoseGeneration = player.InputGeneration;
        }
        if (player.GuardPoseGeneration != player.InputGeneration)
        {
            player.GuardTransition.Reset();
            player.GuardPoseGeneration = player.InputGeneration;
        }
        SwordPose target = SwordPoseMath.Calculate(player.TargetRotation, guard,
            new SwordPoseSettings(PlayerChestCenter(player),
                gripRadius, guardRadius, guardPlaneOffset, guardHeight), ref player.LastGuardDirection);
        bool staggering = !IsCharacterPreview && (IsSingleMode && player == first
            ? SwordPoseTime < singleStunnedUntil : match.IsStunned(player == first ? 0 : 1, SwordPoseTime));
        // Recovery permits defence immediately. Preserve an explicit recenter's
        // smooth pose change, but do not delay a new guard behind recoil.
        if (guard && !wasGuarding && staggering && SwordPoseTime >= player.RecenterPoseUntil)
            player.GuardTransition.Reset();
        player.TargetPose = player.GuardTransition.Update(target, guard, SwordPoseTime);
        if (guard) player.ThrustAt = -100d;
        float age = (float)(SwordPoseTime - player.ThrustAt);
        if (age >= 0f && age < ThrustPose.DurationSeconds)
        {
            SwordPose forwardPose = SwordPoseMath.Calculate(Quaternion.identity, false,
                new SwordPoseSettings(PlayerChestCenter(player),
                    gripRadius, guardRadius, guardPlaneOffset, guardHeight), ref player.LastGuardDirection);
            player.TargetPose = ThrustPose.ApplyCentered(player.TargetPose, player.ThrustFrom, forwardPose, age);
        }
    }

    private Vector3 PlayerChestCenter(PlayerSword player)
    {
        int index = player == first ? 0 : 1;
        Vector3 center = characterChestCenters != null && characters[index] < characterChestCenters.Length
            ? characterChestCenters[characters[index]] : chestCenter;
        return center + presentation.ChestOffset(index);
    }

    private static Vector3 SingleNpcBodyCenter(SingleNpc npc)
        => npc.Root.transform.TransformPoint(npc.ChestCenter + npc.Avatar.ChestOffset);

    private void ApplyPose(PlayerSword player, SwordPose pose)
    {
        if (!IsCharacterPreview && presentation != null)
            pose = presentation.PoseSword(player == first ? 0 : 1, pose, SwordPoseTime, player.GuardHeld);
        player.Sword.SetLocalPositionAndRotation(pose.Position, pose.Rotation);
    }

    private static Vector3 WorldSwordDirection(PlayerSword player, Quaternion rotation)
    {
        Quaternion parentRotation = player.Sword.parent != null ? player.Sword.parent.rotation : Quaternion.identity;
        return parentRotation * rotation * Vector3.forward;
    }

    private static SwordPose SwordAtContact(Transform parent, SwordPose current, CombatPose motion, float fraction)
    {
        SwordPose world = CombatContact.SampleSwordPose(motion, parent.rotation * current.Rotation, fraction);
        return new SwordPose(parent.InverseTransformPoint(world.Position), Quaternion.Inverse(parent.rotation) * world.Rotation);
    }

    private void BeginReaction(CombatResult result, double now)
    {
        reactionActive = true;
        reactionStart = now;
        hitStopSeconds = result.HitStopSeconds;
        pushStart = transform.position;
        PlayerSword attacker = result.Attacker == 0 ? first : second;
        Vector3 cutVelocity = result.Kind == AttackKind.Thrust ? Vector3.zero
            : CombatContact.IncomingCutDirection(PlayerDefencePose(attacker));
        Vector3 forward = first.Player.forward;
        Vector3 displacement = SwordPoseMath.PushDisplacement(result, forward, cutVelocity);
        Vector3 advance = forward * result.SignedPush;
        pushedSword = result.PushedPlayer == 0 ? first : result.PushedPlayer == 1 ? second : null;
        if (IsFreeMode || !RingOutEnabled)
        {
            // Stop the whole exchange at the field boundary, preserving the
            // spacing and the pushed player's sideways reaction.
            Vector3 sideways = displacement - advance;
            float fraction = Mathf.Min(
                FieldMovementFraction(first.Player.position, advance + (pushedSword == first ? sideways : Vector3.zero)),
                FieldMovementFraction(second.Player.position, advance + (pushedSword == second ? sideways : Vector3.zero)));
            displacement *= fraction;
            advance *= fraction;
        }
        pushTarget = pushStart + advance;
        if (pushedSword != null)
        {
            lateralStart = pushedSword.Player.localPosition;
            lateralTarget = lateralStart + transform.InverseTransformVector(displacement - advance);
        }
        // Keep the timed thrust drive visible when a nearby body is already
        // within reach at the start of the extension.
        frozenFirstPose = result.Kind == AttackKind.Thrust && result.Attacker == 0 ? first.TargetPose
            : SwordAtContact(first.Player, first.TargetPose, PlayerDefencePose(first), result.ContactFraction);
        frozenSecondPose = result.Kind == AttackKind.Thrust && result.Attacker == 1 ? second.TargetPose
            : SwordAtContact(second.Player, second.TargetPose, PlayerDefencePose(second), result.ContactFraction);
        // Effects originate from the impact pose, before freezing the blades.
        ApplyPose(first, first.GuardHeld ? first.TargetPose : frozenFirstPose);
        ApplyPose(second, second.GuardHeld ? second.TargetPose : frozenSecondPose);
        Vector3 bladeDirection = WorldSwordDirection(attacker, attacker.TargetPose.Rotation);
        Vector3 direction = result.Kind == AttackKind.Thrust || cutVelocity.sqrMagnitude < .0001f
            ? bladeDirection : cutVelocity;
        if (pushedSword != null) pushedSword.ThrustAt = -100d;
        presentation.PlayAt(result, now, displacement, direction,
            CombatContact.ContactPoint(result, PlayerDefencePose(first), PlayerDefencePose(second)));
    }

    private float FieldMovementFraction(Vector3 position, Vector3 displacement)
        => MovementFraction(position, displacement, FieldRadius);

    private float MovementFraction(Vector3 position, Vector3 displacement, float boundaryRadius)
    {
        Vector2 start = new Vector2(position.x, position.z) - FieldCenter;
        Vector2 step = new Vector2(displacement.x, displacement.z);
        // The stadium's circular floor reaches the stands; use its imported
        // geometry rather than the old cricket field's smaller boundary.
        float radius = boundaryRadius - 0.01f;
        if ((start + step).sqrMagnitude <= radius * radius || step.sqrMagnitude < 0.000001f)
            return 1f;
        float projection = Vector2.Dot(start, step);
        float discriminant = projection * projection - step.sqrMagnitude * (start.sqrMagnitude - radius * radius);
        return Mathf.Clamp01((-projection + Mathf.Sqrt(Mathf.Max(0f, discriminant))) / step.sqrMagnitude);
    }

    private void AdvanceReaction(double now)
    {
        if (!reactionActive)
            return;

        float progress = Mathf.Clamp01((float)(now - reactionStart - hitStopSeconds) / DuelMatch.PushSeconds);
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        transform.position = Vector3.Lerp(pushStart, pushTarget, eased);
        if (pushedSword != null)
            pushedSword.Player.localPosition = Vector3.Lerp(lateralStart, lateralTarget, eased);
        if (progress >= 1f)
            reactionActive = false;
    }

    private void FaceOpponent(float dt)
    {
        Vector3 direction = second.Player.position - first.Player.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        first.Player.rotation = ShowdownCombat.Turn(first.Player.rotation, direction, ref first.TurnVelocity, dt);
        second.Player.rotation = ShowdownCombat.Turn(second.Player.rotation, -direction, ref second.TurnVelocity, dt);
    }

    private void RestoreDuelSpacing(float dt)
    {
        if (reactionActive || (!IsFreeMode && series.Phase != RoundPhase.Fight)) return;

        Vector3 separation = second.Player.position - first.Player.position;
        separation.y = 0f;
        Vector3 initialSeparation = transform.TransformVector(second.InitialPosition - first.InitialPosition);
        initialSeparation.y = 0f;
        // Imported/scaled characters must still remain inside shared blade reach.
        float fightingDistance = Mathf.Min(initialSeparation.magnitude, 3f);
        float excess = separation.magnitude - fightingDistance;
        if (excess <= .001f) return;

        // Keep the pushed fighter at the full recoil position. Close the gap
        // as soon as the displacement finishes, including during guard recovery,
        // so successive exchanges cannot accumulate an unreachable distance.
        PlayerSword approaching = pushedSword == first ? second : first;
        Vector3 direction = separation.normalized * (approaching == first ? 1f : -1f);
        Vector3 step = direction * Mathf.Min(excess, 8f * Mathf.Max(0f, dt));
        if (IsFreeMode || !RingOutEnabled) step *= FieldMovementFraction(approaching.Player.position, step);
        approaching.Player.position += step;
    }

    private void CheckWinner()
    {
        if (!RingOutEnabled || match.Winner != MatchWinner.None)
            return;

        Vector3 firstPosition = first.Player.position;
        Vector3 secondPosition = second.Player.position;
        MatchWinner winner = match.CheckRingOut(
            new Vector2(firstPosition.x, firstPosition.z) - arenaCenter,
            new Vector2(secondPosition.x, secondPosition.z) - arenaCenter, CurrentArenaRadius);
        if (winner == MatchWinner.None)
            return;

        reactionActive = false;
        if (series.EndRound(winner, matchTime))
            presentation.BeginRoundResult(winner, matchTime, true);
    }

    public void ResetMatch()
    {
        if (first == null || second == null)
            return;

        series.Reset(matchTime);
        showdown.Reset();
        singleResolvingUntil = singleStunnedUntil = 0d;
        singlePushActive = singleCrowdInitialized = false;
        singlePushNpc = null;
        singleStarted = matchTime;
        ResetRound();
        ClearSingleNpcs();
        singleCourse.Reset(first.Player.position, FieldCenter, FieldRadius);
        if (IsSingleMode) first.Player.position = singleCourse.StartPosition;
        singleSpawnTimer = 0f;
        singleStroke.Reset();
        singleGameOver = false;
        singleDefeatAt = double.NaN;
        first.CombatBaseline = second.CombatBaseline = false;
        presentation.ShowSingleGameOver(false);
        presentation.SetFocusTarget(0, IsSingleMode ? singleFocusAnchor : null);
        presentation.SetMatchMode(IsSingleMode);
        presentation.SetFreePresentation(IsFreeMode);
    }

    private void ResetRound()
    {
        bool smallStage = !IsSingleMode && !IsFreeMode && StageIndex == 0
            && series.IsFinalRound && chambaraSuddenDeathStage != null;
        if (suddenDeathStageActive != smallStage)
        {
            suddenDeathStageActive = smallStage;
            RefreshStage();
        }
        match.Reset();
        reactionActive = false;
        transform.SetPositionAndRotation(initialPosition, initialRotation);
        first.Player.SetLocalPositionAndRotation(first.InitialPosition, first.InitialRotation);
        second.Player.SetLocalPositionAndRotation(second.InitialPosition, second.InitialRotation);
        presentation.ResetEffects();
        ResetSword(first, true);
        ResetSword(second, false);
        // No scene reload or recalibration: the existing phone sessions survive.
    }

    private void ResumeFromPresentation()
    {
        SetGameplaySuspended(false);
    }

    public void SetPaused(bool paused)
    {
        SetGameplaySuspended(paused);
    }

    public void SetGameplaySuspended(bool suspended)
    {
        if (first == null || presentation == null || IsPaused == suspended) return;
        IsPaused = suspended;
        presentation.SetSuspended(suspended);
        if (!suspended)
        {
            first.InputGeneration++; second.InputGeneration++;
            ReadInput(first, true, false); ReadInput(second, false, false);
            singleStroke.Reset();
            first.CombatBaseline = second.CombatBaseline = false;
            if (!reactionActive || matchTime >= reactionStart + hitStopSeconds)
            { ApplyPose(first, first.TargetPose); ApplyPose(second, second.TargetPose); }
            presentation.ClearMotionHistory();
        }
    }

    private bool ShouldMirror(bool isFirst) => !IsSingleMode && DisplayMode == 2 && (isFirst ? 0 : 1) != ViewedPlayer;

    public void SetGameMode(bool single)
    {
        if (!IsPaused || IsSingleMode == single) return;
        IsSingleMode = single;
        // Keep slot P2 sampling even when its character is hidden.
        PlayerObject2.SetActive(true);
        SetSingleCharacterHidden(single);
        phoneHub.SetSinglePlayerMode(single);
        SetStage(single ? 2 : 0);
        ResetMatch();
        RefreshSettings();
    }

    public void SetFreeMode(bool free)
    {
        if (!IsPaused || IsFreeMode == free || (free && IsSingleMode)) return;
        IsFreeMode = free;
        ResetMatch();
        RefreshSettings();
    }

    public void SetSingleDifficulty(bool easy)
    {
        if (!IsPaused) return;
        IsEasySingleDifficulty = easy;
    }

    public void SetDisplayMode(int mode)
    {
        if (!IsPaused || (mode != 1 && mode != 2) || mode == DisplayMode) return;
        DisplayMode = mode;
        RefreshSettings();
    }

    public void SetViewedPlayer(int playerIndex)
    {
        if (!IsPaused || playerIndex < 0 || playerIndex > 1 || ViewedPlayer == playerIndex) return;
        ViewedPlayer = playerIndex;
        RefreshSettings();
    }

    public void SetCharacter(int playerIndex, int characterIndex)
    {
        if (!IsPaused || playerIndex < 0 || playerIndex > 1 || characterIndex < 0
            || characterIndex >= CharacterCount
            || characters[playerIndex] == characterIndex) return;
        characters[playerIndex] = characterIndex;
        ApplyAppearance(playerIndex);
    }

    public void SetColor(int playerIndex, int colorIndex)
    {
        if (!IsPaused || playerIndex < 0 || playerIndex > 1 || colorIndex < 0
            || colorIndex >= ColorNames.Length || colors[playerIndex] == colorIndex) return;
        colors[playerIndex] = colorIndex;
        ApplyAppearance(playerIndex);
    }

    private void ApplyAppearance(int playerIndex)
    {
        int variant = characters[playerIndex] * ColorNames.Length + colors[playerIndex];
        presentation.SetCharacter(playerIndex, characterBodies[variant], characterBlades[colors[playerIndex]], matchTime);
        RefreshSettings();
        if (IsSingleMode) SetSingleCharacterHidden(true);
    }

    public void SetStage(int stageIndex)
    {
        if (IsSingleMode || IsFreeMode) stageIndex = 2;
        if (!IsPaused || stageIndex < 0 || stageIndex >= StageNames.Length || StageIndex == stageIndex
            || (stageIndex == 1 && colosseumStage == null)
            || (stageIndex == 2 && circularStadiumStage == null)) return;
        StageIndex = stageIndex;
        suddenDeathStageActive = stageIndex == 0 && !IsSingleMode && !IsFreeMode
            && series.IsFinalRound && chambaraSuddenDeathStage != null;
        RefreshStage();
    }

    private void RefreshStage()
    {
        GameObject stage = StageIndex == 0
            ? (suddenDeathStageActive ? chambaraSuddenDeathStage : chambaraStage)
            : StageIndex == 1 ? colosseumStage : circularStadiumStage;
        presentation.SetStage(stage, RingOutEnabled, replacePlatform: StageIndex == 0,
            backgroundPrefab: RingOutEnabled ? circularStadiumStage : null);
        fieldRadius = 0f;
        DuelArena arena = presentation.GetComponent<DuelArena>();
        if (arena != null && StageIndex == 2)
            arena.TryGetFieldBoundary(out fieldCenter, out fieldRadius);
    }

    private void RefreshSettings()
    {
        // Keep the round, player objects and controller sessions. Only refresh
        // presentation and attack baselines when changing pause-screen settings.
        first.InputGeneration++; second.InputGeneration++;
        first.LastGuardDirection = second.LastGuardDirection = Vector3.up;
        presentation.ClearMotionHistory();
        CombatInput firstInput = ReadInput(first, true, false), secondInput = ReadInput(second, false, false);
        bool firstGuard = firstInput.Valid && firstInput.GuardHeld;
        bool secondGuard = secondInput.Valid && secondInput.GuardHeld;
        presentation.PoseCharacters(matchTime, 0f, first.TargetRotation, second.TargetRotation,
            firstGuard, secondGuard, 0f, 0f, false);
        UpdatePose(first, firstGuard); UpdatePose(second, secondGuard);
        if (!reactionActive || matchTime >= reactionStart + hitStopSeconds)
        { ApplyPose(first, first.TargetPose); ApplyPose(second, second.TargetPose); }
        presentation.SetLayout(IsSingleMode ? 2 : DisplayMode, IsSingleMode ? 0 : ViewedPlayer);
        presentation.SetFocusTarget(0, IsSingleMode ? singleFocusAnchor : null);
        presentation.SetFreePresentation(IsFreeMode);
    }

    private void TickSingleMode(double now, float dt)
    {
        SetSingleCharacterHidden(true);
        AdvanceSingleReaction(now);
        if (singleGameOver)
        {
            TickSingleNpcAvatars(now, dt);
            presentation.PoseCharacters(now, dt, Quaternion.identity, Quaternion.identity, false, false, 0f, 0f, false);
            if (showdown.Cleared || now >= singleDefeatAt + SingleDefeatResultDelay)
                presentation.ShowSingleResult(showdown);
            presentation.Tick(now, false);
            return;
        }

        CombatInput input = ReadInput(first, true);
        bool travellingThisFrame = SingleAdvancing;
        if (travellingThisFrame)
        {
            if (double.IsNegativeInfinity(singleTransitionStart)) singleTransitionStart = now;
            Vector3 origin = first.Player.position;
            float transitionAge = (float)(now - singleTransitionStart);
            if (transitionAge < ShowdownCombat.VisibleWalkSeconds + ShowdownCombat.TransitionFadeSeconds)
            {
                float previousAge = transitionAge - dt;
                float walkingDelta = Mathf.Clamp(ShowdownCombat.VisibleWalkSeconds - previousAge, 0f, dt);
                first.Player.position = singleCourse.WalkBeforeFade(origin, walkingDelta);
            }
            else
            {
                presentation.SetShowdownFade(1f);
                first.Player.position = singleCourse.ArriveUnderFade(origin, 1f);
            }
            Vector3 direction = first.Player.position - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude > .000001f)
                first.Player.rotation = ShowdownCombat.Turn(first.Player.rotation, direction, ref first.TurnVelocity, dt);
            // Arrival places the entire next group behind the opaque fade.
            // Old sword deltas never become arrival strikes.
            singleStroke.Reset();
            first.CombatBaseline = false;
            presentation.SetFocusTarget(0, singleFocusAnchor);
            if (!SingleAdvancing)
            {
                singleStarted = now;
                singleSpawnTimer = 0f;
                // Remove the previous group's fallen bodies under the blackout,
                // before the quick fade-in can reveal the next group's spawn.
                ClearSingleNpcs();
                presentation.SetShowdownFade(1f);
                presentation.ClearMotionHistory();
                SpawnSingleRound();
                presentation.ResetShowdownView();
            }
        }
        bool combatFrame = now - singleStarted >= ShowdownCombat.IntroSeconds(SingleSection) && !travellingThisFrame;
        if (!travellingThisFrame)
        {
            singleSpawnTimer -= dt;
            if (singleSpawnTimer <= 0f && singleCourse.CanSpawn) SpawnSingleRound();
            if (combatFrame)
            {
                MoveSingleNpcs(dt);
                FaceSingleTarget(dt);
            }
            else singleStroke.Reset();
            SingleNpc focus = singleActiveNpc;
            presentation.SetFocusTarget(0, focus != null ? focus.Root.transform : singleFocusAnchor);
        }

        // Both player walking and NPC approach/knockback reach the rig before
        // its feet sample the root displacement for this frame.
        TickSingleNpcAvatars(now, dt);
        presentation.PoseCharacters(now, dt, first.TargetRotation, Quaternion.identity,
            input.Valid && input.GuardHeld, false, input.Valid ? input.AngularSpeed : 0f, 0f, false);
        UpdatePose(first, input.Valid && input.GuardHeld);
        ApplyPose(first, first.TargetPose);
        if (combatFrame)
        {
            UpdateSingleAttack(input);
            ResolveSingleNpcAttacks(input);
            if (!singlePushActive) singleCourse.Refresh(first.Player.position);
            if (SingleAdvancing && !travellingThisFrame) singleTransitionStart = now;
        }
        float fade = SingleAdvancing ? Mathf.Clamp01(((float)(now - singleTransitionStart) - ShowdownCombat.VisibleWalkSeconds)
                / ShowdownCombat.TransitionFadeSeconds)
            : SingleSection > 1 ? 1f - Mathf.Clamp01((float)(now - singleStarted) / ShowdownCombat.TransitionFadeSeconds) : 0f;
        presentation.SetShowdownFade(fade);
        if (!singleGameOver)
            presentation.UpdateShowdown(showdown, now, singleStarted, SingleSection, SingleAdvancing, SingleAdvanceProgress);
        if (showdown.Cleared) { singleGameOver = true; presentation.ShowSingleResult(showdown); }
        else if (showdown.Finished && !singleGameOver) BeginSingleDefeat(now);
        presentation.Tick(now, false);
    }

    private int LivingSingleNpcCount()
    {
        int count = 0;
        foreach (SingleNpc npc in singleNpcs) if (!npc.Defeated && npc.Root != null) count++;
        return count;
    }

    private void FaceSingleTarget(float dt)
    {
        SingleNpc target = singleActiveNpc;
        if (target == null) return;
        Vector3 direction = target.Root.transform.position - first.Player.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        first.Player.rotation = ShowdownCombat.FocusTurn(first.Player.rotation, direction, ref first.TurnVelocity, dt);
    }

    private void SpawnSingleNpc()
        => SpawnSingleNpcAt(null);

    private void SpawnSingleRound()
    {
        if (!singleCourse.TryPlanRoundPositions(first.Player.position, first.Player.forward, out Vector3[] positions))
        { singleSpawnTimer = .25f; return; }
        while (singleCourse.CanSpawn)
        {
            int previousCount = showdown.Spawned;
            int index = previousCount - (SingleSection - 1) * ShowdownCourse.ChallengersPerSection;
            SpawnSingleNpcAt(positions[index]);
            if (showdown.Spawned == previousCount) break;
        }
        singleSpawnTimer = .25f;
    }

    private void SpawnSingleNpcAt(Vector3? roundPosition)
    {
        if (!singleCourse.CanSpawn || characterBodies == null || characterBodies.Length == 0 || characterBlades == null || characterBlades.Length == 0)
            return;
        int character = Random.Range(0, CharacterCount);
        int color = Random.Range(0, characterBlades.Length);
        int bodyIndex = character * ColorNames.Length + color;
        if (bodyIndex < 0 || bodyIndex >= characterBodies.Length || characterBodies[bodyIndex] == null
            || characterBlades[color] == null) return;

        Vector3 spawn = roundPosition ?? first.Player.position + first.Player.forward * 7f;
        bool spawnClear = false;
        if (roundPosition.HasValue)
        {
            singleObstaclePositions.Clear();
            foreach (SingleNpc other in singleNpcs)
                if (!other.Defeated) singleObstaclePositions.Add(other.Root.transform.position);
            spawnClear = ShowdownCombat.CanOccupy(spawn, first.Player.position, singleObstaclePositions);
        }
        for (int attempt = 0; !spawnClear && !roundPosition.HasValue && attempt < 48; attempt++)
        {
            Vector3 spawnDirection = Quaternion.Euler(0f, Random.Range(-75f, 75f), 0f) * first.Player.forward;
            spawn = singleCourse.ConstrainOpponent(first.Player.position + spawnDirection * 7f);
            bool clear = Vector3.Distance(spawn, first.Player.position) >= ShowdownCombat.MinimumSpawnDistance;
            foreach (SingleNpc other in singleNpcs)
                if (!other.Defeated && Vector3.Distance(spawn, other.Root.transform.position) < ShowdownCombat.MinimumSpacing)
                { clear = false; break; }
            if (clear) { spawnClear = true; break; }
        }
        if (!spawnClear)
        {
            singleObstaclePositions.Clear();
            foreach (SingleNpc other in singleNpcs)
                if (!other.Defeated) singleObstaclePositions.Add(other.Root.transform.position);
            spawnClear = ShowdownCombat.TryFindSpawn(first.Player.position, first.Player.forward,
                singleObstaclePositions, singleCourse.ConstrainOpponent, out spawn);
        }
        if (!spawnClear || !singleCourse.TrySpawn()) return;
        var root = new GameObject("Challenger " + showdown.Spawned);
        root.transform.SetParent(transform, false);
        root.transform.position = new Vector3(spawn.x, first.Player.position.y, spawn.z);
        root.transform.rotation = Quaternion.LookRotation(first.Player.position - root.transform.position);
        GameObject body = Instantiate(characterBodies[bodyIndex], root.transform, false);
        body.name = characterBodies[bodyIndex].name;
        body.transform.localPosition = Vector3.down;
        var swordObject = new GameObject("NPC sword");
        Transform sword = swordObject.transform;
        sword.SetParent(root.transform, false);
        sword.localPosition = new Vector3(0f, 0.67349774f, 0.55f);
        sword.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        GameObject blade = Instantiate(characterBlades[color], sword, false);
        blade.name = characterBlades[color].name;
        int maximumHearts = ShowdownCombat.EnemyHearts(showdown.Spawned);
        var npc = new SingleNpc
        {
            Root = root,
            Sword = sword,
            Avatar = new DuelAvatar(root.transform, sword),
            GuardInitialAngle = Random.Range(0f, 360f),
            GuardMotionPhase = Random.Range(0f, Mathf.PI * 2f),
            GuardMotionStarted = matchTime,
            ChestCenter = characterChestCenters != null && character < characterChestCenters.Length
                ? characterChestCenters[character] : chestCenter,
            Hearts = maximumHearts,
            MaximumHearts = maximumHearts,
            WaitingAnchor = root.transform.position,
            WaitingPhase = showdown.Spawned * 2.399963f,
            OpeningAttack = ShowdownCombat.BeginsWithAttack(showdown.Spawned),
            ApproachAttack = !IsEasySingleDifficulty && showdown.Spawned % 4 == 2
        };
        // Opening attackers prepare as they reach range. Others begin their
        // guard pause on arrival, rather than spending it while walking.
        npc.NextAttack = npc.OpeningAttack || npc.ApproachAttack ? matchTime : double.PositiveInfinity;
        npc.NextGuardChange = matchTime + SingleGuardInterval(npc);
        var statusObject = new GameObject("Enemy health", typeof(RectTransform));
        statusObject.transform.SetParent(root.transform, false);
        statusObject.transform.localPosition = Vector3.up * 1.4f;
        npc.Status = statusObject.AddComponent<SingleEnemyStatus>();
        npc.Status.Initialize(npc.Hearts);
        singleNpcs.Add(npc);
        UpdateNpcGuard(npc);
        npc.VisualBasePose = new SwordPose(sword.localPosition, sword.localRotation);
    }

    private void MoveSingleNpcs(float dt)
    {
        ShowdownDifficulty difficulty = SingleDifficulty;
        SelectSingleOpponent();
        UpdateSingleCrowdCenter();
        SingleNpc approaching = NextSingleOpponent();
        foreach (SingleNpc npc in singleNpcs)
        {
            if (npc.Defeated) continue;
            npc.Sword.SetLocalPositionAndRotation(npc.VisualBasePose.Position, npc.VisualBasePose.Rotation);
            // A stagger stops attacking and walking, but its chest-front guard
            // remains valid. It never grants immunity from another new stroke.
            if (matchTime < npc.StunnedUntil)
            {
                npc.AttackTimer = -1f;
                npc.Stroke.Reset();
                UpdateNpcGuard(npc, 0f);
                continue;
            }
            if (singlePushActive && npc == singlePushNpc)
            {
                UpdateNpcGuard(npc, dt);
                continue;
            }
            if (EasySinglePlayerStaggered)
            {
                CancelSingleNpcAttack(npc);
                if (npc == singleActiveNpc) continue;
            }
            Transform root = npc.Root.transform;
            Vector3 towardPlayer = first.Player.position - root.position;
            towardPlayer.y = 0f;
            bool focusReady = npc == singleActiveNpc && SingleOpponentVisible(npc);
            if (npc == singleActiveNpc && !npc.Engaged)
            {
                if (!focusReady) npc.FacingSince = double.PositiveInfinity;
                else
                {
                    if (double.IsPositiveInfinity(npc.FacingSince)) npc.FacingSince = matchTime;
                    npc.Engaged = matchTime - npc.FacingSince >= ShowdownCombat.FocusHoldSeconds;
                }
            }
            bool beganAttack = false;
            if (npc.AttackTimer < 0f)
            {
                if (npc == singleActiveNpc) AdvanceSingleNpc(npc, dt, difficulty.ApproachSpeed,
                    npc.Engaged && focusReady ? 1.8f : ShowdownCombat.NextOpponentRange);
                else if (npc == approaching) AdvanceSingleNpc(npc, dt, difficulty.ApproachSpeed, ShowdownCombat.NextOpponentRange);
                else WanderSingleNpc(npc, dt);
                towardPlayer = first.Player.position - root.position; towardPlayer.y = 0f;
                root.rotation = ShowdownCombat.Turn(root.rotation, towardPlayer, ref npc.TurnVelocity, dt);
                singleObstaclePositions.Clear();
                foreach (SingleNpc other in singleNpcs)
                    if (other != npc && !other.Defeated) singleObstaclePositions.Add(other.Root.transform.position);
                Vector3 target = first.Player.position - root.position; target.y = 0f;
                bool inRange = npc == singleActiveNpc && npc.Engaged && focusReady && target.magnitude >= 1.5f
                    && Vector3.Angle(root.forward, target) < 12f
                    && ShowdownCombat.CanAttack(root.position, first.Player.position, singleObstaclePositions,
                        npc.ApproachAttack ? ShowdownCombat.ApproachAttackRange(difficulty)
                        : npc.OpeningAttack ? ShowdownCombat.OpeningAttackRange : ShowdownCombat.AttackRange);
                if (inRange && double.IsPositiveInfinity(npc.NextAttack)) npc.NextAttack = matchTime + SingleAttackWait(npc);
                if (inRange && matchTime >= npc.AttackNotBefore && !EasySinglePlayerStaggered
                    && (matchTime >= npc.NextAttack || matchTime < singleStunnedUntil))
                {
                    beganAttack = true;
                    npc.AdvanceDuringWindup = npc.OpeningAttack || npc.ApproachAttack;
                    npc.OpeningAttack = false;
                    npc.ApproachAttack = false;
                    // Counters keep the same stage speed as opening attacks.
                    npc.WindupSeconds = difficulty.WindupSeconds;
                    npc.SwingSeconds = difficulty.SwingSeconds;
                    npc.AttackTimer = npc.WindupSeconds + npc.SwingSeconds;
                    npc.ContactResolved = false;
                    // Aim through the same chest-front defence center used by
                    // SwordPose, so either perpendicular guard can intercept.
                    Vector3 defenceCenter = first.Player.TransformPoint(PlayerChestCenter(first)
                        + Vector3.up * guardHeight + Vector3.forward * guardPlaneOffset);
                    npc.AttackTarget = defenceCenter;
                    npc.AttackReadyAngle = Random.Range(-90f, 90f);
                    npc.AttackReadyPhase = Random.Range(0f, Mathf.PI * 2f);
                    npc.AttackReadyDirection = ShowdownCombat.WindupReadyDirection(npc.AttackReadyAngle, npc.AttackReadyPhase, 0f);
                    npc.LastGeneratedReadyDirection = npc.AttackReadyDirection;
                    npc.AttackReadyOverride = npc.AttackReadyCommitted = false;
                    var cutSettings = new SwordPoseSettings(npc.ChestCenter, gripRadius, guardRadius, guardPlaneOffset, guardHeight);
                    npc.AttackAim = ShowdownCombat.CutAim(root.InverseTransformPoint(defenceCenter), cutSettings);
                    SwordPose start = ShowdownCombat.CutPose(npc.AttackAim, npc.AttackReadyDirection, 0f, cutSettings);
                    SwordPose end = ShowdownCombat.CutPose(npc.AttackAim, npc.AttackReadyDirection, 1f, cutSettings);
                    npc.AttackDirection = -root.TransformDirection(Vector3.ProjectOnPlane(npc.AttackReadyDirection, npc.AttackAim).normalized);
                    npc.AttackStart = root.TransformDirection(start.Direction);
                    npc.AttackEnd = root.TransformDirection(end.Direction);
                    npc.AttackEndGrip = root.TransformPoint(end.Position);
                    npc.WindupRotation = npc.Sword.rotation;
                    npc.WindupGrip = npc.Sword.localPosition;
                    npc.AttackStartRotation = root.rotation * start.Rotation;
                    npc.PreviousGrip = npc.Sword.position;
                    npc.PreviousBlade = npc.Sword.forward;
                    npc.CutVelocity = Vector3.zero;
                    npc.Stroke.Reset();
                    npc.Stroke.Update(new CombatInput(Vector3.Angle(start.Direction, Vector3.forward), 0f), matchTime);
                }
            }
            UpdateNpcGuard(npc, dt);
            if (npc.AttackTimer >= 0f)
            {
                float before = npc.AttackTimer;
                npc.AttackTimer = Mathf.Max(0f, npc.AttackTimer - dt);
                if (before >= npc.SwingSeconds && !npc.AttackReadyCommitted)
                {
                    // Keep explicit legacy stroke fixtures stable. Live NPCs
                    // own this direction and vary it only during preparation.
                    if (Vector3.Angle(npc.AttackReadyDirection, npc.LastGeneratedReadyDirection) > .01f) npc.AttackReadyOverride = true;
                    if (npc.AttackTimer <= npc.SwingSeconds)
                    {
                        float previousPreparation = (npc.WindupSeconds + npc.SwingSeconds - before) / npc.WindupSeconds;
                        if (!npc.AttackReadyOverride && previousPreparation >= .5f)
                            npc.AttackReadyDirection = ShowdownCombat.CommitUpperReady(npc.VisualBasePose.Direction, npc.AttackReadyDirection);
                        npc.AttackReadyCommitted = true;
                    }
                    else if (!npc.AttackReadyOverride)
                    {
                        float preparation = (npc.WindupSeconds + npc.SwingSeconds - npc.AttackTimer) / npc.WindupSeconds;
                        npc.AttackReadyDirection = ShowdownCombat.WindupReadyDirection(npc.AttackReadyAngle, npc.AttackReadyPhase, preparation);
                    }
                    npc.LastGeneratedReadyDirection = npc.AttackReadyDirection;
                }
                if (before >= npc.SwingSeconds)
                {
                    if (npc.AdvanceDuringWindup && !beganAttack && before > npc.SwingSeconds)
                        AdvanceSingleNpc(npc, Mathf.Min(dt, before - npc.SwingSeconds), difficulty.ApproachSpeed);
                    var movingSettings = new SwordPoseSettings(npc.ChestCenter, gripRadius, guardRadius, guardPlaneOffset, guardHeight);
                    npc.AttackAim = ShowdownCombat.CutAim(root.InverseTransformPoint(npc.AttackTarget), movingSettings);
                    SwordPose movingStart = ShowdownCombat.CutPose(npc.AttackAim, npc.AttackReadyDirection, 0f, movingSettings);
                    SwordPose movingEnd = ShowdownCombat.CutPose(npc.AttackAim, npc.AttackReadyDirection, 1f, movingSettings);
                    npc.AttackStart = root.TransformDirection(movingStart.Direction);
                    npc.AttackEnd = root.TransformDirection(movingEnd.Direction);
                    npc.AttackEndGrip = root.TransformPoint(movingEnd.Position);
                    npc.AttackStartRotation = root.rotation * movingStart.Rotation;
                }
                if (npc.AttackTimer >= npc.SwingSeconds)
                {
                    // The opening attacker can close distance while preparing,
                    // but its announced target remains fixed in world space.
                    var cutSettings = new SwordPoseSettings(npc.ChestCenter, gripRadius, guardRadius, guardPlaneOffset, guardHeight);
                    npc.AttackAim = ShowdownCombat.CutAim(root.InverseTransformPoint(npc.AttackTarget), cutSettings);
                    SwordPose start = ShowdownCombat.CutPose(npc.AttackAim, npc.AttackReadyDirection, 0f, cutSettings);
                    SwordPose end = ShowdownCombat.CutPose(npc.AttackAim, npc.AttackReadyDirection, 1f, cutSettings);
                    npc.AttackDirection = -root.TransformDirection(Vector3.ProjectOnPlane(npc.AttackReadyDirection, npc.AttackAim).normalized);
                    npc.AttackStart = root.TransformDirection(start.Direction);
                    npc.AttackEnd = root.TransformDirection(end.Direction);
                    npc.AttackEndGrip = root.TransformPoint(end.Position);
                    npc.AttackStartRotation = root.rotation * start.Rotation;
                    float preparation = Mathf.Clamp01((npc.WindupSeconds + npc.SwingSeconds - npc.AttackTimer) / npc.WindupSeconds);
                    SwordPose preparing = ShowdownCombat.BlendFrontPose(
                        new SwordPose(npc.WindupGrip, Quaternion.Inverse(root.rotation) * npc.WindupRotation), start,
                        Mathf.SmoothStep(0f, 1f, preparation));
                    npc.Sword.SetLocalPositionAndRotation(preparing.Position, preparing.Rotation);
                }
                else
                {
                    // When a frame straddles windup and swing, collision starts
                    // at the ready blade, never along its harmless preparation.
                    var cutSettings = new SwordPoseSettings(npc.ChestCenter, gripRadius, guardRadius, guardPlaneOffset, guardHeight);
                    if (before >= npc.SwingSeconds)
                    {
                        SwordPose start = ShowdownCombat.CutPose(npc.AttackAim, npc.AttackReadyDirection, 0f, cutSettings);
                        npc.PreviousGrip = root.TransformPoint(start.Position);
                        npc.PreviousBlade = root.TransformDirection(start.Direction);
                    }
                    float progress = 1f - npc.AttackTimer / npc.SwingSeconds;
                    SwordPose swingPose = ShowdownCombat.CutPose(npc.AttackAim, npc.AttackReadyDirection, progress, cutSettings);
                    Vector3 previousDirection = root.InverseTransformDirection(npc.PreviousBlade);
                    npc.CutVelocity = CombatContact.CutVelocity(npc.CutVelocity, previousDirection, swingPose.Direction, dt);
                    npc.AttackDirection = root.TransformDirection(npc.CutVelocity);
                    float speed = Vector3.Angle(previousDirection, swingPose.Direction) / Mathf.Max(dt, .0001f);
                    Vector3 grip = root.TransformPoint(swingPose.Position), blade = root.TransformDirection(swingPose.Direction);
                    var strokePose = new CombatPose(npc.PreviousGrip, npc.PreviousBlade, grip, blade,
                        SingleNpcBodyCenter(npc), root.forward, npc.AttackDirection, npc.AttackEndGrip, npc.AttackEnd);

                    npc.Stroke.Update(new CombatInput(Vector3.Angle(swingPose.Direction, Vector3.forward), speed), strokePose, matchTime,
                        matchTime < npc.StunnedUntil);
                    npc.Sword.SetLocalPositionAndRotation(swingPose.Position, swingPose.Rotation);
                }
            }
            npc.VisualBasePose = new SwordPose(npc.Sword.localPosition, npc.Sword.localRotation);
        }
    }

    private bool SingleOpponentVisible(SingleNpc npc)
    {
        Camera view = first.Player.GetComponentInChildren<Camera>();
        return view != null && view.isActiveAndEnabled
            && ShowdownCombat.FocusReady(first.Player.position, first.Player.forward,
                view.transform.position, view.transform.forward, npc.Root.transform.position);
    }

    private bool EasySinglePlayerStaggered => IsEasySingleDifficulty
        && (matchTime < singleStunnedUntil || showdown.IsRecovering(matchTime));

    private void CancelSingleNpcAttack(SingleNpc npc)
    {
        npc.AttackTimer = -1f;
        npc.ContactResolved = true;
        npc.Stroke.Reset();
        npc.AdvanceDuringWindup = false;
        npc.CutVelocity = Vector3.zero;
        npc.NextAttack = System.Math.Max(npc.NextAttack, matchTime);
        UpdateNpcGuard(npc, 0f);
        npc.PreviousGrip = npc.Root.transform.TransformPoint(npc.VisualBasePose.Position);
        npc.PreviousBlade = npc.Root.transform.TransformDirection(npc.VisualBasePose.Direction);
    }

    private void AdvanceSingleNpc(SingleNpc npc, float dt, float speed, float stoppingDistance = 1.8f)
    {
        Transform root = npc.Root.transform;
        Vector3 towardPlayer = first.Player.position - root.position; towardPlayer.y = 0f;
        float distance = towardPlayer.magnitude;
        if (matchTime < npc.StunnedUntil) return;
        bool retreat = distance < stoppingDistance && (npc != singleActiveNpc || distance < 1.5f);
        if (!retreat && distance <= stoppingDistance) return;
        Vector3 heading = distance > .001f ? towardPlayer / distance : root.forward;
        Vector3 proposed = root.position + heading * (retreat ? -1f : 1f)
            * Mathf.Min(speed * dt, Mathf.Abs(distance - stoppingDistance));
        foreach (SingleNpc other in singleNpcs)
        {
            if (other == npc || other.Defeated) continue;
            Vector3 gap = proposed - other.Root.transform.position; gap.y = 0f;
            if (gap.magnitude >= ShowdownCombat.MinimumSpacing) continue;
            Vector3 away = gap.sqrMagnitude > .00001f ? gap.normalized : root.right;
            proposed += away * (ShowdownCombat.MinimumSpacing - gap.magnitude);
        }
        Vector3 playerGap = proposed - first.Player.position; playerGap.y = 0f;
        if (playerGap.magnitude < 1.5f)
            proposed = first.Player.position + playerGap.normalized * 1.5f;
        proposed = singleCourse.ConstrainOpponent(Vector3.MoveTowards(root.position, proposed, speed * dt));
        singleObstaclePositions.Clear();
        foreach (SingleNpc other in singleNpcs)
            if (other != npc && !other.Defeated) singleObstaclePositions.Add(other.Root.transform.position);
        bool clear = ShowdownCombat.CanOccupy(proposed, first.Player.position, singleObstaclePositions);
        if (!clear && retreat && Vector3.Distance(proposed, first.Player.position) > distance)
        {
            clear = true;
            foreach (Vector3 obstacle in singleObstaclePositions)
                if (Vector3.Distance(proposed, obstacle) < ShowdownCombat.MinimumSpacing
                    && Vector3.Distance(proposed, obstacle) < Vector3.Distance(root.position, obstacle)) clear = false;
        }
        if (clear) root.position = proposed;
    }

    private void UpdateSingleCrowdCenter()
    {
        SingleNpc target = singleActiveNpc ?? (singlePushActive ? singlePushNpc : null);
        Vector3 center = target != null && target.Root != null
            ? Vector3.Lerp(first.Player.position, target.Root.transform.position, .35f)
            : first.Player.position + singleCrowdOffset;
        if (!singleCrowdInitialized)
        {
            singleCrowdInitialized = true;
            singleCrowdCenter = center;
            singleCrowdOffset = center - first.Player.position;
            return;
        }
        Vector3 delta = center - singleCrowdCenter; delta.y = 0f;
        foreach (SingleNpc npc in singleNpcs)
            if (!npc.Defeated) npc.WaitingAnchor = singleCourse.ConstrainOpponent(npc.WaitingAnchor + delta);
        singleCrowdCenter = center;
        singleCrowdOffset = center - first.Player.position;
    }

    private void WanderSingleNpc(SingleNpc npc, float dt)
    {
        Transform root = npc.Root.transform;
        Vector3 target = ShowdownCombat.WaitingTarget(npc.WaitingAnchor, npc.WaitingPhase, matchTime);
        Vector3 outward = target - singleCrowdCenter; outward.y = 0f;
        if (outward.magnitude < 5.5f)
        {
            if (outward.sqrMagnitude < .001f) outward = Quaternion.Euler(0f, npc.WaitingPhase * Mathf.Rad2Deg, 0f) * Vector3.forward;
            target = singleCrowdCenter + outward.normalized * 5.5f;
            target.y = root.position.y;
        }
        float speed = Vector3.Distance(root.position, target) > 2f ? 3.5f : ShowdownCombat.WaitingWalkSpeed;
        Vector3 proposed = singleCourse.ConstrainOpponent(Vector3.MoveTowards(root.position, target, speed * dt));
        singleObstaclePositions.Clear();
        foreach (SingleNpc other in singleNpcs)
            if (other != npc && !other.Defeated) singleObstaclePositions.Add(other.Root.transform.position);
        bool clear = ShowdownCombat.CanOccupy(proposed, first.Player.position, singleObstaclePositions);
        // A pushed player may enter the crowd before it can take a step.
        // Permit escape from existing overlap, while never worsening another.
        if (!clear)
        {
            clear = Vector3.Distance(proposed, first.Player.position) > Vector3.Distance(root.position, first.Player.position);
            foreach (Vector3 obstacle in singleObstaclePositions)
                if (Vector3.Distance(proposed, obstacle) < ShowdownCombat.MinimumSpacing
                    && Vector3.Distance(proposed, obstacle) < Vector3.Distance(root.position, obstacle)) clear = false;
        }
        if (clear) root.position = proposed;
    }

    private bool SingleNpcGuarding(SingleNpc npc, double now)
        => !npc.Defeated && npc.AttackTimer < 0f
            && (now < npc.StunnedUntil || npc == singleActiveNpc && !npc.ContinuousAttack);

    private void UpdateNpcGuard(SingleNpc npc, float dt = 0f)
    {
        bool guarding = SingleNpcGuarding(npc, matchTime);
        bool enteringGuard = guarding && !npc.CombatGuardHeld;
        npc.CombatGuardHeld = guarding;
        Vector3 towardPlayer = first.Player.position - npc.Root.transform.position;
        towardPlayer.y = 0f;
        if (towardPlayer.sqrMagnitude < 0.001f) return;
        // Continuous attackers retain their last cut between preparations.
        // Their own stagger still forces the shared guard-only recovery.
        if (npc == singleActiveNpc && npc.ContinuousAttack && matchTime >= npc.StunnedUntil) return;
        // Only the engaged opponent guards; the rest keep their swords relaxed.
        if (npc.GuardOctant < 0 && dt > 0f)
            npc.GuardBias = Mathf.MoveTowards(npc.GuardBias, npc.GuardBiasTarget, 30f * dt);
        Vector3 localDirection = npc.GuardOctant >= 0 ? ShowdownCombat.ReadyDirection(npc.GuardOctant)
            : ShowdownCombat.PlaneDirection(ShowdownCombat.GuardAngleDegrees(npc.GuardInitialAngle, npc.GuardMotionPhase,
                matchTime - npc.GuardMotionStarted, npc.MaximumHearts) + npc.GuardBias);
        npc.GuardDirection = npc.Root.transform.TransformDirection(localDirection);
        if (npc.AttackTimer < 0f)
        {
            Vector3 poseDirection = guarding ? localDirection : new Vector3(.65f, -.35f, .6f).normalized;
            Quaternion input = Quaternion.FromToRotation(Vector3.forward, poseDirection);
            SwordPose pose = SwordPoseMath.Calculate(input, guarding,
                new SwordPoseSettings(npc.ChestCenter + npc.Avatar.ChestOffset,
                    gripRadius, guardRadius, guardPlaneOffset, guardHeight), ref npc.GuardPoseDirection);
            npc.Sword.localPosition = dt <= 0f ? pose.Position : guarding
                ? SwordPoseMath.FollowGuardGrip(npc.Sword.localPosition, pose.Position, pose.Direction, dt)
                : Vector3.MoveTowards(npc.Sword.localPosition, pose.Position, 3f * dt);
            npc.Sword.localRotation = dt <= 0f ? pose.Rotation
                : ShowdownCombat.TurnBladeThroughFront(npc.Sword.localRotation, pose.Rotation, 300f * dt);
            npc.VisualBasePose = new SwordPose(npc.Sword.localPosition, npc.Sword.localRotation);
            if (enteringGuard)
            {
                // A newly held guard is valid immediately, just like a player
                // changing GuardHeld. Never defend with its former attack pose.
                npc.PreviousGrip = npc.Root.transform.TransformPoint(npc.VisualBasePose.Position);
                npc.PreviousBlade = npc.Root.transform.TransformDirection(npc.VisualBasePose.Direction);
            }
        }
    }

    // These pacing values belong to this project. Strength stays tied to the
    // initial health, so a wounded strong opponent keeps its faster responses.
    private float SingleAttackWait(SingleNpc npc)
        => SingleDifficulty.AttackWait * (npc.MaximumHearts >= 3 ? .65f : npc.MaximumHearts == 2 ? .82f : 1f);

    private static float SingleGuardInterval(SingleNpc npc)
    {
        float strength = (Mathf.Clamp(npc.MaximumHearts, 1, 3) - 1) * .5f;
        return Random.Range(Mathf.Lerp(.95f, .65f, strength), Mathf.Lerp(1.4f, 1.05f, strength));
    }

    private CombatPose PlayerCombatPose(PlayerSword player, CombatInput input, bool rendered)
    {
        Vector3 grip = rendered ? player.Sword.position : player.Player.TransformPoint(player.TargetPose.Position);
        Vector3 blade = rendered ? player.Sword.forward : WorldSwordDirection(player, player.TargetPose.Rotation);
        bool changed = !player.CombatBaseline || player.CombatGeneration != input.PoseGeneration || player.CombatGuard != input.GuardHeld;
        Quaternion turn = player.Player.rotation * Quaternion.Inverse(player.CombatFacing);
        Vector3 previousGrip = changed ? grip : player.Player.position + turn * (player.CombatGrip - player.Player.position);
        Vector3 previousBlade = changed ? blade : turn * player.CombatBlade;
        player.CombatPreviousGrip = previousGrip; player.CombatPreviousBlade = previousBlade;
        player.CombatGrip = grip; player.CombatBlade = blade; player.CombatFacing = player.Player.rotation;
        player.CombatGeneration = input.PoseGeneration; player.CombatGuard = input.GuardHeld; player.CombatBaseline = input.Valid;
        Vector3 remembered = player.LastGuardDirection;
        SwordPose end = SwordPoseMath.Calculate(Quaternion.identity, false,
            new SwordPoseSettings(PlayerChestCenter(player), gripRadius, guardRadius, guardPlaneOffset, guardHeight), ref remembered);
        Vector3 cut = player.Player.TransformDirection(player.CutVelocity);
        if (cut.sqrMagnitude < .001f) cut = blade;
        return new CombatPose(previousGrip, previousBlade, grip, blade, player.Player.TransformPoint(PlayerChestCenter(player)),
            player.Player.forward, cut, player.Player.TransformPoint(end.Position), player.Player.forward);
    }

    private CombatPose NpcCombatPose(SingleNpc npc)
    {
        Transform root = npc.Root.transform;
        // The guard's committed-cut fallback must use the same neutral
        // endpoint for both attackers, independently of NPC choreography.
        Vector3 remembered = Vector3.up;
        SwordPose end = SwordPoseMath.Calculate(Quaternion.identity, false,
            new SwordPoseSettings(npc.ChestCenter + npc.Avatar.ChestOffset,
                gripRadius, guardRadius, guardPlaneOffset, guardHeight), ref remembered);
        return new CombatPose(npc.PreviousGrip, npc.PreviousBlade,
            root.TransformPoint(npc.VisualBasePose.Position), root.TransformDirection(npc.VisualBasePose.Direction), SingleNpcBodyCenter(npc),
            root.forward, npc.AttackDirection, root.TransformPoint(end.Position), root.forward);
    }

    private CombatPose PlayerDefencePose(PlayerSword player)
    {
        Vector3 grip = player.Player.TransformPoint(player.TargetPose.Position);
        Vector3 blade = WorldSwordDirection(player, player.TargetPose.Rotation);
        return new CombatPose(player.CombatBaseline ? player.CombatPreviousGrip : grip,
            player.CombatBaseline ? player.CombatPreviousBlade : blade, grip, blade, player.Player.TransformPoint(PlayerChestCenter(player)),
            player.Player.forward, player.Player.TransformDirection(player.CutVelocity), grip, blade);
    }

    private bool SinglePlayerBlocked => matchTime < singleResolvingUntil
        || matchTime < singleStunnedUntil || showdown.IsRecovering(matchTime);

    private void UpdateSingleAttack(CombatInput input)
    {
        CombatPose player = PlayerCombatPose(first, input, false);
        if (!singleStroke.Update(input, player, matchTime, SinglePlayerBlocked)) return;
        SingleNpc contact = null;
        CombatExchangeResult selected = default;
        CombatPose selectedEnemy = default;
        float nearest = float.PositiveInfinity;
        foreach (SingleNpc npc in singleNpcs)
        {
            if (npc.Defeated) continue;
            CombatPose enemy = NpcCombatPose(npc);
            bool swinging = npc.Stroke.Active && !npc.ContactResolved;
            CombatExchangeResult result = CombatExchange.Resolve(singleStroke.Kind,
                swinging ? AttackKind.Cut : AttackKind.None, player, enemy,
                input.Valid && input.GuardHeld, SingleNpcGuarding(npc, matchTime));
            if (result.Contact.Outcome == CombatOutcome.None) continue;
            float distance = (npc.Root.transform.position - player.Grip).sqrMagnitude;
            if (contact != null && (result.Fraction > selected.Fraction
                || (Mathf.Approximately(result.Fraction, selected.Fraction) && distance >= nearest))) continue;
            contact = npc; selected = result; selectedEnemy = enemy; nearest = distance;
        }
        if (contact == null) return;
        singleStroke.Consume();
        ApplySingleContact(contact, selected.Contact, player, selectedEnemy, input.GuardHeld);
    }

    private void ApplySingleContact(SingleNpc npc, CombatResult result, CombatPose player, CombatPose enemy, bool guardHeld)
    {
        // Capture recoil at the observed impact, not the frame's later pose.
        // An NPC stops its authored cut here instead of leaving the blade
        // inside the player while waiting for the next preparation.
        SwordPose npcContact = SwordAtContact(npc.Root.transform, npc.VisualBasePose, enemy, result.ContactFraction);
        npc.Sword.SetLocalPositionAndRotation(npcContact.Position, npcContact.Rotation);
        if (result.Attacker == 1 || result.Outcome == CombatOutcome.Clash) npc.VisualBasePose = npcContact;
        if (!guardHeld && result.Kind != AttackKind.Thrust)
            ApplyPose(first, SwordAtContact(first.Player, first.TargetPose, player, result.ContactFraction));
        singleResolvingUntil = matchTime + result.HitStopSeconds + DuelMatch.PushSeconds;
        singleStroke.Interrupt();
        npc.ContactResolved = true;
        npc.Stroke.Consume();
        npc.AttackTimer = -1f;
        npc.NextAttack = matchTime + (npc.ContinuousAttack ? 0f : SingleAttackWait(npc));
        npc.NextGuardChange = matchTime + CombatReaction.Duration(result) + SingleGuardInterval(npc);
        if (result.Outcome == CombatOutcome.Clash)
        {
            presentation.PlaySingleContact(CombatContact.ContactPoint(result, player, enemy), true, false, matchTime);
            return;
        }
        bool playerAttacked = result.Attacker == 0;
        bool blocked = result.Outcome == CombatOutcome.GuardSuccess;
        Vector3 direction = CombatReaction.Direction(result.Kind, playerAttacked ? player : enemy);
        Vector3 position = CombatContact.ContactPoint(result, player, enemy);
        presentation.PlaySingleContact(position, blocked, !playerAttacked && !blocked, matchTime);
        BeginSingleReaction(npc, result, player, enemy);
        if (result.PushedPlayer == 1)
            npc.StunnedUntil = matchTime + CombatReaction.Duration(result);
        else if (result.PushedPlayer == 0)
        {
            singleStunnedUntil = matchTime + CombatReaction.Duration(result);
            first.ThrustAt = -100d;
        }
        if (blocked)
        {
            // Only the current opponent can retaliate. Every counter starts
            // with a new announced preparation; easy waits for player recovery.
            if (playerAttacked && npc == singleActiveNpc)
                npc.NextAttack = matchTime + (npc.ContinuousAttack ? 0f : SingleAttackWait(npc) * .25f);
            Vector3 recoil = result.PushedPlayer == 0 ? -first.Player.forward : -npc.Root.transform.forward;
            if (result.PushedPlayer == 0)
                presentation.GuardRecoilCharacter(0, recoil, matchTime, result.HitStopSeconds);
            else
            {
                npc.Avatar.GuardRecoil(recoil, matchTime, result.HitStopSeconds);
                presentation.ShowStagger(npc.Avatar, matchTime, CombatReaction.RecoverySeconds, result.HitStopSeconds);
            }
            return;
        }
        if (playerAttacked)
        {
            npc.Hearts--;
            npc.RecoveryUntil = matchTime + CombatReaction.Duration(result);
            npc.NextGuardChange = npc.RecoveryUntil + SingleGuardInterval(npc);
            npc.Avatar.Stagger(direction, matchTime, CombatReaction.RecoverySeconds, result.HitStopSeconds);
            presentation.ShowStagger(npc.Avatar, matchTime, CombatReaction.RecoverySeconds);
            if (npc.Hearts <= 0) RemoveSingleNpc(npc);
            else if (npc.GuardOctant >= 0) npc.GuardOctant = (npc.GuardOctant + 2) % 8; // Explicit legacy fixture.
            else npc.GuardBiasTarget += 90f;
        }
        else if (showdown.Damage(matchTime, guardHeld))
        {
            // A landed hit starts a chain of announced cuts. Easy interrupts
            // that chain while the player staggers, then starts a fresh windup.
            npc.ContinuousAttack = true;
            npc.AttackNotBefore = matchTime + ShowdownCombat.HitFollowupDelaySeconds;
            npc.NextAttack = npc.AttackNotBefore;
            presentation.StaggerCharacter(0, direction, matchTime, CombatReaction.RecoverySeconds, result.HitStopSeconds);
            if (showdown.Finished) BeginSingleDefeat(matchTime);
        }
    }

    private void BeginSingleDefeat(double now)
    {
        singleGameOver = true;
        singleDefeatAt = now;
        singleStroke.Reset();
        first.ThrustAt = -100d;
        foreach (SingleNpc opponent in singleNpcs)
            if (!opponent.Defeated) CancelSingleNpcAttack(opponent);
        presentation.BeginSingleDefeat(now);
    }

    private void BeginSingleReaction(SingleNpc npc, CombatResult result, CombatPose player, CombatPose enemy)
    {
        Vector3 forward = first.Player.forward;
        Vector3 cut = result.Kind == AttackKind.Thrust ? Vector3.zero
            : CombatContact.IncomingCutDirection(result.Attacker == 0 ? player : enemy);
        Vector3 advance = forward * result.SignedPush;
        Vector3 displacement = SwordPoseMath.PushDisplacement(result, forward, cut);
        Vector3 playerStep = result.PushedPlayer == 0 ? displacement : advance;
        Vector3 npcStep = result.PushedPlayer == 1 ? displacement : advance;
        float radius = FieldRadius * .88f;
        float fraction = Mathf.Min(MovementFraction(first.Player.position, playerStep, radius),
            MovementFraction(npc.Root.transform.position, npcStep, radius));
        singlePushActive = true;
        singlePushStarted = matchTime;
        singlePushHitStop = result.HitStopSeconds;
        singlePushNpc = npc;
        singlePlayerPushStart = first.Player.position;
        singleNpcPushStart = npc.Root.transform.position;
        singlePlayerPushTarget = singlePlayerPushStart + playerStep * fraction;
        singleNpcPushTarget = singleNpcPushStart + npcStep * fraction;
    }

    private void AdvanceSingleReaction(double now)
    {
        if (!singlePushActive) return;
        float progress = Mathf.Clamp01((float)(now - singlePushStarted - singlePushHitStop) / DuelMatch.PushSeconds);
        float blend = Mathf.SmoothStep(0f, 1f, progress);
        first.Player.position = Vector3.Lerp(singlePlayerPushStart, singlePlayerPushTarget, blend);
        if (singlePushNpc != null && singlePushNpc.Root != null)
            singlePushNpc.Root.transform.position = Vector3.Lerp(singleNpcPushStart, singleNpcPushTarget, blend);
        if (progress >= 1f) singlePushActive = false;
    }

    private void ResolveSingleNpcAttacks(CombatInput input)
    {
        foreach (SingleNpc npc in singleNpcs)
        {
            if (npc.Defeated) continue;
            if (EasySinglePlayerStaggered)
            {
                CancelSingleNpcAttack(npc);
                continue;
            }
            if (npc.Stroke.Active && !npc.ContactResolved
                && matchTime >= singleResolvingUntil)
            {
                CombatPose player = PlayerDefencePose(first), enemy = NpcCombatPose(npc);
                CombatResult result = CombatExchange.Resolve(AttackKind.None, AttackKind.Cut, player, enemy,
                    input.Valid && input.GuardHeld, false).Contact;
                if (result.Outcome != CombatOutcome.None) ApplySingleContact(npc, result, player, enemy, input.GuardHeld);
            }
            npc.PreviousGrip = npc.Root.transform.TransformPoint(npc.VisualBasePose.Position);
            npc.PreviousBlade = npc.Root.transform.TransformDirection(npc.VisualBasePose.Direction);
            if (npc.AttackTimer == 0f)
            {
                npc.AttackTimer = -1f;
                npc.Stroke.Reset();
                npc.NextAttack = matchTime + (npc.ContinuousAttack ? 0f : SingleAttackWait(npc));
                npc.ApproachAttack = !IsEasySingleDifficulty && Random.value < .25f;
                npc.NextGuardChange = matchTime + SingleGuardInterval(npc);
            }
        }
    }

    private void TickSingleNpcAvatars(double now, float dt)
    {
        for (int i = singleNpcs.Count - 1; i >= 0; i--)
        {
            SingleNpc npc = singleNpcs[i];
            if (npc.Defeated && now - npc.DefeatedAt > 1.15)
            { Destroy(npc.Root); singleNpcs.RemoveAt(i); continue; }
            npc.Avatar.Tick(now, dt, npc.VisualBasePose.Rotation,
                SingleNpcGuarding(npc, now), 0f);
            bool guarding = SingleNpcGuarding(npc, now);
            SwordPose visible = npc.Avatar.PoseSword(npc.VisualBasePose, now, guarding);
            npc.Sword.SetLocalPositionAndRotation(visible.Position, visible.Rotation);
            npc.Status.Refresh(npc.Hearts, ShowdownCombat.AttackCueProgress(npc.AttackTimer, npc.SwingSeconds),
                !npc.Defeated && npc == singleActiveNpc, npc.Sword.position + npc.Sword.forward * ShowdownCombat.BladeStart, now);
        }
    }

    // Keep one opponent until defeated while the surrounding challengers stroll.
    private void SelectSingleOpponent()
    {
        if (singleActiveNpc != null && singleActiveNpc.Root != null && !singleActiveNpc.Defeated) return;
        singleActiveNpc = null;
        if (matchTime < singleNextOpponentAt) return;
        foreach (SingleNpc npc in singleNpcs)
        {
            if (npc.Root == null || npc.Defeated) continue;
            singleActiveNpc = npc;
            npc.Engaged = false;
            npc.FacingSince = double.PositiveInfinity;
            npc.Status.Activate(matchTime);
            break;
        }
    }

    private SingleNpc NextSingleOpponent()
    {
        if (singleActiveNpc == null) return null;
        bool passedActive = false;
        foreach (SingleNpc npc in singleNpcs)
        {
            if (npc == singleActiveNpc) { passedActive = true; continue; }
            if (passedActive && !npc.Defeated && npc.Root != null) return npc;
        }
        return null;
    }

    private void RemoveSingleNpc(SingleNpc npc)
    {
        if (npc.Defeated) return;
        npc.Defeated = true; npc.DefeatedAt = matchTime;
        if (npc == singleActiveNpc)
        {
            singleActiveNpc = null;
            singleNextOpponentAt = matchTime + ShowdownCombat.OpponentHandoff(SingleDifficulty);
        }
        npc.Avatar.Defeat(matchTime); npc.Sword.gameObject.SetActive(false);
        showdown.Defeat();
    }

    private void ClearSingleNpcs()
    {
        foreach (SingleNpc npc in singleNpcs)
            if (npc.Root != null) Destroy(npc.Root);
        singleNpcs.Clear();
        singleActiveNpc = null;
        singleNextOpponentAt = 0d;
        singleTransitionStart = double.NegativeInfinity;
        singlePushActive = singleCrowdInitialized = false;
        singlePushNpc = null;
        if (presentation != null) presentation.SetShowdownFade(0f);
    }

    private void OnDestroy()
    {
        EndCharacterPreview();
        if (phoneHub != null) phoneHub.SetOverlayVisible(true);
    }

    private void ResetSword(PlayerSword player, bool isFirst)
    {
        player.InputGeneration++;
        player.TurnVelocity = 0f;
        player.Pitch = -90f;
        player.Roll = 0f;
        player.LastGuardDirection = Vector3.up;
        player.CombatBaseline = false; player.ThrustAt = -100d;
        player.PoseMotion = null; player.RecenterPending = false; player.RecenterPoseUntil = double.NegativeInfinity;
        player.UsingPhone = player.Motion != null && player.Motion.IsConnected;
        player.TargetRotation = player.UsingPhone && player.Motion != null
            ? player.Motion.CurrentLocalRotation : Quaternion.Euler(-90f, 0f, 0f);
        if (player.UsingPhone && ShouldMirror(isFirst)) player.TargetRotation = SwordPoseMath.MirrorHorizontal(player.TargetRotation);
        player.DirectionGeneration = -1;
        player.CutVelocity = Vector3.zero;
        Keyboard keyboard = Keyboard.current;
        bool guard = (player.UsingPhone && player.Motion.IsPoseValid && player.Motion.GuardHeld)
            || (keyboard != null && (IsSingleMode ? keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed : isFirst ? keyboard.leftShiftKey.isPressed : keyboard.rightShiftKey.isPressed));
        UpdatePose(player, guard);
        ApplyPose(player, player.TargetPose);
    }
}
