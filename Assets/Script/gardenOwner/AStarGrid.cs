using System.Collections.Generic;
using UnityEngine;

/// <summary>A* over a world-aligned XZ grid, sampled onto ground colliders.</summary>
[DisallowMultipleComponent]
public class AStarGrid : MonoBehaviour
{
    [Header("Grid: centered on this object's world position; rotation is ignored")]
    public Vector2 worldSize = new Vector2(40f, 40f);
    [Min(0.2f)] public float cellSize = 1f;
    public LayerMask groundMask = ~0;
    public LayerMask obstacleMask;
    [Min(0.1f)] public float probeHeight = 20f;
    [Range(0f, 60f)] public float maxSlope = 45f;
    [Min(0f)] public float maxStepHeight = 0.4f;
    [Min(0.05f)] public float agentRadius = 0.3f;
    [Min(0.1f)] public float agentHeight = 1f;
    [Tooltip("0 = static grid. Call Rebuild after moving obstacles, or enable periodic rebuilds.")]
    [Min(0f)] public float rebuildInterval;
    public bool drawGrid;
    
    private struct Cell
    {
        public Vector3 position;
        public bool walkable;
    }

    private Cell[] cells;
    private int width;
    private int depth;
    private Vector3 origin;
    private float spacing;
    private float nextRebuildTime;

    private void Awake() => Rebuild();

    private void Update()
    {
        if (rebuildInterval > 0f && Time.time >= nextRebuildTime) Rebuild();
    }

    [ContextMenu("Rebuild A* Grid")]
    public void Rebuild()
    {
        spacing = Mathf.Max(0.2f, cellSize);
        width = Mathf.Clamp(Mathf.CeilToInt(worldSize.x / spacing), 1, 200);
        depth = Mathf.Clamp(Mathf.CeilToInt(worldSize.y / spacing), 1, 200);
        origin = transform.position - new Vector3(width * spacing, 0f, depth * spacing) * 0.5f;
        cells = new Cell[width * depth];
        Physics.SyncTransforms();
        for (int z = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
        {
            int index = z * width + x;
            Vector3 center = origin + new Vector3((x + 0.5f) * spacing, 0f, (z + 0.5f) * spacing);
            cells[index].position = center;
            if (!Physics.Raycast(center + Vector3.up * probeHeight, Vector3.down,
                out RaycastHit hit, probeHeight * 2f, groundMask, QueryTriggerInteraction.Ignore)) continue;
            cells[index].position = hit.point;
            cells[index].walkable = Vector3.Angle(hit.normal, Vector3.up) <= maxSlope
                && !Physics.CheckCapsule(CapsuleBottom(hit.point), CapsuleTop(hit.point),
                    agentRadius, obstacleMask, QueryTriggerInteraction.Ignore);
        }
        nextRebuildTime = Time.time + rebuildInterval;
    }

    private Vector3 CapsuleBottom(Vector3 feet) => feet + Vector3.up * (agentRadius + 0.05f);
    private Vector3 CapsuleTop(Vector3 feet) => feet + Vector3.up *
        (Mathf.Max(agentHeight, agentRadius * 2f) - agentRadius + 0.05f);

    public bool Contains(Vector3 position)
    {
        EnsureGrid();
        return IndexAt(position) >= 0;
    }

    public bool TryGetWalkablePosition(Vector3 position, float searchRadius, out Vector3 result)
    {
        EnsureGrid();
        result = position;
        if (IndexAt(position) < 0) return false;
        float bestDistance = Mathf.Max(0f, searchRadius) * Mathf.Max(0f, searchRadius);
        int best = -1;
        for (int i = 0; i < cells.Length; i++)
        {
            if (!cells[i].walkable) continue;
            float distance = (cells[i].position - position).sqrMagnitude;
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = i;
        }
        if (best < 0) return false;
        result = cells[best].position;
        return true;
    }

    /// <summary>Returns false for blocked, outside, or disconnected endpoints. Clears old paths.</summary>
    public bool TryFindPath(Vector3 start, Vector3 destination, List<Vector3> path)
    {
        path.Clear();
        EnsureGrid();
        int startIndex = IndexAt(start);
        int endIndex = IndexAt(destination);
        if (startIndex < 0 || endIndex < 0 || !cells[startIndex].walkable || !cells[endIndex].walkable)
            return false;

        float[] cost = new float[cells.Length];
        int[] parent = new int[cells.Length];
        bool[] closed = new bool[cells.Length];
        bool[] queued = new bool[cells.Length];
        for (int i = 0; i < cost.Length; i++)
        {
            cost[i] = float.PositiveInfinity;
            parent[i] = -1;
        }
        List<int> open = new List<int> { startIndex };
        queued[startIndex] = true;
        cost[startIndex] = 0f;
        while (open.Count > 0)
        {
            int bestSlot = 0;
            for (int i = 1; i < open.Count; i++)
            {
                float candidateF = cost[open[i]] + Heuristic(open[i], endIndex);
                float bestF = cost[open[bestSlot]] + Heuristic(open[bestSlot], endIndex);
                if (candidateF < bestF || (Mathf.Approximately(candidateF, bestF)
                    && Heuristic(open[i], endIndex) < Heuristic(open[bestSlot], endIndex))) bestSlot = i;
            }
            int current = open[bestSlot];
            open.RemoveAt(bestSlot);
            queued[current] = false;
            if (current == endIndex)
            {
                for (int node = endIndex; node >= 0; node = parent[node]) path.Add(cells[node].position);
                path.Reverse();
                return true;
            }
            closed[current] = true;
            int cx = current % width;
            int cz = current / width;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = cx + dx;
                int nz = cz + dz;
                if (nx < 0 || nx >= width || nz < 0 || nz >= depth) continue;
                int next = nz * width + nx;
                if (closed[next] || !CanTraverse(current, next)) continue;
                // Do not cut diagonally between two touching obstacles or across cliffs.
                if (dx != 0 && dz != 0)
                {
                    int sideX = cz * width + nx;
                    int sideZ = nz * width + cx;
                    if (!CanTraverse(current, sideX) || !CanTraverse(current, sideZ)
                        || !CanTraverse(sideX, next) || !CanTraverse(sideZ, next)) continue;
                }
                float nextCost = cost[current] + (dx != 0 && dz != 0 ? 1.41421356f : 1f);
                if (nextCost >= cost[next]) continue;
                cost[next] = nextCost;
                parent[next] = current;
                if (!queued[next])
                {
                    open.Add(next);
                    queued[next] = true;
                }
            }
        }
        return false;
    }

