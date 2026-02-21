#!/usr/bin/env python3
"""
OchibiChansConverterTool version update helper.

Usage:
  Tools/release/update_version.py <version>
  Tools/release/update_version.py <version> --dry-run

Updates:
  - Assets/Aramaa/OchibiChansConverterTool/Editor/Common/OCTEditorConstants.cs (ToolVersion)
  - Assets/Aramaa/OchibiChansConverterTool/package.json (version, url)
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path


def find_repo_root(start: Path) -> Path:
    for candidate in [start, *start.parents]:
        if (candidate / "Assets/Aramaa/OchibiChansConverterTool/package.json").exists():
            return candidate
    raise FileNotFoundError("Repository root not found from script location")


ROOT = find_repo_root(Path(__file__).resolve())
CS_CONSTANTS = ROOT / "Assets/Aramaa/OchibiChansConverterTool/Editor/Common/OCTEditorConstants.cs"
PACKAGE_JSON = ROOT / "Assets/Aramaa/OchibiChansConverterTool/package.json"
SEMVER_PATTERN = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-((?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\."
    r"(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?"
    r"(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"
)


def ensure_file_exists(path: Path) -> None:
    if not path.exists():
        raise FileNotFoundError(f"Required file not found: {path}")


def read_text(path: Path) -> str:
    content = path.read_bytes().decode("utf-8")
    return content.replace("\r\n", "\n").replace("\r", "\n")


def write_text(path: Path, content: str) -> None:
    normalized = content.replace("\r\n", "\n").replace("\r", "\n")
    path.write_bytes(normalized.encode("utf-8"))


def parse_version(version: str) -> str:
    if not SEMVER_PATTERN.fullmatch(version):
        raise argparse.ArgumentTypeError(
            "Invalid version format. Use SemVer style values like '0.5.3' or '0.5.3-beta.1'."
        )
    return version


def update_csharp_constants(version: str, dry_run: bool) -> None:
    ensure_file_exists(CS_CONSTANTS)
    content = read_text(CS_CONSTANTS)
    new_content, count = re.subn(
        r'(public const string ToolVersion = ")([^"]+)(";)',
        rf"\g<1>{version}\g<3>",
        content,
    )
    if count != 1:
        raise ValueError(f"ToolVersion not updated (matches: {count}) in {CS_CONSTANTS}")
    if not dry_run:
        write_text(CS_CONSTANTS, new_content)


def update_package_json(version: str, dry_run: bool) -> None:
    ensure_file_exists(PACKAGE_JSON)
    content = read_text(PACKAGE_JSON)
    version_content, version_count = re.subn(
        r'("version"\s*:\s*")([^"]+)(")',
        rf"\g<1>{version}\g<3>",
        content,
    )
    if version_count != 1:
        raise ValueError(
            f"package.json version not updated (matches: {version_count}) in {PACKAGE_JSON}"
        )
    url = (
        "https://github.com/aramaa-vr/ochibi-chans-converter-tool/releases/download/"
        f"{version}/jp.aramaa.ochibi-chans-converter-tool-{version}.zip"
    )
    new_content, url_count = re.subn(
        r'("url"\s*:\s*")([^"]+)(")',
        rf"\g<1>{url}\g<3>",
        version_content,
    )
    if url_count != 1:
        raise ValueError(
            f"package.json url not updated (matches: {url_count}) in {PACKAGE_JSON}"
        )

    if not dry_run:
        write_text(PACKAGE_JSON, new_content)



def configure_console_encoding() -> None:
    """Windows 環境での文字化けを抑えるため標準出力を UTF-8 に寄せる。"""
    if os.name != "nt":
        return

    for stream_name in ("stdout", "stderr"):
        stream = getattr(sys, stream_name, None)
        if stream is None:
            continue
        reconfigure = getattr(stream, "reconfigure", None)
        if callable(reconfigure):
            reconfigure(encoding="utf-8", errors="replace")

def main() -> None:
    configure_console_encoding()
    parser = argparse.ArgumentParser(description="Update OchibiChansConverterTool version references.")
    parser.add_argument(
        "version",
        type=parse_version,
        help="New version string (e.g. 0.3.0, 0.5.3-beta.1)",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Validate and report changes without writing files.",
    )
    args = parser.parse_args()

    update_csharp_constants(args.version, args.dry_run)
    update_package_json(args.version, args.dry_run)

    if args.dry_run:
        print(f"[dry-run] Version update validated for {args.version}")
    else:
        print(f"Version updated to {args.version}")


if __name__ == "__main__":
    main()
