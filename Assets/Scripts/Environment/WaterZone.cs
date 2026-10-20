using UnityEngine;

/// <summary>
/// Add to a trigger volume that covers the water below its surface.
/// The trigger's top should line up with the visible water surface.
/// A kinematic Rigidbody is ensured so Unity sends trigger callbacks to the zone.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider), typeof(Rigidbody))]
public sealed class WaterZone : MonoBehaviour
{
    [Header("Water surface")]
    [SerializeField] private bool useColliderTopAsSurface = true;
    [SerializeField] private float waterSurfaceOffset = 0f;
    [SerializeField] private bool showDebugLogs = true;

    private Collider zoneCollider;
    private Rigidbody zoneRigidbody;

    private void Reset()
    {
        ConfigureTrigger();
    }

    private void Awake()
    {
        ConfigureTrigger();
    }

    private void ConfigureTrigger()
    {
        zoneCollider = GetComponent<Collider>();
        if (zoneCollider == null)
        {
            Debug.LogError("[WaterZone] Missing Collider on water trigger.", this);
            enabled = false;
            return;
        }

        zoneCollider.isTrigger = true;

        zoneRigidbody = GetComponent<Rigidbody>();
        if (zoneRigidbody == null)
            zoneRigidbody = gameObject.AddComponent<Rigidbody>();

        zoneRigidbody.isKinematic = true;
        zoneRigidbody.useGravity = false;
        zoneRigidbody.detectCollisions = true;
    }

    private float GetWaterSurfaceY()
    {
        if (zoneCollider == null)
            zoneCollider = GetComponent<Collider>();

        float baseY = useColliderTopAsSurface && zoneCollider != null
            ? zoneCollider.bounds.max.y
            : transform.position.y;

        return baseY + waterSurfaceOffset;
    }

    private void OnTriggerEnter(Collider other)
    {
        NetworkPlayerController controller = other.GetComponentInParent<NetworkPlayerController>();
        if (controller == null)
            return;

        if (showDebugLogs)
        {
            Debug.Log(
                $"[WaterZone] Trigger entered by {controller.name}. " +
                $"SurfaceY={GetWaterSurfaceY():F2}, " +
                $"PlayerFeetY={controller.transform.position.y:F2}.", this);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        NetworkPlayerController controller = other.GetComponentInParent<NetworkPlayerController>();
        if (controller == null)
            return;

        // Controller validates that the player's feet have reached/passed the surface.
        controller.TryEnterWater(GetWaterSurfaceY());
    }

    private void OnTriggerExit(Collider other)
    {
        NetworkPlayerController controller = other.GetComponentInParent<NetworkPlayerController>();
        if (controller == null)
            return;

        controller.ExitWaterZone();
        if (showDebugLogs)
            Debug.Log($"[WaterZone] Trigger exited by {controller.name}.", this);
    }

    private void OnDrawGizmosSelected()
    {
        Collider col = GetComponent<Collider>();
        if (col == null)
            return;

        Gizmos.color = new Color(0.1f, 0.55f, 1f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        if (col is BoxCollider box)
            Gizmos.DrawCube(box.center, box.size);
        else if (col is SphereCollider sphere)
            Gizmos.DrawSphere(sphere.center, sphere.radius);
    }
}