    private bool CanTraverse(int from, int to)
    {
        if (!cells[from].walkable || !cells[to].walkable) return false;
        Vector3 difference = cells[to].position - cells[from].position;
        if (Mathf.Abs(difference.y) > maxStepHeight) return false;
        return !Physics.CapsuleCast(CapsuleBottom(cells[from].position), CapsuleTop(cells[from].position),
            agentRadius, difference.normalized, difference.magnitude, obstacleMask,
            QueryTriggerInteraction.Ignore);
    }

    private float Heuristic(int from, int to)
    {
        int dx = Mathf.Abs(from % width - to % width);
        int dz = Mathf.Abs(from / width - to / width);
        return Mathf.Max(dx, dz) + 0.41421356f * Mathf.Min(dx, dz);
    }

    private int IndexAt(Vector3 position)
    {
        int x = Mathf.FloorToInt((position.x - origin.x) / spacing);
        int z = Mathf.FloorToInt((position.z - origin.z) / spacing);
        return x < 0 || x >= width || z < 0 || z >= depth ? -1 : z * width + x;
    }

    private void EnsureGrid()
    {
        if (cells == null) Rebuild();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, new Vector3(worldSize.x, 0.1f, worldSize.y));
        if (!drawGrid || cells == null) return;
        foreach (Cell cell in cells)
        {
            Gizmos.color = cell.walkable ? new Color(0f, 1f, 0f, 0.3f) : new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawCube(cell.position, new Vector3(spacing * 0.9f, 0.05f, spacing * 0.9f));
        }
    }
}
