using System.Collections.Generic;
using UnityEngine;

namespace SchoolFestival.Combat
{
    public readonly struct ShowdownDifficulty
    {
        public const float InitialSwingSeconds = .16f * 1.35f;
        public const float FinalNormalSwingSeconds = .16f;
        public const float InitialWindupSeconds = .55f * 1.5f, FinalNormalWindupSeconds = .5f;
        public static readonly float NormalSpeedStep = Mathf.Pow(InitialSwingSeconds / FinalNormalSwingSeconds,
            1f / (ShowdownCourse.SectionCount - 1));
        public readonly float SpawnInterval, ApproachSpeed, AttackWait, WindupSeconds, SwingSeconds;
        public readonly int MaximumLiving;
        public ShowdownDifficulty(int section, bool easy = false)
        {
            float t = Mathf.Clamp01((section - 1) / 9f);
            SpawnInterval = Mathf.Lerp(1.85f, .95f, t);
            MaximumLiving = ShowdownCourse.ChallengersPerSection;
            ApproachSpeed = Mathf.Lerp(2.25f, 3.5f, t);
            AttackWait = Mathf.Lerp(1.2f, .6f, t);
            WindupSeconds = easy ? InitialWindupSeconds : Mathf.Lerp(InitialWindupSeconds, FinalNormalWindupSeconds, t);
            SwingSeconds = easy ? InitialSwingSeconds : InitialSwingSeconds
                / Mathf.Pow(NormalSpeedStep, Mathf.Clamp(section - 1, 0, ShowdownCourse.SectionCount - 1));
            if (easy)
            {
                SpawnInterval *= 1.5f;
            }
        }
    }

    public static class ShowdownCombat
    {
        public const float BladeStart = CombatContact.BladeStart, BladeLength = CombatContact.BladeLength, BodyRadius = CombatContact.BodyRadius;
        public const float MinimumSpacing = 1.15f, AttackRange = 2.35f;
        public const float MinimumSpawnDistance = 4f;
        public const float OpeningAttackRange = 4.5f, AttackCueSeconds = .5f;
        public const float OpponentHandoffSeconds = .18f;
        public const float WaitingWanderRadius = 1.2f, WaitingWalkSpeed = 1.15f;
        public const float NextOpponentRange = 3.5f;
        public const float EngagementFacingAngle = 12f, FocusHoldSeconds = .15f;
        public const float VisibleWalkSeconds = 1f, TransitionFadeSeconds = 1f;
        public const double HitFollowupDelaySeconds = .3;
        public const int AttackReadyDirectionCount = 5;

        // Across 100 challengers: sixteen two-heart and eight three-heart enemies.
        public static int EnemyHearts(int challengerNumber)
            => challengerNumber > 0 && (challengerNumber % 25 == 0 || challengerNumber % 25 == 12) ? 3
                : challengerNumber > 0 && challengerNumber % 5 == 0 ? 2 : 1;

        public static bool BeginsWithAttack(int challengerNumber) => challengerNumber > 0 && challengerNumber % 3 == 1;

        public static float ApproachAttackRange(ShowdownDifficulty difficulty)
            => AttackRange + difficulty.ApproachSpeed * difficulty.WindupSeconds;

        public static float IntroSeconds(int section) => 2.5f;

        public static bool FocusReady(Vector3 player, Vector3 facing, Vector3 viewPosition, Vector3 viewForward, Vector3 opponent)
        {
            Vector3 toward = opponent - player; toward.y = 0f;
            facing.y = 0f;
            Vector3 viewTarget = opponent + Vector3.up - viewPosition;
            return toward.sqrMagnitude > .0001f && facing.sqrMagnitude > .0001f && viewForward.sqrMagnitude > .0001f
                && Vector3.Angle(facing, toward) <= EngagementFacingAngle
                && Vector3.Angle(viewForward, viewTarget) <= EngagementFacingAngle;
        }

        public static float AttackCueProgress(float attackTimer, float swingSeconds)
            => attackTimer > swingSeconds && attackTimer <= swingSeconds + AttackCueSeconds
                ? Mathf.Clamp01(1f - (attackTimer - swingSeconds) / AttackCueSeconds) : -1f;

        // Guards retain all eight directions, clockwise from overhead.
        public static Vector3 ReadyDirection(int octant)
        {
            float angle = ((octant % 8 + 8) % 8) * Mathf.PI / 4f;
            return new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
        }

        // Cuts start overhead, diagonally above, or at either side. The lower
        // three guard orientations remain available without upward attacks.
        public static Vector3 AttackReadyDirection(int index)
        {
            index = (index % AttackReadyDirectionCount + AttackReadyDirectionCount) % AttackReadyDirectionCount;
            return ReadyDirection(index < 3 ? index : index + 3);
        }

