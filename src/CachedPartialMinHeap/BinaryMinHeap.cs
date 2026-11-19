using System.Runtime.CompilerServices;

namespace CachedPartialMinHeap;

/// <summary>
/// Standard indexed binary min-heap serving as the classic O(log n) baseline.
/// Supports dynamic growth, O(1) <see cref="Contains"/>, and O(log n) decrease-key / increase-key.
/// </summary>
public sealed class BinaryMinHeap<T> : IIndexedPriorityQueue<T> where T : class, IHeapItem
{
    private readonly IComparer<T> _comparer;
    private T[] _items;
    private int _count;

    private long _enqueues;
    private long _dequeues;
    private long _updates;
    private long _containsChecks;
    private long _swaps;
    private long _comparisons;

    /// <summary>Creates a new indexed binary min-heap.</summary>
    public BinaryMinHeap(int initialCapacity = 64, IComparer<T>? comparer = null)
    {
        if (initialCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Initial capacity must be at least 1.");

        _items = new T[initialCapacity];
        _comparer = comparer ?? Comparer<T>.Default;
    }

    /// <inheritdoc />
    public int Count => _count;

    /// <summary>Current allocated backing array capacity.</summary>
    public int Capacity => _items.Length;

    /// <inheritdoc />
    public void Add(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        if (_count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);

        _enqueues++;
        item.HeapIndex = _count;
        _items[_count] = item;
        _count++;
        SiftUp(_count - 1);
    }

    /// <inheritdoc />
    public T PeekMin()
    {
        if (_count == 0)
            throw new InvalidOperationException("Heap is empty.");
        return _items[0];
    }

    /// <inheritdoc />
    public T RemoveMin()
    {
        if (_count == 0)
            throw new InvalidOperationException("Heap is empty.");

        _dequeues++;
        T min = _items[0];
        min.HeapIndex = -1;
        _count--;

        if (_count > 0)
        {
            T last = _items[_count];
            _items[_count] = default!;
            _items[0] = last;
            last.HeapIndex = 0;
            SiftDown(0);
        }
        else
        {
            _items[0] = default!;
        }

        return min;
    }

    /// <inheritdoc />
    public void UpdateItem(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        int idx = item.HeapIndex;
        if ((uint)idx >= (uint)_count || !ReferenceEquals(_items[idx], item))
            throw new InvalidOperationException("Item is not in this heap.");

        _updates++;
        if (idx > 0 && Compare(_items[idx], _items[(idx - 1) >> 1]) < 0)
            SiftUp(idx);
        else
            SiftDown(idx);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(T item)
    {
        _containsChecks++;
        int idx = item.HeapIndex;
        return (uint)idx < (uint)_count && ReferenceEquals(_items[idx], item);
    }

    /// <inheritdoc />
    public void Clear()
    {
        for (int i = 0; i < _count; i++)
        {
            _items[i].HeapIndex = -1;
            _items[i] = default!;
        }
        _count = 0;
    }

    /// <inheritdoc />
    public HeapMetrics GetMetrics() => new(
        Enqueues: _enqueues,
        Dequeues: _dequeues,
        Updates: _updates,
        ContainsChecks: _containsChecks,
        Swaps: _swaps,
        Comparisons: _comparisons,
        OverflowEnqueues: 0,
        CacheEvictions: 0,
        Replenishes: 0,
        Discards: 0);

    /// <inheritdoc />
    public void ResetMetrics()
    {
        _enqueues = _dequeues = _updates = _containsChecks = _swaps = _comparisons = 0;
    }

    private void SiftUp(int index)
    {
        T item = _items[index];
        while (index > 0)
        {
            int parentIdx = (index - 1) >> 1;
            T parent = _items[parentIdx];
            if (Compare(item, parent) >= 0)
                break;

            _items[index] = parent;
            parent.HeapIndex = index;
            _swaps++;
            index = parentIdx;
        }
        _items[index] = item;
        item.HeapIndex = index;
    }

    private void SiftDown(int index)
    {
        T item = _items[index];
        int half = _count >> 1;

        while (index < half)
        {
            int child = (index << 1) + 1;
            int right = child + 1;

            if (right < _count && Compare(_items[right], _items[child]) < 0)
                child = right;

            if (Compare(item, _items[child]) <= 0)
                break;

            T childItem = _items[child];
            _items[index] = childItem;
            childItem.HeapIndex = index;
            _swaps++;
            index = child;
        }

        _items[index] = item;
        item.HeapIndex = index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Compare(T a, T b)
    {
        _comparisons++;
        return _comparer.Compare(a, b);
    }
}
