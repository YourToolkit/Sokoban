"""Read-only Unity asset, GUID migration, and runtime-boundary audit.

Only Logs/asset-audit.txt is written. Library and .utmp are never traversed.
Run from any directory with: python Tools/audit_assets.py
"""

from __future__ import annotations

import argparse
from collections import defaultdict
from datetime import datetime, timezone
import json
import os
from pathlib import Path, PurePosixPath
import re
import sys


GUID = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)
BASELINE = re.compile(r"^(.*?)\s+guid:\s*([0-9a-fA-F]{32})\s*$")
CS_NONCODE = re.compile(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\r\n]*|/\*[\s\S]*?\*/')
OLD_ASSET_PATH = re.compile(r"Assets(?:/|\\{1,2})Sokoban(?:/|\\|[\"'])")
OLD_RESOURCE_PATH = re.compile(r'Resources\s*\.\s*Load(?:\s*<[^>]+>)?\s*\(\s*"Sokoban/')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.project.resolve()
    assets = root / "Assets"
    report = root / "Logs" / "asset-audit.txt"
    errors: dict[str, list[str]] = defaultdict(list)
    entities: list[Path] = []
    metas: list[Path] = []
    by_guid: dict[str, list[Path]] = defaultdict(list)

    def relative(path: Path) -> str:
        return path.relative_to(root).as_posix()

    if not assets.is_dir():
        errors["Missing Assets directory"].append(str(assets))
    else:
        for directory, folders, files in os.walk(assets, followlinks=False):
            folders[:] = sorted(name for name in folders if name not in {"Library", ".utmp"})
            parent = Path(directory)
            entities.extend(parent / name for name in folders)
            for name in sorted(files):
                path = parent / name
                (metas if name.endswith(".meta") else entities).append(path)

    for entity in entities:
        if not Path(str(entity) + ".meta").is_file():
            errors["Missing .meta"].append(relative(entity))
    for meta in metas:
        entity = meta.with_name(meta.name[:-5])
        if not entity.exists():
            errors["Orphan .meta"].append(relative(meta))
        try:
            matches = GUID.findall(meta.read_text(encoding="utf-8-sig"))
            if len(matches) != 1:
                errors["Invalid meta GUID"].append(relative(meta))
            else:
                by_guid[matches[0].lower()].append(entity)
        except (OSError, UnicodeError) as error:
            errors["Unreadable .meta"].append(f"{relative(meta)}: {error}")
    for guid, paths in sorted(by_guid.items()):
        if len(paths) > 1:
            errors["Duplicate GUID"].append(f"{guid}: " + ", ".join(relative(path) for path in paths))

    baseline_path = root / "Tools" / "asset-guid-baseline.txt"
    preserved = 0
    excluded_folders: list[str] = []
    baseline_entries: list[tuple[str, str]] = []
    if not baseline_path.is_file():
        errors["Missing migration baseline"].append(relative(baseline_path))
    else:
        for number, line in enumerate(baseline_path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if not line.strip():
                continue
            match = BASELINE.match(line.strip())
            if not match or not match[1].endswith(".meta"):
                errors["Invalid baseline entry"].append(f"line {number}: {line}")
                continue
            baseline_entries.append((match[1][:-5].replace("\\", "/"), match[2].lower()))
        old_paths = [path for path, _ in baseline_entries]
        for old_path, guid in baseline_entries:
            destinations = by_guid.get(guid, [])
            was_folder = any(path.startswith(old_path + "/") for path in old_paths)
            # The baseline stores only paths/GUIDs. Its extensionless leaf entries
            # are Unity folders (including empty folders); record this exemption.
            if was_folder or not PurePosixPath(old_path).suffix:
                excluded_folders.append(old_path)
                continue
            if len(destinations) == 1 and destinations[0].is_file():
                preserved += 1
            else:
                errors["Missing migrated asset GUID"].append(f"{old_path}: {guid}")

    runtime_files = 0
    path_files = 0
    for entity in entities:
        if not entity.is_file() or entity.suffix != ".cs":
            continue
        try:
            source = entity.read_text(encoding="utf-8-sig")
        except (OSError, UnicodeError) as error:
            errors["Unreadable source"].append(f"{relative(entity)}: {error}")
            continue
        name = relative(entity)
        path_files += 1
        for number, line in enumerate(source.splitlines(), 1):
            if OLD_ASSET_PATH.search(line) or OLD_RESOURCE_PATH.search(line):
                errors["Obsolete asset/resource load path"].append(f"{name}:{number}: {line.strip()}")
        if name.startswith("Assets/Scripts/") and not name.startswith(("Assets/Scripts/Editor/", "Assets/Scripts/Tests/")):
            runtime_files += 1
            code = CS_NONCODE.sub(lambda match: re.sub(r"[^\r\n]", " ", match[0]), source)
            for match in re.finditer(r"\bUnityEditor\b", code):
                number = code.count("\n", 0, match.start()) + 1
                errors["UnityEditor reference in Runtime/Core"].append(f"{name}:{number}")
    for entity in entities:
        name = relative(entity)
        if entity.suffix == ".asmdef" and name.startswith("Assets/Scripts/") and not name.startswith(("Assets/Scripts/Editor/", "Assets/Scripts/Tests/")):
            try:
                definition = json.loads(entity.read_text(encoding="utf-8-sig"))
                for reference in definition.get("references", []):
                    if "UnityEditor" in reference:
                        errors["UnityEditor assembly reference in Runtime/Core"].append(f"{name}: {reference}")
            except (OSError, UnicodeError, ValueError) as error:
                errors["Unreadable assembly definition"].append(f"{name}: {error}")

    count = sum(map(len, errors.values()))
    lines = [
        "UNITY ASSET AUDIT: " + ("PASS" if count == 0 else "FAIL"),
        "Time: " + datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "Project: " + str(root),
        "This audit does not modify assets. Library and .utmp were not scanned.",
        f"Assets: {sum(path.is_file() for path in entities)} files, {sum(path.is_dir() for path in entities)} directories, {len(metas)} metadata files.",
        f"Migration: {preserved} file GUIDs preserved; {len(excluded_folders)} original folder-only entries excluded.",
        f"Code: {runtime_files} Runtime/Core files checked for editor references; {path_files} C# files checked for obsolete load paths.",
        f"Problems: {count}",
    ]
    for category, messages in sorted(errors.items()):
        lines.extend(["", category + f" ({len(messages)}):"])
        lines.extend("  - " + message for message in sorted(messages))
    if excluded_folders:
        lines.extend(["", "Original folder entries excluded from file-GUID preservation:"])
        lines.extend("  - " + path for path in sorted(excluded_folders))
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines[:8]))
    print("Report: " + str(report))
    return 1 if count else 0


if __name__ == "__main__":
    sys.exit(main())
