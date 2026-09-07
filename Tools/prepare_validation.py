"""Create an isolated Unity validation checkout with stable source meta files."""
from pathlib import Path
import shutil
import uuid
import argparse

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--target", default=".utmp/ValidationProject", help="Disposable validation project inside this workspace")
args = parser.parse_args()

root = Path(__file__).resolve().parents[1]
assets = root / "Assets"
for path in list(assets.rglob("*")):
    if path.suffix == ".meta":
        continue
    meta = path.with_name(path.name + ".meta")
    if meta.exists():
        continue
    header = f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n"
    if path.is_dir():
        body = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    elif path.suffix == ".cs":
        body = "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    elif path.suffix == ".asmdef":
        body = "AssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    elif path.suffix == ".asmref":
        body = "AssemblyDefinitionReferenceImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    elif path.suffix in (".otf", ".ttf"):
        body = "TrueTypeFontImporter:\n  serializedVersion: 2\n  fontSize: 16\n  forceTextureCase: -2\n  characterSpacing: 1\n  characterPadding: 0\n  includeFontData: 1\n  use2xBehaviour: 0\n  fontNames: []\n  fallbackFontReferences: []\n  customCharacters: \n  fontRenderingMode: 0\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    else:
        body = "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    meta.write_text(header + body, encoding="utf-8")

target = (root / args.target).resolve()
if not target.is_relative_to(root / ".utmp"):
    raise SystemExit("Validation target must be inside the project's .utmp directory.")
target.mkdir(parents=True, exist_ok=True)
# This is a disposable checkout. Mirror scripts so GUID-preserving moves cannot
# leave old files in its Assets and compile duplicate classes or assemblies.
validation_scripts = (target / "Assets/Scripts").resolve()
if not validation_scripts.is_relative_to(target) or not validation_scripts.is_relative_to(root / ".utmp"):
    raise SystemExit("Validation Scripts resolved outside the disposable checkout.")
if validation_scripts.exists():
    shutil.rmtree(validation_scripts)
for folder in ("Assets", "Packages", "ProjectSettings"):
    shutil.copytree(root / folder, target / folder, dirs_exist_ok=True)
print(target)
