using CachedPartialMinHeap;
using CachedPartialMinHeap.Pathfinding;

namespace CachedPartialMinHeap.Tests;

internal sealed class TestItem : IHeapItem, IComparable<TestItem>
{
    public int Id { get; }
    public int Key { get; set; }
    public int HeapIndex { get; set; } = -1;

    public TestItem(int id, int key)
    {
        Id = id;
        Key = key;
    }

    public int CompareTo(TestItem? other)
    {
        if (other is null) return -1;
        int cmp = Key.CompareTo(other.Key);
        return cmp != 0 ? cmp : Id.CompareTo(other.Id);
    }

    public override string ToString() => $"({Id}:k={Key},idx={HeapIndex})";
}

internal static class Program
{
    private static int _passed;
    private static int _failed;

    public static int Main()
    {
        Run("MinMaxHeap: basic operations & BuildFrom", TestMinMaxHeapBasic);
        Run("MinMaxHeap: 25,000-step randomized fuzz (add/popMin/popMax/replaceMax/update/removeAt)", TestMinMaxHeapFuzz);
        Run("BinaryMinHeap & DaryMinHeap: 10,000-step randomized fuzz", TestBaselineHeapsFuzz);
        Run("PartialMinHeap: bounded top-k & strict discard-threshold mode", TestPartialMinHeap);
        Run("PartialMinHeap: 15,000-step randomized fuzz", TestPartialMinHeapFuzz);
        Run("CachedMinHeap: edge capacities (1, 2, 3, 7, 15, 63) + replenish + duplicate keys", TestCachedMinHeapEdges);
        Run("CachedMinHeap: 30,000-step randomized fuzz vs exact reference", TestCachedMinHeapFuzz);
        Run("GridMap: Moving AI .map parser", TestMovingAiMapParser);
        Run("AStarPathfinder: exact optimality across 100 random grids + slalom barriers", TestAStarOptimality);

        Console.WriteLine();
        Console.WriteLine($"Summary: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }

    private static void TestMinMaxHeapBasic()
    {
        var heap = new MinMaxHeap<TestItem>(16);
        int[] values = { 42, 7, 19, 3, 99, 15, 1, 88, 25, 3, 7, 100, 0 };
        var items = values.Select((v, i) => new TestItem(i, v)).ToArray();

        foreach (var item in items)
        {
            heap.Add(item);
            Assert(heap.VerifyInvariant(), "Invariant broken after Add");
        }

        Assert(heap.PeekMin().Key == 0, "Expected min=0");
        Assert(heap.PeekMax().Key == 100, "Expected max=100");

        var poppedMax = heap.RemoveMax();
        Assert(poppedMax.Key == 100 && poppedMax.HeapIndex == -1, "RemoveMax failed");
        Assert(heap.VerifyInvariant(), "Invariant broken after RemoveMax");

        var replacement = new TestItem(999, 50);
        var evicted = heap.ReplaceMax(replacement);
        Assert(evicted.Key == 99 && evicted.HeapIndex == -1, "ReplaceMax failed");
        Assert(heap.VerifyInvariant(), "Invariant broken after ReplaceMax");

        // Test BuildFrom (Floyd O(n) build)
        var bulk = Enumerable.Range(0, 200).Select(i => new TestItem(i, (i * 73) % 53)).ToArray();
        var built = new MinMaxHeap<TestItem>(200);
        built.BuildFrom(bulk);
        Assert(built.VerifyInvariant(), "Invariant broken after BuildFrom");

        int prev = int.MinValue;
        while (built.Count > 0)
        {
            var cur = built.RemoveMin();
            Assert(cur.Key >= prev, "BuildFrom RemoveMin not sorted");
            Assert(built.VerifyInvariant(), "Invariant broken during drain");
            prev = cur.Key;
        }
    }

    private static void TestMinMaxHeapFuzz()
    {
        var rng = new Random(1337);
        var heap = new MinMaxHeap<TestItem>(8);
        var reference = new List<TestItem>();
        int nextId = 0;

        for (int step = 0; step < 25_000; step++)
        {
            int op = rng.Next(100);
            if (reference.Count == 0 || op < 40)
            {
                var item = new TestItem(nextId++, rng.Next(-50, 50));
                heap.Add(item);
                reference.Add(item);
            }
            else if (op < 60)
            {
                reference.Sort();
                var expected = reference[0];
                reference.RemoveAt(0);
                var actual = heap.RemoveMin();
                Assert(ReferenceEquals(actual, expected), $"Step {step}: RemoveMin mismatch ({actual} vs {expected})");
                Assert(actual.HeapIndex == -1, "Popped item HeapIndex must be -1");
            }
            else if (op < 75)
            {
                reference.Sort();
                var expected = reference[^1];
                reference.RemoveAt(reference.Count - 1);
                var actual = heap.RemoveMax();
                Assert(ReferenceEquals(actual, expected), $"Step {step}: RemoveMax mismatch ({actual} vs {expected})");
                Assert(actual.HeapIndex == -1, "Popped max HeapIndex must be -1");
            }
            else if (op < 90)
            {
                var target = reference[rng.Next(reference.Count)];
                target.Key += rng.Next(-40, 41); // both DecreaseKey and IncreaseKey!
                heap.UpdateItem(target);
            }
            else if (op < 96)
            {
                reference.Sort();
                var expectedEvicted = reference[^1];
                reference.RemoveAt(reference.Count - 1);
                var fresh = new TestItem(nextId++, rng.Next(-50, 50));
                var actualEvicted = heap.ReplaceMax(fresh);
                reference.Add(fresh);
                Assert(ReferenceEquals(actualEvicted, expectedEvicted), $"Step {step}: ReplaceMax mismatch");
            }
            else
            {
                var target = reference[rng.Next(reference.Count)];
                reference.Remove(target);
                var removed = heap.RemoveAt(target.HeapIndex);
                Assert(ReferenceEquals(removed, target), $"Step {step}: RemoveAt mismatch");
            }

            Assert(heap.Count == reference.Count, $"Step {step}: Count mismatch");
            if (step % 5 == 0)
                Assert(heap.VerifyInvariant(), $"Step {step}: Invariant violated");
        }
    }

    private static void TestBaselineHeapsFuzz()
    {
        IIndexedPriorityQueue<TestItem>[] heaps =
        {
            new BinaryMinHeap<TestItem>(4),
            new DaryMinHeap<TestItem>(arity: 4, initialCapacity: 4),
            new DaryMinHeap<TestItem>(arity: 8, initialCapacity: 4),
        };

        foreach (var heap in heaps)
        {
            var rng = new Random(42);
            var reference = new List<TestItem>();
            int nextId = 0;

            for (int step = 0; step < 10_000; step++)
            {
                int op = rng.Next(100);
                if (reference.Count == 0 || op < 50)
                {
                    var item = new TestItem(nextId++, rng.Next(-100, 100));
                    heap.Add(item);
                    reference.Add(item);
                }
                else if (op < 80)
                {
                    reference.Sort();
                    var expected = reference[0];
                    reference.RemoveAt(0);
                    var actual = heap.RemoveMin();
                    Assert(ReferenceEquals(actual, expected), $"{heap.GetType().Name} step {step}: RemoveMin mismatch");
                    Assert(!heap.Contains(actual), "Removed item must not be contained");
                }
                else
                {
                    var target = reference[rng.Next(reference.Count)];
                    target.Key += rng.Next(-60, 61);
                    heap.UpdateItem(target);
                    Assert(heap.Contains(target), "Updated item must still be contained");
                }
            }
        }
    }

    private static void TestPartialMinHeap()
    {
        // Pure bounded top-k mode
        var pure = new PartialMinHeap<TestItem>(capacity: 5);
        int[] keys = { 30, 10, 50, 20, 40, 5, 60, 15, 25, 1 };
        foreach (var (k, idx) in keys.Select((k, idx) => (k, idx)))
        {
            pure.Add(new TestItem(idx, k));
            Assert(pure.VerifyInvariant(), "PartialMinHeap invariant broken");
        }

        Assert(pure.Count == 5, $"Expected count 5, got {pure.Count}");
        Assert(pure.DiscardCount == 5, $"Expected 5 discards, got {pure.DiscardCount}");

        var drained = new List<int>();
        while (pure.Count > 0)
            drained.Add(pure.RemoveMin().Key);

        Assert(drained.SequenceEqual(new[] { 1, 5, 10, 15, 20 }),
            $"Expected top-5 [1,5,10,15,20], got [{string.Join(",", drained)}]");

        // Strict thesis discard-threshold mode
        var strict = new PartialMinHeap<TestItem>(capacity: 3, discardKeySelector: x => x.Key);
        strict.Add(new TestItem(1, 10));
        strict.Add(new TestItem(2, 20));
        strict.Add(new TestItem(3, 30));
        strict.Add(new TestItem(4, 25)); // evicts 30 -> discardThreshold = 30
        Assert(strict.DiscardThreshold == 30, $"Expected threshold 30, got {strict.DiscardThreshold}");

        var min = strict.RemoveMin(); // removes 10; Count = 2 < Capacity = 3
        Assert(min.Key == 10, "Expected min 10");

        var lateHigh = new TestItem(5, 35);
        strict.Add(lateHigh); // >= threshold 30 -> discarded even though Count < Capacity
        Assert(!strict.Contains(lateHigh), "Item >= discardThreshold should be discarded");
        Assert(strict.Count == 2, "Count should remain 2");

        var lateLow = new TestItem(6, 12);
        strict.Add(lateLow); // < threshold 30 -> accepted
        Assert(strict.Contains(lateLow) && strict.Count == 3, "Item < discardThreshold should be accepted");
    }

    private static void TestPartialMinHeapFuzz()
    {
        var rng = new Random(2026);
        var heap = PartialMinHeap<TestItem>.FromDepth(4); // capacity 15
        var active = new List<TestItem>();
        int nextId = 0;

        for (int step = 0; step < 15_000; step++)
        {
            int op = rng.Next(100);
            if (active.Count == 0 || op < 55)
            {
                var item = new TestItem(nextId++, rng.Next(0, 200));
                heap.Add(item);
                if (heap.Contains(item))
                {
                    active.Add(item);
                    if (active.Count > heap.Capacity)
                    {
                        active.RemoveAll(x => !heap.Contains(x));
                    }
                }
            }
            else if (op < 85)
            {
                active.Sort();
                var expected = active[0];
                active.RemoveAt(0);
                var actual = heap.RemoveMin();
                Assert(ReferenceEquals(actual, expected), $"Step {step}: PartialMinHeap RemoveMin mismatch");
            }
            else
            {
                var target = active[rng.Next(active.Count)];
                target.Key = Math.Max(0, target.Key - rng.Next(1, 30));
                heap.UpdateItem(target);
            }

            Assert(heap.Count == active.Count && heap.Count <= heap.Capacity, $"Step {step}: count invariant mismatch");
            if (step % 5 == 0)
                Assert(heap.VerifyInvariant(), $"Step {step}: PartialMinHeap invariant broken");
        }
    }

    private static void TestCachedMinHeapEdges()
    {
        foreach (int cacheCap in new[] { 1, 2, 3, 7, 15, 63 })
        {
            var heap = new CachedMinHeap<TestItem>(cacheCap, initialOverflowCapacity: 4);
            var rng = new Random(cacheCap * 97);
            var expected = new List<TestItem>();

            // Insert 400 items with heavy duplicate keys
            for (int i = 0; i < 400; i++)
            {
                var item = new TestItem(i, rng.Next(0, 25));
                heap.Add(item);
                expected.Add(item);
                Assert(heap.VerifyInvariant(), $"cacheCap={cacheCap}: invariant broken after Add #{i}");
            }

            expected.Sort();
            for (int i = 0; i < expected.Count; i++)
            {
                var popped = heap.RemoveMin();
                Assert(ReferenceEquals(popped, expected[i]), $"cacheCap={cacheCap}: RemoveMin #{i} mismatch");
                Assert(heap.VerifyInvariant(), $"cacheCap={cacheCap}: invariant broken after RemoveMin #{i}");
            }

            Assert(heap.Count == 0 && heap.OverflowCount == 0, "Heap should be completely empty");
        }
    }

    private static void TestCachedMinHeapFuzz()
    {
        foreach (int cacheCap in new[] { 3, 15, 63 })
        {
            var rng = new Random(777 + cacheCap);
            var heap = new CachedMinHeap<TestItem>(cacheCap, initialOverflowCapacity: 8);
            var reference = new List<TestItem>();
            int nextId = 0;

            for (int step = 0; step < 10_000; step++)
            {
                int op = rng.Next(100);
                if (reference.Count == 0 || op < 48)
                {
                    var item = new TestItem(nextId++, rng.Next(-100, 100));
                    heap.Add(item);
                    reference.Add(item);
                    Assert(heap.Contains(item), $"Step {step}: freshly added item missing from Contains");
                }
                else if (op < 80)
                {
                    reference.Sort();
                    var expected = reference[0];
                    reference.RemoveAt(0);
                    var actual = heap.RemoveMin();
                    Assert(ReferenceEquals(actual, expected), $"cacheCap={cacheCap} step {step}: RemoveMin mismatch ({actual} vs {expected})");
                    Assert(actual.HeapIndex == -1 && !heap.Contains(actual), "Removed item should have HeapIndex=-1");
                }
                else
                {
                    // Mix DecreaseKey (75%) and IncreaseKey (25%) across both Cache and Overflow
                    var target = reference[rng.Next(reference.Count)];
                    int delta = rng.Next(100) < 75 ? -rng.Next(1, 80) : rng.Next(1, 80);
                    target.Key += delta;
                    heap.UpdateItem(target);
                    Assert(heap.Contains(target), $"Step {step}: updated item lost from Contains");
                }

                Assert(heap.Count == reference.Count, $"Step {step}: Count mismatch");
                if (step % 4 == 0)
                    Assert(heap.VerifyInvariant(), $"cacheCap={cacheCap} step {step}: CachedMinHeap invariant broken");
            }
        }
    }

    private static void TestMovingAiMapParser()
    {
        const string sample =
            "type octile\n" +
            "height 4\n" +
            "width 5\n" +
            "map\n" +
            "..@..\n" +
            ".@@..\n" +
            ".....\n" +
            "..TT.\n";

        var map = GridMap.ParseMovingAiMap(sample);
        Assert(map.Width == 5 && map.Height == 4, "Dimensions mismatch");
        Assert(map[0, 0].Walkable && !map[2, 0].Walkable && !map[2, 3].Walkable, "Walkable flags mismatch");

        var resBinary = AStarPathfinder.FindPath(map, (0, 0), (4, 0), new BinaryMinHeap<GridNode>());
        var resCached = AStarPathfinder.FindPath(map, (0, 0), (4, 0), new CachedMinHeap<GridNode>(3));
        Assert(resBinary.Found && resCached.Found, "Path should be found");
        Assert(resBinary.PathCost == resCached.PathCost, $"Optimal cost mismatch: {resBinary.PathCost} vs {resCached.PathCost}");
    }

    private static void TestAStarOptimality()
    {
        var rng = new Random(9001);

        // 1. 100 random obstacle tests across densities 10%..35%
        for (int trial = 0; trial < 100; trial++)
        {
            int density = 10 + (trial % 6) * 5;
            var map = GridMap.CreateRandomObstacles(64, 64, density, seed: 1000 + trial);
            var start = map.PickRandomWalkable(rng);
            var goal = map.PickRandomWalkable(rng);

            var rBin = AStarPathfinder.FindPath(map, start, goal, new BinaryMinHeap<GridNode>(256));
            var rD4 = AStarPathfinder.FindPath(map, start, goal, new DaryMinHeap<GridNode>(4, 256));
            var rCached = AStarPathfinder.FindPath(map, start, goal, new CachedMinHeap<GridNode>(31, 256));
            var rDotNet = AStarPathfinder.FindPathWithDotNetPriorityQueue(map, start, goal);

            Assert(rBin.Found == rD4.Found && rBin.Found == rCached.Found && rBin.Found == rDotNet.Found,
                $"Trial {trial}: reachability mismatch");

            if (rBin.Found)
            {
                Assert(rBin.PathCost == rD4.PathCost, $"Trial {trial}: D4 cost mismatch ({rBin.PathCost} vs {rD4.PathCost})");
                Assert(rBin.PathCost == rCached.PathCost, $"Trial {trial}: CachedMinHeap cost mismatch ({rBin.PathCost} vs {rCached.PathCost})");
                Assert(rBin.PathCost == rDotNet.PathCost, $"Trial {trial}: .NET PQ cost mismatch ({rBin.PathCost} vs {rDotNet.PathCost})");
            }
        }

        // 2. Slalom barrier maze (forces large open lists + multiple cache replenishments)
        var maze = GridMap.CreateSlalomBarriers(120, 120, barrierCount: 8, gapWidth: 3);
        var mBin = AStarPathfinder.FindPath(maze, (2, 2), (117, 117), new BinaryMinHeap<GridNode>(512));
        var mCached = AStarPathfinder.FindPath(maze, (2, 2), (117, 117), new CachedMinHeap<GridNode>(63, 512));
        var mPartial = AStarPathfinder.FindPath(maze, (2, 2), (117, 117), new PartialMinHeap<GridNode>(255));

        Assert(mBin.Found && mCached.Found, "Slalom path must be found by exact heaps");
        Assert(mBin.PathCost == mCached.PathCost, $"Slalom exact cost mismatch: {mBin.PathCost} vs {mCached.PathCost}");
        Assert(mCached.Metrics.Replenishes > 0, "Expected slalom map to trigger cache replenishment");
        Assert(mPartial.Found && mPartial.PathCost >= mBin.PathCost, "PartialMinHeap should find a valid path on k=255");
    }
}
