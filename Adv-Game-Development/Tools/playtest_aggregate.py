#!/usr/bin/env python3
"""
#63 計測ログの集計スクリプト（Python 3 標準ライブラリのみ）

PlaytestLogger が 1 プレイごとに保存する *_summary.csv / *_events.csv を束ねて、
テストID × ビルド番号ごとに 中央値・p75・p95 などを出す。

使い方:
    python Tools/playtest_aggregate.py                      # PlaytestLogs/ を集計
    python Tools/playtest_aggregate.py PlaytestLogs/T3 --test-id T3 --build 17
    python Tools/playtest_aggregate.py logs --group-by test_id,build_number,seed
    python Tools/playtest_aggregate.py --self-test

出力: 集計 CSV（既定: 最初のフォルダの aggregate_YYYYmmdd-HHMMSS.csv）と、主要な値の画面表示。

分位点は線形補間（Excel の PERCENTILE.INC / numpy の既定と同じ。PlaytestStats.cs と同じ定義）。
途中終了のプレイ（completed=0）は既定で除く（--include-incomplete で含める）。
"""

import argparse
import collections
import csv
import datetime
import glob
import math
import os
import sys

META = "#meta"
DOMINANCE_SHARE = 0.70  # v8 11章: 最初に選ばれる対象が 1 種類へ 70% 超で偏ったら支配戦略

# 画面に出す主要な値（T1〜T3 の合格値と照合するもの）
HIGHLIGHT = [
    ("t1", "first_rescue_sec"),
    ("t1", "first_correct_color_throw"),
    ("t1", "unachieved_stages"),
    ("t1", "interventions"),
    ("t1", "idle3s_count"),
    ("t2", "decision_sec"),
    ("t3", "total.idle_sec"),
    ("t3", "last_segment.hit_rate_drop_pt"),
    ("t3", "last_segment.black_conversions_le_rescues"),
    ("t3", "total.max_simultaneous_black"),
    ("play", "hit_rate"),
    ("play", "black_conversion_rate"),
    ("play", "throw_interval_sec.p50"),
    ("pooled", "throw_interval_sec"),
    ("pooled", "receive_to_fire_ms"),
]


def percentile(sorted_values, p):
    """線形補間の分位点。sorted_values は昇順。"""
    if not sorted_values:
        return None
    p = min(1.0, max(0.0, p))
    h = (len(sorted_values) - 1) * p
    lo = int(math.floor(h))
    hi = min(lo + 1, len(sorted_values) - 1)
    return sorted_values[lo] + (h - lo) * (sorted_values[hi] - sorted_values[lo])


def to_float(text):
    if text is None or text == "":
        return None
    try:
        value = float(text)
    except ValueError:
        return None
    return value if math.isfinite(value) else None


def read_log(path):
    """(meta dict, rows list[dict]) を返す。"""
    meta, header, rows = {}, None, []
    with open(path, encoding="utf-8-sig", newline="") as f:
        for record in csv.reader(f):
            if not record:
                continue
            if record[0] == META:
                meta[record[1] if len(record) > 1 else ""] = record[2] if len(record) > 2 else ""
                continue
            if header is None:
                header = record
                continue
            rows.append(dict(zip(header, record)))
    return meta, rows


def find_summaries(paths):
    files = []
    for path in paths:
        if os.path.isdir(path):
            files.extend(glob.glob(os.path.join(path, "**", "*_summary.csv"), recursive=True))
        elif path.endswith("_summary.csv"):
            files.append(path)
    return sorted(set(files))


def stats_row(values):
    values = sorted(v for v in values if v is not None)
    if not values:
        return dict(n=0, median="", p75="", p95="", mean="", min="", max="")
    return dict(
        n=len(values),
        median=percentile(values, 0.50),
        p75=percentile(values, 0.75),
        p95=percentile(values, 0.95),
        mean=sum(values) / len(values),
        min=values[0],
        max=values[-1],
    )


def fmt(value):
    if value == "" or value is None:
        return ""
    if isinstance(value, float):
        return f"{value:.4f}".rstrip("0").rstrip(".")
    return str(value)


def pooled_from_events(events_path):
    """1 プレイの events から、プレイをまたいで束ねたい 1 投ごとの値を取り出す。"""
    pooled = collections.defaultdict(list)
    if not os.path.exists(events_path):
        return pooled
    _, rows = read_log(events_path)
    previous_fire = None
    for row in rows:
        if row.get("event") != "fire":
            continue
        t = to_float(row.get("t"))
        if t is not None:
            if previous_fire is not None:
                pooled["throw_interval_sec"].append(t - previous_fire)
            previous_fire = t
        receive, fire = to_float(row.get("receive_time")), to_float(row.get("fire_time"))
        if receive is not None and fire is not None:
            pooled["receive_to_fire_ms"].append((fire - receive) * 1000.0)
    return pooled


