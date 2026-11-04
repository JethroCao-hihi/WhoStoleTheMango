using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
public class GardenTrap : MonoBehaviour
{
    [Min(0f)] public float armDelay = 0.5f;
    [Min(0f)] public float rootDuration = 2f;
    [Min(0f)] public float damage = 10f;
    [Min(0.1f)] public float lifetime = 30f;
    private GardenPlayer owner;
    private float armedAt;
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
        Destroy(gameObject, lifetime);
    }

    private void OnTriggerEnter(Collider other) => TryCatch(other);
    private void OnTriggerStay(Collider other) => TryCatch(other);

    private void TryCatch(Collider other)
    {
        if (consumed || Time.time < armedAt) return;
        GardenPlayer target = other.GetComponentInParent<GardenPlayer>();
        if (target == null || !target.CanBeTargetedBy(owner)) return;
        consumed = true;
        target.Root(rootDuration);
        target.TakeDamage(damage);
        Destroy(gameObject);
    }
}
