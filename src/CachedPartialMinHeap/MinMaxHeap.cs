using System.Numerics;
using System.Runtime.CompilerServices;

namespace CachedPartialMinHeap;

/// <summary>
/// Indexed double-ended Min-Max Heap (Atkinson, Sack, Santoro, Strothotte, 1986).
/// Provides O(1) <see cref="PeekMin"/> and <see cref="PeekMax"/>, O(log n) <see cref="Add"/>,
/// <see cref="RemoveMin"/>, <see cref="RemoveMax"/>, <see cref="ReplaceMax"/>, <see cref="UpdateItem"/>,
/// and O(n) bottom-up <see cref="BuildFrom"/>.
/// Serves as the fast O(log k) cache core for both <see cref="PartialMinHeap{T}"/> and <see cref="CachedMinHeap{T}"/>.
/// </summary>
public sealed class MinMaxHeap<T> : IIndexedPriorityQueue<T> where T : class, IHeapItem
{
    private readonly IComparer<T> _comparer;
    private readonly bool _fixedCapacity;
    private T[] _items;
    private int _count;

    private long _enqueues;
    private long _dequeues;
    private long _updates;
    private long _containsChecks;
    private long _swaps;
    private long _comparisons;

    /// <summary>Creates a new indexed Min-Max Heap.</summary>
    public MinMaxHeap(int capacity = 64, IComparer<T>? comparer = null, bool fixedCapacity = false)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");

