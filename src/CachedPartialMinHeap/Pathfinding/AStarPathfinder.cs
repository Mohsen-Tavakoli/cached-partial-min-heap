using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CachedPartialMinHeap.Pathfinding;

/// <summary>
/// Outcome of a single A* pathfinding execution.
/// </summary>
public readonly record struct PathfindingResult(
    bool Found,
    int PathCost,
    int PathLength,
    int NodesExpanded,
    int MaxOpenSetSize,
    double ElapsedMicroseconds,
    HeapMetrics Metrics);

/// <summary>
/// Standalone octile A* pathfinder with zero Unity dependencies.
/// Supports any <see cref="IIndexedPriorityQueue{T}"/> as well as .NET's built-in
/// <see cref="PriorityQueue{TElement, TPriority}"/> (lazy-duplicate decrease-key) for apples-to-apples benchmarking.
/// </summary>
public static class AStarPathfinder
{
    private static readonly (int Dx, int Dy, int Cost)[] Directions =
    {
        (-1,  0, 10), ( 1,  0, 10), ( 0, -1, 10), ( 0,  1, 10),
        (-1, -1, 14), (-1,  1, 14), ( 1, -1, 14), ( 1,  1, 14),
    };

    /// <summary>
    /// Runs octile A* on <paramref name="map"/> using the supplied indexed priority queue.
    /// </summary>
    public static PathfindingResult FindPath(
        GridMap map,
        (int X, int Y) start,
        (int X, int Y) goal,
        IIndexedPriorityQueue<GridNode> openSet)
    {
        int searchId = map.AllocateSearchId();
        GridNode startNode = map[start.X, start.Y];
        GridNode goalNode = map[goal.X, goal.Y];

        if (!startNode.Walkable || !goalNode.Walkable)
            return new PathfindingResult(false, int.MaxValue, 0, 0, 0, 0, default);

        openSet.Clear();
        openSet.ResetMetrics();

        long startTicks = Stopwatch.GetTimestamp();

        InitNode(startNode, searchId, gCost: 0, hCost: OctileDistance(startNode, goalNode), parent: null);
        openSet.Add(startNode);

        int nodesExpanded = 0;
        int maxOpenSize = 1;

        while (openSet.Count > 0)
        {
            GridNode current = openSet.RemoveMin();
            current.IsClosed = true;
            nodesExpanded++;

            if (ReferenceEquals(current, goalNode))
            {
                double elapsedUs = TicksToMicroseconds(Stopwatch.GetTimestamp() - startTicks);
                int pathLength = CountPathLength(startNode, goalNode);
                return new PathfindingResult(
                    Found: true,
                    PathCost: goalNode.GCost,
                    PathLength: pathLength,
                    NodesExpanded: nodesExpanded,
                    MaxOpenSetSize: maxOpenSize,
                    ElapsedMicroseconds: elapsedUs,
                    Metrics: openSet.GetMetrics());
            }

            int cx = current.X;
            int cy = current.Y;
            int width = map.Width;
            int height = map.Height;

            for (int d = 0; d < 8; d++)
            {
                var (dx, dy, stepCost) = Directions[d];
                int nx = cx + dx;
                int ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;

                GridNode neighbor = map[nx, ny];
                if (!neighbor.Walkable)
                    continue;

                bool visited = neighbor.SearchId == searchId;
                if (visited && neighbor.IsClosed)
                    continue;

                int tentativeG = current.GCost + stepCost;
                bool inOpen = visited && openSet.Contains(neighbor);

                if (!inOpen)
                {
                    if (!visited || tentativeG < neighbor.GCost)
                    {
                        InitNode(neighbor, searchId, tentativeG, OctileDistance(neighbor, goalNode), current);
                        openSet.Add(neighbor);
                        if (openSet.Count > maxOpenSize)
                            maxOpenSize = openSet.Count;
                    }
                }
                else if (tentativeG < neighbor.GCost)
                {
                    neighbor.GCost = tentativeG;
                    neighbor.Parent = current;
                    openSet.UpdateItem(neighbor);
                }
            }
        }

        double failElapsedUs = TicksToMicroseconds(Stopwatch.GetTimestamp() - startTicks);
        return new PathfindingResult(
            Found: false,
            PathCost: int.MaxValue,
            PathLength: 0,
            NodesExpanded: nodesExpanded,
            MaxOpenSetSize: maxOpenSize,
            ElapsedMicroseconds: failElapsedUs,
            Metrics: openSet.GetMetrics());
    }

