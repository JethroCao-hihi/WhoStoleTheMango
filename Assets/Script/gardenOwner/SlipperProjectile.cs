using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class SlipperProjectile : MonoBehaviour
{
    [Min(0f)] public float damage = 20f;
    [Min(0.1f)] public float lifetime = 5f;
    private GardenPlayer owner;
    private Rigidbody body;
    private bool consumed;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        GetComponent<BoxCollider>().isTrigger = false;
    }

    public void Initialize(GardenPlayer source, Vector3 velocity)
    {
        owner = source;
        // Ignore every owner collider, including colliders on child bones.
        Collider[] projectileColliders = GetComponentsInChildren<Collider>();
        if (owner != null)
            foreach (Collider sourceCollider in owner.GetComponentsInChildren<Collider>())
                foreach (Collider projectileCollider in projectileColliders)
                    Physics.IgnoreCollision(projectileCollider, sourceCollider);
        body.linearVelocity = velocity;
        body.angularVelocity = transform.right * 12f;
        Destroy(gameObject, lifetime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (consumed) return;
        GardenPlayer target = collision.collider.GetComponentInParent<GardenPlayer>();
        if (target != null && target == owner) return;
        consumed = true;
        if (target != null && target.CanBeTargetedBy(owner)) target.TakeDamage(damage);
        Destroy(gameObject);
    }
}
