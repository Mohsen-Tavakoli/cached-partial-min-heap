using System.Diagnostics;
using System.Globalization;
using System.Text;
using CachedPartialMinHeap;
using CachedPartialMinHeap.Pathfinding;

namespace CachedPartialMinHeap.Benchmarks;

internal sealed class BenchItem : IHeapItem, IComparable<BenchItem>
{
    public int Key { get; set; }
    public int HeapIndex { get; set; } = -1;

    public BenchItem(int key) => Key = key;

    public int CompareTo(BenchItem? other) => other is null ? -1 : Key.CompareTo(other.Key);
}

internal readonly record struct AggregateStats(
    string QueueName,
    string Scenario,
    int Trials,
    int Successes,
    double MeanTimeUs,
    double MedianTimeUs,
    double MeanExpanded,
    double MeanInternalOps,
    double MeanPathCost,
    double MeanDiscards,
    double MeanReplenishes);

internal static class Program
{
    public static int Main(string[] args)
    {
        string? csvOut = null;
        int gridSize = 160;
        int trialsPerDensity = 30;
        int cacheCapacity = 127; // depth 7 (2^7 - 1)
        int partialCapacity = 127;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--csv" when i + 1 < args.Length:
                    csvOut = args[++i];
                    break;
                case "--grid" when i + 1 < args.Length:
                    gridSize = int.Parse(args[++i]);
                    break;
                case "--trials" when i + 1 < args.Length:
                    trialsPerDensity = int.Parse(args[++i]);
                    break;
                case "--cache" when i + 1 < args.Length:
                    cacheCapacity = int.Parse(args[++i]);
                    break;
                case "--partial" when i + 1 < args.Length:
                    partialCapacity = int.Parse(args[++i]);
                    break;
            }
        }

        Console.WriteLine($"=== Cached & Partial Min-Heap v2 Benchmark Suite ===");
        Console.WriteLine($"Runtime: .NET {Environment.Version} | Grid: {gridSize}x{gridSize} | Trials/scenario: {trialsPerDensity} | Cache k={cacheCapacity} | Partial k={partialCapacity}");
        Console.WriteLine();

        // Warmup JIT
        RunWarmup();

        var allStats = new List<AggregateStats>();

        // Suite 1: Random Obstacle Grids (10%, 20%, 30%, 35%)
        foreach (int density in new[] { 10, 20, 30, 35 })
        {
            string label = $"Random {gridSize}x{gridSize} ({density}% obs)";
            var stats = BenchmarkRandomGrid(label, gridSize, density, trialsPerDensity, cacheCapacity, partialCapacity);
            allStats.AddRange(stats);
        }

        // Suite 2: Structured Slalom Barrier Maze (large open lists, heavy backtracking)
        {
            string label = $"Slalom Maze {gridSize}x{gridSize}";
            var stats = BenchmarkSlalomMaze(label, gridSize, Math.Max(10, trialsPerDensity / 2), cacheCapacity, partialCapacity);
            allStats.AddRange(stats);
        }

        PrintMarkdownTable("A* Pathfinding Benchmark Results", allStats);

        // Suite 3: Pure Queue Microbenchmark (Insert-Heavy + Drain — typical A* frontier ratio ~4:1 enqueue:dequeue)
        Console.WriteLine();
        RunQueueMicrobenchmark(cacheCapacity);

        if (csvOut is not null)
        {
            WriteCsv(csvOut, allStats);
            Console.WriteLine($"\nWrote CSV results to: {csvOut}");
        }

        return 0;
    }

    private static void RunWarmup()
    {
        var map = GridMap.CreateRandomObstacles(64, 64, 20, seed: 1);
        var rng = new Random(1);
        for (int i = 0; i < 5; i++)
        {
            var s = map.PickRandomWalkable(rng);
            var g = map.PickRandomWalkable(rng);
            AStarPathfinder.FindPath(map, s, g, new BinaryMinHeap<GridNode>(256));
            AStarPathfinder.FindPath(map, s, g, new DaryMinHeap<GridNode>(4, 256));
            AStarPathfinder.FindPath(map, s, g, new CachedMinHeap<GridNode>(63, 256));
            AStarPathfinder.FindPath(map, s, g, new PartialMinHeap<GridNode>(63));
            AStarPathfinder.FindPathWithDotNetPriorityQueue(map, s, g);
        }
    }

    private static List<AggregateStats> BenchmarkRandomGrid(
        string scenario, int gridSize, int density, int trials, int cacheCap, int partialCap)
    {
        var map = GridMap.CreateRandomObstacles(gridSize, gridSize, density, seed: 4200 + density);
        var rng = new Random(9900 + density);

        // Generate valid start/goal pairs with meaningful separation (>= gridSize / 2)
        var pairs = new List<((int X, int Y) Start, (int X, int Y) Goal)>(trials);
        int attempts = 0;
        while (pairs.Count < trials && attempts < trials * 50)
        {
            attempts++;
            var s = map.PickRandomWalkable(rng);
            var g = map.PickRandomWalkable(rng);
            int dist = Math.Abs(s.X - g.X) + Math.Abs(s.Y - g.Y);
            if (dist >= gridSize / 2)
                pairs.Add((s, g));
        }

        return RunPairSuite(scenario, map, pairs, cacheCap, partialCap);
    }

    private static List<AggregateStats> BenchmarkSlalomMaze(
        string scenario, int gridSize, int trials, int cacheCap, int partialCap)
    {
        var map = GridMap.CreateSlalomBarriers(gridSize, gridSize, barrierCount: 10, gapWidth: 4);
        var rng = new Random(777);
        var pairs = new List<((int X, int Y) Start, (int X, int Y) Goal)>(trials);
        for (int i = 0; i < trials; i++)
        {
            var s = (rng.Next(1, 10), rng.Next(1, gridSize - 1));
            var g = (gridSize - 1 - rng.Next(1, 10), rng.Next(1, gridSize - 1));
            if (map[s.Item1, s.Item2].Walkable && map[g.Item1, g.Item2].Walkable)
                pairs.Add((s, g));
        }

        return RunPairSuite(scenario, map, pairs, cacheCap, partialCap);
    }

    private static List<AggregateStats> RunPairSuite(
        string scenario,
        GridMap map,
        List<((int X, int Y) Start, (int X, int Y) Goal)> pairs,
        int cacheCap,
        int partialCap)
    {
        var runners = new (string Name, Func<GridMap, (int, int), (int, int), PathfindingResult> Run)[]
        {
            ("BinaryMinHeap", (m, s, g) => AStarPathfinder.FindPath(m, s, g, new BinaryMinHeap<GridNode>(1024))),
            ("DaryMinHeap (d=4)", (m, s, g) => AStarPathfinder.FindPath(m, s, g, new DaryMinHeap<GridNode>(4, 1024))),
            (".NET PriorityQueue", (m, s, g) => AStarPathfinder.FindPathWithDotNetPriorityQueue(m, s, g)),
            ($"CachedMinHeap (k={cacheCap})", (m, s, g) => AStarPathfinder.FindPath(m, s, g, new CachedMinHeap<GridNode>(cacheCap, 1024))),
            ($"PartialMinHeap (k={partialCap})", (m, s, g) => AStarPathfinder.FindPath(m, s, g, new PartialMinHeap<GridNode>(partialCap))),
        };

        var results = new List<AggregateStats>();
        foreach (var (name, run) in runners)
        {
            var list = new List<PathfindingResult>(pairs.Count);
            foreach (var (s, g) in pairs)
                list.Add(run(map, s, g));

            results.Add(Aggregate(name, scenario, list));
        }

        return results;
    }

    private static AggregateStats Aggregate(string name, string scenario, List<PathfindingResult> runs)
    {
        var times = runs.Select(r => r.ElapsedMicroseconds).OrderBy(x => x).ToArray();
        double median = times.Length == 0 ? 0 : times[times.Length / 2];
        var successes = runs.Where(r => r.Found).ToList();

        return new AggregateStats(
            QueueName: name,
            Scenario: scenario,
            Trials: runs.Count,
            Successes: successes.Count,
            MeanTimeUs: runs.Average(r => r.ElapsedMicroseconds),
            MedianTimeUs: median,
            MeanExpanded: runs.Average(r => r.NodesExpanded),
            MeanInternalOps: runs.Average(r => (double)r.Metrics.TotalInternalOps),
            MeanPathCost: successes.Count > 0 ? successes.Average(r => r.PathCost) : 0,
            MeanDiscards: runs.Average(r => (double)r.Metrics.Discards),
            MeanReplenishes: runs.Average(r => (double)r.Metrics.Replenishes));
    }

    private static void RunQueueMicrobenchmark(int cacheCap)
    {
        Console.WriteLine("### Synthetic Frontier Stress Test (100,000 Enqueues, 20,000 Dequeues — 5:1 A*-style Open/Closed Ratio)");
        Console.WriteLine();
        Console.WriteLine("| Priority Queue | Time (ms) | Internal Ops (Cmp+Swap) | Overflow Enqueues | Replenishes | Discards |");
        Console.WriteLine("| :--- | ---: | ---: | ---: | ---: | ---: |");

        const int totalEnqueues = 100_000;
        const int totalDequeues = 20_000;

        var rng = new Random(2026);
        int[] keys = new int[totalEnqueues];
        // Simulate A* f-cost drift: gradually increasing baseline + spread
        for (int i = 0; i < totalEnqueues; i++)
            keys[i] = (i / 10) + rng.Next(0, 10_000);

        var factories = new (string Name, Func<IIndexedPriorityQueue<BenchItem>> Create)[]
        {
            ("BinaryMinHeap", () => new BinaryMinHeap<BenchItem>(totalEnqueues)),
            ("DaryMinHeap (d=4)", () => new DaryMinHeap<BenchItem>(4, totalEnqueues)),
            ($"CachedMinHeap (k={cacheCap})", () => new CachedMinHeap<BenchItem>(cacheCap, totalEnqueues)),
            ($"CachedMinHeap (k=511)", () => new CachedMinHeap<BenchItem>(511, totalEnqueues)),
            ($"PartialMinHeap (k={cacheCap})", () => new PartialMinHeap<BenchItem>(cacheCap)),
        };

        foreach (var (name, create) in factories)
        {
            var items = new BenchItem[totalEnqueues];
            for (int i = 0; i < totalEnqueues; i++)
                items[i] = new BenchItem(keys[i]);

            var q = create();
            long start = Stopwatch.GetTimestamp();
            int enqIdx = 0;
            for (int d = 0; d < totalDequeues; d++)
            {
                for (int r = 0; r < 5; r++)
                    q.Add(items[enqIdx++]);
                if (q.Count > 0)
                    q.RemoveMin();
            }
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            var m = q.GetMetrics();

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "| {0} | {1:F2} | {2:N0} | {3:N0} | {4:N0} | {5:N0} |",
                name, ms, m.TotalInternalOps, m.OverflowEnqueues, m.Replenishes, m.Discards));
        }
    }

    private static void PrintMarkdownTable(string title, List<AggregateStats> stats)
    {
        Console.WriteLine($"### {title}");
        Console.WriteLine();
        Console.WriteLine("| Scenario | Queue | Success | Median (µs) | Mean (µs) | Expanded | Internal Ops | Path Cost | Replenishes | Discards |");
        Console.WriteLine("| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var s in stats)
        {
            string opsStr = s.MeanInternalOps > 0 ? s.MeanInternalOps.ToString("N0", CultureInfo.InvariantCulture) : "n/a";
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "| {0} | {1} | {2}/{3} | {4:F1} | {5:F1} | {6:F1} | {7} | {8:F1} | {9:F1} | {10:F1} |",
                s.Scenario, s.QueueName, s.Successes, s.Trials,
                s.MedianTimeUs, s.MeanTimeUs, s.MeanExpanded, opsStr,
                s.MeanPathCost, s.MeanReplenishes, s.MeanDiscards));
        }
    }

    private static void WriteCsv(string path, List<AggregateStats> stats)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine("Scenario,Queue,Trials,Successes,MeanTimeUs,MedianTimeUs,MeanExpanded,MeanInternalOps,MeanPathCost,MeanReplenishes,MeanDiscards");
        foreach (var s in stats)
        {
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "\"{0}\",\"{1}\",{2},{3},{4:F3},{5:F3},{6:F2},{7:F2},{8:F2},{9:F2},{10:F2}",
                s.Scenario, s.QueueName, s.Trials, s.Successes,
                s.MeanTimeUs, s.MedianTimeUs, s.MeanExpanded, s.MeanInternalOps,
                s.MeanPathCost, s.MeanReplenishes, s.MeanDiscards));
        }
        File.WriteAllText(path, sb.ToString());
    }
}