    /// <summary>
    /// Runs octile A* using .NET's built-in 4-ary <see cref="PriorityQueue{TElement, TPriority}"/>
    /// with lazy duplicate insertion (standard pattern when decrease-key is unavailable).
    /// </summary>
    public static PathfindingResult FindPathWithDotNetPriorityQueue(
        GridMap map,
        (int X, int Y) start,
        (int X, int Y) goal)
    {
        int searchId = map.AllocateSearchId();
        GridNode startNode = map[start.X, start.Y];
        GridNode goalNode = map[goal.X, goal.Y];

        if (!startNode.Walkable || !goalNode.Walkable)
            return new PathfindingResult(false, int.MaxValue, 0, 0, 0, 0, default);

        var openSet = new PriorityQueue<GridNode, (int F, int H)>(256);
        long enqueues = 0;
        long dequeues = 0;

        long startTicks = Stopwatch.GetTimestamp();

        InitNode(startNode, searchId, gCost: 0, hCost: OctileDistance(startNode, goalNode), parent: null);
        openSet.Enqueue(startNode, (startNode.FCost, startNode.HCost));
        enqueues++;

        int nodesExpanded = 0;
        int maxOpenSize = 1;

        while (openSet.Count > 0)
        {
            GridNode current = openSet.Dequeue();
            dequeues++;
            if (current.IsClosed)
                continue;

            current.IsClosed = true;
            nodesExpanded++;

            if (ReferenceEquals(current, goalNode))
            {
                double elapsedUs = TicksToMicroseconds(Stopwatch.GetTimestamp() - startTicks);
                int pathLength = CountPathLength(startNode, goalNode);
                var metrics = new HeapMetrics(enqueues, dequeues, 0, 0, 0, 0, 0, 0, 0, 0);
                return new PathfindingResult(true, goalNode.GCost, pathLength, nodesExpanded, maxOpenSize, elapsedUs, metrics);
            }

            int cx = current.X;
            int cy = current.Y;
            int width = map.Width;
            int height = map.Height;

            for (int d = 0; d < 8; d++)
            {
                var (dx, dy, stepCost) = Directions[d];
                int nx = cx + dx;
                int ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;

                GridNode neighbor = map[nx, ny];
                if (!neighbor.Walkable)
                    continue;

                bool visited = neighbor.SearchId == searchId;
                if (visited && neighbor.IsClosed)
                    continue;

                int tentativeG = current.GCost + stepCost;
                if (!visited || tentativeG < neighbor.GCost)
                {
                    InitNode(neighbor, searchId, tentativeG, OctileDistance(neighbor, goalNode), current);
                    openSet.Enqueue(neighbor, (neighbor.FCost, neighbor.HCost));
                    enqueues++;
                    if (openSet.Count > maxOpenSize)
                        maxOpenSize = openSet.Count;
                }
            }
        }

        double failElapsedUs = TicksToMicroseconds(Stopwatch.GetTimestamp() - startTicks);
        var failMetrics = new HeapMetrics(enqueues, dequeues, 0, 0, 0, 0, 0, 0, 0, 0);
        return new PathfindingResult(false, int.MaxValue, 0, nodesExpanded, maxOpenSize, failElapsedUs, failMetrics);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InitNode(GridNode node, int searchId, int gCost, int hCost, GridNode? parent)
    {
        node.SearchId = searchId;
        node.IsClosed = false;
        node.HeapIndex = -1;
        node.GCost = gCost;
        node.HCost = hCost;
        node.Parent = parent;
    }

    /// <summary>Computes exact admissible octile distance (10 cardinal, 14 diagonal).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OctileDistance(GridNode a, GridNode b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        return dx > dy
            ? 14 * dy + 10 * (dx - dy)
            : 14 * dx + 10 * (dy - dx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double TicksToMicroseconds(long ticks)
    {
        return ticks * 1_000_000.0 / Stopwatch.Frequency;
    }

    private static int CountPathLength(GridNode start, GridNode goal)
    {
        int length = 0;
        GridNode? cur = goal;
        while (cur is not null && !ReferenceEquals(cur, start))
        {
            length++;
            cur = cur.Parent;
        }
        return length;
    }
}
