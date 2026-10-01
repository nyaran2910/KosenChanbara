using UnityEngine;

public sealed class MotionController : MonoBehaviour
{
    // The sword model extends along this transform's local +Z axis. Rotating
    // -90 degrees around local X points that axis straight up for both players.
    private static readonly Quaternion UprightStanceRotation = Quaternion.Euler(-90f, 0f, 0f);

    [SerializeField] private ControllerSlot slot = ControllerSlot.P1;
    [SerializeField] private bool recenterOnFirstSample = true;

    private PhoneControllerHub hub;
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
    public bool IsConnected => hub != null && hub.IsConnected(slot);
    public bool IsStale => hub == null || hub.IsStale(slot);
    public bool IsPoseValid => poseValid && !IsStale;
    public int PoseGeneration { get; private set; }
    public Quaternion CurrentLocalRotation { get; private set; } = UprightStanceRotation;
    public bool GuardHeld => IsPoseValid && hub != null && hub.GuardHeld(slot);
    public float AngularSpeedDegrees => IsPoseValid ? angularSpeedDegrees : 0f;

    private void Awake()
    {
        baseRotation = UprightStanceRotation;
        hub = PhoneControllerHub.EnsureInstance();
    }

    private void Update()
    {
        if (hub == null)
        {
            hub = PhoneControllerHub.EnsureInstance();
        }

        int recenterVersion = hub.GetRecenterVersion(slot);
        if (recenterVersion != observedRecenterVersion)
        {
            observedRecenterVersion = recenterVersion;
            hasReference = false;
            hasObservedMotionSequence = false;
            hasPreviousSample = false;
            angularSpeedDegrees = 0f;
            poseValid = false;
            PoseGeneration++;
        }

        if (hub.IsStale(slot) || !hub.TryGetLatestSample(slot, out MotionSample sample) || !sample.OrientationValid)
        {
            InvalidatePose();
            return;
        }

        if (!hasObservedMotionSequence || MotionPacketCodec.IsNewerSequence(sample.Sequence, observedMotionSequence))
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

        Quaternion relativeRotation = Quaternion.Inverse(referenceRotation) * sample.Rotation;
        CurrentLocalRotation = baseRotation * relativeRotation;
        poseValid = true;
    }

    private void InvalidatePose()
    {
        if (poseValid)
            PoseGeneration++;
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
        if (hub == null)
        {
            sample = default;
            return false;
        }

        return hub.TryGetLatestSample(slot, out sample);
    }

    public void Recenter()
    {
        hasReference = false;
        InvalidatePose();
        PoseGeneration++;
        hub?.Recenter(slot);
    }

    // Kept for scene buttons wired to the previous component.
    public void Calibrate() => Recenter();
}
