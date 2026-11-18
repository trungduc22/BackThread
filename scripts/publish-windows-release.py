#!/usr/bin/env python3
"""Stage self-contained win-x64 release bundle under artifacts/release/Windows."""
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path


PROJECTS = [
    "src/Windows/Host/BackThread.Host.csproj",
    "src/Windows/Tray/BackThread.Tray.csproj",
    "src/Simulator/BackThread.Simulator.csproj",
    "tests/BackThread.DeviceTests/BackThread.DeviceTests.csproj",
]

FORBIDDEN_FILENAMES = re.compile(
    r"(?:^|/)(?:session\.key|local\.json|.*\.secret|.*\.key|spotify\.token.*)$",
    re.IGNORECASE,
)


def collect_licenses(repo_root: Path, dest_dir: Path) -> None:
    dest_dir.mkdir(parents=True, exist_ok=True)
    assets_file = repo_root / "src/Windows/Tray/obj/project.assets.json"
    if assets_file.exists():
        assets = json.loads(assets_file.read_text(encoding="utf-8"))
        pkg_roots = list(assets.get("packageFolders", {}).keys())
        if pkg_roots:
            pkg_root = Path(pkg_roots[0])
            candidate_dirs: set[Path] = set()
            for lib in assets.get("libraries", {}).values():
                if lib.get("type") == "package" and lib.get("path"):
                    candidate_dirs.add(pkg_root / lib["path"])
            for runtime_name in (
                "microsoft.netcore.app.runtime.win-x64",
                "microsoft.aspnetcore.app.runtime.win-x64",
                "microsoft.windowsdesktop.app.runtime.win-x64",
            ):
                parent = pkg_root / runtime_name
                if parent.is_dir():
                    for child in parent.iterdir():
                        if child.is_dir():
                            candidate_dirs.add(child)
            for p in sorted(candidate_dirs):
                if not p.is_dir():
                    continue
                notices = [
                    f
                    for f in p.iterdir()
                    if f.is_file()
                    and re.match(r"^(LICENSE|COPYING|THIRD.*NOTICE)", f.name, re.IGNORECASE)
                ]
                if not notices:
                    continue
                target = dest_dir / f"{p.parent.name}-{p.name}"
                target.mkdir(parents=True, exist_ok=True)
                for n in notices:
                    shutil.copy2(n, target / n.name)

    sdk_src = repo_root / "docs/licenses"
    if sdk_src.is_dir():
        sdk_dest = dest_dir / "Windows-SDK-and-CsWinRT"
        sdk_dest.mkdir(parents=True, exist_ok=True)
        for item in sdk_src.iterdir():
            if item.is_file():
                shutil.copy2(item, sdk_dest / item.name)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--dotnet",
        default=shutil.which("dotnet") or "/home/antigravity/.dotnet/dotnet",
    )
    parser.add_argument("--output", default="artifacts/release/Windows")
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parents[1]
    out_dir = (repo_root / args.output).resolve()
    artifacts_dir = (repo_root / "artifacts").resolve()
    if artifacts_dir not in out_dir.parents and out_dir != artifacts_dir:
        raise SystemExit(f"Refusing publish outside artifacts/: {out_dir}")

    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    for proj in PROJECTS:
        cmd = [
            args.dotnet,
            "publish",
            proj,
            "-c",
            "Release",
            "-r",
            "win-x64",
            "--self-contained",
            "true",
            "-p:EnableWindowsTargeting=true",
            "-o",
            str(out_dir),
            "--nologo",
        ]
        print("+", " ".join(cmd))
        subprocess.run(cmd, cwd=repo_root, check=True)

    (out_dir / "config").mkdir(parents=True, exist_ok=True)
    shutil.copy2(repo_root / "config/example.json", out_dir / "config/example.json")
    shutil.copy2(repo_root / "LICENSE", out_dir / "LICENSE")
    shutil.copy2(repo_root / "THIRD_PARTY_NOTICES.md", out_dir / "THIRD_PARTY_NOTICES.md")
    shutil.copy2(repo_root / "scripts/Setup-Portable.ps1", out_dir / "Setup-Portable.ps1")
    shutil.copy2(repo_root / "docs/PORTABLE_README.txt", out_dir / "READ-ME-FIRST.txt")
    shutil.copytree(repo_root / "extensions/chromium", out_dir / "extensions/chromium", dirs_exist_ok=True)
    shutil.copytree(repo_root / "packaging/msix", out_dir / "packaging/msix", dirs_exist_ok=True)
    collect_licenses(repo_root, out_dir / "licenses")

    files = sorted(
        p for p in out_dir.rglob("*") if p.is_file() and p.name != "checksums.sha256"
    )
    lines = []
    for f in files:
        rel = f.relative_to(out_dir).as_posix()
        if FORBIDDEN_FILENAMES.search(rel):
            raise SystemExit(f"Forbidden secret-like file staged: {rel}")
        digest = hashlib.sha256(f.read_bytes()).hexdigest()
        lines.append(f"{digest}  {rel}")

    manifest_path = out_dir / "checksums.sha256"
    manifest_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Published {len(lines)} files cleanly to {out_dir}; manifest: {manifest_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
