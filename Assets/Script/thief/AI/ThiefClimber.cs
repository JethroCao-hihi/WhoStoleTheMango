using UnityEngine;

namespace MangoThiefAI
{
    [DisallowMultipleComponent]
    public sealed class ThiefClimber : MonoBehaviour
    {
        [Min(0.1f)] public float climbSpeed = 2f;
        [Min(0.1f)] public float mountDistance = 1.5f;
        public bool ControlsMovement { get; private set; }
        public bool IsTraversing { get; private set; }
        public bool IsPerched { get; private set; }
        public bool IsDescending { get; private set; }
        public bool Failed { get; private set; }
        public ClimbableTree Tree { get; private set; }
        public Vector3 LandingPosition => start;
        private CharacterController controller;
        private ThiefMotor motor;
        private Vector3 start;
        private Vector3 end;
        private Vector3 perch;
        private Vector3 rootOffset;
        private int segment;
        private bool previousControllerEnabled;
        private readonly Collider[] landingOccupants = new Collider[32];

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            motor = GetComponent<ThiefMotor>();
        }
        private LayerMask Obstacles => motor.navigationGrid != null
            ? motor.navigationGrid.obstacleMask : (LayerMask)0;

        public bool IsRouteClear(ClimbableTree tree)
        {
            return tree != null && tree.IsAvailableFor(this)
                && tree.hidePoint.position.y - tree.climbStartPoint.position.y > 2f
                && SegmentClear(tree.climbStartPoint.position, tree.climbEndPoint.position)
                && SegmentClear(tree.climbEndPoint.position, tree.hidePoint.position);
        }

        public void ClearFailure() => Failed = false;

        private bool SegmentClear(Vector3 from, Vector3 to)
        {
            float radius = controller.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            float height = Mathf.Max(radius * 2f, controller.height * transform.lossyScale.y);
            Vector3 bottom = from + Vector3.up * (radius + 0.05f);
            Vector3 top = from + Vector3.up * (height - radius + 0.05f);
            if (Physics.CheckCapsule(bottom, top, radius, Obstacles, QueryTriggerInteraction.Ignore)) return false;
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.000001f) return true;
            return !Physics.CapsuleCast(bottom, top, radius, delta.normalized,
                delta.magnitude, Obstacles, QueryTriggerInteraction.Ignore);
        }

        public bool BeginUp(ClimbableTree tree)
        {
            if (ControlsMovement || !IsRouteClear(tree)
                || Vector3.Distance(motor.FeetPosition, tree.climbStartPoint.position) > mountDistance
                || !SegmentClear(motor.FeetPosition, tree.climbEndPoint.position)
                || !tree.TryReserve(this)) return false;
            Tree = tree;
            start = tree.climbStartPoint.position;
            end = tree.climbEndPoint.position;
            perch = tree.hidePoint.position;
            rootOffset = transform.position - motor.FeetPosition;
            previousControllerEnabled = controller.enabled;
            controller.enabled = false;
            motor.Stop();
            motor.ResetVerticalMotion();
            ControlsMovement = IsTraversing = true;
            IsPerched = IsDescending = Failed = false;
            segment = 0;
            return true;
        }

        public bool BeginDown()
        {
            if (!ControlsMovement || IsTraversing) return false;
            AStarGrid grid = motor.navigationGrid;
            if (grid == null || !grid.isActiveAndEnabled
                || !grid.TryGetWalkablePosition(start, 1.5f, out Vector3 landing)
                || !LandingClear(landing) || !SegmentClear(perch, end) || !SegmentClear(end, landing)) return false;
            start = landing;
            IsDescending = IsTraversing = true;
            IsPerched = Failed = false;
            segment = 0;
            return true;
        }

        private bool LandingClear(Vector3 feet)
        {
            float radius = controller.radius;
            int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (radius + 0.05f),
                feet + Vector3.up * (controller.height - radius + 0.05f), radius,
                landingOccupants, ~0, QueryTriggerInteraction.Ignore);
            if (count == landingOccupants.Length) return false;
            for (int i = 0; i < count; i++)
                if (landingOccupants[i] != null && !landingOccupants[i].transform.IsChildOf(transform)) return false;
            return true;
        }

        public void ReverseUp()
        {
            if (!ControlsMovement) return;
            IsDescending = IsPerched = false;
            IsTraversing = true;
            segment = 0;
        }

        public void Tick(float deltaTime, bool canMove)
        {
            if (!ControlsMovement || !canMove) return;
            // Cached world anchors allow a continuous descent even if the tree is removed.
            if (!IsTraversing)
            {
                if ((Tree == null || !Tree.isActiveAndEnabled) && !BeginDown()) Abort();
                return;
            }
            Vector3 target = segment == 0 ? end : IsDescending ? start : perch;
            Vector3 feet = transform.position - rootOffset;
            Vector3 next = Vector3.MoveTowards(feet, target, Mathf.Max(0.1f, climbSpeed) * deltaTime);
            if (!SegmentClear(feet, next))
            {
                // Restore physical collisions/gravity rather than crossing a new obstruction.
                Failed = true;
                Abort();
                return;
            }
            transform.position = next + rootOffset;
            if ((next - target).sqrMagnitude > 0.0001f) return;
            if (segment++ == 0) return;
            IsTraversing = false;
            if (IsDescending) FinishDown();
            else IsPerched = true;
        }

        private void FinishDown()
        {
            ControlsMovement = IsPerched = IsDescending = false;
            controller.enabled = previousControllerEnabled;
            motor.ResetVerticalMotion();
            Release();
        }
        public void Release()
        {
            if (Tree != null) Tree.Release(this);
            Tree = null;
        }
        public void Abort()
        {
            if (ControlsMovement) controller.enabled = previousControllerEnabled;
            ControlsMovement = IsTraversing = IsPerched = IsDescending = false;
            if (motor != null) motor.ResetVerticalMotion();
            Release();
        }
        private void OnDisable() => Abort();
    }
}
