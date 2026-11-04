using System.Collections.Generic;
using UnityEngine;

namespace MangoThiefAI
{
    public sealed class ThiefHideSpot : MonoBehaviour
    {
        private static readonly HashSet<ThiefHideSpot> spots = new HashSet<ThiefHideSpot>();
        public static IEnumerable<ThiefHideSpot> ActiveSpots => spots;
        public Transform standPoint;
        public Vector3 Position => standPoint != null ? standPoint.position : transform.position;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => spots.Clear();
        private void OnEnable() => spots.Add(this);
        private void OnDisable() => spots.Remove(this);
        public bool IsCoveredFrom(Vector3 threatEyes, LayerMask obstacles, float eyeHeight)
        {
            return Physics.Linecast(threatEyes, Position + Vector3.up * eyeHeight,
                obstacles, QueryTriggerInteraction.Ignore);
        }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(Position + Vector3.up * 0.7f, 0.45f);
        }
    }
}
