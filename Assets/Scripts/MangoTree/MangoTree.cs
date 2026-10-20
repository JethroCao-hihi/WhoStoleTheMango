using UnityEngine;

/// <summary>
/// Scene component for a mango tree. Assign climb points and fruit spawn points
/// in the Inspector. This class is intentionally separate from
/// NetworkDroppedMangoMarker, which belongs on the network mango prefab.
/// </summary>
public class MangoTree : MonoBehaviour
{
    [Header("Climb Points")]
    public Transform ClimbStartPoint;
    public Transform ClimbEndPoint;

    [Header("Mango Spawn Points")]
    public Transform[] MangoPoints;

    public bool HasClimbPoints => ClimbStartPoint != null && ClimbEndPoint != null;

    private void OnDrawGizmos()
    {
        if (ClimbStartPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(ClimbStartPoint.position, 0.12f);
        }

        if (ClimbEndPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(ClimbEndPoint.position, 0.12f);
        }

        if (ClimbStartPoint != null && ClimbEndPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(ClimbStartPoint.position, ClimbEndPoint.position);
        }

        if (MangoPoints == null)
            return;

        Gizmos.color = new Color(1f, 0.6f, 0f, 1f);
        foreach (Transform point in MangoPoints)
        {
            if (point != null)
                Gizmos.DrawSphere(point.position, 0.08f);
        }
    }
}
