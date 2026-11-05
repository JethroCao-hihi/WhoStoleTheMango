using UnityEngine;
using Fusion;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class SlipperProjectile : NetworkBehaviour
{
    [Min(0f)] public float damage = 20f;
    [Min(0.1f)] public float lifetime = 5f;
    private GardenPlayer owner;
    private NetworkObject ownerObject;
    private Rigidbody body;
    private bool consumed;
    private float despawnAt;

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
        despawnAt = Time.time + lifetime;
    }

    public void Initialize(NetworkObject source, Vector3 velocity)
    {
        ownerObject = source;
        Initialize(source != null ? source.GetComponent<GardenPlayer>() : null, velocity);
    }

    private void Update()
    {
        if (Object != null && Object.IsValid)
        {
            if (Object.HasStateAuthority && Time.time >= despawnAt)
                Runner.Despawn(Object);
            return;
        }

        if (Time.time >= despawnAt)
            Destroy(gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority) return;
        if (consumed) return;
        GardenPlayer target = collision.collider.GetComponentInParent<GardenPlayer>();
        if (target != null && target == owner) return;
        consumed = true;
        if (target != null && target.CanBeTargetedBy(owner)) target.TakeDamage(damage);
        if (Object != null && Object.IsValid) Runner.Despawn(Object);
        else Destroy(gameObject);
    }
}
