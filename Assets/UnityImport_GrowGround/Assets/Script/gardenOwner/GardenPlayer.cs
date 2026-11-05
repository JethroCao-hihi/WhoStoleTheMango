using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Attach to the root of every player that dogs and skills can target.</summary>
[DisallowMultipleComponent]
public class GardenPlayer : NetworkBehaviour
{
    private static readonly HashSet<GardenPlayer> players = new HashSet<GardenPlayer>();
    public static IEnumerable<GardenPlayer> ActivePlayers => players;

    [Min(1f)] public float maxHealth = 100f;
    public UnityEvent onDamaged = new UnityEvent();
    public UnityEvent onDied = new UnityEvent();
    public UnityEvent onRooted = new UnityEvent();
    [Networked] public float Health { get; private set; }
    public bool IsAlive => Health > 0f;
    public bool IsRooted => Runner != null ? Runner.SimulationTime < RootedUntil : Time.time < rootedUntil;
    public bool CanMove => IsAlive && !IsRooted;
    private float rootedUntil;
    [Networked] private float RootedUntil { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => players.Clear();

    private void Awake()
    {
        if (Runner == null)
            Health = Mathf.Max(1f, maxHealth);
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            Health = Mathf.Max(1f, maxHealth);
            RootedUntil = 0f;
        }
    }
    private void OnEnable() => players.Add(this);
    private void OnDisable() => players.Remove(this);

    public bool CanBeTargetedBy(GardenPlayer owner)
    {
        return owner != null && this != owner && isActiveAndEnabled && IsAlive;
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive || damage <= 0f) return;
        if (Object != null && Object.IsValid && !Object.HasStateAuthority) return;
        Health = Mathf.Max(0f, Health - damage);
        onDamaged.Invoke();
        if (!IsAlive) onDied.Invoke();
    }

    public void Root(float duration)
    {
        if (!IsAlive || duration <= 0f) return;
        if (Object != null && Object.IsValid && !Object.HasStateAuthority) return;
        float until = (Runner != null ? (float)Runner.SimulationTime : Time.time) + duration;
        if (Runner != null)
            RootedUntil = Mathf.Max(RootedUntil, until);
        else
            rootedUntil = Mathf.Max(rootedUntil, until);
        onRooted.Invoke();
    }
}
