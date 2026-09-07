"""Materialize Unity's installed TMP Essential Resources without an interactive importer."""
from pathlib import Path
import tarfile

root = Path(__file__).resolve().parents[1]
packages = sorted((root / "Library/PackageCache").glob("com.unity.textmeshpro@*/Package Resources/TMP Essential Resources.unitypackage"))
if not packages:
    raise SystemExit("Import the Unity packages before extracting TMP resources.")
with tarfile.open(packages[-1], "r:gz") as archive:
    members = {member.name: member for member in archive.getmembers()}
    count = 0
    for name, member in members.items():
        if not name.endswith("/pathname"):
            continue
        relative = archive.extractfile(member).read().decode("utf-8-sig").strip()
        target = (root / relative).resolve()
        if not target.is_relative_to(root / "Assets"):
            raise ValueError("Package asset escapes Assets: " + relative)
        prefix = name.rsplit("/", 1)[0]
        payload = members.get(prefix + "/asset")
        if payload is not None:
            target.parent.mkdir(parents=True, exist_ok=True)
            if not target.exists():
                target.write_bytes(archive.extractfile(payload).read())
        else:
            target.mkdir(parents=True, exist_ok=True)
        meta = members.get(prefix + "/asset.meta")
        meta_path = Path(str(target) + ".meta")
        if meta is not None and not meta_path.exists():
            meta_path.write_bytes(archive.extractfile(meta).read())
        count += 1
    print("TMP resource entries:", count)
