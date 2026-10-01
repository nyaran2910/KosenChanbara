using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class MasterController : MonoBehaviour
{
    public const float radsX = 45f;
    public const float radsZ = 45f;

    [SerializeField] private GameObject Sword1;
    [SerializeField] private GameObject Sword2;
    [SerializeField] private GameObject PlayerObject1;
    [SerializeField] private GameObject PlayerObject2;
    [SerializeField] private SpriteRenderer arena;
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
        public Vector3 LastGuardDirection = Vector3.up;
        public float Pitch = -90f;
        public float Roll;
        public int InputGeneration;
        public int MotionGeneration = -1;
        public bool UsingPhone;
    }

    private readonly DuelMatch match = new DuelMatch();
    private PlayerSword first;
    private PlayerSword second;
    private DuelPresentation presentation;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector2 arenaCenter;
    private float arenaRadius;
    private bool reactionActive;
    private double reactionStart;
    private float hitStopSeconds;
    private Vector3 pushStart;
    private Vector3 pushTarget;
    private SwordPose frozenFirstPose;
    private SwordPose frozenSecondPose;

    private PhoneControllerHub phoneHub;
    private double matchTime;
    public bool IsPaused { get; private set; }
    public double MatchTime => matchTime;
    public MatchWinner Winner => match.Winner;

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
        presentation = gameObject.AddComponent<DuelPresentation>();
        presentation.Initialize(PlayerObject1, PlayerObject2, Sword1, Sword2, arena, ResetMatch, () => SetPaused(false));
        phoneHub = PhoneControllerHub.EnsureInstance();
        ResetSword(first, true);
        ResetSword(second, false);
        SetPaused(true);
    }

    private static PlayerSword CreatePlayerSword(GameObject sword, GameObject player)
    {
        return new PlayerSword
        {
            Sword = sword.transform,
            Player = player.transform,
            Motion = sword.GetComponent<MotionController>()
        };
    }

    // MotionController samples input in Update. This component owns the entire
    // visible pose and reads both players afterwards, regardless of script order.
    private void LateUpdate()
    {
        if (first == null || second == null)
            return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetPaused(!IsPaused);

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
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
        CombatInput firstInput = ReadInput(first, true);
        CombatInput secondInput = ReadInput(second, false);
        AdvanceReaction(now);
        CheckWinner();
        bool alreadyFrozen = reactionActive && now < reactionStart + hitStopSeconds;
        presentation.PoseCharacters(now, Time.unscaledDeltaTime, first.TargetRotation, second.TargetRotation,
            firstInput.Valid && firstInput.GuardHeld, secondInput.Valid && secondInput.GuardHeld,
            firstInput.Valid ? firstInput.AngularSpeed : 0f, secondInput.Valid ? secondInput.AngularSpeed : 0f, alreadyFrozen);
        UpdatePose(first, firstInput.Valid && firstInput.GuardHeld);
        UpdatePose(second, secondInput.Valid && secondInput.GuardHeld);

        Vector3 firstDirection = WorldSwordDirection(first, first.TargetPose.Rotation);
        Vector3 secondDirection = WorldSwordDirection(second, second.TargetPose.Rotation);
        float swordAngle = Vector3.Angle(firstDirection, secondDirection);
        swordAngle = Mathf.Min(swordAngle, 180f - swordAngle);
        CombatResult result = match.Step(firstInput, secondInput, swordAngle, now);
        if (result.Outcome != CombatOutcome.None)
            BeginReaction(result, now);

        // Hold the visible swords during hit stop without stopping WebRTC,
        // sensor sampling, UI or the clock used to detect stale input.
        bool frozen = reactionActive && now < reactionStart + hitStopSeconds;
        ApplyPose(first, frozen ? frozenFirstPose : first.TargetPose);
        ApplyPose(second, frozen ? frozenSecondPose : second.TargetPose);
        presentation.Tick(now, frozen);
    }

    private CombatInput ReadInput(PlayerSword player, bool isFirst, bool integrateKeyboard = true)
    {
        bool usingPhone = player.Motion != null && player.Motion.IsConnected;
        if (usingPhone != player.UsingPhone)
        {
            player.UsingPhone = usingPhone;
            player.InputGeneration++;
            presentation.ClearMotionHistory();
            player.Pitch = -90f;
            player.Roll = 0f;
        }

        bool valid = true;
        bool guard;
        float speed;
        if (usingPhone)
        {
            if (player.MotionGeneration != player.Motion.PoseGeneration)
            {
                player.MotionGeneration = player.Motion.PoseGeneration;
                player.InputGeneration++;
                presentation.ClearMotionHistory();
            }
            player.TargetRotation = player.Motion.CurrentLocalRotation;
            valid = player.Motion.IsPoseValid;
            guard = player.Motion.GuardHeld;
            speed = player.Motion.AngularSpeedDegrees;
        }
        else
        {
            Keyboard keyboard = Keyboard.current;
            float dt = integrateKeyboard ? Time.unscaledDeltaTime : 0f;
            float pitchSpeed = 0f;
            float rollSpeed = 0f;
            if (keyboard != null)
            {
                bool forward = isFirst ? keyboard.upArrowKey.isPressed : keyboard.wKey.isPressed;
                bool upright = isFirst ? keyboard.downArrowKey.isPressed : keyboard.sKey.isPressed;
                bool left = isFirst ? keyboard.leftArrowKey.isPressed : keyboard.aKey.isPressed;
                bool right = isFirst ? keyboard.rightArrowKey.isPressed : keyboard.dKey.isPressed;
                pitchSpeed = ((forward ? 1f : 0f) - (upright ? 1f : 0f)) * radsX;
                rollSpeed = ((left ? 1f : 0f) - (right ? 1f : 0f)) * radsZ;
                float previousPitch = player.Pitch;
                player.Pitch = Mathf.Clamp(player.Pitch + pitchSpeed * dt, -90f, 0f);
                if (player.Pitch == previousPitch)
                    pitchSpeed = 0f;
                player.Roll += rollSpeed * dt;
            }
            // Apply sideways tilt after the forward swing. Euler(pitch, 0,
            // roll) applies Z first, which only twists the blade around its
            // own axis and cannot produce the perpendicular guard stance.
            player.TargetRotation = Quaternion.AngleAxis(player.Roll, Vector3.forward)
                * Quaternion.Euler(player.Pitch, 0f, 0f);
            // Quaternion.Angle rounds tiny frame-to-frame rotations to zero.
            // Keyboard axes are orthogonal, so their commanded speeds give a
            // stable angular speed even when the editor runs at a very high FPS.
            speed = Mathf.Sqrt(pitchSpeed * pitchSpeed + rollSpeed * rollSpeed);
            guard = keyboard != null && (isFirst ? keyboard.rightShiftKey.isPressed : keyboard.leftShiftKey.isPressed);
        }

        UpdatePose(player, valid && guard);
        // Guard moves the rendered sword across the body. Attack gates and
        // speed must still follow the sensor/keyboard input, not that movement.
        float forwardAngle = Vector3.Angle(WorldSwordDirection(player, player.TargetRotation), player.Player.forward);
        return new CombatInput(forwardAngle, speed, guard, valid, player.InputGeneration);
    }

    private void UpdatePose(PlayerSword player, bool guard)
    {
        player.TargetPose = SwordPoseMath.Calculate(player.TargetRotation, guard,
            new SwordPoseSettings(chestCenter + presentation.ChestOffset(player == first ? 0 : 1),
                gripRadius, guardRadius, guardPlaneOffset, guardHeight), ref player.LastGuardDirection);
    }

    private static void ApplyPose(PlayerSword player, SwordPose pose)
    {
        player.Sword.SetLocalPositionAndRotation(pose.Position, pose.Rotation);
    }

    private static Vector3 WorldSwordDirection(PlayerSword player, Quaternion rotation)
    {
        Quaternion parentRotation = player.Sword.parent != null ? player.Sword.parent.rotation : Quaternion.identity;
        return parentRotation * rotation * Vector3.forward;
    }

    private void BeginReaction(CombatResult result, double now)
    {
        reactionActive = true;
        reactionStart = now;
        hitStopSeconds = result.HitStopSeconds;
        pushStart = transform.position;
        Vector3 direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        pushTarget = pushStart + direction * result.SignedPush;
        frozenFirstPose = first.TargetPose;
        frozenSecondPose = second.TargetPose;
        // Effects originate from the impact pose, before freezing the blades.
        ApplyPose(first, first.TargetPose); ApplyPose(second, second.TargetPose);
        presentation.Play(result, now);
    }

    private void AdvanceReaction(double now)
    {
        if (!reactionActive)
            return;

        float progress = Mathf.Clamp01((float)(now - reactionStart - hitStopSeconds) / DuelMatch.PushSeconds);
        transform.position = Vector3.Lerp(pushStart, pushTarget, Mathf.SmoothStep(0f, 1f, progress));
        if (progress >= 1f)
            reactionActive = false;
    }

    private void CheckWinner()
    {
        if (match.Winner != MatchWinner.None)
            return;

        Vector3 firstPosition = first.Player.position;
        Vector3 secondPosition = second.Player.position;
        MatchWinner winner = match.CheckRingOut(
            new Vector2(firstPosition.x, firstPosition.z) - arenaCenter,
            new Vector2(secondPosition.x, secondPosition.z) - arenaCenter, arenaRadius);
        if (winner == MatchWinner.None)
            return;

        reactionActive = false;
        presentation.ShowWinner(winner, matchTime);
    }

    public void ResetMatch()
    {
        if (first == null || second == null)
            return;

        match.Reset();
        reactionActive = false;
        transform.SetPositionAndRotation(initialPosition, initialRotation);
        presentation.ResetEffects();
        ResetSword(first, true);
        ResetSword(second, false);
        // No scene reload or recalibration: the existing phone sessions survive.
    }

    public void SetPaused(bool paused)
    {
        if (first == null || presentation == null || IsPaused == paused) return;
        IsPaused = paused;
        presentation.SetPaused(paused);
        phoneHub.SetOverlayVisible(paused);
        if (!paused)
        {
            // Sensor samples keep arriving during pause. Start a fresh attack baseline.
            first.InputGeneration++; second.InputGeneration++;
            ReadInput(first, true, false); ReadInput(second, false, false);
            if (!reactionActive || matchTime >= reactionStart + hitStopSeconds)
            { ApplyPose(first, first.TargetPose); ApplyPose(second, second.TargetPose); }
            presentation.ClearMotionHistory();
        }
    }

    private void OnDestroy()
    {
        if (phoneHub != null) phoneHub.SetOverlayVisible(true);
    }

    private void ResetSword(PlayerSword player, bool isFirst)
    {
        player.InputGeneration++;
        player.Pitch = -90f;
        player.Roll = 0f;
        player.LastGuardDirection = Vector3.up;
        player.UsingPhone = player.Motion != null && player.Motion.IsConnected;
        player.TargetRotation = player.UsingPhone && player.Motion != null
            ? player.Motion.CurrentLocalRotation : Quaternion.Euler(-90f, 0f, 0f);
        Keyboard keyboard = Keyboard.current;
        bool guard = player.UsingPhone ? player.Motion.GuardHeld
            : keyboard != null && (isFirst ? keyboard.rightShiftKey.isPressed : keyboard.leftShiftKey.isPressed);
        UpdatePose(player, guard);
        ApplyPose(player, player.TargetPose);
    }
}
