"""Unit and randomized fuzz tests for Python 3 priority queue implementations."""

from __future__ import annotations

import random
import unittest
from dataclasses import dataclass

from heaps import BinaryMinHeap, CachedMinHeap, DaryMinHeap, MinMaxHeap, PartialMinHeap


@dataclass
class Item:
    id: int
    key: int

    def sort_key(self) -> tuple[int, int]:
        return (self.key, self.id)


class TestHeaps(unittest.TestCase):
    def test_minmax_heap_fuzz(self) -> None:
        rng = random.Random(1337)
        heap = MinMaxHeap[Item](key=Item.sort_key)
        ref: list[Item] = []
        next_id = 0

        for step in range(5000):
            op = rng.randrange(100)
            if not ref or op < 42:
                it = Item(next_id, rng.randint(-50, 50))
                next_id += 1
                heap.add(it)
                ref.append(it)
            elif op < 64:
                ref.sort(key=Item.sort_key)
                expected = ref.pop(0)
                actual = heap.remove_min()
                self.assertIs(actual, expected, f"step {step}")
            elif op < 80:
                ref.sort(key=Item.sort_key)
                expected = ref.pop()
                actual = heap.remove_max()
                self.assertIs(actual, expected, f"step {step}")
            elif op < 92:
                target = rng.choice(ref)
                target.key += rng.randint(-40, 40)
                heap.update_item(target)
            else:
                ref.sort(key=Item.sort_key)
                expected = ref.pop()
                fresh = Item(next_id, rng.randint(-50, 50))
                next_id += 1
                actual = heap.replace_max(fresh)
                ref.append(fresh)
                self.assertIs(actual, expected, f"step {step}")

            self.assertEqual(len(heap), len(ref))
            if step % 10 == 0:
                self.assertTrue(heap.verify_invariant(), f"invariant failed at step {step}")

    def test_baseline_heaps_fuzz(self) -> None:
        for heap in (BinaryMinHeap[Item](key=Item.sort_key), DaryMinHeap[Item](4, key=Item.sort_key)):
            rng = random.Random(42)
            ref: list[Item] = []
            next_id = 0
            for step in range(3000):
                op = rng.randrange(100)
                if not ref or op < 50:
                    it = Item(next_id, rng.randint(-100, 100))
                    next_id += 1
                    heap.add(it)
                    ref.append(it)
                elif op < 80:
                    ref.sort(key=Item.sort_key)
                    expected = ref.pop(0)
                    actual = heap.remove_min()
                    self.assertIs(actual, expected)
                    self.assertNotIn(actual, heap)
                else:
                    target = rng.choice(ref)
                    target.key += rng.randint(-50, 50)
                    heap.update_item(target)
                    self.assertIn(target, heap)

    def test_partial_min_heap(self) -> None:
        heap = PartialMinHeap[Item](capacity=5, key=Item.sort_key)
        for i, k in enumerate([30, 10, 50, 20, 40, 5, 60, 15, 25, 1]):
            heap.add(Item(i, k))
            self.assertTrue(heap.verify_invariant())
        self.assertEqual(len(heap), 5)
        drained = [heap.remove_min().key for _ in range(len(heap))]
        self.assertEqual(drained, [1, 5, 10, 15, 20])

    def test_cached_min_heap_fuzz(self) -> None:
        for cap in (1, 3, 15, 31):
            rng = random.Random(900 + cap)
            heap = CachedMinHeap[Item](cache_capacity=cap, key=Item.sort_key)
            ref: list[Item] = []
            next_id = 0

            for step in range(4000):
                op = rng.randrange(100)
                if not ref or op < 48:
                    it = Item(next_id, rng.randint(-60, 60))
                    next_id += 1
                    heap.add(it)
                    ref.append(it)
                elif op < 80:
                    ref.sort(key=Item.sort_key)
                    expected = ref.pop(0)
                    actual = heap.remove_min()
                    self.assertIs(actual, expected, f"cap={cap} step={step}")
                    self.assertNotIn(actual, heap)
                else:
                    target = rng.choice(ref)
                    delta = -rng.randint(1, 50) if rng.randrange(100) < 75 else rng.randint(1, 50)
                    target.key += delta
                    heap.update_item(target)
                    self.assertIn(target, heap)

                self.assertEqual(len(heap), len(ref))
                if step % 10 == 0:
                    self.assertTrue(heap.verify_invariant(), f"cap={cap} step={step}")


if __name__ == "__main__":
    unittest.main()
