"""Package the source and an existing Windows build without Unity caches."""
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Builds"
SOURCE_ITEMS = (
    "Assets", "Packages", "ProjectSettings", "Tools", "docs",
    "README.md", "CONTEXT.md", ".gitignore", ".gitattributes",
)


def write_archive(destination, entries):
    with ZipFile(destination, "w", compression=ZIP_DEFLATED, compresslevel=6) as archive:
        for path, name in entries:
            archive.write(path, name)
    with ZipFile(destination) as archive:
        invalid = archive.testzip()
        if invalid is not None:
            raise RuntimeError(f"Archive integrity failed: {invalid}")
        print(f"{destination.name}: {len(archive.infolist())} entries, "
              f"{destination.stat().st_size:,} bytes; integrity verified")


def walk(path):
    yield path
    if path.is_dir():
        for child in sorted(path.rglob("*")):
            if "__pycache__" not in child.parts and child.suffix != ".pyc":
                yield child


def main():
    runtime = OUTPUT / "Windows"
    if not (runtime / "CrateShift.exe").is_file():
        raise SystemExit("Build the Windows player before packaging.")
    source = []
    for item in SOURCE_ITEMS:
        path = ROOT / item
        if not path.exists():
            raise SystemExit(f"Missing source item: {item}")
        source.extend((entry, entry.relative_to(ROOT)) for entry in walk(path))
    write_archive(OUTPUT / "CrateShift-Source.zip", source)
    player = []
    for entry in sorted(runtime.rglob("*")):
        if not any("DoNotShip" in part for part in entry.parts):
            player.append((entry, Path("CrateShift") / entry.relative_to(runtime)))
    write_archive(OUTPUT / "CrateShift-Windows.zip", player)


if __name__ == "__main__":
    main()
