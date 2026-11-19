namespace CachedPartialMinHeap;

/// <summary>
/// Represents an element stored in an indexed priority queue.
/// Maintaining <see cref="HeapIndex"/> enables O(1) membership checks and O(log n) priority updates.
/// </summary>
public interface IHeapItem
{
    /// <summary>
    /// Internal slot index managed by the priority queue.
    /// Set to <c>-1</c> when the item is not currently inside any heap.
    /// </summary>
    int HeapIndex { get; set; }
}

/// <summary>
/// Operation counters collected by a priority queue for empirical analysis.
/// </summary>
public readonly record struct HeapMetrics(
    long Enqueues,
    long Dequeues,
    long Updates,
    long ContainsChecks,
    long Swaps,
    long Comparisons,
    long OverflowEnqueues,
    long CacheEvictions,
    long Replenishes,
    long Discards)
{
    /// <summary>Total comparison + swap work performed inside the data structure.</summary>
    public long TotalInternalOps => Comparisons + Swaps;
}

/// <summary>
/// Common contract for indexed min-priority queues supporting O(1) <see cref="Contains"/>
/// and O(log n) <see cref="UpdateItem"/> (both decrease-key and increase-key).
/// </summary>
public interface IIndexedPriorityQueue<T> where T : class, IHeapItem
{
    /// <summary>Number of items currently stored in the queue.</summary>
    int Count { get; }

    /// <summary>Inserts an item into the priority queue.</summary>
    void Add(T item);

    /// <summary>Returns the minimum item without removing it.</summary>
    T PeekMin();

    /// <summary>Removes and returns the minimum item.</summary>
    T RemoveMin();

    /// <summary>Restores heap order after an already-enqueued item's priority changes.</summary>
    void UpdateItem(T item);

    /// <summary>Returns <c>true</c> if <paramref name="item"/> is currently stored in this queue.</summary>
    bool Contains(T item);

    /// <summary>Removes all items and resets their <see cref="IHeapItem.HeapIndex"/> to <c>-1</c>.</summary>
    void Clear();

    /// <summary>Returns current operation counters.</summary>
    HeapMetrics GetMetrics();

    /// <summary>Resets all operation counters to zero.</summary>
    void ResetMetrics();
}
