using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MangoThiefAI
{
    [DisallowMultipleComponent]
    public sealed class MangoTree : MonoBehaviour
    {
        private static readonly HashSet<MangoTree> trees = new HashSet<MangoTree>();
        public static IEnumerable<MangoTree> ActiveTrees => trees;
        public Transform harvestPoint;
        [Min(1)] public int capacity = 6;
        [Min(0)] public int startingMangoes = 6;
        [Min(0.1f)] public float interactionRange = 1.5f;
        [Tooltip("0 disables regrowth.")][Min(0f)] public float regrowInterval = 12f;
        public GameObject[] fruitVisuals = new GameObject[0];
        public UnityEvent<int> onStockChanged = new UnityEvent<int>();
        public int MangoCount { get; private set; }
        public bool HasMangoes => isActiveAndEnabled && MangoCount > 0;
        public Vector3 HarvestPosition => harvestPoint != null ? harvestPoint.position : transform.position;
        private float nextRegrow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => trees.Clear();
        private void Awake()
        {
            MangoCount = Mathf.Clamp(startingMangoes, 0, Mathf.Max(1, capacity));
            nextRegrow = Time.time + regrowInterval;
            RefreshVisuals();
        }
        private void OnEnable() => trees.Add(this);
        private void OnDisable() => trees.Remove(this);
        private void Update()
        {
            if (regrowInterval <= 0f || Time.time < nextRegrow) return;
            nextRegrow = Time.time + regrowInterval;
            if (MangoCount >= capacity) return;
            MangoCount++;
            RefreshVisuals();
            onStockChanged.Invoke(MangoCount);
        }
        public bool TryHarvest(ThiefInventory inventory, Vector3 thiefFeet)
        {
            if (!HasMangoes || inventory == null || inventory.IsFull
                || Vector3.Distance(thiefFeet, HarvestPosition) > interactionRange) return false;
            MangoCount--;
            if (!inventory.TryAddMango()) { MangoCount++; return false; }
            nextRegrow = Time.time + regrowInterval;
            RefreshVisuals();
            onStockChanged.Invoke(MangoCount);
            return true;
        }
        private void RefreshVisuals()
        {
            if (fruitVisuals == null) return;
            for (int i = 0; i < fruitVisuals.Length; i++)
                if (fruitVisuals[i] != null) fruitVisuals[i].SetActive(i < MangoCount);
        }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(HarvestPosition, interactionRange);
        }
    }
}
