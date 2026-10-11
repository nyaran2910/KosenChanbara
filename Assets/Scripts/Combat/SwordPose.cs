using UnityEngine;

namespace SchoolFestival.Combat
{
    public readonly struct SwordPoseSettings
    {
        public readonly Vector3 ChestCenter;
        public readonly float NormalRadius, GuardRadius, FrontOffset, GuardHeight, MinimumForward;

        public SwordPoseSettings(Vector3 chestCenter, float normalRadius = 0.55f,
            float guardRadius = 0.6f, float frontOffset = 0.55f, float guardHeight = 0.55f,
            float minimumForward = 0.45f)
        {
            ChestCenter = chestCenter;
            NormalRadius = normalRadius;
            GuardRadius = guardRadius;
            FrontOffset = frontOffset;
            GuardHeight = guardHeight;
            MinimumForward = minimumForward;
        }
    }

    public readonly struct SwordPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public Vector3 Direction => Rotation * Vector3.forward;

        public SwordPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    // This is the shared logical pose transition, before presentation reactions.
    // Rendered and contact poses therefore enter and leave guard together.
    public sealed class GuardPoseTransition
    {
        public const float DurationSeconds = .12f;
        private bool initialized, guarding;
        private double startedAt, lastTime;
        private SwordPose from, current;

        public void Reset() => initialized = false;

        public void BeginFrom(SwordPose pose, bool guard, double now)
        {
            initialized = true;
            guarding = guard;
            startedAt = lastTime = now;
            from = current = pose;
        }

