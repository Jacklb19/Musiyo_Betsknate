"""Installs the few SlimUI files the museum uses from a local Asset Store import.

The package is Asset Store content and this repository is public, so its files are never committed.
Each developer imports the package from their own Asset Store account and runs this script:

    python tools/ThirdParty/slimui_selection.py            # copy the selection
    python tools/ThirdParty/slimui_selection.py --check    # report what is installed
    python tools/ThirdParty/slimui_selection.py --hash     # print hashes of the local import

Copies get identifiers derived from their destination path, so prefabs that use them resolve on every
machine and the copies never collide with the original import. Nothing outside the destination is touched.
"""
import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = Path(__file__).with_name("slimui_selection.json")
TEXT_ASSETS = {".anim", ".controller", ".mat", ".asset", ".prefab"}
GUID_LINE = re.compile(r"^guid: ([0-9a-f]{32})\s*$", re.MULTILINE)


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def new_guid(namespace, destination):
    return hashlib.md5((namespace + ":" + destination).encode("utf-8")).hexdigest()


def load():
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    source = ROOT / manifest["source"]
    target = ROOT / manifest["destination"]
    return manifest, source, target


def original_guid(meta):
    match = GUID_LINE.search(meta.read_text(encoding="utf-8"))
    if not match:
        raise SystemExit("No identifier in " + str(meta))
    return match.group(1)


def install(check_only):
    manifest, source, target = load()
    if not source.is_dir():
        raise SystemExit("Import %s %s from the Asset Store first: %s is missing."
                         % (manifest["package"], manifest["version"], manifest["source"]))
    files = manifest["files"]
    problems = []
    remap = {}
    for item in files:
        origin = source / item["from"]
        if not origin.is_file() or not origin.with_name(origin.name + ".meta").is_file():
            problems.append("missing in the import: " + item["from"])
            continue
        if sha256(origin) != item["sha256"]:
            problems.append("differs from version %s: %s" % (manifest["version"], item["from"]))
        remap[original_guid(origin.with_name(origin.name + ".meta"))] = new_guid(manifest["identifier_namespace"], item["to"])
    if problems:
        raise SystemExit("The local import does not match the selection:\n  " + "\n  ".join(problems))
    if len(set(remap.values())) != len(files):
        raise SystemExit("Two selected files would share an identifier.")
    pending = 0
    for item in files:
        origin = source / item["from"]
        copy = target / item["to"]
        meta = origin.with_name(origin.name + ".meta").read_text(encoding="utf-8")
        own = new_guid(manifest["identifier_namespace"], item["to"])
        meta = GUID_LINE.sub("guid: " + own, meta, count=1)
        if origin.suffix in TEXT_ASSETS:
            # References among the selected files follow their copies; references to Unity packages stay as they are.
            text = origin.read_text(encoding="utf-8")
            for old, new in remap.items():
                text = text.replace(old, new)
            data = text.encode("utf-8")
        else:
            data = origin.read_bytes()
        meta_path = copy.with_name(copy.name + ".meta")
        current = copy.is_file() and copy.read_bytes() == data and meta_path.is_file() \
            and meta_path.read_text(encoding="utf-8") == meta
        if current:
            continue
        pending += 1
        if check_only:
            print("not installed:", item["to"])
            continue
        copy.parent.mkdir(parents=True, exist_ok=True)
        copy.write_bytes(data)
        meta_path.write_text(meta, encoding="utf-8", newline="\n")
    notice = target / "NOTICE.txt"
    lines = [manifest["package"] + " " + manifest["version"] + ", by " + manifest["publisher"],
             manifest["url"], "", manifest["license"], "",
             "Partial local copy for Musiyo Bëtsknaté. Installed by tools/ThirdParty/slimui_selection.py;",
             "do not commit or redistribute these files. Files:"] + ["  " + item["to"] for item in files]
    body = "\n".join(lines) + "\n"
    if not check_only and (not notice.is_file() or notice.read_text(encoding="utf-8") != body):
        target.mkdir(parents=True, exist_ok=True)
        notice.write_text(body, encoding="utf-8", newline="\n")
    print("%d of %d files %s in %s" % (len(files) - pending if check_only else len(files), len(files),
                                       "installed" if check_only else "in place", manifest["destination"]))
    return 1 if check_only and pending else 0


def hashes():
    manifest, source, _ = load()
    for item in manifest["files"]:
        origin = source / item["from"]
        print(sha256(origin) if origin.is_file() else "missing".ljust(64), item["from"])
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="report what is installed without writing")
    parser.add_argument("--hash", action="store_true", help="print the hashes of the selected files in the local import")
    arguments = parser.parse_args()
    sys.exit(hashes() if arguments.hash else install(arguments.check))