def aggregate(files, group_by, test_id=None, build=None, include_incomplete=False):
    numeric = collections.defaultdict(lambda: collections.defaultdict(list))
    text = collections.defaultdict(lambda: collections.defaultdict(collections.Counter))
    pooled = collections.defaultdict(lambda: collections.defaultdict(list))
    plays = collections.Counter()
    skipped = 0

    for path in files:
        meta, rows = read_log(path)
        if test_id and meta.get("test_id") != test_id:
            continue
        if build and meta.get("build_number") != build:
            continue
        if not include_incomplete and meta.get("completed") == "0":
            skipped += 1
            continue

        key = tuple(meta.get(k, "") for k in group_by)
        plays[key] += 1
        for row in rows:
            name = (row.get("section", ""), row.get("metric", ""))
            value = row.get("value", "")
            number = to_float(value)
            if number is not None:
                numeric[key][name].append(number)
            elif value != "":
                text[key][name][value] += 1

        for name, values in pooled_from_events(path[: -len("_summary.csv")] + "_events.csv").items():
            pooled[key][name].extend(values)

    return plays, numeric, text, pooled, skipped


def write_output(out_path, group_by, plays, numeric, text, pooled):
    columns = list(group_by) + ["section", "metric", "kind", "n", "median", "p75", "p95", "mean", "min", "max", "top_value", "top_share"]
    with open(out_path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(columns)
        for key in sorted(plays):
            writer.writerow(list(key) + ["meta", "plays", "count", plays[key]] + [""] * 8)
            for (section, metric), values in sorted(numeric[key].items()):
                s = stats_row(values)
                writer.writerow(list(key) + [section, metric, "numeric", s["n"], fmt(s["median"]), fmt(s["p75"]),
                                             fmt(s["p95"]), fmt(s["mean"]), fmt(s["min"]), fmt(s["max"]), "", ""])
            for name, values in sorted(pooled[key].items()):
                s = stats_row(values)
                writer.writerow(list(key) + ["pooled", name, "per_throw", s["n"], fmt(s["median"]), fmt(s["p75"]),
                                             fmt(s["p95"]), fmt(s["mean"]), fmt(s["min"]), fmt(s["max"]), "", ""])
            for (section, metric), counter in sorted(text[key].items()):
                total = sum(counter.values())
                top, count = counter.most_common(1)[0]
                writer.writerow(list(key) + [section, metric, "text", total] + [""] * 6 + [top, fmt(count / total)])


def print_report(group_by, plays, numeric, text, pooled, skipped):
    if skipped:
        print(f"途中終了のプレイ {skipped} 件を除きました（--include-incomplete で含める）")
    for key in sorted(plays):
        label = ", ".join(f"{k}={v}" for k, v in zip(group_by, key))
        print(f"\n== {label}  プレイ数 {plays[key]}")
        print(f"{'指標':<48}{'n':>4}{'中央値':>10}{'p75':>10}{'p95':>10}")
        for section, metric in HIGHLIGHT:
            values = pooled[key].get(metric) if section == "pooled" else numeric[key].get((section, metric))
            if not values:
                continue
            s = stats_row(values)
            print(f"{section + '.' + metric:<48}{s['n']:>4}{fmt(s['median']):>10}{fmt(s['p75']):>10}{fmt(s['p95']):>10}")

        counter = text[key].get(("t2", "first_target_category"))
        if counter:
            total = sum(counter.values())
            top, count = counter.most_common(1)[0]
            share = count / total
            flag = "  ← 支配戦略の疑い（70% 超）" if share > DOMINANCE_SHARE else ""
            print(f"t2.first_target_category 最多 = {top} {count}/{total}（{share:.0%}）{flag}")


def self_test():
    assert percentile([1, 2, 3, 4, 5], 0.5) == 3
    assert percentile([1, 2, 3, 4, 5], 0.75) == 4
    assert abs(percentile([1, 2, 3, 4, 5], 0.95) - 4.8) < 1e-9
    assert percentile([10, 20], 0.5) == 15
    assert abs(percentile([10, 20], 0.95) - 19.5) < 1e-9
    assert percentile([], 0.5) is None
    assert percentile([7], 0.95) == 7
    print("self-test OK")


def main(argv=None):
    parser = argparse.ArgumentParser(description="#63 計測ログ（*_summary.csv）を集計して中央値・p75・p95 を出す")
    parser.add_argument("paths", nargs="*", default=["PlaytestLogs"], help="フォルダ（再帰的に探す）または *_summary.csv")
    parser.add_argument("--test-id", help="このテストIDだけ集計する")
    parser.add_argument("--build", help="このビルド番号だけ集計する")
    parser.add_argument("--group-by", default="test_id,build_number", help="まとめる単位（メタ行のキーをカンマ区切り）")
    parser.add_argument("--include-incomplete", action="store_true", help="途中終了のプレイも含める")
    parser.add_argument("--out", help="集計 CSV の保存先")
    parser.add_argument("--self-test", action="store_true", help="分位点の計算を確認して終わる")
    args = parser.parse_args(argv)

    if args.self_test:
        self_test()
        return 0

    files = find_summaries(args.paths)
    if not files:
        print(f"*_summary.csv が見つかりません: {', '.join(args.paths)}", file=sys.stderr)
        return 1

    group_by = [k.strip() for k in args.group_by.split(",") if k.strip()]
    plays, numeric, text, pooled, skipped = aggregate(files, group_by, args.test_id, args.build, args.include_incomplete)
    if not plays:
        print("条件に合うプレイがありません", file=sys.stderr)
        return 1

    out = args.out
    if not out:
        folder = args.paths[0] if os.path.isdir(args.paths[0]) else os.path.dirname(args.paths[0]) or "."
        out = os.path.join(folder, "aggregate_" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S") + ".csv")

    write_output(out, group_by, plays, numeric, text, pooled)
    print_report(group_by, plays, numeric, text, pooled, skipped)
    print(f"\n保存: {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
