using System.Collections.Generic;
using UnityEngine;

namespace MangoThiefAI
{
    [DisallowMultipleComponent]
    public sealed class ThiefTactics : MonoBehaviour
    {
        [Min(1f)] public float mangoSearchRadius = 20f;
        [Min(1f)] public float hideSearchRadius = 14f;
        [Min(1f)] public float climbSearchRadius = 15f;
        [Min(1f)] public float fleeDistance = 10f;
        [Min(1f)] public float safeDistance = 9f;
        [Range(4, 24)] public int escapeSamples = 12;
        [Range(1, 32)] public int maxCandidatePaths = 8;
        public Vector3 SelectedPosition { get; private set; }
        private ThiefMotor motor;
        private ThiefPerception perception;
        private ThiefClimber climber;
        private readonly List<Vector3> path = new List<Vector3>();
        private readonly List<Vector3> probe = new List<Vector3>();
        private readonly Dictionary<int, float> excludedUntil = new Dictionary<int, float>();

        private void Awake()
        {
            motor = GetComponent<ThiefMotor>();
            perception = GetComponent<ThiefPerception>();
            climber = GetComponent<ThiefClimber>();
        }
        public void Exclude(MangoTree tree, float duration)
        {
            if (tree != null) excludedUntil[tree.GetInstanceID()] = Time.time + duration;
        }
        private float PathLength()
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }
        private float ThreatClearance()
        {
            if (!perception.HasThreatMemory) return safeDistance;
            float closest = float.PositiveInfinity;
            foreach (Vector3 point in path)
                closest = Mathf.Min(closest, Vector3.Distance(point, perception.ThreatPosition));
            return closest;
        }
        public bool TryMango(out MangoTree chosen)
        {
            chosen = null;
            float best = float.NegativeInfinity;
            int attempts = 0;
            foreach (MangoTree tree in MangoTree.ActiveTrees)
            {
                if (tree == null || !tree.HasMangoes
                    || (tree.transform.position - transform.position).sqrMagnitude > mangoSearchRadius * mangoSearchRadius
                    || (excludedUntil.TryGetValue(tree.GetInstanceID(), out float until) && Time.time < until)
                    || !perception.CanObserve(tree.HarvestPosition + Vector3.up)) continue;
                if (++attempts > maxCandidatePaths) break;
                if (!motor.TryPlan(tree.HarvestPosition, path, out Vector3 sampled)
                    || Vector3.Distance(sampled, tree.HarvestPosition) > tree.interactionRange) continue;
                float dangerPenalty = perception.HasThreatMemory
                    ? Mathf.Max(0f, safeDistance - Vector3.Distance(sampled, perception.ThreatPosition)) * 4f : 0f;
                float score = tree.MangoCount * 0.2f - PathLength() - dangerPenalty;
                if (score <= best) continue;
                chosen = tree;
                best = score;
                SelectedPosition = sampled;
            }
            return chosen != null;
        }
        public bool TryHide(out Vector3 position)
        {
            position = transform.position;
            if (!perception.HasThreatMemory) return false;
            float best = float.NegativeInfinity;
            int attempts = 0;
            Vector3 threatEyes = perception.ThreatPosition + Vector3.up * perception.ownerEyeHeight;
            foreach (ThiefHideSpot spot in ThiefHideSpot.ActiveSpots)
            {
                if (spot == null || !spot.isActiveAndEnabled
                    || Vector3.Distance(transform.position, spot.Position) > hideSearchRadius
                    || !spot.IsCoveredFrom(threatEyes, perception.ObstacleMask, perception.eyeHeight)) continue;
                if (++attempts > maxCandidatePaths) break;
                if (!motor.TryPlan(spot.Position, path, out Vector3 sampled)) continue;
                // Validate the actual sampled endpoint, not only the designer's point.
                if (!Physics.Linecast(threatEyes, sampled + Vector3.up * perception.eyeHeight,
                    perception.ObstacleMask, QueryTriggerInteraction.Ignore)) continue;
                Vector3 fromOwner = sampled - perception.OwnerPosition;
                float behind = -Vector3.Dot(perception.OwnerForward, fromOwner.normalized);
                float score = Vector3.Distance(sampled, perception.ThreatPosition) + behind * 3f
                    + ThreatClearance() - PathLength() * 0.7f;
                if (score <= best) continue;
                best = score;
                position = sampled;
            }
            if (float.IsNegativeInfinity(best)) return false;
            SelectedPosition = position;
            return true;
        }
        public bool TryClimbTree(out ClimbableTree chosen)
        {
            chosen = null;
            float best = float.NegativeInfinity;
            int attempts = 0;
            foreach (ClimbableTree tree in ClimbableTree.ActiveTrees)
            {
                if (tree == null || !tree.IsAvailableFor(climber)
                    || Vector3.Distance(transform.position, tree.climbStartPoint.position) > climbSearchRadius
                    || !perception.CanObserve(tree.climbStartPoint.position + Vector3.up)
                    || !climber.IsRouteClear(tree)) continue;
                if (perception.OwnerObserved
                    && Vector3.Distance(tree.climbStartPoint.position, perception.OwnerPosition) < safeDistance * 0.6f) continue;
                if (++attempts > maxCandidatePaths) break;
                if (!motor.TryPlan(tree.climbStartPoint.position, path, out Vector3 sampled)
                    || Vector3.Distance(sampled, tree.climbStartPoint.position) > climber.mountDistance) continue;
                float score = -PathLength() + ThreatClearance() * 0.4f;
                if (score <= best) continue;
                chosen = tree;
                SelectedPosition = sampled;
                best = score;
            }
            return chosen != null;
        }
        public bool TryFlee(out Vector3 position)
        {
            position = transform.position;
            AStarGrid grid = motor.navigationGrid;
            if (grid == null || !grid.isActiveAndEnabled) return false;
            Vector3 threat = perception.ThreatPosition;
            Vector3 away = transform.position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.Normalize();
            float best = float.NegativeInfinity;
            int samples = Mathf.Max(4, escapeSamples);
            // Try a large ring and a smaller ring so a border or dead end doesn't invalidate all options.
            for (int ring = 0; ring < 2; ring++)
            {
                int candidates = 0;
                for (int i = 0; i < samples; i++)
                {
                    int offset = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
                    Vector3 direction = Quaternion.Euler(0f, offset * 360f / samples, 0f) * away;
                    Vector3 request = motor.FeetPosition + direction * fleeDistance * (ring == 0 ? 1f : 0.45f);
                    if (!motor.TryPlan(request, path, out Vector3 sampled)) continue;
                    if (++candidates > maxCandidatePaths) break;
                    float clearance = ThreatClearance();
                    float currentClearance = Vector3.Distance(motor.FeetPosition, threat);
                    if (clearance < Mathf.Min(2f, currentClearance * 0.7f)) continue;
                    float length = PathLength();
                    float distance = Vector3.Distance(sampled, threat);
                    int exits = 0;
                    for (int side = 0; side < 4; side++)
                    {
                        Vector3 neighbor = sampled + Quaternion.Euler(0f, side * 90f, 0f) * Vector3.forward * 2f;
                        if (grid.TryGetWalkablePosition(neighbor, 0.6f, out Vector3 point)
                            && grid.TryFindPath(sampled, point, probe)) exits++;
                    }
                    float score = distance * 2f + clearance + exits * 1.5f - length * 0.25f;
                    if (score <= best) continue;
                    best = score;
                    position = sampled;
                }
                if (!float.IsNegativeInfinity(best)) break;
            }
            if (float.IsNegativeInfinity(best)) return false;
            SelectedPosition = position;
            return true;
        }
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(SelectedPosition, 0.35f);
        }
    }
}
