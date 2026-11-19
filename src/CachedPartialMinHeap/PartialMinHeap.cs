using System.Runtime.CompilerServices;

namespace CachedPartialMinHeap;

/// <summary>
/// Bounded-capacity Partial Min-Heap backed by an indexed <see cref="MinMaxHeap{T}"/>.
/// <para>
/// Maintains at most <see cref="Capacity"/> lowest-priority items. When full, incoming items worse
/// than the current maximum are discarded in O(1), while better items evict the current maximum
/// in O(log k) instead of the O(k) leaf scan used in v1.
/// </para>
/// <para>
/// Optionally supports the thesis <c>discardThreshold</c> mode via a value-snapshot
/// <c>discardKeySelector</c>, discarding any subsequent item with key &gt;= the best previously
/// discarded key even if the heap has shrunk below capacity.
/// </para>
/// </summary>
public sealed class PartialMinHeap<T> : IIndexedPriorityQueue<T> where T : class, IHeapItem
{
    private readonly MinMaxHeap<T> _heap;
    private readonly IComparer<T> _comparer;
    private readonly Func<T, double>? _discardKeySelector;

    private double _discardThreshold = double.PositiveInfinity;
    private long _enqueues;
    private long _dequeues;
    private long _updates;
    private long _containsChecks;
    private long _evictions;
    private long _discards;
    private long _extraComparisons;

    /// <summary>Creates a bounded Partial Min-Heap with exact capacity <paramref name="capacity"/>.</summary>
    /// <param name="capacity">Maximum number of items held simultaneously.</param>
    /// <param name="comparer">Optional priority comparer (defaults to <see cref="Comparer{T}.Default"/>).</param>
    /// <param name="discardKeySelector">
    /// Optional immutable key extractor enabling strict thesis discard-threshold pruning.
    /// When <c>null</c>, behaves as a pure bounded top-k min-heap (only discards when full).
    /// </param>
    public PartialMinHeap(int capacity, IComparer<T>? comparer = null, Func<T, double>? discardKeySelector = null)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");

        _comparer = comparer ?? Comparer<T>.Default;
        _discardKeySelector = discardKeySelector;
        _heap = new MinMaxHeap<T>(capacity, _comparer, fixedCapacity: true);
    }

    /// <summary>Creates a Partial Min-Heap sized to a complete binary tree of depth <paramref name="depth"/> (<c>2^depth - 1</c>).</summary>
    public static PartialMinHeap<T> FromDepth(int depth, IComparer<T>? comparer = null, Func<T, double>? discardKeySelector = null)
    {
        if (depth < 1 || depth > 30)
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be between 1 and 30.");
        int capacity = (1 << depth) - 1;
        return new PartialMinHeap<T>(capacity, comparer, discardKeySelector);
    }

    /// <inheritdoc />
    public int Count => _heap.Count;

    /// <summary>Maximum number of elements this bounded heap can store.</summary>
    public int Capacity => _heap.Capacity;

    /// <summary>Total number of items discarded or evicted due to capacity / threshold bounds.</summary>
    public long DiscardCount => _discards;

    /// <summary>Lowest key discarded so far when strict discard-threshold mode is enabled.</summary>
    public double DiscardThreshold => _discardThreshold;

    /// <inheritdoc />
    public void Add(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        if (_heap.Count < _heap.Capacity)
        {
            if (_discardKeySelector is not null)
            {
                double key = _discardKeySelector(item);
                _extraComparisons++;
                if (key >= _discardThreshold)
                {
                    item.HeapIndex = -1;
                    _discards++;
                    return;
                }
            }

            _enqueues++;
            _heap.Add(item);
            return;
        }

        // Heap is full: compare against current max in O(1)
        T currentMax = _heap.PeekMax();
        _extraComparisons++;
        if (_comparer.Compare(item, currentMax) >= 0)
        {
            if (_discardKeySelector is not null)
            {
                double key = _discardKeySelector(item);
                if (key < _discardThreshold)
                    _discardThreshold = key;
            }
            item.HeapIndex = -1;
            _discards++;
            return;
        }

        // Better than currentMax: evict max and insert item in O(log k)
        if (_discardKeySelector is not null)
        {
            double evictedKey = _discardKeySelector(currentMax);
            if (evictedKey < _discardThreshold)
                _discardThreshold = evictedKey;
        }

        _heap.ReplaceMax(item);
        _enqueues++;
        _evictions++;
        _discards++;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T PeekMin() => _heap.PeekMin();

    /// <summary>Returns the worst (maximum) element currently retained in O(1) time.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T PeekMax() => _heap.PeekMax();

    /// <inheritdoc />
    public T RemoveMin()
    {
        _dequeues++;
        return _heap.RemoveMin();
    }

    /// <inheritdoc />
    public void UpdateItem(T item)
    {
        _updates++;
        _heap.UpdateItem(item);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(T item)
    {
        _containsChecks++;
        return _heap.Contains(item);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _heap.Clear();
        _discardThreshold = double.PositiveInfinity;
    }

    /// <inheritdoc />
    public HeapMetrics GetMetrics()
    {
        var inner = _heap.GetMetrics();
        return new HeapMetrics(
            Enqueues: _enqueues,
            Dequeues: _dequeues,
            Updates: _updates,
            ContainsChecks: _containsChecks,
            Swaps: inner.Swaps,
            Comparisons: inner.Comparisons + _extraComparisons,
            OverflowEnqueues: 0,
            CacheEvictions: _evictions,
            Replenishes: 0,
            Discards: _discards);
    }

    /// <inheritdoc />
    public void ResetMetrics()
    {
        _heap.ResetMetrics();
        _enqueues = _dequeues = _updates = _containsChecks = _evictions = _discards = _extraComparisons = 0;
    }

    /// <summary>Verifies internal Min-Max Heap invariants (for testing/diagnostics).</summary>
    public bool VerifyInvariant() => _heap.VerifyInvariant();
}
