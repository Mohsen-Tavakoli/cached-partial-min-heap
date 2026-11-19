"""
Zero-dependency SVG benchmark chart generator.
Reads the CSV emitted by either the .NET or Python benchmark CLI and generates clean,
GitHub-friendly grouped bar charts in SVG format (no matplotlib required).
"""

from __future__ import annotations

import argparse
import csv
from collections import defaultdict
from html import escape
from pathlib import Path
from typing import Dict, List

PALETTE = [
    "#3b82f6",  # BinaryMinHeap (blue)
    "#10b981",  # DaryMinHeap (emerald)
    "#8b5cf6",  # .NET PriorityQueue / heapq (violet)
    "#f59e0b",  # CachedMinHeap (amber)
    "#ef4444",  # PartialMinHeap (rose)
]


def render_grouped_bar_svg(
    title: str,
    y_label: str,
    scenarios: List[str],
    queues: List[str],
    values: Dict[str, Dict[str, float]],
    out_path: Path,
) -> None:
    width = 920
    height = 460
    pad_l, pad_r, pad_t, pad_b = 85, 30, 82, 95
    plot_w = width - pad_l - pad_r
    plot_h = height - pad_t - pad_b

    max_val = max(
        (values[s].get(q, 0.0) for s in scenarios for q in queues),
        default=1.0,
    )
    max_val = max(max_val * 1.12, 1.0)

    lines: List[str] = [
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}">',
        '<style>',
        '  .bg { fill: #0d1117; }',
        '  .title { fill: #f0f6fc; font: 600 18px system-ui, -apple-system, Segoe UI, sans-serif; }',
        '  .axis-label { fill: #8b949e; font: 12px system-ui, -apple-system, Segoe UI, sans-serif; }',
        '  .tick { fill: #8b949e; font: 11px ui-monospace, SFMono-Regular, monospace; }',
        '  .legend { fill: #c9d1d9; font: 12px system-ui, -apple-system, Segoe UI, sans-serif; }',
        '  .grid { stroke: #21262d; stroke-width: 1; }',
        '  .axis { stroke: #30363d; stroke-width: 1.5; }',
        '</style>',
        f'<rect class="bg" width="{width}" height="{height}" rx="10" />',
        f'<text class="title" x="{pad_l}" y="34">{escape(title)}</text>',
    ]

    # Legend
    lx = pad_l
    for idx, q in enumerate(queues):
        color = PALETTE[idx % len(PALETTE)]
        lines.append(f'<rect x="{lx}" y="48" width="12" height="12" rx="2" fill="{color}" />')
        lines.append(f'<text class="legend" x="{lx + 17}" y="58">{escape(q)}</text>')
        lx += max(125, len(q) * 7 + 30)

    # Horizontal grid + Y ticks
    ticks = 5
    for t in range(ticks + 1):
        val = max_val * t / ticks
        y = pad_t + plot_h - (plot_h * t / ticks)
        lines.append(f'<line class="grid" x1="{pad_l}" y1="{y:.1f}" x2="{width - pad_r}" y2="{y:.1f}" />')
        lbl = f"{val:,.0f}" if max_val >= 50 else f"{val:.1f}"
        lines.append(f'<text class="tick" x="{pad_l - 10}" y="{y + 4:.1f}" text-anchor="end">{lbl}</text>')

    lines.append(
        f'<text class="axis-label" x="22" y="{pad_t + plot_h / 2:.1f}" '
        f'transform="rotate(-90 22 {pad_t + plot_h / 2:.1f})" text-anchor="middle">{escape(y_label)}</text>'
    )

    # Bars
    group_w = plot_w / max(len(scenarios), 1)
    inner_pad = group_w * 0.14
    bar_area = group_w - 2 * inner_pad
    bar_w = bar_area / max(len(queues), 1)

    for s_idx, scenario in enumerate(scenarios):
        gx = pad_l + s_idx * group_w + inner_pad
        for q_idx, q in enumerate(queues):
            val = values[scenario].get(q, 0.0)
            bh = (val / max_val) * plot_h
            bx = gx + q_idx * bar_w
            by = pad_t + plot_h - bh
            color = PALETTE[q_idx % len(PALETTE)]
            lines.append(
                f'<rect x="{bx:.1f}" y="{by:.1f}" width="{max(bar_w - 3, 2):.1f}" '
                f'height="{max(bh, 1):.1f}" rx="2" fill="{color}">'
                f'<title>{escape(scenario)} | {escape(q)}: {val:,.1f}</title></rect>'
            )

        cx = pad_l + (s_idx + 0.5) * group_w
        short = scenario.replace("Random 200x200 ", "Random ").replace(" 200x200", "")
        lines.append(f'<text class="axis-label" x="{cx:.1f}" y="{pad_t + plot_h + 24}" text-anchor="middle">{escape(short)}</text>')

    # Axes
    lines.append(f'<line class="axis" x1="{pad_l}" y1="{pad_t}" x2="{pad_l}" y2="{pad_t + plot_h}" />')
    lines.append(f'<line class="axis" x1="{pad_l}" y1="{pad_t + plot_h}" x2="{width - pad_r}" y2="{pad_t + plot_h}" />')
    lines.append('</svg>')

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate SVG charts from benchmark CSV")
    parser.add_argument("csv_path", type=Path, help="Input CSV file produced by the benchmark CLI")
    parser.add_argument("--out-dir", type=Path, default=Path("docs"), help="Output directory for SVGs")
    args = parser.parse_args()

    scenarios: List[str] = []
    queues: List[str] = []
    time_vals: Dict[str, Dict[str, float]] = defaultdict(dict)
    ops_vals: Dict[str, Dict[str, float]] = defaultdict(dict)

    with args.csv_path.open("r", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            s = row["Scenario"]
            q = row["Queue"]
            if s not in scenarios:
                scenarios.append(s)
            if q not in queues:
                queues.append(q)
            time_vals[s][q] = float(row["MedianTimeUs"])
            ops_vals[s][q] = float(row["MeanInternalOps"])

    random_scenarios = [s for s in scenarios if "Random" in s]
    target_scenarios = random_scenarios if random_scenarios else scenarios

    render_grouped_bar_svg(
        title="A* Median Pathfinding Latency by Obstacle Density (Lower is Better)",
        y_label="Median Time (microseconds)",
        scenarios=target_scenarios,
        queues=queues,
        values=time_vals,
        out_path=args.out_dir / "benchmark_latency.svg",
    )

    indexed_queues = [q for q in queues if any(ops_vals[s].get(q, 0.0) > 0 for s in target_scenarios)]
    render_grouped_bar_svg(
        title="Internal Heap Operations (Comparisons + Swaps, Lower is Better)",
        y_label="Mean Internal Ops per Search",
        scenarios=target_scenarios,
        queues=indexed_queues,
        values=ops_vals,
        out_path=args.out_dir / "benchmark_ops.svg",
    )
    print(f"Generated SVG charts in {args.out_dir}")


if __name__ == "__main__":
    main()
