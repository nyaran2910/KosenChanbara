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

    public static class SwordPoseMath
    {
        public const float GuardProjectionThreshold = 0.1f;

        public static SwordPose Calculate(Quaternion inputRotation, bool guard,
            SwordPoseSettings settings, ref Vector3 lastGuardDirection)
        {
            Vector3 center = settings.ChestCenter + Vector3.up * (guard ? settings.GuardHeight : 0.1f);
            SwordPose pose = Calculate(inputRotation, guard, center,
                guard ? settings.GuardRadius : settings.NormalRadius,
                settings.FrontOffset, ref lastGuardDirection);
            Vector3 position = pose.Position;
            if (!guard)
                position.z += settings.FrontOffset;
            position.z = Mathf.Max(position.z, settings.MinimumForward);
            return new SwordPose(position, pose.Rotation);
        }

        // All coordinates are in the player's local space. The model's origin
        // is the middle of the grip and its blade extends along local +Z.
        public static SwordPose Calculate(Quaternion inputRotation, bool guard,
            Vector3 chestCenter, float gripRadius, float guardPlaneOffset,
            ref Vector3 lastGuardDirection)
        {
            Vector3 direction = inputRotation * Vector3.forward;
            if (!guard)
                return new SwordPose(chestCenter + gripRadius * direction, inputRotation);

            Vector3 projected = new Vector3(direction.x, direction.y, 0f);
            if (projected.sqrMagnitude >= GuardProjectionThreshold * GuardProjectionThreshold)
                lastGuardDirection = projected.normalized;
            else if (lastGuardDirection.sqrMagnitude < 0.5f)
                lastGuardDirection = Vector3.up;

            Vector3 guardDirection = lastGuardDirection;
            Vector3 center = chestCenter + Vector3.forward * guardPlaneOffset;
            Quaternion rotation = Quaternion.FromToRotation(direction, guardDirection) * inputRotation;
            return new SwordPose(center - gripRadius * guardDirection, rotation);
        }
    }
}
