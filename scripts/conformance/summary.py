#!/usr/bin/env python3
"""Summary of a conformance-suite plan: one line per test module with its result and the first failures/warnings.

Usage: python scripts/conformance/summary.py <plan-id> [--all]
"""
import json
import ssl
import sys
import urllib.request

BASE = "https://localhost.emobix.co.uk:8443"
CTX = ssl._create_unverified_context()


def get(path):
    with urllib.request.urlopen(BASE + path, context=CTX, timeout=30) as r:
        return json.load(r)


def main():
    plan = get("/api/plan/" + sys.argv[1])
    show_all = "--all" in sys.argv
    for module in plan["modules"]:
        for instance in module.get("instances", [])[-1:]:
            info = get("/api/info/" + instance)
            result = info.get("result") or info.get("status")
            print(f"{result:<12} {module['testModule']}")
            if result in ("PASSED", "SKIPPED") and not show_all:
                continue
            for entry in get("/api/log/" + instance):
                if entry.get("result") in ("FAILURE", "WARNING") or entry.get("msg", "").startswith("Test interrupted") or "error" in entry.get("msg", "").lower() and entry.get("result") == "INTERRUPTED":
                    extra = {k: v for k, v in entry.items() if k in ("expected", "actual", "error", "error_description", "url", "requirements")}
                    print(f"    {entry.get('result')}: {entry.get('src')}: {entry.get('msg')[:300]} {json.dumps(extra)[:400] if extra else ''}")
            if info.get("error"):
                print("    ERROR:", str(info.get("error"))[:400])


if __name__ == "__main__":
    main()
