"""
Zero-dependency Python 3 implementations of:
  - BinaryMinHeap (indexed baseline)
  - DaryMinHeap (indexed d-ary cache-friendly baseline)
  - MinMaxHeap (indexed double-ended heap: O(1) min/max, O(log k) ops, O(k) build)
  - PartialMinHeap (bounded capacity k, O(log k) eviction, optional discard threshold)
  - CachedMinHeap (two-tier exact priority queue: MinMaxHeap cache + overflow + O(n) Quickselect)

All classes support O(1) membership checks and O(log n) priority updates (decrease-key & increase-key).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Callable, Generic, List, Optional, Sequence, TypeVar

T = TypeVar("T")


@dataclass
class HeapMetrics:
    enqueues: int = 0
    dequeues: int = 0
    updates: int = 0
    contains_checks: int = 0
    swaps: int = 0
    comparisons: int = 0
    overflow_enqueues: int = 0
    cache_evictions: int = 0
    replenishes: int = 0
    discards: int = 0

    @property
    def total_internal_ops(self) -> int:
        return self.comparisons + self.swaps


def _default_key(item):
    return item


class BinaryMinHeap(Generic[T]):
    """Indexed binary min-heap supporting O(1) contains and O(log n) update_item."""

    def __init__(self, key: Callable[[T], object] = _default_key) -> None:
        self._key = key
        self._items: List[T] = []
        self._pos: dict[int, int] = {}
        self.metrics = HeapMetrics()

    def __len__(self) -> int:
        return len(self._items)

    def __contains__(self, item: T) -> bool:
        self.metrics.contains_checks += 1
        return id(item) in self._pos

    def add(self, item: T) -> None:
        self.metrics.enqueues += 1
        idx = len(self._items)
        self._items.append(item)
        self._pos[id(item)] = idx
        self._sift_up(idx)

    def peek_min(self) -> T:
        if not self._items:
            raise IndexError("Heap is empty")
        return self._items[0]

    def remove_min(self) -> T:
        if not self._items:
            raise IndexError("Heap is empty")
        self.metrics.dequeues += 1
        top = self._items[0]
        del self._pos[id(top)]
        last = self._items.pop()
        if self._items:
            self._items[0] = last
            self._pos[id(last)] = 0
            self._sift_down(0)
        return top

    def update_item(self, item: T) -> None:
        idx = self._pos.get(id(item))
        if idx is None:
            raise KeyError("Item is not in this heap")
        self.metrics.updates += 1
        if idx > 0 and self._lt(self._items[idx], self._items[(idx - 1) >> 1]):
            self._sift_up(idx)
        else:
            self._sift_down(idx)

    def clear(self) -> None:
        self._items.clear()
        self._pos.clear()

    def _lt(self, a: T, b: T) -> bool:
        self.metrics.comparisons += 1
        return self._key(a) < self._key(b)

    def _sift_up(self, idx: int) -> None:
        item = self._items[idx]
        while idx > 0:
            parent_idx = (idx - 1) >> 1
            parent = self._items[parent_idx]
            if not self._lt(item, parent):
                break
            self._items[idx] = parent
            self._pos[id(parent)] = idx
            self.metrics.swaps += 1
            idx = parent_idx
        self._items[idx] = item
        self._pos[id(item)] = idx

    def _sift_down(self, idx: int) -> None:
        n = len(self._items)
        item = self._items[idx]
        half = n >> 1
        while idx < half:
            child = (idx << 1) + 1
            right = child + 1
            if right < n and self._lt(self._items[right], self._items[child]):
                child = right
            if not self._lt(self._items[child], item):
                break
            c_item = self._items[child]
            self._items[idx] = c_item
            self._pos[id(c_item)] = idx
            self.metrics.swaps += 1
            idx = child
        self._items[idx] = item
        self._pos[id(item)] = idx


class DaryMinHeap(Generic[T]):
    """Indexed d-ary min-heap (default 4-ary)."""

    def __init__(self, arity: int = 4, key: Callable[[T], object] = _default_key) -> None:
        if arity < 2:
            raise ValueError("Arity must be >= 2")
        self.arity = arity
        self._key = key
        self._items: List[T] = []
        self._pos: dict[int, int] = {}
        self.metrics = HeapMetrics()

    def __len__(self) -> int:
        return len(self._items)

    def __contains__(self, item: T) -> bool:
        self.metrics.contains_checks += 1
        return id(item) in self._pos

    def add(self, item: T) -> None:
        self.metrics.enqueues += 1
        idx = len(self._items)
        self._items.append(item)
        self._pos[id(item)] = idx
        self._sift_up(idx)

    def peek_min(self) -> T:
        if not self._items:
            raise IndexError("Heap is empty")
        return self._items[0]

    def remove_min(self) -> T:
        if not self._items:
            raise IndexError("Heap is empty")
        self.metrics.dequeues += 1
        top = self._items[0]
        del self._pos[id(top)]
        last = self._items.pop()
        if self._items:
            self._items[0] = last
            self._pos[id(last)] = 0
            self._sift_down(0)
        return top

    def update_item(self, item: T) -> None:
        idx = self._pos.get(id(item))
        if idx is None:
            raise KeyError("Item is not in this heap")
        self.metrics.updates += 1
        if idx > 0 and self._lt(self._items[idx], self._items[(idx - 1) // self.arity]):
            self._sift_up(idx)
        else:
            self._sift_down(idx)

    def clear(self) -> None:
        self._items.clear()
        self._pos.clear()

    def _lt(self, a: T, b: T) -> bool:
        self.metrics.comparisons += 1
        return self._key(a) < self._key(b)

    def _sift_up(self, idx: int) -> None:
        d = self.arity
        item = self._items[idx]
        while idx > 0:
            p_idx = (idx - 1) // d
            parent = self._items[p_idx]
            if not self._lt(item, parent):
                break
            self._items[idx] = parent
            self._pos[id(parent)] = idx
            self.metrics.swaps += 1
            idx = p_idx
        self._items[idx] = item
        self._pos[id(item)] = idx

    def _sift_down(self, idx: int) -> None:
        d = self.arity
        n = len(self._items)
        item = self._items[idx]
        while True:
            first = idx * d + 1
            if first >= n:
                break
            last = min(first + d, n)
            smallest = first
            for c in range(first + 1, last):
                if self._lt(self._items[c], self._items[smallest]):
                    smallest = c
            if not self._lt(self._items[smallest], item):
                break
            c_item = self._items[smallest]
            self._items[idx] = c_item
            self._pos[id(c_item)] = idx
            self.metrics.swaps += 1
            idx = smallest
        self._items[idx] = item
        self._pos[id(item)] = idx


class MinMaxHeap(Generic[T]):
    """
    Indexed Min-Max Heap (Atkinson et al., 1986).
    Provides O(1) peek_min / peek_max, O(log n) add / remove_min / remove_max / replace_max / update_item,
    and O(n) bottom-up build_from.
    """

    def __init__(self, key: Callable[[T], object] = _default_key) -> None:
        self._key = key
        self._items: List[T] = []
        self._pos: dict[int, int] = {}
        self.metrics = HeapMetrics()

    def __len__(self) -> int:
        return len(self._items)

    def __contains__(self, item: T) -> bool:
        self.metrics.contains_checks += 1
        return id(item) in self._pos

    def add(self, item: T) -> None:
        self.metrics.enqueues += 1
        idx = len(self._items)
        self._items.append(item)
        self._pos[id(item)] = idx
        self._push_up(idx)

    def peek_min(self) -> T:
        if not self._items:
            raise IndexError("Heap is empty")
        return self._items[0]

    def peek_max(self) -> T:
        if not self._items:
            raise IndexError("Heap is empty")
        return self._items[self._max_index()]

    def remove_min(self) -> T:
        if not self._items:
            raise IndexError("Heap is empty")
        self.metrics.dequeues += 1
        top = self._items[0]
        del self._pos[id(top)]
        last = self._items.pop()
        if self._items:
            self._items[0] = last
            self._pos[id(last)] = 0
            self._push_down_min(0)
        return top

    def remove_max(self) -> T:
        if not self._items:
            raise IndexError("Heap is empty")
        self.metrics.dequeues += 1
        max_idx = self._max_index()
        return self.remove_at(max_idx)

    def replace_max(self, replacement: T) -> T:
        if not self._items:
            raise IndexError("Heap is empty")
        max_idx = self._max_index()
        evicted = self._items[max_idx]
        del self._pos[id(evicted)]
        self._items[max_idx] = replacement
        self._pos[id(replacement)] = max_idx
        self._restore_at(max_idx)
        return evicted

    def remove_at(self, idx: int) -> T:
        removed = self._items[idx]
        del self._pos[id(removed)]
        last = self._items.pop()
        if idx < len(self._items):
            self._items[idx] = last
            self._pos[id(last)] = idx
            self._restore_at(idx)
        return removed

    def update_item(self, item: T) -> None:
        idx = self._pos.get(id(item))
        if idx is None:
            raise KeyError("Item is not in this heap")
        self.metrics.updates += 1
        self._restore_at(idx)

    def build_from(self, items: Sequence[T]) -> None:
        self._items = list(items)
        self._pos = {id(x): i for i, x in enumerate(self._items)}
        for i in range((len(self._items) >> 1) - 1, -1, -1):
            if self._is_min_level(i):
                self._push_down_min(i)
            else:
                self._push_down_max(i)

    def clear(self) -> None:
        self._items.clear()
        self._pos.clear()

    def verify_invariant(self) -> bool:
        n = len(self._items)
        if len(self._pos) != n:
            return False
        for i, item in enumerate(self._items):
            if self._pos.get(id(item)) != i:
                return False
            is_min = self._is_min_level(i)
            ki = self._key(item)
            for c in ((i << 1) + 1, (i << 1) + 2):
                if c < n:
                    kc = self._key(self._items[c])
                    if (is_min and ki > kc) or (not is_min and ki < kc):
                        return False
            first_gc = (i << 2) + 3
            for gc in range(first_gc, min(first_gc + 4, n)):
                kgc = self._key(self._items[gc])
                if (is_min and ki > kgc) or (not is_min and ki < kgc):
                    return False
        return True

    @staticmethod
    def _is_min_level(idx: int) -> bool:
        # floor(log2(idx + 1)) is (idx + 1).bit_length() - 1
        return (((idx + 1).bit_length() - 1) & 1) == 0

    def _max_index(self) -> int:
        n = len(self._items)
        if n == 1:
            return 0
        if n == 2:
            return 1
        return 1 if not self._lt(self._items[1], self._items[2]) else 2

    def _lt(self, a: T, b: T) -> bool:
        self.metrics.comparisons += 1
        return self._key(a) < self._key(b)

    def _gt(self, a: T, b: T) -> bool:
        self.metrics.comparisons += 1
        return self._key(a) > self._key(b)

    def _swap(self, i: int, j: int) -> None:
        a, b = self._items[i], self._items[j]
        self._items[i], self._items[j] = b, a
        self._pos[id(b)] = i
        self._pos[id(a)] = j
        self.metrics.swaps += 1

    def _push_up(self, idx: int) -> None:
        if idx == 0:
            return
        parent = (idx - 1) >> 1
        if self._is_min_level(idx):
            if self._gt(self._items[idx], self._items[parent]):
                self._swap(idx, parent)
                self._push_up_max(parent)
            else:
                self._push_up_min(idx)
        else:
            if self._lt(self._items[idx], self._items[parent]):
                self._swap(idx, parent)
                self._push_up_min(parent)
            else:
                self._push_up_max(idx)

    def _push_up_min(self, idx: int) -> None:
        while idx >= 3:
            gp = (((idx - 1) >> 1) - 1) >> 1
            if self._lt(self._items[idx], self._items[gp]):
                self._swap(idx, gp)
                idx = gp
            else:
                break

    def _push_up_max(self, idx: int) -> None:
        while idx >= 3:
            gp = (((idx - 1) >> 1) - 1) >> 1
            if self._gt(self._items[idx], self._items[gp]):
                self._swap(idx, gp)
                idx = gp
            else:
                break

    def _push_down_min(self, idx: int) -> None:
        n = len(self._items)
        while True:
            left = (idx << 1) + 1
            if left >= n:
                break
            min_idx = left
            right = left + 1
            if right < n and self._lt(self._items[right], self._items[min_idx]):
                min_idx = right
            first_gc = (idx << 2) + 3
            for gc in range(first_gc, min(first_gc + 4, n)):
                if self._lt(self._items[gc], self._items[min_idx]):
                    min_idx = gc

            if min_idx >= first_gc:
                if self._lt(self._items[min_idx], self._items[idx]):
                    self._swap(min_idx, idx)
                    parent_of_min = (min_idx - 1) >> 1
                    if self._gt(self._items[min_idx], self._items[parent_of_min]):
                        self._swap(min_idx, parent_of_min)
                    idx = min_idx
                else:
                    break
            else:
                if self._lt(self._items[min_idx], self._items[idx]):
                    self._swap(min_idx, idx)
                break

    def _push_down_max(self, idx: int) -> None:
        n = len(self._items)
        while True:
            left = (idx << 1) + 1
            if left >= n:
                break
            max_idx = left
            right = left + 1
            if right < n and self._gt(self._items[right], self._items[max_idx]):
                max_idx = right
            first_gc = (idx << 2) + 3
            for gc in range(first_gc, min(first_gc + 4, n)):
                if self._gt(self._items[gc], self._items[max_idx]):
                    max_idx = gc

            if max_idx >= first_gc:
                if self._gt(self._items[max_idx], self._items[idx]):
                    self._swap(max_idx, idx)
                    parent_of_max = (max_idx - 1) >> 1
                    if self._lt(self._items[max_idx], self._items[parent_of_max]):
                        self._swap(max_idx, parent_of_max)
                    idx = max_idx
                else:
                    break
            else:
                if self._gt(self._items[max_idx], self._items[idx]):
                    self._swap(max_idx, idx)
                break

    def _restore_at(self, idx: int) -> None:
        if self._is_min_level(idx):
            if idx > 0:
                parent = (idx - 1) >> 1
                if self._gt(self._items[idx], self._items[parent]):
                    self._swap(idx, parent)
                    self._push_up_max(parent)
                    self._push_down_min(idx)
                    return
            if idx >= 3:
                gp = (((idx - 1) >> 1) - 1) >> 1
                if self._lt(self._items[idx], self._items[gp]):
                    self._push_up_min(idx)
                    return
            self._push_down_min(idx)
        else:
            parent = (idx - 1) >> 1
            if self._lt(self._items[idx], self._items[parent]):
                self._swap(idx, parent)
                self._push_up_min(parent)
                self._push_down_max(idx)
                return
            if idx >= 3:
                gp = (((idx - 1) >> 1) - 1) >> 1
                if self._gt(self._items[idx], self._items[gp]):
                    self._push_up_max(idx)
                    return
            self._push_down_max(idx)


class PartialMinHeap(Generic[T]):
    """Bounded-capacity Partial Min-Heap backed by an indexed MinMaxHeap."""

    def __init__(
        self,
        capacity: int,
        key: Callable[[T], object] = _default_key,
        strict_discard_threshold: bool = False,
    ) -> None:
        if capacity < 1:
            raise ValueError("Capacity must be >= 1")
        self.capacity = capacity
        self._key = key
        self._strict = strict_discard_threshold
        self._discard_threshold: Optional[object] = None
        self._heap: MinMaxHeap[T] = MinMaxHeap(key=key)
        self.metrics = HeapMetrics()

    @classmethod
    def from_depth(
        cls,
        depth: int,
        key: Callable[[T], object] = _default_key,
        strict_discard_threshold: bool = False,
    ) -> "PartialMinHeap[T]":
        return cls((1 << depth) - 1, key=key, strict_discard_threshold=strict_discard_threshold)

    def __len__(self) -> int:
        return len(self._heap)

    def __contains__(self, item: T) -> bool:
        self.metrics.contains_checks += 1
        return item in self._heap

    def add(self, item: T) -> None:
        k = self._key(item)
        if len(self._heap) < self.capacity:
            if self._strict and self._discard_threshold is not None:
                self.metrics.comparisons += 1
                if k >= self._discard_threshold:
                    self.metrics.discards += 1
                    return
            self.metrics.enqueues += 1
            self._heap.add(item)
            return

        max_item = self._heap.peek_max()
        max_k = self._key(max_item)
        self.metrics.comparisons += 1
        if k >= max_k:
            if self._strict and (self._discard_threshold is None or k < self._discard_threshold):
                self._discard_threshold = k
            self.metrics.discards += 1
            return

        if self._strict and (self._discard_threshold is None or max_k < self._discard_threshold):
            self._discard_threshold = max_k

        self._heap.replace_max(item)
        self.metrics.enqueues += 1
        self.metrics.cache_evictions += 1
        self.metrics.discards += 1

    def peek_min(self) -> T:
        return self._heap.peek_min()

    def peek_max(self) -> T:
        return self._heap.peek_max()

    def remove_min(self) -> T:
        self.metrics.dequeues += 1
        return self._heap.remove_min()

    def update_item(self, item: T) -> None:
        self.metrics.updates += 1
        self._heap.update_item(item)

    def clear(self) -> None:
        self._heap.clear()
        self._discard_threshold = None

    def get_metrics(self) -> HeapMetrics:
        m = self._heap.metrics
        return HeapMetrics(
            enqueues=self.metrics.enqueues,
            dequeues=self.metrics.dequeues,
            updates=self.metrics.updates,
            contains_checks=self.metrics.contains_checks,
            swaps=m.swaps,
            comparisons=m.comparisons + self.metrics.comparisons,
            cache_evictions=self.metrics.cache_evictions,
            discards=self.metrics.discards,
        )

    def verify_invariant(self) -> bool:
        return len(self._heap) <= self.capacity and self._heap.verify_invariant()


class CachedMinHeap(Generic[T]):
    """
    Two-tier exact Cached Min-Heap:
      - Tier 1 (Cache): fixed-size MinMaxHeap of capacity `cache_capacity`
      - Tier 2 (Overflow): unsorted buffer replenished via O(n) iterative Quickselect + O(k) MinMaxHeap build
    """

    def __init__(self, cache_capacity: int, key: Callable[[T], object] = _default_key) -> None:
        if cache_capacity < 1:
            raise ValueError("cache_capacity must be >= 1")
        self.cache_capacity = cache_capacity
        self._key = key
        self._cache: MinMaxHeap[T] = MinMaxHeap(key=key)
        self._extra: List[T] = []
        self._extra_pos: dict[int, int] = {}
        self._extra_min: Optional[T] = None
        self.metrics = HeapMetrics()

    @classmethod
    def from_depth(cls, cache_depth: int, key: Callable[[T], object] = _default_key) -> "CachedMinHeap[T]":
        return cls((1 << cache_depth) - 1, key=key)

    def __len__(self) -> int:
        return len(self._cache) + len(self._extra)

    def __contains__(self, item: T) -> bool:
        self.metrics.contains_checks += 1
        return (item in self._cache) or (id(item) in self._extra_pos)

    def add(self, item: T) -> None:
        self.metrics.enqueues += 1
        if len(self._cache) < self.cache_capacity:
            if self._extra and not self._lt(item, self._extra_min):  # type: ignore[arg-type]
                self._append_extra(item)
            else:
                self._cache.add(item)
            return

        cache_max = self._cache.peek_max()
        if not self._lt(item, cache_max):
            self._append_extra(item)
            return

        evicted = self._cache.replace_max(item)
        self.metrics.cache_evictions += 1
        self._append_extra(evicted)

    def peek_min(self) -> T:
        if not self:
            raise IndexError("Heap is empty")
        if not self._cache:
            self._replenish_cache()
        return self._cache.peek_min()

    def remove_min(self) -> T:
        if not self:
            raise IndexError("Heap is empty")
        if not self._cache:
            self._replenish_cache()
        self.metrics.dequeues += 1
        return self._cache.remove_min()

    def update_item(self, item: T) -> None:
        self.metrics.updates += 1
        if item in self._cache:
            self._cache.update_item(item)
            if self._extra and self._gt(item, self._extra_min):  # type: ignore[arg-type]
                idx = self._cache._pos[id(item)]
                self._cache.remove_at(idx)
                self._append_extra(item)
            return

        extra_idx = self._extra_pos.get(id(item))
        if extra_idx is None:
            raise KeyError("Item is not in this heap")

        if not self._cache:
            self._refresh_extra_min(item)
            return

        if len(self._cache) < self.cache_capacity:
            if item is self._extra_min:
                if self._lt(item, self._cache.peek_max()):
                    self._remove_extra_at(extra_idx)
                    self._cache.add(item)
                self._recompute_extra_min()
            elif self._lt(item, self._extra_min):  # type: ignore[arg-type]
                self._remove_extra_at(extra_idx)
                self._cache.add(item)
            return

        cache_max = self._cache.peek_max()
        if self._lt(item, cache_max):
            was_min = item is self._extra_min
            evicted = self._cache.replace_max(item)
            self.metrics.cache_evictions += 1
            del self._extra_pos[id(item)]
            self._extra[extra_idx] = evicted
            self._extra_pos[id(evicted)] = extra_idx
            if was_min or self._lt(evicted, self._extra_min):  # type: ignore[arg-type]
                self._extra_min = evicted
        else:
            self._refresh_extra_min(item)

    def clear(self) -> None:
        self._cache.clear()
        self._extra.clear()
        self._extra_pos.clear()
        self._extra_min = None

    def get_metrics(self) -> HeapMetrics:
        m = self._cache.metrics
        return HeapMetrics(
            enqueues=self.metrics.enqueues,
            dequeues=self.metrics.dequeues,
            updates=self.metrics.updates,
            contains_checks=self.metrics.contains_checks,
            swaps=m.swaps + self.metrics.swaps,
            comparisons=m.comparisons + self.metrics.comparisons,
            overflow_enqueues=self.metrics.overflow_enqueues,
            cache_evictions=self.metrics.cache_evictions,
            replenishes=self.metrics.replenishes,
            discards=0,
        )

    def verify_invariant(self) -> bool:
        if not self._cache.verify_invariant():
            return False
        if not self._extra:
            return self._extra_min is None and not self._extra_pos
        if self._extra_min is None or len(self._extra_pos) != len(self._extra):
            return False
        cache_max_k = self._key(self._cache.peek_max()) if self._cache else None
        extra_min_k = self._key(self._extra_min)
        found_min = False
        for i, item in enumerate(self._extra):
            if self._extra_pos.get(id(item)) != i:
                return False
            if item is self._extra_min:
                found_min = True
            ki = self._key(item)
            if ki < extra_min_k:
                return False
            if cache_max_k is not None and cache_max_k > ki:
                return False
        return found_min

    def _replenish_cache(self) -> None:
        self.metrics.replenishes += 1
        n = len(self._extra)
        move_count = min(n, self.cache_capacity)

        if n <= self.cache_capacity:
            self._cache.build_from(self._extra)
            self._extra.clear()
            self._extra_pos.clear()
            self._extra_min = None
            return

        self._quickselect(self._extra, 0, n - 1, move_count - 1)
        self._cache.build_from(self._extra[:move_count])

        remaining = self._extra[move_count:]
        self._extra = remaining
        self._extra_pos = {}
        min_cand = remaining[0]
        for i, item in enumerate(remaining):
            self._extra_pos[id(item)] = i
            if i > 0 and self._lt(item, min_cand):
                min_cand = item
        self._extra_min = min_cand

    def _quickselect(self, arr: List[T], left: int, right: int, k: int) -> None:
        while left < right:
            mid = (left + right) >> 1
            if self._lt(arr[mid], arr[left]):
                arr[left], arr[mid] = arr[mid], arr[left]
                self.metrics.swaps += 1
            if self._lt(arr[right], arr[left]):
                arr[left], arr[right] = arr[right], arr[left]
                self.metrics.swaps += 1
            if self._lt(arr[right], arr[mid]):
                arr[mid], arr[right] = arr[right], arr[mid]
                self.metrics.swaps += 1

            pivot = arr[mid]
            i, j = left, right
            while True:
                while self._lt(arr[i], pivot):
                    i += 1
                while self._gt(arr[j], pivot):
                    j -= 1
                if i >= j:
                    break
                arr[i], arr[j] = arr[j], arr[i]
                self.metrics.swaps += 1
                i += 1
                j -= 1

            if k <= j:
                right = j
            else:
                left = j + 1

    def _append_extra(self, item: T) -> None:
        idx = len(self._extra)
        self._extra.append(item)
        self._extra_pos[id(item)] = idx
        self.metrics.overflow_enqueues += 1
        if idx == 0 or self._lt(item, self._extra_min):  # type: ignore[arg-type]
            self._extra_min = item

    def _remove_extra_at(self, idx: int) -> None:
        removed = self._extra[idx]
        del self._extra_pos[id(removed)]
        last = self._extra.pop()
        if idx < len(self._extra):
            self._extra[idx] = last
            self._extra_pos[id(last)] = idx
        if not self._extra:
            self._extra_min = None

    def _refresh_extra_min(self, item: T) -> None:
        if item is self._extra_min:
            self._recompute_extra_min()
        elif self._lt(item, self._extra_min):  # type: ignore[arg-type]
            self._extra_min = item

    def _recompute_extra_min(self) -> None:
        if not self._extra:
            self._extra_min = None
            return
        best = self._extra[0]
        for item in self._extra[1:]:
            if self._lt(item, best):
                best = item
        self._extra_min = best

    def _lt(self, a: T, b: T) -> bool:
        self.metrics.comparisons += 1
        return self._key(a) < self._key(b)

    def _gt(self, a: T, b: T) -> bool:
        self.metrics.comparisons += 1
        return self._key(a) > self._key(b)
