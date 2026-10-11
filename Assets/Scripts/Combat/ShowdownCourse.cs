using System;
using UnityEngine;

namespace SchoolFestival.Combat
{
    public enum ShowdownCoursePhase { Fighting, Travelling, Complete, Failed }

    // An original ten-stop course on the existing circular platform. The run
    // owns health and defeat credit; this class owns section gates and walking.
    public sealed class ShowdownCourse
    {
        public const int SectionCount = 10;
        public const int ChallengersPerSection = 10;
        public const float WalkingSpeed = 2.8f;
        private readonly ShowdownProgress run;
        private readonly Vector3[] stops = new Vector3[SectionCount];
        private Vector2 center;
        private float boundaryRadius, travelDistance;
        public int SectionNumber { get; private set; } = 1;
        public ShowdownCoursePhase Phase { get; private set; }
        public float TravelProgress { get; private set; }
        public Vector3 StartPosition => stops[0];
        public Vector3 Destination => stops[Math.Min(SectionNumber, SectionCount - 1)];
        public bool CanSpawn => Phase == ShowdownCoursePhase.Fighting && !run.Finished
            && run.Spawned < SectionNumber * ChallengersPerSection;
        public int RemainingChallengers => CanSpawn
            ? SectionNumber * ChallengersPerSection - run.Spawned : 0;

        public ShowdownCourse(ShowdownProgress progress)
        {
            run = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        public void Reset(Vector3 start, Vector2 arenaCenter, float arenaRadius)
        {
            if (float.IsNaN(arenaRadius) || float.IsInfinity(arenaRadius) || arenaRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(arenaRadius));
            center = arenaCenter;
            boundaryRadius = arenaRadius * .88f;
            float routeRadius = Mathf.Min(arenaRadius, 30f);
            // All stops and the straight paths between them lie in the inner
            // half of the disk, leaving room for the surrounding opponents.
            Vector2 startOffset = Vector2.ClampMagnitude(new Vector2(start.x, start.z) - center, routeRadius * .5f);
            stops[0] = new Vector3(center.x + startOffset.x, start.y, center.y + startOffset.y);
            Vector2[] route = {
                new Vector2(-.65f, -.65f), new Vector2(.65f, -.65f),
                new Vector2(.8f, 0f), new Vector2(-.8f, 0f),
                new Vector2(-.65f, .65f), new Vector2(.65f, .65f),
                new Vector2(0f, .95f), new Vector2(-.95f, .1f), new Vector2(0f, 0f)
            };
            for (int i = 0; i < route.Length; i++)
                stops[i + 1] = new Vector3(center.x + route[i].x * routeRadius * .45f,
                    start.y, center.y + route[i].y * routeRadius * .45f);
            SectionNumber = 1;
            Phase = ShowdownCoursePhase.Fighting;
            TravelProgress = travelDistance = 0f;
        }

        public bool TrySpawn() => CanSpawn && run.TrySpawn();

        public bool TryPlanRoundPositions(Vector3 player, Vector3 forward, out Vector3[] positions)
            => ShowdownCombat.TryPlanRound(player, forward, center, boundaryRadius, out positions);

        public void Refresh(Vector3 playerPosition)
        {
            if (run.Finished)
            {
                Phase = run.Cleared ? ShowdownCoursePhase.Complete : ShowdownCoursePhase.Failed;
                return;
            }
            if (Phase != ShowdownCoursePhase.Fighting
                || run.Defeated < SectionNumber * ChallengersPerSection) return;
            Phase = ShowdownCoursePhase.Travelling;
            travelDistance = Vector3.Distance(playerPosition, Destination);
            TravelProgress = 0f;
        }

        public Vector3 Move(Vector3 playerPosition, float deltaTime)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return playerPosition;
            Vector3 next = WalkBeforeFade(playerPosition, deltaTime);
            if (Phase == ShowdownCoursePhase.Travelling && !run.Finished && next.Equals(Destination))
                return ArriveUnderFade(next, 1f);
            return next;
        }

        public Vector3 WalkBeforeFade(Vector3 playerPosition, float deltaTime)
        {
            if (Phase != ShowdownCoursePhase.Travelling || run.Finished
                || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return playerPosition;
            Vector3 next = Vector3.MoveTowards(playerPosition, Destination, WalkingSpeed * deltaTime);
            float remaining = Vector3.Distance(next, Destination);
            TravelProgress = travelDistance <= .001f ? 1f : Mathf.Clamp01(1f - remaining / travelDistance);
            return next;
        }

        public Vector3 ArriveUnderFade(Vector3 playerPosition, float opacity)
        {
            if (Phase != ShowdownCoursePhase.Travelling || run.Finished || float.IsNaN(opacity) || opacity < 1f)
                return playerPosition;
            Vector3 next = Destination;
            SectionNumber++;
            Phase = ShowdownCoursePhase.Fighting;
            TravelProgress = 1f;
            return next;
        }

        public Vector3 ConstrainOpponent(Vector3 position)
        {
            Vector2 offset = Vector2.ClampMagnitude(new Vector2(position.x, position.z) - center, boundaryRadius);
            return new Vector3(center.x + offset.x, position.y, center.y + offset.y);
        }
    }
}
