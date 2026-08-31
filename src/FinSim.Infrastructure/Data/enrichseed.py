#!/usr/bin/env python3
"""
Throwaway: adds company profile fields to FinSim's seed.json.

Reads seed.json, fetches static company metadata from Yahoo for each symbol,
writes the enriched rows back to seed.json in place.

Only static descriptors are taken. Price-derived figures (market cap, P/E,
52-week range) are deliberately skipped: FinSim's prices drift from the real
ones by design, so those numbers would be wrong the moment the sim runs.
sharesOutstanding is a share count, not a price, so it stays valid and lets
market cap be computed against FinSim's own CurrentPrice.

Resumable: rows that already carry a sector are skipped, so a run that dies
partway can just be re-run.

    pip install yfinance
    python enrichseed.py

Delete this file once seed.json is enriched.
"""

import json
import sys
import time

import yfinance as yf

SEED = "seed.json"

# Yahoo key -> seed.json key. Everything here is static company metadata.
FIELDS = {
    "sector": "sector",
    "industry": "industry",
    "longBusinessSummary": "description",
    "fullTimeEmployees": "employees",
    "website": "website",
    "city": "city",
    "sharesOutstanding": "sharesOutstanding",
}


def fetch(symbol):
    """Returns (fields, error). Never raises."""
    try:
        info = yf.Ticker(f"{symbol}.IS").info
    except Exception as exc:
        return None, str(exc)

    if not info:
        return None, "empty info"

    out = {}
    for yahoo_key, seed_key in FIELDS.items():
        value = info.get(yahoo_key)
        # Yahoo returns "" and 0 for absent values as often as it returns null.
        if value in (None, "", 0):
            continue
        if isinstance(value, str):
            value = value.strip()
            if not value:
                continue
        out[seed_key] = value

    if not out:
        return None, "no usable fields"

    return out, None


def main():
    with open(SEED, encoding="utf-8") as f:
        rows = json.load(f)

    todo = [r for r in rows if not r.get("sector")]
    print(f"{len(rows)} instruments, {len(todo)} to fetch "
          f"({len(rows) - len(todo)} already enriched)\n")

    if not todo:
        print("nothing to do")
        return

    failed = []

    for i, row in enumerate(todo, 1):
        symbol = row["symbol"]
        fields, err = fetch(symbol)

        if fields is None:
            failed.append((symbol, err))
            print(f"[{i}/{len(todo)}] {symbol:<6} FAILED ({err})")
        else:
            row.update(fields)
            sector = fields.get("sector", "-")
            has_desc = "desc" if "description" in fields else "    "
            print(f"[{i}/{len(todo)}] {symbol:<6} {has_desc}  {sector}")

        # Write after every row so a crash never loses completed work.
        with open(SEED, "w", encoding="utf-8") as f:
            json.dump(rows, f, ensure_ascii=False, indent=2)

        time.sleep(0.5)   # undocumented endpoint, IP-limited

    # ---- coverage summary: this is the bit worth reading ----
    print("\n--- coverage ---")
    for seed_key in FIELDS.values():
        have = sum(1 for r in rows if r.get(seed_key))
        print(f"  {seed_key:<18} {have:>3}/{len(rows)}")

    sectors = {}
    for r in rows:
        s = r.get("sector")
        if s:
            sectors[s] = sectors.get(s, 0) + 1

    print(f"\n--- {len(sectors)} distinct sectors ---")
    for name, count in sorted(sectors.items(), key=lambda kv: -kv[1]):
        print(f"  {count:>3}  {name}")

    if failed:
        print(f"\nfailed {len(failed)}: " + ", ".join(s for s, _ in failed))
        print("re-run to retry them")

    missing = [r["symbol"] for r in rows if not r.get("sector")]
    if missing:
        print(f"\nno sector: {', '.join(missing)}")


if __name__ == "__main__":
    sys.exit(main())