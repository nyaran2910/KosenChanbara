using UnityEngine;

public sealed class MotionController : MonoBehaviour
{
    // The sword model extends along this transform's local +Z axis. Rotating
    // -90 degrees around local X points that axis straight up for both players.
    private static readonly Quaternion UprightStanceRotation = Quaternion.Euler(-90f, 0f, 0f);

    [SerializeField] private ControllerSlot slot = ControllerSlot.P1;
    [SerializeField] private bool recenterOnFirstSample = true;

    private PhoneControllerHub hub;
    private HardwareControllerHub hardware;
    private ControllerInputSource observedSource;
    private Quaternion baseRotation;
    private Quaternion referenceRotation = Quaternion.identity;
    private bool hasReference;
    private int observedRecenterVersion = -1;
    private uint observedMotionSequence;
    private bool hasObservedMotionSequence;
    private Quaternion previousSampleRotation;
    private double previousSampleTimestampMilliseconds;
    private bool hasPreviousSample;
    private float angularSpeedDegrees;
    private bool poseValid;

    public ControllerSlot Slot => slot;
    public ControllerInputSource ActiveSource => hub != null && hub.IsConnected(slot)
        ? ControllerInputSource.Phone
        : hardware != null && hardware.IsConnected(slot) ? ControllerInputSource.Hardware : ControllerInputSource.None;
    public bool IsConnected => ActiveSource != ControllerInputSource.None;
    public bool IsStale => ActiveSource == ControllerInputSource.Phone ? hub.IsStale(slot)
        : hardware == null || hardware.IsStale(slot);
    public int RecenterVersion => ActiveSource == ControllerInputSource.Hardware
        ? hardware.GetRecenterVersion(slot) : hub != null ? hub.GetRecenterVersion(slot) : 0;
    public bool IsPoseValid => poseValid && observedSource == ActiveSource
        && observedRecenterVersion == RecenterVersion && !IsStale;
    public int PoseGeneration { get; private set; }
    public Quaternion ReferenceRotation => referenceRotation;
    public int InputSessionGeneration => ActiveSource == ControllerInputSource.Phone
        ? hub.GetConnectionGeneration(slot) : 0;
    public Quaternion CurrentLocalRotation { get; private set; } = UprightStanceRotation;
    public bool GuardHeld => !IsStale && TryGetLatestSample(out MotionSample sample) && sample.GuardHeld;
    public bool RecenterHeld => !IsStale && TryGetLatestSample(out MotionSample sample) && sample.RecenterHeld;
    public ushort ResetCounter => TryGetLatestSample(out MotionSample sample) ? sample.RecenterSequence : (ushort)0;
    public Vector3 AngularVelocityDegrees => IsPoseValid && TryGetLatestSample(out MotionSample sample) && sample.GyroscopeValid
        ? sample.AngularVelocityDegrees : Vector3.zero;
    public float AngularSpeedDegrees => IsPoseValid ? angularSpeedDegrees : 0f;

    private void Awake()
    {
        baseRotation = UprightStanceRotation;
        hub = PhoneControllerHub.EnsureInstance();
        hardware = HardwareControllerHub.EnsureInstance();
    }

    private void Update()
    {
        if (hub == null)
        {
            hub = PhoneControllerHub.EnsureInstance();
        }

        if (hardware == null) hardware = HardwareControllerHub.EnsureInstance();
        ControllerInputSource source = ActiveSource;
        int recenterVersion = source == ControllerInputSource.Hardware
            ? hardware.GetRecenterVersion(slot) : hub.GetRecenterVersion(slot);
        if (source != observedSource || recenterVersion != observedRecenterVersion)
        {
            observedSource = source;
            observedRecenterVersion = recenterVersion;
            hasReference = false;
            hasObservedMotionSequence = false;
            hasPreviousSample = false;
            angularSpeedDegrees = 0f;
            poseValid = false;
            AdvancePoseGeneration();
        }

        if (IsStale || !TryGetLatestSample(out MotionSample sample) || !sample.OrientationValid)
        {
            InvalidatePose();
            return;
        }

        bool newSample = !hasObservedMotionSequence || MotionPacketCodec.IsNewerSequence(sample.Sequence, observedMotionSequence);
        if (newSample)
        {
            UpdateAngularSpeed(sample);
            observedMotionSequence = sample.Sequence;
            hasObservedMotionSequence = true;
        }

        if (!hasReference)
        {
            referenceRotation = recenterOnFirstSample || observedRecenterVersion > 0
                ? sample.Rotation
                : Quaternion.identity;
            hasReference = true;
        }

        CurrentLocalRotation = CalculateLocalRotation(sample);
        poseValid = true;
    }

    private void InvalidatePose()
    {
        if (poseValid)
            AdvancePoseGeneration();
        poseValid = false;
        angularSpeedDegrees = 0f;
        hasPreviousSample = false;
        hasObservedMotionSequence = false;
    }

    private void UpdateAngularSpeed(MotionSample sample)
    {
        if (sample.GyroscopeValid)
        {
            angularSpeedDegrees = sample.AngularVelocityDegrees.magnitude;
        }
        else if (hasPreviousSample)
        {
            double elapsedSeconds = (sample.BrowserTimestampMilliseconds - previousSampleTimestampMilliseconds) / 1000.0;
            angularSpeedDegrees = elapsedSeconds > 0.0001 && elapsedSeconds < 1.0
                ? Quaternion.Angle(previousSampleRotation, sample.Rotation) / (float)elapsedSeconds
                : 0f;
        }
        else
        {
            angularSpeedDegrees = 0f;
        }

        previousSampleRotation = sample.Rotation;
        previousSampleTimestampMilliseconds = sample.BrowserTimestampMilliseconds;
        hasPreviousSample = true;
    }

    public bool TryGetLatestSample(out MotionSample sample)
    {
        if (ActiveSource == ControllerInputSource.Phone) return hub.TryGetLatestSample(slot, out sample);
        if (ActiveSource == ControllerInputSource.Hardware) return hardware.TryGetLatestSample(slot, out sample);
        sample = default;
        return false;
    }

    private void AdvancePoseGeneration()
    {
        PoseGeneration++;
    }

    // Menu angular motion uses the device sample, never the combat recenter.
    // Both hardware paths have already applied the physical PCB mount once.
    public bool TryGetMenuSample(out MotionSample sample)
    {
        sample = default;
        return poseValid && ActiveSource == observedSource && TryGetLatestSample(out sample);
    }

    private Quaternion CalculateLocalRotation(MotionSample sample) =>
        baseRotation * Quaternion.Inverse(referenceRotation) * sample.Rotation;

    public void Recenter()
    {
        hasReference = false;
        InvalidatePose();
        AdvancePoseGeneration();
        if (ActiveSource == ControllerInputSource.Hardware) hardware?.Recenter(slot);
        else hub?.Recenter(slot);
    }

    // Kept for scene buttons wired to the previous component.
    public void Calibrate() => Recenter();
}