        public static float OpponentHandoff(ShowdownDifficulty difficulty)
            => OpponentHandoffSeconds * difficulty.SpawnInterval / 1.85f;

        // Spread the round around this stop, leaving each waiting challenger
        // room to stroll without a long trek across the stadium.
        public static bool TryPlanRound(Vector3 player, Vector3 forward, Vector2 center,
            float radius, out Vector3[] positions)
        {
            forward.y = 0f;
            forward = forward.sqrMagnitude > .00001f ? forward.normalized : Vector3.forward;
            positions = new Vector3[ShowdownCourse.ChallengersPerSection];
            float usableRadius = radius - WaitingWanderRadius;
            float spreadRadius = Mathf.Min(usableRadius, 11f);
            float facing = Mathf.Atan2(forward.x, forward.z);
            float spacing = MinimumSpacing + WaitingWanderRadius * 2f;
            for (int i = 0; i < positions.Length; i++)
            {
                bool placed = false;
                for (int attempt = 0; attempt < 96; attempt++)
                {
                    float angle = facing + (i == 0 ? attempt * 15f : i * 137.50776f + attempt * 37f) * Mathf.Deg2Rad;
                    Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    int band = (i + attempt) % 3;
                    float depth = spreadRadius * (band == 0 ? .82f : band == 1 ? .65f : .95f);
                    Vector3 candidate = i == 0 ? player + direction * 4.75f
                        : player + direction * depth;
                    Vector2 offset = new Vector2(candidate.x, candidate.z) - center;
                    if (offset.magnitude > usableRadius || Vector3.Distance(candidate, player) < MinimumSpawnDistance) continue;
                    bool clear = true;
                    for (int other = 0; other < i; other++)
                        if (Vector3.Distance(candidate, positions[other]) < spacing) { clear = false; break; }
                    if (!clear) continue;
                    positions[i] = candidate; placed = true; break;
                }
                if (placed) continue;
                positions = null;
                return false;
            }
            return true;
        }

        public static Vector3 WaitingTarget(Vector3 anchor, float phase, double now)
        {
            float time = (float)now;
            Vector3 offset = new Vector3(Mathf.Sin(time * .85f + phase), 0f,
                Mathf.Sin(time * .68f + phase * 1.7f)) * WaitingWanderRadius;
            return anchor + Vector3.ClampMagnitude(offset, WaitingWanderRadius);
        }

        public static Vector3 CutAim(Vector3 target, SwordPoseSettings settings)
        {
            Vector3 horizontal = target - settings.ChestCenter;
            horizontal.y = 0f;
            if (horizontal.sqrMagnitude < .00001f || horizontal.z <= 0f) return Vector3.forward;
            horizontal.Normalize();
            // Solve from the actual chambered hand, including the shared raised
            // stance. An imaginary hand position makes a close cut miss guards.
            float low = -40f, high = 40f;
            Vector3 aim = Vector3.forward;
            for (int step = 0; step < 16; step++)
            {
                float pitch = (low + high) * .5f;
                float radians = pitch * Mathf.Deg2Rad;
                aim = horizontal * Mathf.Cos(radians) + Vector3.up * Mathf.Sin(radians);
                SwordPose pose = CutPose(aim, Vector3.up, .5f, settings);
                float along = Vector3.Dot(target - pose.Position, horizontal) / Mathf.Cos(radians);
                float height = (pose.Position + aim * along).y;
                if (height < target.y) low = pitch;
                else high = pitch;
            }
            return aim;
        }

