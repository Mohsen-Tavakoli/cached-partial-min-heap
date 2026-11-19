using System.Runtime.CompilerServices;

namespace CachedPartialMinHeap;

/// <summary>
/// Two-tier exact Cached Min-Heap designed for cache-conscious priority scheduling and A* pathfinding.
/// <para>
/// Tier 1 (Cache): a small, L1/L2-cache-friendly indexed <see cref="MinMaxHeap{T}"/> of fixed capacity <c>k</c>.
/// Tier 2 (Overflow): an unsorted contiguous buffer holding items worse than the cache frontier.
/// </para>
/// <para>
/// Invariant: every element in Cache is &lt;= every element in Overflow, guaranteeing exact global
/// minimum extraction identical to a standard min-heap while deferring poor candidates in O(1)
/// and replenishing in O(n) expected time via iterative Quickselect + O(k) Floyd Min-Max Heapify.
/// </para>
/// </summary>
public sealed class CachedMinHeap<T> : IIndexedPriorityQueue<T> where T : class, IHeapItem
{
    private readonly MinMaxHeap<T> _cache;
    private readonly IComparer<T> _comparer;
    private readonly int _cacheCapacity;

    private T[] _extra;
    private int _extraCount;
    private T? _extraMin;

    private long _enqueues;
    private long _dequeues;
    private long _updates;
    private long _containsChecks;
    private long _overflowEnqueues;
    private long _cacheEvictions;
    private long _replenishes;
    private long _extraComparisons;
    private long _extraSwaps;

    /// <summary>Creates a new two-tier Cached Min-Heap.</summary>
    /// <param name="cacheCapacity">Maximum number of items held in the active Tier-1 Min-Max Heap cache.</param>
    /// <param name="initialOverflowCapacity">Initial backing capacity of the Tier-2 overflow buffer (grows automatically as needed).</param>
    /// <param name="comparer">Optional priority comparer (defaults to <see cref="Comparer{T}.Default"/>).</param>
    public CachedMinHeap(int cacheCapacity, int initialOverflowCapacity = 128, IComparer<T>? comparer = null)
    {
        if (cacheCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(cacheCapacity), "Cache capacity must be at least 1.");
        if (initialOverflowCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(initialOverflowCapacity), "Initial overflow capacity must be at least 1.");

        _cacheCapacity = cacheCapacity;
        _comparer = comparer ?? Comparer<T>.Default;
        _cache = new MinMaxHeap<T>(cacheCapacity, _comparer, fixedCapacity: true);
        _extra = new T[initialOverflowCapacity];
        _extraCount = 0;
        _extraMin = null;
    }

    /// <summary>Creates a Cached Min-Heap using complete-tree depth parameters (<c>2^depth - 1</c>) matching the original thesis.</summary>
    public static CachedMinHeap<T> FromDepths(int cacheDepth, int totalDepth, IComparer<T>? comparer = null)
    {
        if (cacheDepth < 1 || cacheDepth > 30)
            throw new ArgumentOutOfRangeException(nameof(cacheDepth), "Cache depth must be between 1 and 30.");
        if (totalDepth < cacheDepth || totalDepth > 30)
            throw new ArgumentOutOfRangeException(nameof(totalDepth), "Total depth must be >= cacheDepth and <= 30.");

        int cacheCap = (1 << cacheDepth) - 1;
        int totalCap = (1 << totalDepth) - 1;
        int overflowCap = Math.Max(16, totalCap - cacheCap);
        return new CachedMinHeap<T>(cacheCap, overflowCap, comparer);
    }

    /// <inheritdoc />
    public int Count => _cache.Count + _extraCount;

    /// <summary>Number of items currently in Tier 1 (Cache).</summary>
    public int CacheCount => _cache.Count;

    /// <summary>Fixed maximum capacity of Tier 1 (Cache).</summary>
    public int CacheCapacity => _cacheCapacity;

    /// <summary>Number of items currently deferred in Tier 2 (Overflow).</summary>
    public int OverflowCount => _extraCount;

    /// <inheritdoc />
    public void Add(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        _enqueues++;

        if (_cache.Count < _cacheCapacity)
        {
            if (_extraCount > 0 && Compare(item, _extraMin!) >= 0)
            {
                AppendToOverflow(item);
            }
            else
            {
                _cache.Add(item);
            }
            return;
        }

        // Cache is full: check against cache max in O(1)
        T cacheMax = _cache.PeekMax();
        if (Compare(item, cacheMax) >= 0)
        {
            AppendToOverflow(item);
            return;
        }

        // Item belongs in cache: evict cacheMax into overflow and insert item in O(log k)
        T evicted = _cache.ReplaceMax(item);
        _cacheEvictions++;
        AppendToOverflow(evicted);
    }

    /// <inheritdoc />
    public T PeekMin()
    {
        if (Count == 0)
            throw new InvalidOperationException("Heap is empty.");
        if (_cache.Count == 0)
            ReplenishCache();
        return _cache.PeekMin();
    }