        public SwordPose Update(SwordPose target, bool guard, double now)
        {
            if (!initialized || now < lastTime)
            {
                initialized = true;
                guarding = guard;
                startedAt = now - DurationSeconds;
                lastTime = now;
                current = guard ? SwordPoseMath.ConstrainGuardPose(target) : target;
                return current;
            }
            else if (guard != guarding)
            {
                guarding = guard;
                from = current;
                startedAt = now;
            }
            double previousTime = lastTime;
            float dt = (float)(now - previousTime);
            lastTime = now;
            float age = (float)(now - startedAt);
            if (age >= DurationSeconds && previousTime - startedAt < DurationSeconds)
                return current = target;
            if (age >= DurationSeconds)
                return current = guard
                    ? new SwordPose(SwordPoseMath.FollowGuardGrip(current.Position, target.Position, target.Direction, dt), target.Rotation)
                    : target;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / DurationSeconds));
            current = new SwordPose(Vector3.Lerp(from.Position, target.Position, blend),
                Quaternion.Slerp(from.Rotation, target.Rotation, blend));
            return current = guard ? SwordPoseMath.ConstrainGuardPose(current) : current;
        }
    }

    public static class ThrustPose
    {
        public const float DurationSeconds = .3f;
        public const float ChamberSeconds = .025f, DriveSeconds = .065f, HoldSeconds = .025f;
        public const float Reach = .62f, Chamber = .07f;

        public static float Extension(float age)
        {
            if (age < 0f || age >= DurationSeconds) return 0f;
            if (age < ChamberSeconds) return -Chamber * Mathf.SmoothStep(0f, 1f, age / ChamberSeconds);
            float driveEnd = ChamberSeconds + DriveSeconds;
            if (age < driveEnd)
            {
                float t = (age - ChamberSeconds) / DriveSeconds;
                // Accelerate immediately, then settle the point at full reach.
                return Mathf.Lerp(-Chamber, Reach, 1f - Mathf.Pow(1f - t, 3f));
            }
            float returnAt = driveEnd + HoldSeconds;
            if (age < returnAt) return Reach;
            return Reach * (1f - Mathf.SmoothStep(0f, 1f, (age - returnAt) / (DurationSeconds - returnAt)));
        }

        public static SwordPose Apply(SwordPose pose, float age)
            => new SwordPose(pose.Position + pose.Direction * Extension(age), pose.Rotation);
    }

    public readonly struct ReactionMotion
    {
        public readonly float Impact, Balance;
        public readonly Vector3 ChestAngles;
        public Quaternion ChestRotation => Quaternion.Euler(ChestAngles);

        public ReactionMotion(float impact, float balance, Vector3 chestAngles)
        {
            Impact = impact; Balance = balance; ChestAngles = chestAngles;
        }
    }

    // Presentation only: logical SwordPoseMath and combat contacts never use
    // this pose. The body, grip and blade share one reaction clock.
    public static class ReactionSwordPose
    {
        public const float GuardDurationSeconds = CombatReaction.RecoverySeconds;

        public static ReactionMotion Sample(Vector3 localDirection, float age, float duration, bool blocked)
        {
            if (age < 0f || duration <= 0f || age >= duration)
                return new ReactionMotion(0f, 0f, Vector3.zero);
            float t = Mathf.Clamp01(age / duration);
            if (blocked)
            {
                float impact = Mathf.Sin(t * Mathf.PI);
                float balance = Mathf.Sin(t * Mathf.PI * 2f) * impact;
                return new ReactionMotion(impact, balance,
                    new Vector3(-28f * impact, balance * 14f, -localDirection.x * impact * 26f));
            }
            float hit = t < .16f ? Mathf.SmoothStep(0f, 1f, t / .16f)
                : Mathf.SmoothStep(1f, 0f, (t - .16f) / .84f);
            float catchBalance = t > .28f ? Mathf.Sin((t - .28f) * Mathf.PI * 4f) * .28f * (1f - t) : 0f;
            float pitch = localDirection.z - localDirection.y * .7f;
            return new ReactionMotion(hit, catchBalance,
                new Vector3(40f * pitch, -14f * localDirection.x, -40f * localDirection.x) * (hit + catchBalance));
        }

        public static SwordPose Apply(SwordPose capturedPose, SwordPose inputPose,
            Vector3 capturedChest, Vector3 currentChest, Vector3 localDirection,
            float age, float duration, bool blocked)
        {
            if (age < 0f) return capturedPose;
            if (duration <= 0f || age >= duration) return inputPose;
            ReactionMotion motion = Sample(localDirection, age, duration, blocked);
            // Keep the struck pose at first, then hand control back while the
            // body settles. Both endpoints have zero interpolation velocity.
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age / duration - .35f) / .65f));
            Vector3 grip = Vector3.Lerp(capturedPose.Position - capturedChest,
                inputPose.Position - currentChest, blend);
            Quaternion rotation = Quaternion.Slerp(capturedPose.Rotation, inputPose.Rotation, blend);
            float pitch = localDirection.z - localDirection.y * .7f;
            Quaternion wrist = Quaternion.Euler(blocked ? -22f * motion.Impact : -12f * pitch * motion.Impact,
                motion.Balance * (blocked ? 16f : 10f), -localDirection.x * motion.Impact * 12f);
            return new SwordPose(currentChest + motion.ChestRotation * grip,
                motion.ChestRotation * wrist * rotation);
        }
    }

    public static class SwordPoseMath
    {
        public const float GuardInwardFraction = .2f;
        public const float GuardGripOutwardScale = 1.6f;
        public const float GuardProjectionThreshold = 0.1f;
        public const float MaximumGuardForwardTiltDegrees = 12f;

        // Reflect the entire orientation across the player's YZ plane. Mirroring
        // Euler angles loses roll and breaks diagonal smartphone poses.
        public static Quaternion MirrorHorizontal(Quaternion rotation)
            => new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w);

        public static Vector3 PushDisplacement(CombatResult result, Vector3 arenaForward,
            Vector3 cutVelocity)
        {
            Vector3 forward = Vector3.ProjectOnPlane(arenaForward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float sideways = Vector3.Dot(cutVelocity, right);
            float acrossBody = new Vector2(sideways, cutVelocity.y).magnitude;
            float fraction = acrossBody > 0.01f ? sideways / acrossBody : 0f;
            // Ignore tiny sensor drift and keep lateral travel secondary to the
            // existing forward push. A successful guard reverses the same cut.
            if (Mathf.Abs(fraction) < 0.05f) fraction = 0f;
            float deflection = result.Outcome == CombatOutcome.GuardSuccess ? -1f : 1f;
            return forward * result.SignedPush
                + right * (Mathf.Abs(result.SignedPush) * 0.2f * fraction * deflection);
        }

        public static Quaternion ClampGuardRotation(Quaternion input, ref Vector3 remembered)
        {
            Vector3 direction = input * Vector3.forward;
            if (direction.z < 0f)
            {
                Vector3 projected = new Vector3(direction.x, direction.y, 0f);
                if (projected.sqrMagnitude < GuardProjectionThreshold * GuardProjectionThreshold)
                    projected = new Vector3(remembered.x, remembered.y, 0f);
                if (projected.sqrMagnitude < .000001f) projected = Vector3.up;
                Vector3 constrained = projected.normalized;
                Quaternion transported = Quaternion.FromToRotation(direction, constrained) * input;
                // FromToRotation may return identity for tiny rearward angles;
                // reconstruct the forward axis exactly at the boundary.
                input = Quaternion.LookRotation(constrained, transported * Vector3.up);
            }
            remembered = input * Vector3.forward;
            return input;
        }

        public static SwordPose ConstrainGuardPose(SwordPose pose, float minimumForward = .45f)
        {
            Vector3 remembered = pose.Direction;
            Quaternion rotation = ClampGuardRotation(pose.Rotation, ref remembered);
            Vector3 position = pose.Position;
            position.z = Mathf.Max(position.z, minimumForward);
            return new SwordPose(position, rotation);
        }

        public static Vector3 FollowGuardGrip(Vector3 current, Vector3 target, Vector3 bladeDirection, float dt)
        {
            float side = Mathf.Abs(bladeDirection.x);
            float response = Mathf.Lerp(.16f, .045f, side * side);
            return Vector3.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0f, dt) / response));
        }

        public static float GuardHeightOffset(float verticalDirection)
            => .18f * verticalDirection - .18f * Mathf.Max(0f, verticalDirection)
                - .22f * Mathf.Max(0f, -verticalDirection);

        public static SwordPose Calculate(Quaternion inputRotation, bool guard,
            SwordPoseSettings settings, ref Vector3 lastGuardDirection)
        {
            if (guard)
            {
                Quaternion rotation = ClampGuardRotation(inputRotation, ref lastGuardDirection);
                Vector3 guardDirection = rotation * Vector3.forward;
                float radius = settings.GuardRadius * GuardGripOutwardScale;
                // Counterbalance the blade farther outside the chest, while
                // every point of the guarded blade remains in front of it.
                Vector3 inward = settings.ChestCenter + new Vector3(
                    -radius * guardDirection.x * Mathf.Abs(guardDirection.x),
                    settings.GuardHeight - radius * guardDirection.y
                        + GuardHeightOffset(guardDirection.y),
                    settings.FrontOffset + .25f * Mathf.Max(0f, guardDirection.z));
                inward.z = Mathf.Max(inward.z, settings.MinimumForward);
                lastGuardDirection = guardDirection;
                return new SwordPose(inward, rotation);
            }
            Vector3 center = settings.ChestCenter + Vector3.up * .1f;
            SwordPose pose = Calculate(inputRotation, false, center,
                settings.NormalRadius, settings.FrontOffset, ref lastGuardDirection);
            Vector3 position = pose.Position;
            Vector3 direction = inputRotation * Vector3.forward;
            {
                // An elliptical upper/rear region raises the grip smoothly.
                // Upward input is required: a horizontal turn around either
                // side of the body never selects the raised stance.
                float up = Mathf.Max(0f, direction.y) / 0.75f;
                float rear = Mathf.Max(0f, -direction.z) / 0.65f;
                float raised = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Sqrt(up * up + rear * rear) - 0.2f) / 0.8f));
                raised *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(direction.y / 0.35f));
                float rearHeight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rear));
                float overhead = settings.ChestCenter.y + Mathf.Lerp(0.65f, 1.12f, rearHeight)
                    + Mathf.Max(0f, -direction.y) * 0.6f;
                position.y = Mathf.Lerp(position.y, Mathf.Max(position.y, overhead), raised);
                // Carry a low rear-facing grip around the torso, instead of
                // keeping it in front with its blade pointing through the body.
                float lowGripZ = position.z + settings.FrontOffset * (1f - rearHeight);
                float raisedGripZ = Mathf.Max(position.z + settings.FrontOffset, settings.MinimumForward);
                // Return to the front only near full height; the path to the
                // overhead stance must pass above the head, not through it.
                float returnToFront = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((direction.y - 0.35f) / 0.35f));
                position.z = Mathf.Lerp(lowGripZ, raisedGripZ, returnToFront);
                if (direction.z >= 0f) position.z = Mathf.Max(position.z, settings.MinimumForward);
            }
            return new SwordPose(position, pose.Rotation);
        }

        // All coordinates are in the player's local space. The model's origin
        // is the middle of the grip and its blade extends along local +Z.
        public static SwordPose Calculate(Quaternion inputRotation, bool guard,
            Vector3 chestCenter, float gripRadius, float guardPlaneOffset,
            ref Vector3 lastGuardDirection)
        {
            if (guard) inputRotation = ClampGuardRotation(inputRotation, ref lastGuardDirection);
            Vector3 direction = inputRotation * Vector3.forward;
            if (guard) lastGuardDirection = direction;
            Vector3 position = guard
                ? chestCenter + new Vector3(-gripRadius * GuardGripOutwardScale * direction.x * Mathf.Abs(direction.x),
                    -gripRadius * GuardGripOutwardScale * direction.y, guardPlaneOffset + .25f * Mathf.Max(0f, direction.z))
                : chestCenter + gripRadius * direction;
            return new SwordPose(position, inputRotation);
        }
    }
}
