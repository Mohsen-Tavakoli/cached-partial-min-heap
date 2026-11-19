"""
Zero-dependency Python 3 A* pathfinding benchmark CLI.
Mirrors the .NET benchmark suite on random obstacle grids and slalom barrier mazes,
and supports loading standard Moving AI Lab .map files.
"""

from __future__ import annotations

import argparse
import csv
import heapq
import random
import statistics
import time
from dataclasses import dataclass
from typing import Callable, List, Optional, Tuple

from heaps import BinaryMinHeap, CachedMinHeap, DaryMinHeap, HeapMetrics, PartialMinHeap

DIRECTIONS = (
    (-1, 0, 10), (1, 0, 10), (0, -1, 10), (0, 1, 10),
    (-1, -1, 14), (-1, 1, 14), (1, -1, 14), (1, 1, 14),
)


class GridNode:
    __slots__ = ("x", "y", "walkable", "g", "h", "parent", "search_id", "closed")

    def __init__(self, x: int, y: int, walkable: bool) -> None:
        self.x = x
        self.y = y
        self.walkable = walkable
        self.g = 0
        self.h = 0
        self.parent: Optional[GridNode] = None
        self.search_id = 0
        self.closed = False

    def priority_key(self) -> Tuple[int, int, int, int]:
        return (self.g + self.h, self.h, self.x, self.y)


