// WfcCell.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum ConstrainResult
{
    NoChange,
    Reduced,
    Conflict
}

public class WfcCell
{
    public int x, y;
    public List<WfcTileType> possibilities;

    public WfcCell(int x, int y, IEnumerable<WfcTileType> allTypes)
    {
        this.x = x;
        this.y = y;
        this.possibilities = new List<WfcTileType>(allTypes);
    }

    public int Entropy => possibilities.Count;

    public bool IsCollapsed => possibilities.Count == 1;

    /// <summary>
    /// Collapse cell to a single tile using weights.
    /// </summary>
    public void CollapseWeighted(System.Random rng)
    {
        if (IsCollapsed) return;

        // Weighted random choice:
        long total = 0;
        foreach (var t in possibilities) total += Mathf.Max(1, t.weight);

        long r = (long)(rng.NextDouble() * total);
        long acc = 0;
        foreach (var t in possibilities)
        {
            acc += Mathf.Max(1, t.weight);
            if (r < acc)
            {
                possibilities = new List<WfcTileType> { t };
                break;
            }
        }
        // Safety: if not selected (due to rounding), choose last
        if (!IsCollapsed && possibilities.Count > 0)
        {
            possibilities = new List<WfcTileType> { possibilities[possibilities.Count - 1] };
        }
    }

    /// <summary>
    /// Constrain this cell by neighbour possibilities that face it from given direction.
    /// direction = index in neighbour: 0=N,1=E,2=S,3=W (the side of neighbour that's adjacent to this cell).
    /// Returns NoChange / Reduced / Conflict.
    /// </summary>
    public ConstrainResult Constrain(IEnumerable<WfcTileType> neighbourPossibilities, int direction)
    {
        if (possibilities.Count == 0) return ConstrainResult.Conflict;

        // connectors = set of edge codes that neighbours provide towards this cell (they use 'direction')
        HashSet<EdgeType> connectors = new HashSet<EdgeType>();
        foreach (var np in neighbourPossibilities)
        {
            connectors.Add(np.edges[direction]);
        }

        // opposite side index: if neighbour says its direction = dir, then this cell must match opposite
        int opposite = (direction + 2) % 4;

        bool reduced = false;
        // Remove any possibility that doesn't have its opposite edge present in connectors
        for (int i = possibilities.Count - 1; i >= 0; i--)
        {
            var p = possibilities[i];
            if (!connectors.Contains(p.edges[opposite]))
            {
                possibilities.RemoveAt(i);
                reduced = true;
            }
        }

        if (possibilities.Count == 0) return ConstrainResult.Conflict;
        return reduced ? ConstrainResult.Reduced : ConstrainResult.NoChange;
    }
}
