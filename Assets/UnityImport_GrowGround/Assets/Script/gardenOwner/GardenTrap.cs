using UnityEngine;
using Fusion;

[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
public class GardenTrap : NetworkBehaviour
{
    [Min(0f)] public float armDelay = 0.5f;
    [Min(0f)] public float rootDuration = 2f;
    [Min(0f)] public float damage = 10f;
    [Min(0.1f)] public float lifetime = 30f;
    private GardenPlayer owner;
    private NetworkObject ownerObject;
    private float armedAt;
    private float despawnAt;
    private bool consumed;

    private void Awake()
    {
        GetComponent<SphereCollider>().isTrigger = true;
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    public void Initialize(GardenPlayer source)
    {
        owner = source;
        armedAt = Time.time + armDelay;
        despawnAt = Time.time + lifetime;
    }

    public void Initialize(NetworkObject source)
    {
        ownerObject = source;
        Initialize(source != null ? source.GetComponent<GardenPlayer>() : null);
    }

    private void OnTriggerEnter(Collider other) => TryCatch(other);
    private void OnTriggerStay(Collider other) => TryCatch(other);

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

    private void TryCatch(Collider other)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority) return;
        if (consumed || Time.time < armedAt) return;
        GardenPlayer target = other.GetComponentInParent<GardenPlayer>();
        if (target == null || !target.CanBeTargetedBy(owner)) return;
        consumed = true;
        target.Root(rootDuration);
        target.TakeDamage(damage);
        if (Object != null && Object.IsValid) Runner.Despawn(Object);
        else Destroy(gameObject);
    }
}
