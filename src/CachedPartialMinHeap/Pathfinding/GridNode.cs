namespace CachedPartialMinHeap.Pathfinding;

/// <summary>
/// A single cell in a 2D octile-movement navigation grid.
/// Uses a <see cref="SearchId"/> generation counter so repeated A* searches reset in O(1) time
/// without reallocating or clearing the entire grid.
/// </summary>
public sealed class GridNode : IHeapItem, IComparable<GridNode>
{
    /// <summary>Grid X coordinate.</summary>
    public int X { get; }

    /// <summary>Grid Y coordinate.</summary>
    public int Y { get; }

    /// <summary>Whether agents can traverse this cell.</summary>
    public bool Walkable { get; set; }

    /// <summary>Exact cost from the start node (multiplied by 10: cardinal = 10, diagonal = 14).</summary>
    public int GCost { get; set; }

    /// <summary>Admissible octile heuristic estimate to the goal node.</summary>
    public int HCost { get; set; }

    /// <summary>Total estimated path cost <c>f = g + h</c>.</summary>
    public int FCost => GCost + HCost;

    /// <summary>Predecessor node along the best known path.</summary>
    public GridNode? Parent { get; set; }

    /// <summary>Search generation stamp used for O(1) reset between pathfinding runs.</summary>
    public int SearchId { get; set; }

    /// <summary>Whether this node has already been expanded (placed in the closed set) in the current search.</summary>
    public bool IsClosed { get; set; }

    /// <inheritdoc />
    public int HeapIndex { get; set; } = -1;

    /// <summary>Creates a new grid cell.</summary>
    public GridNode(int x, int y, bool walkable)
    {
        X = x;
        Y = y;
        Walkable = walkable;
    }

    /// <summary>
    /// Orders nodes by ascending <see cref="FCost"/>, breaking ties by ascending <see cref="HCost"/>
    /// (preferring nodes closer to the goal, matching standard A* tie-breaking).
    /// </summary>
    public int CompareTo(GridNode? other)
    {
        if (other is null) return -1;
        int cmp = FCost.CompareTo(other.FCost);
        if (cmp != 0) return cmp;
        cmp = HCost.CompareTo(other.HCost);
        if (cmp != 0) return cmp;
        cmp = X.CompareTo(other.X);
        return cmp != 0 ? cmp : Y.CompareTo(other.Y);
    }
}
