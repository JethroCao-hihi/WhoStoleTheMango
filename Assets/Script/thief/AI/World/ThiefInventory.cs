using UnityEngine;
using UnityEngine.Events;

namespace MangoThiefAI
{
    [DisallowMultipleComponent]
    public sealed class ThiefInventory : MonoBehaviour
    {
        [Tooltip("0 means unlimited.")][Min(0)] public int capacity;
        public UnityEvent<int> onMangoCountChanged = new UnityEvent<int>();
        public int MangoCount { get; private set; }
        public bool IsFull => capacity > 0 && MangoCount >= capacity;
        public bool TryAddMango()
        {
            if (IsFull) return false;
            MangoCount++;
            onMangoCountChanged.Invoke(MangoCount);
            return true;
        }
    }
}
