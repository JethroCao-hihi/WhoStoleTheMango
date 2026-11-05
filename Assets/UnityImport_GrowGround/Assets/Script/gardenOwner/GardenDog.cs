    using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class GardenDog : NetworkBehaviour
{
    [Min(0f)] public float moveSpeed = 5f;
    [Min(0f)] public float detectionRadius = 25f;
    [Min(0.1f)] public float repathInterval = 0.5f;
    [Min(0.1f)] public float lifetime = 20f;
    [Min(0.1f)] public float attackRange = 1.6f;
    [Min(0.1f)] public float attackCooldown = 1f;
    [Min(0f)] public float damage = 10f;
    [Min(0f)] public float rotationSpeed = 12f;
    public float gravity = -25f;
    public GardenPlayer Target { get; private set; }

    private readonly List<Vector3> path = new List<Vector3>();
    private readonly List<GardenPlayer> candidates = new List<GardenPlayer>();
    private CharacterController controller;
    private GardenPlayer owner;
    private AStarGrid grid;
    private int waypoint;
    private float nextRepathTime;
    private float nextAttackTime;
    private float verticalSpeed;
    private float despawnAt;
    private bool initialized;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private static readonly int IsRunning =
    Animator.StringToHash("isRunning");
    private void Awake()
    {
    controller = GetComponent<CharacterController>();
    controller.minMoveDistance = 0f;

    if (animator == null)
        animator = GetComponentInChildren<Animator>();
    }

    public void Initialize(GardenPlayer source, AStarGrid navigationGrid)
    {
        owner = source;
        grid = navigationGrid;
        initialized = owner != null && grid != null;
        // The summoner should never block their own dog.
        if (owner != null)
            foreach (Collider sourceCollider in owner.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(controller, sourceCollider);
        despawnAt = Time.time + lifetime;
    }

    private void Update()
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority) return;
        if (!initialized || !controller.enabled) return;
        if (Time.time >= despawnAt)
        {
            if (Object != null && Object.IsValid) Runner.Despawn(Object);
            else Destroy(gameObject);
            return;
        }
        if (owner == null || !owner.isActiveAndEnabled || !owner.IsAlive)
        {
            if (Object != null && Object.IsValid) Runner.Despawn(Object);
            else Destroy(gameObject);
            return;
        }
        if (grid == null || !grid.isActiveAndEnabled)
        {
            Target = null;
            path.Clear();
        }
        else if (Time.time >= nextRepathTime)
        {
            FindReachableTarget();
            nextRepathTime = Time.time + repathInterval;
        }

        if (Target != null && (!Target.CanBeTargetedBy(owner)
            || (Target.transform.position - transform.position).sqrMagnitude > detectionRadius * detectionRadius))
        {
            Target = null;
            path.Clear();
        }

        bool canAttack = Target != null && InAttackRange();
        Vector3 direction = Vector3.zero;
        if (Target != null && !canAttack)
        {
            while (waypoint < path.Count)
            {
                Vector3 offset = path[waypoint] - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude > 0.0144f) break;
                waypoint++;
            }
            if (waypoint < path.Count)
            {
                direction = path[waypoint] - transform.position;
                direction.y = 0f;
                // Clamp the step so high speeds cannot overshoot and oscillate around a waypoint.
                direction = Vector3.ClampMagnitude(direction, moveSpeed * Time.deltaTime);
            }
        }
        if (direction.sqrMagnitude > 0.00001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction),
                1f - Mathf.Exp(-rotationSpeed * Time.deltaTime));
        if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
        verticalSpeed += Mathf.Min(-0.01f, gravity) * Time.deltaTime;
        Vector3 oldPosition = transform.position;

    controller.Move(
    direction + Vector3.up * (verticalSpeed * Time.deltaTime)
    );

// Kiểm tra chó có thực sự di chuyển trên mặt đất không
    Vector3 moved = transform.position - oldPosition;
    moved.y = 0f;

    bool isRunning = Target != null
                 && !canAttack
                 && moved.sqrMagnitude > 0.000001f;

    if (animator != null)
{
    animator.SetBool(IsRunning, isRunning);
}

        if (canAttack && Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + attackCooldown;
            Target.TakeDamage(damage);
        }
    }

    private void FindReachableTarget()
    {
        candidates.Clear();
        foreach (GardenPlayer candidate in GardenPlayer.ActivePlayers)
        {
            if (candidate == null || !candidate.CanBeTargetedBy(owner)) continue;
            if ((candidate.transform.position - transform.position).sqrMagnitude <= detectionRadius * detectionRadius)
                candidates.Add(candidate);
        }
        candidates.Sort((a, b) => (a.transform.position - transform.position).sqrMagnitude
            .CompareTo((b.transform.position - transform.position).sqrMagnitude));
        Target = null;
        path.Clear();
        waypoint = 0;
        foreach (GardenPlayer candidate in candidates)
        {
            if (!grid.TryFindPath(transform.position, candidate.transform.position, path)) continue;
            Target = candidate;
            waypoint = path.Count > 1 ? 1 : 0;
            break;
        }
    }

    private bool InAttackRange()
    {
        if ((Target.transform.position - transform.position).sqrMagnitude > attackRange * attackRange) return false;
        return !Physics.Linecast(transform.position + Vector3.up * 0.5f,
            Target.transform.position + Vector3.up * 0.5f, grid.obstacleMask, QueryTriggerInteraction.Ignore);
    }
}
