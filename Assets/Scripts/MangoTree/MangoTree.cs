
using UnityEngine;

public class MangoTree : MonoBehaviour
{
    [Header("Climbing Points")]
    [SerializeField] private Transform climbStartPoint;
    [SerializeField] private Transform climbEndPoint;

    [Header("Mango Points")]
    [SerializeField] private Transform[] mangoPoints;

    public Transform ClimbStartPoint => climbStartPoint;
    public Transform ClimbEndPoint => climbEndPoint;
    public Transform[] MangoPoints => mangoPoints;

    public bool HasClimbPoints =>
        climbStartPoint != null &&
        climbEndPoint != null;

    private void OnDrawGizmos()
    {
        if (climbStartPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(climbStartPoint.position, 0.15f);
        }

        if (climbEndPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(climbEndPoint.position, 0.15f);

            if (climbStartPoint != null)
            {
                Gizmos.DrawLine(
                    climbStartPoint.position,
                    climbEndPoint.position
                );
            }
        }

        if (mangoPoints == null)
            return;

        Gizmos.color = Color.yellow;

        foreach (Transform point in mangoPoints)
        {
            if (point != null)
                Gizmos.DrawSphere(point.position, 0.12f);
        }
    }
}