    /// <inheritdoc />
    public T RemoveMin()
    {
        if (Count == 0)
            throw new InvalidOperationException("Heap is empty.");
        if (_cache.Count == 0)
            ReplenishCache();

        _dequeues++;
        return _cache.RemoveMin();
    }

    /// <inheritdoc />
    public void UpdateItem(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        int idx = item.HeapIndex;
        if (idx < 0)
            throw new InvalidOperationException("Item is not in this heap.");

        _updates++;

        // Case 1: Item is in Tier 1 (Cache)
        if (idx < _cacheCapacity)
        {
            if (!_cache.Contains(item))
                throw new InvalidOperationException("Item is not in this heap.");

            _cache.UpdateItem(item);

            // Handle IncreaseKey that pushes a cache item past the overflow frontier
            if (_extraCount > 0 && Compare(item, _extraMin!) > 0)
            {
                _cache.RemoveAt(item.HeapIndex);
                AppendToOverflow(item);
            }
            return;
        }

        // Case 2: Item is in Tier 2 (Overflow)
        int extraIdx = idx - _cacheCapacity;
        if ((uint)extraIdx >= (uint)_extraCount || !ReferenceEquals(_extra[extraIdx], item))
            throw new InvalidOperationException("Item is not in this heap.");

        if (_cache.Count == 0)
        {
            RefreshExtraMinAfterMutation(item);
            return;
        }

        if (_cache.Count < _cacheCapacity)
        {
            if (ReferenceEquals(item, _extraMin))
            {
                // item IS _extraMin and was mutated in-place; compare against cacheMax instead of itself
                if (Compare(item, _cache.PeekMax()) < 0)
                {
                    RemoveFromOverflowAt(extraIdx);
                    _cache.Add(item);
                    RecomputeExtraMin();
                }
                else
                {
                    RecomputeExtraMin();
                }
            }
            else if (Compare(item, _extraMin!) < 0)
            {
                RemoveFromOverflowAt(extraIdx);
                _cache.Add(item);
            }
            return;
        }

        // Cache is full: promote if strictly smaller than cacheMax
        T cacheMax = _cache.PeekMax();
        if (Compare(item, cacheMax) < 0)
        {
            // Swap item into cache and place evicted cacheMax into item's overflow slot in O(1)
            bool wasExtraMin = ReferenceEquals(item, _extraMin);
            T evicted = _cache.ReplaceMax(item);
            _cacheEvictions++;

            _extra[extraIdx] = evicted;
            evicted.HeapIndex = _cacheCapacity + extraIdx;

            // evicted came from cache, so evicted <= all other overflow items (or < old _extraMin)
            if (wasExtraMin || Compare(evicted, _extraMin!) < 0)
                _extraMin = evicted;
        }
        else
        {
            RefreshExtraMinAfterMutation(item);
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(T item)
    {
        _containsChecks++;
        int idx = item.HeapIndex;
        if (idx < 0) return false;
        if (idx < _cacheCapacity)
            return _cache.Contains(item);

        int extraIdx = idx - _cacheCapacity;
        return (uint)extraIdx < (uint)_extraCount && ReferenceEquals(_extra[extraIdx], item);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _cache.Clear();
        for (int i = 0; i < _extraCount; i++)
        {
            _extra[i].HeapIndex = -1;
            _extra[i] = default!;
        }
        _extraCount = 0;
        _extraMin = null;
    }

    /// <inheritdoc />
    public HeapMetrics GetMetrics()
    {
        var inner = _cache.GetMetrics();
        return new HeapMetrics(
            Enqueues: _enqueues,
            Dequeues: _dequeues,
            Updates: _updates,
            ContainsChecks: _containsChecks,
            Swaps: inner.Swaps + _extraSwaps,
            Comparisons: inner.Comparisons + _extraComparisons,
            OverflowEnqueues: _overflowEnqueues,
            CacheEvictions: _cacheEvictions,
            Replenishes: _replenishes,
            Discards: 0);
    }

    /// <inheritdoc />
    public void ResetMetrics()
    {
        _cache.ResetMetrics();
        _enqueues = _dequeues = _updates = _containsChecks = 0;
        _overflowEnqueues = _cacheEvictions = _replenishes = 0;
        _extraComparisons = _extraSwaps = 0;
    }

    /// <summary>
    /// Diagnostic helper verifying Min-Max Heap invariants, index encoding,
    /// <see cref="_extraMin"/> accuracy, and the strict partition invariant (<c>max(Cache) &lt;= min(Overflow)</c>).
    /// </summary>
    public bool VerifyInvariant()
    {
        if (!_cache.VerifyInvariant())
            return false;

        if (_extraCount == 0)
            return _extraMin is null;

        if (_extraMin is null)
            return false;

        bool foundExtraMin = false;
        T? cacheMax = _cache.Count > 0 ? _cache.PeekMax() : null;

        for (int i = 0; i < _extraCount; i++)
        {
            T item = _extra[i];
            if (item.HeapIndex != _cacheCapacity + i)
                return false;
            if (ReferenceEquals(item, _extraMin))
                foundExtraMin = true;
            if (_comparer.Compare(item, _extraMin) < 0)
                return false;
            if (cacheMax is not null && _comparer.Compare(cacheMax, item) > 0)
                return false;
        }

        return foundExtraMin;
    }

    /// <summary>
    /// Replenishes an empty Tier-1 cache from Tier-2 overflow in O(n) expected time:
    /// partitions the smallest <c>min(extraCount, cacheCapacity)</c> items via iterative Quickselect
    /// and builds the Tier-1 Min-Max Heap in O(k) bottom-up time.
    /// </summary>
    private void ReplenishCache()
    {
        _replenishes++;
        int moveCount = Math.Min(_extraCount, _cacheCapacity);

        if (_extraCount <= _cacheCapacity)
        {
            _cache.BuildFrom(_extra.AsSpan(0, moveCount));
            Array.Clear(_extra, 0, _extraCount);
            _extraCount = 0;
            _extraMin = null;
            return;
        }

        // Partition _extra so _extra[0..moveCount-1] contains the exact smallest moveCount items
        QuickSelect(_extra, 0, _extraCount - 1, moveCount - 1);

        // Build Tier-1 Min-Max Heap in O(k)
        _cache.BuildFrom(_extra.AsSpan(0, moveCount));

        // Compact remaining overflow items to the front, update HeapIndex, and find exact _extraMin in one linear pass
        int remaining = _extraCount - moveCount;
        T minCandidate = _extra[moveCount];
        for (int i = 0; i < remaining; i++)
        {
            T item = _extra[moveCount + i];
            _extra[i] = item;
            item.HeapIndex = _cacheCapacity + i;
            if (i > 0 && Compare(item, minCandidate) < 0)
                minCandidate = item;
        }

        Array.Clear(_extra, remaining, _extraCount - remaining);
        _extraCount = remaining;
        _extraMin = minCandidate;
    }

    /// <summary>
    /// Iterative Hoare/Lomuto-hybrid Quickselect with median-of-three pivot selection.
    /// Partitions <paramref name="arr"/>[left..right] so that all elements in [left..k] are &lt;= all elements in [k+1..right].
    /// </summary>
    private void QuickSelect(T[] arr, int left, int right, int k)
    {
        while (left < right)
        {
            int mid = left + ((right - left) >> 1);
            // Median-of-three pivot sorting: arr[left] <= arr[mid] <= arr[right]
            if (Compare(arr[mid], arr[left]) < 0) SwapArray(arr, left, mid);
            if (Compare(arr[right], arr[left]) < 0) SwapArray(arr, left, right);
            if (Compare(arr[right], arr[mid]) < 0) SwapArray(arr, mid, right);

            T pivot = arr[mid];
            int i = left;
            int j = right;

            while (true)
            {
                while (Compare(arr[i], pivot) < 0) i++;
                while (Compare(arr[j], pivot) > 0) j--;
                if (i >= j) break;
                SwapArray(arr, i, j);
                i++;
                j--;
            }

            // Hoare partition guarantees arr[left..j] <= pivot <= arr[j+1..right]
            if (k <= j)
                right = j;
            else
                left = j + 1;
        }
    }

    private void AppendToOverflow(T item)
    {
        if (_extraCount == _extra.Length)
            Array.Resize(ref _extra, _extra.Length * 2);

        item.HeapIndex = _cacheCapacity + _extraCount;
        _extra[_extraCount] = item;
        _extraCount++;
        _overflowEnqueues++;

        if (_extraCount == 1 || Compare(item, _extraMin!) < 0)
            _extraMin = item;
    }

    private void RemoveFromOverflowAt(int extraIdx)
    {
        _extra[extraIdx].HeapIndex = -1;
        _extraCount--;
        if (extraIdx < _extraCount)
        {
            T last = _extra[_extraCount];
            _extra[extraIdx] = last;
            last.HeapIndex = _cacheCapacity + extraIdx;
        }
        _extra[_extraCount] = default!;
        if (_extraCount == 0)
            _extraMin = null;
    }

    private void RefreshExtraMinAfterMutation(T item)
    {
        if (ReferenceEquals(item, _extraMin))
        {
            RecomputeExtraMin();
        }
        else if (Compare(item, _extraMin!) < 0)
        {
            _extraMin = item;
        }
    }

    private void RecomputeExtraMin()
    {
        if (_extraCount == 0)
        {
            _extraMin = null;
            return;
        }

        T best = _extra[0];
        for (int i = 1; i < _extraCount; i++)
        {
            if (Compare(_extra[i], best) < 0)
                best = _extra[i];
        }
        _extraMin = best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SwapArray(T[] arr, int i, int j)
    {
        T tmp = arr[i];
        arr[i] = arr[j];
        arr[j] = tmp;
        _extraSwaps++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Compare(T a, T b)
    {
        _extraComparisons++;
        return _comparer.Compare(a, b);
    }
}
