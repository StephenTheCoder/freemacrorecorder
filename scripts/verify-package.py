"""Create/verify the local release ZIP and a source ZIP without external dependencies."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("package", type=Path)
    parser.add_argument("--create", action="store_true")
    parser.add_argument("--source", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    package = args.package.resolve()
    archive = package.with_name(package.name + "-win-x64.zip")
    required = [
        "MacroStudio.exe", "MacroStudio.dll", "MacroStudio.Core.dll", "MacroStudio.Windows.dll",
        "coreclr.dll", "hostfxr.dll", "PresentationFramework.dll", "MacroStudio.runtimeconfig.json",
        "Lab/MacroStudio.TestLab.exe", "Lab/PresentationFramework.dll", "Lab/coreclr.dll",
        "README.md", "docs/FORMAT.md", "docs/VERIFICATION.md", "docs/ADVANCED.md", "docs/SCHEDULER.md", "OpenCvSharp.dll", "OpenCvSharpExtern.dll",
        "samples/unicode-and-delay.json", "samples/emergency-stop.json", "samples/recorded-lab.json",
    ]
    for name in required:
        if not (package / name).is_file():
            raise RuntimeError(f"Release file missing: {name}")
    files = sorted(p for p in package.rglob("*") if p.is_file())
    if args.create:
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as output:
            for path in files:
                output.write(path, f"{package.name}/{path.relative_to(package).as_posix()}")
    prefix = package.name + "/"
    with zipfile.ZipFile(archive) as packaged:
        bad = packaged.testzip()
        if bad:
            raise RuntimeError(f"Corrupt archive entry: {bad}")
        expected = {prefix + p.relative_to(package).as_posix() for p in files}
        actual = {p.filename for p in packaged.infolist() if not p.is_dir()}
        if actual != expected:
            raise RuntimeError(f"ZIP file set differs: missing={expected-actual}; unexpected={actual-expected}")
        for path in files:
            name = prefix + path.relative_to(package).as_posix()
            if digest(packaged.read(name)) != digest(path.read_bytes()):
                raise RuntimeError(f"ZIP content differs from release: {name}")
    source_info = None
    if args.source:
        source_archive = package.with_name(package.name + "-source.zip")
        source_files = [root / name for name in [".gitignore", "Directory.Build.props", "NuGet.Config", "README.md"]]
        for folder in ["src", "tests", "scripts", "docs", "samples"]:
            source_files += [p for p in (root / folder).rglob("*") if p.is_file() and not {"bin", "obj", "__pycache__"}.intersection(p.relative_to(root).parts)]
        source_files += [root / name for name in ["work/advanced-native-regression/results.json", "work/advanced-verification/visual-result.json", "work/advanced-verification/gui-smoke.json"] if (root / name).is_file()]
        with zipfile.ZipFile(source_archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as output:
            for path in sorted(source_files):
                output.write(path, "MacroStudio/" + path.relative_to(root).as_posix())
        with zipfile.ZipFile(source_archive) as source:
            if source.testzip():
                raise RuntimeError("Source archive CRC verification failed")
            for path in source_files:
                if digest(source.read("MacroStudio/" + path.relative_to(root).as_posix())) != digest(path.read_bytes()):
                    raise RuntimeError(f"Source archive mismatch: {path}")
        source_info = {"path": str(source_archive), "files": len(source_files), "sha256": digest(source_archive.read_bytes())}
    report = {"release": str(package), "release_files": len(files), "zip": str(archive),
              "zip_sha256": digest(archive.read_bytes()), "source": source_info, "every_member_matches": True}
    (root / "work/package-verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
