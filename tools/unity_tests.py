"""Run the Unity project's tests from the terminal and exit 0 only when every test passed (PLAN T0.4).

Two routes, the same verdict:
  * an Editor with the Pipeline package is open on the project: the tests run in it (`unity command test run`);
  * otherwise Unity runs them in batch mode (`-runTests`) and the NUnit XML result is read.

Usage (from the repository root):
  python tools/unity_tests.py [--mode editmode|playmode|all] [--batch]

Exit codes: 0 all passed; 1 a test failed or no test ran; 2 Unity could not run the tests.
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.join(ROOT, "unity")
EDITOR_VERSION = "6000.6.8f1"
EDITOR = os.path.join(r"C:\Program Files\Unity\Hub\Editor", EDITOR_VERSION, "Editor", "Unity.exe")


def unity_cli(*args, timeout=900):
    """Runs the Unity CLI and returns its parsed JSON result, or None."""
    cmd = ["unity", *args, "--non-interactive", "--no-banner"]
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace",
                          timeout=timeout, shell=(os.name == "nt"))
    try:
        return json.loads(proc.stdout)
    except json.JSONDecodeError:
        return None


def editor_reachable():
    proc = subprocess.run(["unity", "pipeline", "list", "--non-interactive", "--no-banner", "--format", "tsv"],
                          capture_output=True, text=True, shell=(os.name == "nt"))
    for line in proc.stdout.splitlines():
        cells = line.split("\t")
        if len(cells) > 8 and os.path.normcase(cells[1]) == os.path.normcase(PROJECT) and cells[8].strip() == "true":
            return True
    return False


def report(results):
    """Prints failures and the totals; returns (total, failed)."""
    total = failed = 0
    for r in results:
        total += 1
        if r["status"] != "Passed":
            failed += 1
            print("FAILED " + r["name"])
            if r.get("message"):
                print("  " + r["message"].strip().replace("\n", "\n  "))
    return total, failed


def run_pipeline(mode):
    pipeline_mode = {"editmode": "editor", "playmode": "playmode", "all": "all"}[mode]
    data = unity_cli("command", "test", "run", "--result-only", "--mode", pipeline_mode, "--timeout", "600")
    if data is None:
        return None
    results = [{"name": r["FullName"], "status": r["Status"], "message": r.get("Message")} for r in data.get("Results") or []]
    if data.get("result") == "playmode_running":
        for _ in range(300):
            time.sleep(2)
            status = unity_cli("command", "test", "status", "--result-only")
            if status and status.get("status") == "completed":
                results += [{"name": r["FullName"], "status": r["Status"], "message": r.get("Message")}
                            for r in status.get("results") or []]
                break
        else:
            print("PlayMode tests did not finish.")
            return None
    return results


def run_batch(mode):
    platforms = ["EditMode", "PlayMode"] if mode == "all" else ["EditMode" if mode == "editmode" else "PlayMode"]
    results = []
    for platform in platforms:
        out = os.path.join(tempfile.gettempdir(), "outerrim-unity-" + platform.lower() + ".xml")
        log = os.path.join(tempfile.gettempdir(), "outerrim-unity-" + platform.lower() + ".log")
        if os.path.exists(out):
            os.remove(out)
        subprocess.run([EDITOR, "-batchmode", "-projectPath", PROJECT, "-runTests", "-testPlatform", platform,
                        "-testResults", out, "-logFile", log], timeout=1800)
        if not os.path.exists(out):
            print("Unity wrote no results for " + platform + "; see " + log)
            return None
        for case in ET.parse(out).getroot().iter("test-case"):
            message = case.find("./failure/message")
            results.append({"name": case.get("fullname"), "status": case.get("result"),
                            "message": message.text if message is not None else None})
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--mode", choices=["editmode", "playmode", "all"], default="all")
    parser.add_argument("--batch", action="store_true", help="use batch mode even if an Editor is open")
    args = parser.parse_args()

    use_editor = not args.batch and editor_reachable()
    print("Running " + args.mode + " tests " + ("in the open Editor" if use_editor else "in batch mode") + "...")
    results = run_pipeline(args.mode) if use_editor else run_batch(args.mode)
    if results is None:
        return 2
    total, failed = report(results)
    print("{} tests, {} passed, {} failed".format(total, total - failed, failed))
    return 0 if total > 0 and failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
