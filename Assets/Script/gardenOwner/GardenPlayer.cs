using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Attach to the root of every player that dogs and skills can target.</summary>
[DisallowMultipleComponent]
public class GardenPlayer : MonoBehaviour
{
    private static readonly HashSet<GardenPlayer> players = new HashSet<GardenPlayer>();
    public static IEnumerable<GardenPlayer> ActivePlayers => players;

    [Min(1f)] public float maxHealth = 100f;
    public UnityEvent onDamaged = new UnityEvent();
    public UnityEvent onDied = new UnityEvent();
    public UnityEvent onRooted = new UnityEvent();
    public float Health { get; private set; }
    public bool IsAlive => Health > 0f;
    public bool IsRooted => Time.time < rootedUntil;
    public bool CanMove => IsAlive && !IsRooted;
    private float rootedUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => players.Clear();

    private void Awake() => Health = Mathf.Max(1f, maxHealth);
    private void OnEnable() => players.Add(this);
    private void OnDisable() => players.Remove(this);

    public bool CanBeTargetedBy(GardenPlayer owner)
    {
        return owner != null && this != owner && isActiveAndEnabled && IsAlive;
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive || damage <= 0f) return;
        Health = Mathf.Max(0f, Health - damage);
        onDamaged.Invoke();
        if (!IsAlive) onDied.Invoke();
    }

    public void Root(float duration)
    {
        if (!IsAlive || duration <= 0f) return;
        rootedUntil = Mathf.Max(rootedUntil, Time.time + duration);
        onRooted.Invoke();
    }
}
