namespace CachedPartialMinHeap.Pathfinding;

/// <summary>
/// Standalone 2D octile navigation grid supporting random obstacle maps,
/// structured room/corridor mazes, and standard Moving AI Lab <c>.map</c> files.
/// </summary>
public sealed class GridMap
{
    private readonly GridNode[] _nodes;
    private int _nextSearchId;

    /// <summary>Grid width in cells.</summary>
    public int Width { get; }

    /// <summary>Grid height in cells.</summary>
    public int Height { get; }

    /// <summary>Total number of cells (<c>Width * Height</c>).</summary>
    public int Size => Width * Height;

    /// <summary>Creates a grid with all cells initially walkable.</summary>
    public GridMap(int width, int height)
    {
        if (width < 2 || height < 2)
            throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions must be at least 2x2.");

        Width = width;
        Height = height;
        _nodes = new GridNode[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                _nodes[y * width + x] = new GridNode(x, y, walkable: true);
            }
        }
    }

    /// <summary>Returns the node at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public GridNode this[int x, int y] => _nodes[y * Width + x];

    /// <summary>Allocates a fresh monotonically increasing search generation ID.</summary>
    internal int AllocateSearchId() => ++_nextSearchId;

    /// <summary>Generates a seeded random obstacle grid where each cell has <paramref name="obstaclePercent"/>% chance of being blocked.</summary>
    public static GridMap CreateRandomObstacles(int width, int height, int obstaclePercent, int seed)
    {
        if (obstaclePercent < 0 || obstaclePercent >= 100)
            throw new ArgumentOutOfRangeException(nameof(obstaclePercent), "Obstacle percentage must be in [0, 99].");

        var map = new GridMap(width, height);
        var rng = new Random(seed);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (rng.Next(100) < obstaclePercent)
                    map[x, y].Walkable = false;
            }
        }

        return map;
    }

    /// <summary>
    /// Generates a structured room-and-barrier obstacle map that forces long A* detours and large open lists
    /// (stress-testing priority queue scalability and Partial Min-Heap pruning limits).
    /// </summary>
    public static GridMap CreateSlalomBarriers(int width, int height, int barrierCount, int gapWidth = 4)
    {
        var map = new GridMap(width, height);
        if (barrierCount <= 0) return map;

        int spacing = Math.Max(4, width / (barrierCount + 1));
        for (int b = 1; b <= barrierCount; b++)
        {
            int bx = b * spacing;
            if (bx >= width - 1) break;

            bool gapAtTop = (b & 1) == 0;
            for (int y = 0; y < height; y++)
            {
                bool inGap = gapAtTop ? y < gapWidth : y >= height - gapWidth;
                if (!inGap)
                    map[bx, y].Walkable = false;
            }
        }

        return map;
    }

    /// <summary>
    /// Parses a standard Moving AI Lab 2D grid <c>.map</c> file
    /// (<c>.</c> and <c>G</c> are walkable; <c>@</c>, <c>O</c>, <c>T</c>, <c>W</c> are blocked).
    /// </summary>
    public static GridMap ParseMovingAiMap(string mapText)
    {
        using var reader = new StringReader(mapText);
        int height = -1;
        int width = -1;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.StartsWith("height ", StringComparison.OrdinalIgnoreCase))
                height = int.Parse(line.AsSpan(7));
            else if (line.StartsWith("width ", StringComparison.OrdinalIgnoreCase))
                width = int.Parse(line.AsSpan(6));
            else if (line.Equals("map", StringComparison.OrdinalIgnoreCase))
                break;
        }

        if (width < 2 || height < 2)
            throw new FormatException("Invalid Moving AI .map header: missing width/height.");

        var map = new GridMap(width, height);
        for (int y = 0; y < height; y++)
        {
            string? row = reader.ReadLine() ?? throw new FormatException($"Unexpected EOF at row {y}.");
            if (row.Length < width)
                throw new FormatException($"Row {y} length {row.Length} is smaller than width {width}.");

            for (int x = 0; x < width; x++)
            {
                char c = row[x];
                map[x, y].Walkable = c == '.' || c == 'G';
            }
        }

        return map;
    }

    /// <summary>Picks a random walkable coordinate using <paramref name="rng"/>.</summary>
    public (int X, int Y) PickRandomWalkable(Random rng)
    {
        for (int attempt = 0; attempt < 10_000; attempt++)
        {
            int x = rng.Next(Width);
            int y = rng.Next(Height);
            if (this[x, y].Walkable)
                return (x, y);
        }

        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (this[x, y].Walkable)
                    return (x, y);

        throw new InvalidOperationException("Map has no walkable cells.");
    }
}
