using System.Collections.Generic;
using UnityEngine;

namespace MangoThiefAI
{
    public enum ThiefMoveStatus { None, Moving, Arrived, Failed }

    /// <summary>CharacterController movement along the existing AStarGrid. Ticked by ThiefAI.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class ThiefMotor : MonoBehaviour
    {
        [Header("Navigation")]
        public AStarGrid navigationGrid;
        [Min(0f)] public float destinationSampleRadius = 1f;
        [Min(0.05f)] public float stoppingDistance = 0.18f;
        [Min(0.02f)] public float waypointTolerance = 0.08f;
        [Min(0.1f)] public float repathInterval = 0.8f;

        [Header("Movement")]
        [Min(0f)] public float walkSpeed = 2.5f;
        [Min(0f)] public float runSpeed = 5f;
        [Min(0f)] public float rotationSpeed = 12f;
        public float gravity = -25f;

        [Header("Recovery")]
        [Min(0.2f)] public float stuckTimeout = 1.5f;
        [Min(0.01f)] public float progressDistance = 0.15f;
        [Min(0)] public int maxStuckRepaths = 2;
        public bool drawGizmos = true;

        public ThiefMoveStatus Status { get; private set; }
        public float ActualSpeed { get; private set; }
        public bool RequestedRun { get; private set; }
        public Vector3 Destination { get; private set; }
        public string FailureReason { get; private set; }
        public IReadOnlyList<Vector3> Path => path;
        public Vector3 FeetPosition => transform.TransformPoint(controller.center)
            - Vector3.up * (controller.height * transform.lossyScale.y * 0.5f);

        private readonly List<Vector3> path = new List<Vector3>();
        private CharacterController controller;
        private int waypoint;
        private int stuckRepaths;
        private float verticalSpeed;
        private float nextRepathTime;
        private float lastProgressTime;
        private Vector3 progressAnchor;
        private Vector3 requestedDestination;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            controller.minMoveDistance = 0f;
        }

        public bool TrySetDestination(Vector3 destination, bool run)
        {
            Stop();
            requestedDestination = destination;
            RequestedRun = run;
            FailureReason = null;
            stuckRepaths = 0;
            if (!isActiveAndEnabled || !controller.enabled)
                return Fail("Movement component or CharacterController is disabled.");
            if (!TryBuildPath()) return false;
            Status = ThiefMoveStatus.Moving;
            ResetProgress();
            return true;
        }

        private bool TryBuildPath()
        {
            if (navigationGrid == null || !navigationGrid.isActiveAndEnabled)
                return Fail("No active AStarGrid assigned.");
            if (!navigationGrid.TryGetWalkablePosition(requestedDestination,
                destinationSampleRadius, out Vector3 sampled))
                return Fail("Destination is outside the grid or has no nearby walkable cell.");
            if (!navigationGrid.TryFindPath(FeetPosition, sampled, path) || path.Count == 0)
                return Fail("Start is blocked/outside the grid, or no connected A* path exists.");
            Destination = path[path.Count - 1];
            // Visit the start cell center too: skipping it can cut across a corner.
            waypoint = 0;
            nextRepathTime = Time.time + Mathf.Max(0.1f, repathInterval);
            return true;
        }

        public void Stop()
        {
            path.Clear();
            waypoint = 0;
            Status = ThiefMoveStatus.None;
            ActualSpeed = 0f;
            RequestedRun = false;
        }

        public void ResetVerticalMotion() => verticalSpeed = 0f;

        public bool TryPlan(Vector3 destination, List<Vector3> result, out Vector3 sampled)
        {
            sampled = destination;
            result.Clear();
            return navigationGrid != null && navigationGrid.isActiveAndEnabled
                && navigationGrid.TryGetWalkablePosition(destination, destinationSampleRadius, out sampled)
                && navigationGrid.TryFindPath(FeetPosition, sampled, result) && result.Count > 0;
        }

        public void Tick(float deltaTime, bool canMove)
        {
            ActualSpeed = 0f;
            if (!isActiveAndEnabled || !controller.enabled || deltaTime <= 0f) return;
            Vector3 step = Vector3.zero;
            if (canMove && Status == ThiefMoveStatus.Moving)
            {
                if (navigationGrid == null || !navigationGrid.isActiveAndEnabled)
                    Fail("Navigation grid was removed or disabled.");
                else if (Time.time >= nextRepathTime)
                    TryBuildPath();

                if (Status == ThiefMoveStatus.Moving)
                {
                    Vector3 feet = FeetPosition;
                    while (waypoint < path.Count)
                    {
                        Vector3 offset = path[waypoint] - feet;
                        float heightDifference = Mathf.Abs(offset.y);
                        offset.y = 0f;
                        float tolerance = waypoint == path.Count - 1
                            ? Mathf.Max(0.05f, stoppingDistance) : Mathf.Max(0.02f, waypointTolerance);
                        if (offset.sqrMagnitude > tolerance * tolerance
                            || heightDifference > controller.stepOffset + 0.15f) break;
                        waypoint++;
                    }
                    if (waypoint >= path.Count) Status = ThiefMoveStatus.Arrived;
                    else
                    {
                        Vector3 offset = path[waypoint] - feet;
                        offset.y = 0f;
                        float speed = Mathf.Max(0f, RequestedRun ? runSpeed : walkSpeed);
                        step = Vector3.ClampMagnitude(offset, speed * deltaTime);
                    }
                }
            }
            else if (!canMove) ResetProgress(); // A trap must not consume stuck retries.

            if (step.sqrMagnitude > 0.000001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(step),
                    1f - Mathf.Exp(-Mathf.Max(0f, rotationSpeed) * deltaTime));
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            verticalSpeed += Mathf.Min(-0.01f, gravity) * deltaTime;
            Vector3 before = transform.position;
            CollisionFlags flags = controller.Move(step + Vector3.up * (verticalSpeed * deltaTime));
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
            Vector3 movement = transform.position - before;
            movement.y = 0f;
            ActualSpeed = movement.magnitude / deltaTime;

            if (canMove && Status == ThiefMoveStatus.Moving)
            {
                Vector3 progress = FeetPosition - progressAnchor;
                progress.y = 0f;
                float threshold = Mathf.Max(0.01f, progressDistance);
                if (progress.sqrMagnitude >= threshold * threshold)
                {
                    stuckRepaths = 0;
                    ResetProgress();
                }
                else if (Time.time - lastProgressTime >= Mathf.Max(0.2f, stuckTimeout))
                {
                    if (stuckRepaths >= maxStuckRepaths) Fail("Stuck recovery limit reached.");
                    else
                    {
                        stuckRepaths++;
                        TryBuildPath();
                        ResetProgress();
                    }
                }
            }
        }

        private void ResetProgress()
        {
            progressAnchor = FeetPosition;
            lastProgressTime = Time.time;
        }

        private bool Fail(string reason)
        {
            path.Clear();
            Status = ThiefMoveStatus.Failed;
            FailureReason = reason;
            return false;
        }

        private void OnDisable()
        {
            Stop();
            verticalSpeed = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || path.Count == 0) return;
            Gizmos.color = Color.cyan;
            for (int i = 1; i < path.Count; i++)
                Gizmos.DrawLine(path[i - 1] + Vector3.up * 0.1f, path[i] + Vector3.up * 0.1f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Destination, stoppingDistance);
            if (waypoint < path.Count)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, path[waypoint]);
            }
        }
    }
}