class GridMap:
    def __init__(self, width: int, height: int) -> None:
        self.width = width
        self.height = height
        self._nodes = [GridNode(x, y, True) for y in range(height) for x in range(width)]
        self._search_id = 0

    def node(self, x: int, y: int) -> GridNode:
        return self._nodes[y * self.width + x]

    def next_search_id(self) -> int:
        self._search_id += 1
        return self._search_id

    @classmethod
    def random_obstacles(cls, width: int, height: int, obstacle_pct: int, seed: int) -> "GridMap":
        m = cls(width, height)
        rng = random.Random(seed)
        for n in m._nodes:
            if rng.randrange(100) < obstacle_pct:
                n.walkable = False
        return m

    @classmethod
    def slalom_barriers(cls, width: int, height: int, barriers: int = 8, gap: int = 4) -> "GridMap":
        m = cls(width, height)
        spacing = max(4, width // (barriers + 1))
        for b in range(1, barriers + 1):
            bx = b * spacing
            if bx >= width - 1:
                break
            gap_top = (b & 1) == 0
            for y in range(height):
                in_gap = (y < gap) if gap_top else (y >= height - gap)
                if not in_gap:
                    m.node(bx, y).walkable = False
        return m

    def pick_walkable(self, rng: random.Random) -> Tuple[int, int]:
        for _ in range(10_000):
            x, y = rng.randrange(self.width), rng.randrange(self.height)
            if self.node(x, y).walkable:
                return (x, y)
        raise RuntimeError("No walkable cell found")


def octile(a: GridNode, b: GridNode) -> int:
    dx = abs(a.x - b.x)
    dy = abs(a.y - b.y)
    return 14 * dy + 10 * (dx - dy) if dx > dy else 14 * dx + 10 * (dy - dx)


@dataclass
class RunResult:
    found: bool
    cost: int
    expanded: int
    elapsed_us: float
    metrics: HeapMetrics


def find_path(grid: GridMap, start: Tuple[int, int], goal: Tuple[int, int], open_set) -> RunResult:
    sid = grid.next_search_id()
    s_node = grid.node(*start)
    g_node = grid.node(*goal)
    if not s_node.walkable or not g_node.walkable:
        return RunResult(False, 0, 0, 0.0, HeapMetrics())

    open_set.clear()
    t0 = time.perf_counter()

    s_node.search_id = sid
    s_node.closed = False
    s_node.g = 0
    s_node.h = octile(s_node, g_node)
    s_node.parent = None
    open_set.add(s_node)

    expanded = 0
    w, h = grid.width, grid.height

    while len(open_set) > 0:
        cur = open_set.remove_min()
        cur.closed = True
        expanded += 1
        if cur is g_node:
            dt = (time.perf_counter() - t0) * 1_000_000.0
            metrics = open_set.get_metrics() if hasattr(open_set, "get_metrics") else open_set.metrics
            return RunResult(True, g_node.g, expanded, dt, metrics)

        cx, cy = cur.x, cur.y
        for dx, dy, step_cost in DIRECTIONS:
            nx, ny = cx + dx, cy + dy
            if not (0 <= nx < w and 0 <= ny < h):
                continue
            nb = grid.node(nx, ny)
            if not nb.walkable:
                continue
            visited = nb.search_id == sid
            if visited and nb.closed:
                continue
            tentative_g = cur.g + step_cost
            in_open = visited and (nb in open_set)
            if not in_open:
                if not visited or tentative_g < nb.g:
                    nb.search_id = sid
                    nb.closed = False
                    nb.g = tentative_g
                    nb.h = octile(nb, g_node)
                    nb.parent = cur
                    open_set.add(nb)
            elif tentative_g < nb.g:
                nb.g = tentative_g
                nb.parent = cur
                open_set.update_item(nb)

    dt = (time.perf_counter() - t0) * 1_000_000.0
    metrics = open_set.get_metrics() if hasattr(open_set, "get_metrics") else open_set.metrics
    return RunResult(False, 0, expanded, dt, metrics)


def find_path_heapq(grid: GridMap, start: Tuple[int, int], goal: Tuple[int, int]) -> RunResult:
    """Baseline using Python stdlib heapq (C-accelerated binary heap with lazy duplicates)."""
    sid = grid.next_search_id()
    s_node = grid.node(*start)
    g_node = grid.node(*goal)
    if not s_node.walkable or not g_node.walkable:
        return RunResult(False, 0, 0, 0.0, HeapMetrics())

    t0 = time.perf_counter()
    s_node.search_id = sid
    s_node.closed = False
    s_node.g = 0
    s_node.h = octile(s_node, g_node)
    s_node.parent = None

    pq: List[Tuple[int, int, int, int, GridNode]] = [(s_node.g + s_node.h, s_node.h, s_node.x, s_node.y, s_node)]
    expanded = 0
    w, h = grid.width, grid.height

    while pq:
        _, _, _, _, cur = heapq.heappop(pq)
        if cur.closed:
            continue
        cur.closed = True
        expanded += 1
        if cur is g_node:
            dt = (time.perf_counter() - t0) * 1_000_000.0
            return RunResult(True, g_node.g, expanded, dt, HeapMetrics())

        cx, cy = cur.x, cur.y
        for dx, dy, step_cost in DIRECTIONS:
            nx, ny = cx + dx, cy + dy
            if not (0 <= nx < w and 0 <= ny < h):
                continue
            nb = grid.node(nx, ny)
            if not nb.walkable:
                continue
            visited = nb.search_id == sid
            if visited and nb.closed:
                continue
            tentative_g = cur.g + step_cost
            if not visited or tentative_g < nb.g:
                nb.search_id = sid
                nb.closed = False
                nb.g = tentative_g
                nb.h = octile(nb, g_node)
                nb.parent = cur
                heapq.heappush(pq, (nb.g + nb.h, nb.h, nb.x, nb.y, nb))

    dt = (time.perf_counter() - t0) * 1_000_000.0
    return RunResult(False, 0, expanded, dt, HeapMetrics())


def main() -> None:
    parser = argparse.ArgumentParser(description="Python 3 A* priority queue benchmark")
    parser.add_argument("--grid", type=int, default=80, help="Grid width/height (default: 80)")
    parser.add_argument("--trials", type=int, default=15, help="Trials per density (default: 15)")
    parser.add_argument("--cache", type=int, default=63, help="CachedMinHeap cache capacity (default: 63)")
    parser.add_argument("--partial", type=int, default=63, help="PartialMinHeap capacity (default: 63)")
    parser.add_argument("--csv", type=str, default=None, help="Optional CSV output path")
    args = parser.parse_args()

    rows = []
    for density in (10, 20, 30):
        grid = GridMap.random_obstacles(args.grid, args.grid, density, seed=4200 + density)
        rng = random.Random(9900 + density)
        pairs = []
        while len(pairs) < args.trials:
            s, g = grid.pick_walkable(rng), grid.pick_walkable(rng)
            if abs(s[0] - g[0]) + abs(s[1] - g[1]) >= args.grid // 2:
                pairs.append((s, g))

        scenario = f"Random {args.grid}x{args.grid} ({density}% obs)"
        runners: list[tuple[str, Callable[[GridMap, Tuple[int, int], Tuple[int, int]], RunResult]]] = [
            ("BinaryMinHeap", lambda m, s, g: find_path(m, s, g, BinaryMinHeap(key=GridNode.priority_key))),
            ("DaryMinHeap (d=4)", lambda m, s, g: find_path(m, s, g, DaryMinHeap(4, key=GridNode.priority_key))),
            ("Python heapq", find_path_heapq),
            (f"CachedMinHeap (k={args.cache})", lambda m, s, g: find_path(m, s, g, CachedMinHeap(args.cache, key=GridNode.priority_key))),
            (f"PartialMinHeap (k={args.partial})", lambda m, s, g: find_path(m, s, g, PartialMinHeap(args.partial, key=GridNode.priority_key))),
        ]

        for name, fn in runners:
            runs = [fn(grid, s, g) for s, g in pairs]
            ok = [r for r in runs if r.found]
            rows.append({
                "Scenario": scenario,
                "Queue": name,
                "Trials": len(runs),
                "Successes": len(ok),
                "MeanTimeUs": round(statistics.mean(r.elapsed_us for r in runs), 2),
                "MedianTimeUs": round(statistics.median(r.elapsed_us for r in runs), 2),
                "MeanExpanded": round(statistics.mean(r.expanded for r in runs), 2),
                "MeanInternalOps": round(statistics.mean(r.metrics.total_internal_ops for r in runs), 2),
                "MeanPathCost": round(statistics.mean(r.cost for r in ok), 2) if ok else 0.0,
                "MeanReplenishes": round(statistics.mean(r.metrics.replenishes for r in runs), 2),
                "MeanDiscards": round(statistics.mean(r.metrics.discards for r in runs), 2),
            })

    print("| Scenario | Queue | Success | Median (us) | Expanded | Internal Ops | Path Cost |")
    print("| :--- | :--- | ---: | ---: | ---: | ---: | ---: |")
    for r in rows:
        print(
            f"| {r['Scenario']} | {r['Queue']} | {r['Successes']}/{r['Trials']} | "
            f"{r['MedianTimeUs']:.1f} | {r['MeanExpanded']:.1f} | {r['MeanInternalOps']:.0f} | {r['MeanPathCost']:.1f} |"
        )

    if args.csv:
        with open(args.csv, "w", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            writer.writeheader()
            writer.writerows(rows)
        print(f"\nWrote CSV to {args.csv}")


if __name__ == "__main__":
    main()