        _items = new T[capacity];
        _comparer = comparer ?? Comparer<T>.Default;
        _fixedCapacity = fixedCapacity;
    }

    /// <inheritdoc />
    public int Count => _count;

    /// <summary>Current backing array capacity.</summary>
    public int Capacity => _items.Length;

    /// <inheritdoc />
    public void Add(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        if (_count == _items.Length)
        {
            if (_fixedCapacity)
                throw new InvalidOperationException("MinMaxHeap has reached its fixed capacity.");
            Array.Resize(ref _items, _items.Length * 2);
        }

        _enqueues++;
        _items[_count] = item;
        item.HeapIndex = _count;
        _count++;
        PushUp(_count - 1);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T PeekMin()
    {
        if (_count == 0)
            throw new InvalidOperationException("Heap is empty.");
        return _items[0];
    }

    /// <summary>Returns the maximum element in O(1) time without removing it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T PeekMax()
    {
        if (_count == 0)
            throw new InvalidOperationException("Heap is empty.");
        return _items[GetMaxIndex()];
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
            PushDownMin(0);
        }
        else
        {
            _items[0] = default!;
        }

        return min;
    }

    /// <summary>Removes and returns the maximum element in O(log n) time.</summary>
    public T RemoveMax()
    {
        if (_count == 0)
            throw new InvalidOperationException("Heap is empty.");

        _dequeues++;
        int maxIdx = GetMaxIndex();
        T max = _items[maxIdx];
        max.HeapIndex = -1;
        _count--;

        if (maxIdx < _count)
        {
            T last = _items[_count];
            _items[_count] = default!;
            _items[maxIdx] = last;
            last.HeapIndex = maxIdx;
            RestoreAt(maxIdx);
        }
        else
        {
            _items[maxIdx] = default!;
        }

        return max;
    }

    /// <summary>
    /// Atomically evicts the current maximum element and inserts <paramref name="replacement"/>
    /// in a single O(log n) operation, returning the evicted maximum.
    /// </summary>
    public T ReplaceMax(T replacement)
    {
        if (replacement is null) throw new ArgumentNullException(nameof(replacement));
        if (_count == 0)
            throw new InvalidOperationException("Heap is empty.");

        int maxIdx = GetMaxIndex();
        T evicted = _items[maxIdx];
        evicted.HeapIndex = -1;

        _items[maxIdx] = replacement;
        replacement.HeapIndex = maxIdx;
        RestoreAt(maxIdx);

        return evicted;
    }

    /// <summary>Removes and returns the element at <paramref name="index"/> in O(log n) time.</summary>
    public T RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));

        T removed = _items[index];
        removed.HeapIndex = -1;
        _count--;

        if (index < _count)
        {
            T last = _items[_count];
            _items[_count] = default!;
            _items[index] = last;
            last.HeapIndex = index;
            RestoreAt(index);
        }
        else
        {
            _items[index] = default!;
        }

        return removed;
    }

    /// <inheritdoc />
    public void UpdateItem(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        int idx = item.HeapIndex;
        if ((uint)idx >= (uint)_count || !ReferenceEquals(_items[idx], item))
            throw new InvalidOperationException("Item is not in this heap.");

        _updates++;
        RestoreAt(idx);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(T item)
    {
        _containsChecks++;
        int idx = item.HeapIndex;
        return (uint)idx < (uint)_count && ReferenceEquals(_items[idx], item);
    }

    /// <summary>
    /// Replaces the entire heap contents with <paramref name="source"/> using Floyd's bottom-up
    /// O(n) Min-Max Heap construction algorithm.
    /// </summary>
    public void BuildFrom(ReadOnlySpan<T> source)
    {
        if (source.Length > _items.Length)
        {
            if (_fixedCapacity)
                throw new ArgumentException("Source length exceeds fixed capacity.", nameof(source));
            _items = new T[Math.Max(source.Length, _items.Length * 2)];
        }

        // Clear any leftover references beyond source.Length
        for (int i = source.Length; i < _count; i++)
            _items[i] = default!;

        _count = source.Length;
        for (int i = 0; i < _count; i++)
        {
            T item = source[i];
            _items[i] = item;
            item.HeapIndex = i;
        }

        for (int i = (_count >> 1) - 1; i >= 0; i--)
        {
            if (IsMinLevel(i))
                PushDownMin(i);
            else
                PushDownMax(i);
        }
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

    /// <summary>
    /// Diagnostic helper that verifies both the Min-Max Heap ordering invariants and
    /// <see cref="IHeapItem.HeapIndex"/> consistency across all elements.
    /// </summary>
    public bool VerifyInvariant()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_items[i].HeapIndex != i)
                return false;

            bool isMin = IsMinLevel(i);
            int left = (i << 1) + 1;
            int right = left + 1;

            if (left < _count && (isMin ? _comparer.Compare(_items[i], _items[left]) > 0 : _comparer.Compare(_items[i], _items[left]) < 0))
                return false;
            if (right < _count && (isMin ? _comparer.Compare(_items[i], _items[right]) > 0 : _comparer.Compare(_items[i], _items[right]) < 0))
                return false;

            int firstGc = (i << 2) + 3;
            int lastGc = Math.Min(firstGc + 4, _count);
            for (int gc = firstGc; gc < lastGc; gc++)
            {
                if (isMin ? _comparer.Compare(_items[i], _items[gc]) > 0 : _comparer.Compare(_items[i], _items[gc]) < 0)
                    return false;
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetMaxIndex()
    {
        if (_count == 1) return 0;
        if (_count == 2) return 1;
        return Compare(_items[1], _items[2]) >= 0 ? 1 : 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsMinLevel(int index)
    {
        return (BitOperations.Log2((uint)(index + 1)) & 1) == 0;
    }

    private void PushUp(int index)
    {
        if (index == 0) return;

        int parent = (index - 1) >> 1;
        if (IsMinLevel(index))
        {
            if (Compare(_items[index], _items[parent]) > 0)
            {
                Swap(index, parent);
                PushUpMax(parent);
            }
            else
            {
                PushUpMin(index);
            }
        }
        else
        {
            if (Compare(_items[index], _items[parent]) < 0)
            {
                Swap(index, parent);
                PushUpMin(parent);
            }
            else
            {
                PushUpMax(index);
            }
        }
    }

    private void PushUpMin(int index)
    {
        while (index >= 3)
        {
            int grandparent = (((index - 1) >> 1) - 1) >> 1;
            if (Compare(_items[index], _items[grandparent]) < 0)
            {
                Swap(index, grandparent);
                index = grandparent;
            }
            else
            {
                break;
            }
        }
    }

    private void PushUpMax(int index)
    {
        while (index >= 3)
        {
            int grandparent = (((index - 1) >> 1) - 1) >> 1;
            if (Compare(_items[index], _items[grandparent]) > 0)
            {
                Swap(index, grandparent);
                index = grandparent;
            }
            else
            {
                break;
            }
        }
    }

    private void PushDownMin(int index)
    {
        while (true)
        {
            int left = (index << 1) + 1;
            if (left >= _count) break;

            int minIdx = left;
            int right = left + 1;
            if (right < _count && Compare(_items[right], _items[minIdx]) < 0)
                minIdx = right;

            int firstGc = (index << 2) + 3;
            int lastGc = Math.Min(firstGc + 4, _count);
            for (int gc = firstGc; gc < lastGc; gc++)
            {
                if (Compare(_items[gc], _items[minIdx]) < 0)
                    minIdx = gc;
            }

            if (minIdx >= firstGc)
            {
                if (Compare(_items[minIdx], _items[index]) < 0)
                {
                    Swap(minIdx, index);
                    int parentOfMin = (minIdx - 1) >> 1;
                    if (Compare(_items[minIdx], _items[parentOfMin]) > 0)
                        Swap(minIdx, parentOfMin);
                    index = minIdx;
                }
                else
                {
                    break;
                }
            }
            else
            {
                if (Compare(_items[minIdx], _items[index]) < 0)
                    Swap(minIdx, index);
                break;
            }
        }
    }

    private void PushDownMax(int index)
    {
        while (true)
        {
            int left = (index << 1) + 1;
            if (left >= _count) break;

            int maxIdx = left;
            int right = left + 1;
            if (right < _count && Compare(_items[right], _items[maxIdx]) > 0)
                maxIdx = right;

            int firstGc = (index << 2) + 3;
            int lastGc = Math.Min(firstGc + 4, _count);
            for (int gc = firstGc; gc < lastGc; gc++)
            {
                if (Compare(_items[gc], _items[maxIdx]) > 0)
                    maxIdx = gc;
            }

            if (maxIdx >= firstGc)
            {
                if (Compare(_items[maxIdx], _items[index]) > 0)
                {
                    Swap(maxIdx, index);
                    int parentOfMax = (maxIdx - 1) >> 1;
                    if (Compare(_items[maxIdx], _items[parentOfMax]) < 0)
                        Swap(maxIdx, parentOfMax);
                    index = maxIdx;
                }
                else
                {
                    break;
                }
            }
            else
            {
                if (Compare(_items[maxIdx], _items[index]) > 0)
                    Swap(maxIdx, index);
                break;
            }
        }
    }

    /// <summary>
    /// Restores the Min-Max Heap invariants after the value at <paramref name="index"/> is arbitrarily modified or replaced.
    /// </summary>
    private void RestoreAt(int index)
    {
        if (IsMinLevel(index))
        {
            if (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (Compare(_items[index], _items[parent]) > 0)
                {
                    Swap(index, parent);
                    PushUpMax(parent);
                    PushDownMin(index);
                    return;
                }
            }

            if (index >= 3)
            {
                int gp = (((index - 1) >> 1) - 1) >> 1;
                if (Compare(_items[index], _items[gp]) < 0)
                {
                    PushUpMin(index);
                    return;
                }
            }

            PushDownMin(index);
        }
        else
        {
            int parent = (index - 1) >> 1;
            if (Compare(_items[index], _items[parent]) < 0)
            {
                Swap(index, parent);
                PushUpMin(parent);
                PushDownMax(index);
                return;
            }

            if (index >= 3)
            {
                int gp = (((index - 1) >> 1) - 1) >> 1;
                if (Compare(_items[index], _items[gp]) > 0)
                {
                    PushUpMax(index);
                    return;
                }
            }

            PushDownMax(index);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Swap(int i, int j)
    {
        T tmp = _items[i];
        _items[i] = _items[j];
        _items[j] = tmp;
        _items[i].HeapIndex = i;
        _items[j].HeapIndex = j;
        _swaps++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Compare(T a, T b)
    {
        _comparisons++;
        return _comparer.Compare(a, b);
    }
}
