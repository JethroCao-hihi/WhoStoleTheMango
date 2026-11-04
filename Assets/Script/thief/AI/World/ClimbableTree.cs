using System.Collections.Generic;
using UnityEngine;

namespace MangoThiefAI
{
    public sealed class ClimbableTree : MonoBehaviour
    {
        private static readonly HashSet<ClimbableTree> trees = new HashSet<ClimbableTree>();
        public static IEnumerable<ClimbableTree> ActiveTrees => trees;
        public Transform climbStartPoint;
        public Transform climbEndPoint;
        public Transform hidePoint;
        private ThiefClimber occupant;
        public bool IsConfigured => isActiveAndEnabled && climbStartPoint != null
            && climbEndPoint != null && hidePoint != null;
        public bool IsAvailableFor(ThiefClimber climber) => IsConfigured && (occupant == null || occupant == climber);
        public bool TryReserve(ThiefClimber climber)
        {
            if (climber == null || !IsAvailableFor(climber)) return false;
            occupant = climber;
            return true;
        }
        public void Release(ThiefClimber climber) { if (occupant == climber) occupant = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => trees.Clear();
        private void OnEnable() => trees.Add(this);
        private void OnDisable() => trees.Remove(this);
        private void OnDrawGizmosSelected()
        {
            if (!IsConfigured) return;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(climbStartPoint.position, climbEndPoint.position);
            Gizmos.DrawLine(climbEndPoint.position, hidePoint.position);
            Gizmos.DrawWireSphere(hidePoint.position, 0.4f);
        }
    }
}
