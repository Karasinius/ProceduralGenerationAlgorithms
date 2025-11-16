
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

    public void CollapseWeighted(System.Random rng)
    {
        if (IsCollapsed) return;

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
        if (!IsCollapsed && possibilities.Count > 0)
        {
            possibilities = new List<WfcTileType> { possibilities[possibilities.Count - 1] };
        }
    }

    public ConstrainResult Constrain(IEnumerable<WfcTileType> neighbourPossibilities, int direction)
    {
        if (possibilities.Count == 0) return ConstrainResult.Conflict;

        HashSet<EdgeType> connectors = new HashSet<EdgeType>();
        foreach (var np in neighbourPossibilities)
        {
            connectors.Add(np.edges[direction]);
        }

        int opposite = (direction + 2) % 4;

        bool reduced = false;
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