        // Drive the hand and blade together through the shared sword kinematics.
        // The preparation and collision sweep both use these exact endpoints.
        public static SwordPose CutPose(Vector3 aim, Vector3 readyDirection, float progress,
            SwordPoseSettings settings)
        {
            aim = aim.sqrMagnitude > .00001f ? aim.normalized : Vector3.forward;
            Vector3 side = Vector3.ProjectOnPlane(readyDirection, Vector3.forward);
            if (side.sqrMagnitude < .00001f)
                side = Vector3.ProjectOnPlane(Mathf.Abs(aim.y) < .9f ? Vector3.up : Vector3.right, aim);
            side.Normalize();
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
            Vector3 blade = t <= .5f ? Vector3.Slerp(side, aim, t * 2f).normalized
                : Vector3.Slerp(aim, -side, (t - .5f) * 2f).normalized;
            Vector3 remembered = Vector3.up;
            SwordPose pose = SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, blade), false, settings, ref remembered);
            // Prepare with the hand drawn back. The extra front offset of the
            // player's neutral pose would put the blade root beyond a nearby
            // opponent's guard before this authored swing even reaches forward.
            Vector3 chamber = pose.Position - Vector3.forward * (settings.FrontOffset + .1f);
            // Bring the forward part of the cut through the chest-front guard
            // centre. Low horizontal cuts otherwise reach the body before the
            // finite guard blade, especially at the closest approach distance.
            chamber += Vector3.up * Mathf.Max(0f, settings.GuardHeight - .1f) * Mathf.Max(0f, blade.z);
            chamber.z = Mathf.Max(chamber.z, settings.ChestCenter.z + settings.MinimumForward);
            return new SwordPose(chamber, pose.Rotation);
        }

        // An opposite guard/ready pair must travel around the front, never
        // choose the backward half-turn that would point its blade at the body.
        public static SwordPose BlendFrontPose(SwordPose from, SwordPose to, float progress)
        {
            float t = Mathf.Clamp01(progress);
            if (t <= 0f) return from;
            if (t >= 1f) return to;
            Vector3 blade = t <= .5f ? Vector3.Slerp(from.Direction, Vector3.forward, t * 2f)
                : Vector3.Slerp(Vector3.forward, to.Direction, (t - .5f) * 2f);
            return new SwordPose(Vector3.Lerp(from.Position, to.Position, t),
                Quaternion.FromToRotation(Vector3.forward, blade));
        }

        public static Quaternion TurnBladeThroughFront(Quaternion current, Quaternion target, float maximumDegrees)
        {
            if (maximumDegrees <= 0f) return current;
            Vector3 blade = current * Vector3.forward, goal = target * Vector3.forward;
            if (Quaternion.Angle(current, target) <= maximumDegrees && Vector3.Angle(blade, goal) <= 90f) return target;
            // Numerical error around an exact front-facing blade can report
            // 90.00001 degrees and keep selecting the same front waypoint.
            bool goViaFront = Vector3.Angle(blade, goal) > 90.05f
                && Vector3.Angle(blade, Vector3.forward) > .05f;
            Vector3 waypoint = goViaFront ? Vector3.forward : goal;
            float low = 0f, high = Mathf.Min(1f, maximumDegrees / Mathf.Max(Vector3.Angle(blade, waypoint), .0001f));
            Quaternion next = current;
            // FromTo orientations can rotate farther than their blade vectors.
            // Keep the authored 300 deg/s return limit for the entire sword.
            for (int step = 0; step < 12; step++)
            {
                float t = (low + high) * .5f;
                Quaternion sample = Quaternion.FromToRotation(Vector3.forward, Vector3.Slerp(blade, waypoint, t));
                if (Quaternion.Angle(current, sample) > maximumDegrees) high = t;
                else { low = t; next = sample; }
            }
            if (Vector3.Angle(next * Vector3.forward, goal) < .01f
                && Quaternion.Angle(current, target) <= maximumDegrees) return target;
            return next;
        }

        public static bool CanOccupy(Vector3 position, Vector3 player, IReadOnlyList<Vector3> others)
        {
            Vector3 gap = position - player; gap.y = 0f;
            if (gap.sqrMagnitude < 1.5f * 1.5f) return false;
            for (int i = 0; i < others.Count; i++)
            {
                gap = position - others[i]; gap.y = 0f;
                if (gap.sqrMagnitude < MinimumSpacing * MinimumSpacing - .00001f) return false;
            }
            return true;
        }

        // A crowded preferred arc still leaves room in adjacent approach lanes.
        // Search those lanes explicitly instead of repeatedly hoping for a new
        // random point on the same occupied radius.
        public static bool TryFindSpawn(Vector3 player, Vector3 forward, IReadOnlyList<Vector3> others,
            System.Func<Vector3, Vector3> constrain, out Vector3 spawn)
        {
            forward.y = 0f;
            forward = forward.sqrMagnitude > .00001f ? forward.normalized : Vector3.forward;
            for (int ring = 0; ring < 3; ring++)
            {
                float radius = ring == 0 ? 7f : ring == 1 ? 5.75f : 8.25f;
                for (int step = 0; step <= 60; step++)
                {
                    Vector3 direction = Quaternion.Euler(0f, -75f + step * 2.5f, 0f) * forward;
                    Vector3 candidate = constrain(player + direction * radius);
                    if ((candidate - player).sqrMagnitude < MinimumSpawnDistance * MinimumSpawnDistance) continue;
                    bool clear = true;
                    for (int i = 0; i < others.Count; i++)
                        if ((candidate - others[i]).sqrMagnitude < MinimumSpacing * MinimumSpacing)
                        { clear = false; break; }
                    if (clear) { spawn = candidate; return true; }
                }
            }
            spawn = player;
            return false;
        }

        // The selected opponent also needs a clear approach lane. Attack-role
        // ownership is managed by the scene, independently of this geometry.
        public static bool CanAttack(Vector3 attacker, Vector3 player, IReadOnlyList<Vector3> others,
            float maximumRange = AttackRange)
        {
            Vector3 toward = player - attacker; toward.y = 0f;
            float distance = toward.magnitude;
            if (distance < 1.1f || distance > maximumRange) return false;
            Vector3 forward = toward / distance;
            for (int i = 0; i < others.Count; i++)
            {
                Vector3 offset = others[i] - attacker; offset.y = 0f;
                float along = Vector3.Dot(offset, forward);
                if (along <= .05f || along >= distance - .25f) continue;
                if ((offset - forward * along).sqrMagnitude < MinimumSpacing * MinimumSpacing * .49f)
                    return false;
            }
            return true;
        }

        // Compatibility names for the course and its tests; geometry is shared.
        public static float PointSegmentDistanceSquared(Vector3 point, Vector3 start, Vector3 end)
            => CombatContact.PointSegmentDistanceSquared(point, start, end);
        public static bool SweptBladeTouchesSphere(Vector3 previousGrip, Vector3 previousDirection,
            Vector3 grip, Vector3 direction, Vector3 center, float radius)
            => TrySweptBladeTouchesSphere(previousGrip, previousDirection, grip, direction, center, radius, out _);
        public static bool TrySweptBladeTouchesSphere(Vector3 previousGrip, Vector3 previousDirection,
            Vector3 grip, Vector3 direction, Vector3 center, float radius, out float fraction)
            => CombatContact.TrySweptBladeTouchesSphere(previousGrip, previousDirection, grip, direction, center, radius, out fraction);
        public static bool SweptBladeTouchesBody(Vector3 previousGrip, Vector3 previousDirection,
            Vector3 grip, Vector3 direction, Vector3 bodyCenter)
            => TrySweptBladeTouchesBody(previousGrip, previousDirection, grip, direction, bodyCenter, out _);
        public static bool TrySweptBladeTouchesBody(Vector3 previousGrip, Vector3 previousDirection,
            Vector3 grip, Vector3 direction, Vector3 bodyCenter, out float fraction)
            => CombatContact.TrySweptBladeTouchesBody(previousGrip, previousDirection, grip, direction, bodyCenter, out fraction);
        public static bool TrySweptBladeTouchesBlade(Vector3 previousGrip, Vector3 previousDirection,
            Vector3 grip, Vector3 direction, Vector3 guardGrip, Vector3 guardDirection, out float fraction)
            => CombatContact.TrySweptBladeTouchesBlade(previousGrip, previousDirection, grip, direction, guardGrip, guardDirection, out fraction);
        public static bool GuardInterceptsCommittedCut(Vector3 grip, Vector3 blade, Vector3 endBlade, Vector3 guardGrip, Vector3 guardBlade)
            => CombatContact.GuardInterceptsCommittedCut(grip, blade, grip, endBlade, guardGrip, guardBlade);
        public static bool GuardInterceptsCommittedCut(Vector3 grip, Vector3 blade, Vector3 endGrip, Vector3 endBlade, Vector3 guardGrip, Vector3 guardBlade)
            => CombatContact.GuardInterceptsCommittedCut(grip, blade, endGrip, endBlade, guardGrip, guardBlade);

        public static Quaternion Turn(Quaternion current, Vector3 direction, ref float angularVelocity, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < .000001f || dt <= 0f) return current;
            float target = Quaternion.LookRotation(direction).eulerAngles.y;
            float yaw = current.eulerAngles.y;
            float error = Mathf.DeltaAngle(yaw, target);
            // Acceleration and stopping distance make fast turns settle without
            // the indefinitely drifting tail of an exponential interpolation.
            const float acceleration = 3600f, maximumSpeed = 540f;
            float desired = Mathf.Sign(error) * Mathf.Min(maximumSpeed, Mathf.Sqrt(2f * acceleration * Mathf.Abs(error)));
            angularVelocity = Mathf.MoveTowards(angularVelocity, desired, acceleration * dt);
            float step = angularVelocity * dt;
            if (Mathf.Abs(step) >= Mathf.Abs(error) && Mathf.Sign(step) == Mathf.Sign(error))
            { angularVelocity = 0f; return Quaternion.Euler(0f, target, 0f); }
            return Quaternion.Euler(0f, yaw + step, 0f);
        }

        public static Quaternion FocusTurn(Quaternion current, Vector3 direction, ref float angularVelocity, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < .000001f || dt <= 0f) return current;
            float target = Quaternion.LookRotation(direction).eulerAngles.y;
            float yaw = Mathf.SmoothDampAngle(current.eulerAngles.y, target, ref angularVelocity, .22f, 320f, dt);
            return Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
