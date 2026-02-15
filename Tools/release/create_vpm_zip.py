#!/usr/bin/env python3
"""
VPM ZIP作成スクリプト。

Python 標準ライブラリのみで ZIP を作成します。
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import sys
import tempfile
import zipfile
from pathlib import Path

ZIP_NAME_PREFIX = "jp.aramaa.ochibi-chans-converter-tool"
SEMVER_PATTERN = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-((?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\."
    r"(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?"
    r"(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"
)


def find_repo_root(start: Path) -> Path:
    for candidate in [start, *start.parents]:
        if (candidate / "Assets/Aramaa/OchibiChansConverterTool/package.json").exists():
            return candidate
    raise FileNotFoundError("Repository root not found from script location")


ROOT_DIR = find_repo_root(Path(__file__).resolve())
SOURCE_DIR = ROOT_DIR / "Assets/Aramaa/OchibiChansConverterTool"
BUILD_DIR = ROOT_DIR / "Build"
PACKAGE_JSON = ROOT_DIR / "Assets/Aramaa/OchibiChansConverterTool/package.json"
DOCUMENTATION_DIR_NAME = "Documentation~"
LEGAL_FILES = [
    {
        "source": Path("Assets/Aramaa/OchibiChansConverterTool/LICENSE.txt"),
        "dest": Path("LICENSE.txt"),
        "required": True,
    },
    {
        "source": Path("THIRD_PARTY_NOTICES.md"),
        "dest": Path("THIRD_PARTY_NOTICES.md"),
        "required": True,
    },
    {
        "source": Path("LicenseVN3/README.md"),
        "dest": Path("LicenseVN3/README.md"),
        "required": False,
    },
    {
        "source": Path("LicenseVN3/20260215063734vn3license_ja.pdf"),
        "dest": Path("LicenseVN3/20260215063734vn3license_ja.pdf"),
        "required": True,
    },
    {
        "source": Path("LicenseVN3/20260215063734vn3license_en.pdf"),
        "dest": Path("LicenseVN3/20260215063734vn3license_en.pdf"),
        "required": True,
    },
    {
        "source": Path("LicenseVN3/20260215063734vn3license_ko.pdf"),
        "dest": Path("LicenseVN3/20260215063734vn3license_ko.pdf"),
        "required": True,
    },
    {
        "source": Path("LicenseVN3/20260215063734vn3license_zh.pdf"),
        "dest": Path("LicenseVN3/20260215063734vn3license_zh.pdf"),
        "required": True,
    },
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="VPM ZIPを作成します。")
    parser.add_argument("version", nargs="?", help="バージョン (例: 0.3.0)")
    return parser.parse_args()


def read_version_from_package_json() -> str:
    if not PACKAGE_JSON.exists():
        raise FileNotFoundError(f"package.jsonが見つかりません: {PACKAGE_JSON}")
    content = PACKAGE_JSON.read_text(encoding="utf-8")
    try:
        data = json.loads(content)
    except json.JSONDecodeError as exc:
        raise ValueError(f"package.jsonの解析に失敗しました: {PACKAGE_JSON}") from exc
    version = data.get("version")
    if not isinstance(version, str) or not version:
        raise ValueError(f"package.jsonからversionを取得できません: {PACKAGE_JSON}")
    return version


def validate_version(version: str) -> None:
    if not SEMVER_PATTERN.fullmatch(version):
        raise ValueError(
            f"バージョン形式が不正です: {version} (例: 0.5.3 / 0.5.3-beta.1)"
        )


def add_directory_entry(zip_file: zipfile.ZipFile, relative: Path) -> None:
    """空ディレクトリもZIPに入れるためのエントリ追加。"""
    if relative == Path("."):
        return
    archive_name = relative.as_posix().rstrip("/") + "/"
    zip_info = zipfile.ZipInfo(archive_name)
    zip_info.external_attr = 0o40775 << 16
    zip_file.writestr(zip_info, "")


def create_zip(zip_path: Path, source_dir: Path) -> None:
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zip_file:
        for root, dirs, files in os.walk(source_dir):
            dirs.sort()
            files.sort()
            root_path = Path(root)
            relative_root = root_path.relative_to(source_dir)
            if not files and not dirs:
                add_directory_entry(zip_file, relative_root)
            for file_name in files:
                file_path = root_path / file_name
                archive_name = (relative_root / file_name).as_posix()
                zip_file.write(file_path, archive_name)


def create_staging_source(source_dir: Path) -> Path:
    temp_dir = Path(tempfile.mkdtemp(prefix="vpm-zip-staging-"))
    staging_source_dir = temp_dir / source_dir.name
    shutil.copytree(source_dir, staging_source_dir)

    documentation_dir = staging_source_dir / DOCUMENTATION_DIR_NAME
    documentation_dir.mkdir(parents=True, exist_ok=True)

    missing_required_sources: list[str] = []
    for legal_file in LEGAL_FILES:
        source_path = ROOT_DIR / legal_file["source"]
        destination_path = documentation_dir / legal_file["dest"]

        if not source_path.exists():
            message = f"[WARN] 法務ファイルが見つかりません: {source_path}"
            if legal_file["required"]:
                print(message, file=sys.stderr)
                missing_required_sources.append(str(source_path))
            else:
                print(message, file=sys.stderr)
            continue

        destination_path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source_path, destination_path)

    if missing_required_sources:
        missing_files = "\n- ".join(missing_required_sources)
        raise FileNotFoundError(
            "必須の法務ファイルが見つかりません。\n- " + missing_files
        )

    return staging_source_dir



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

def main() -> int:
    configure_console_encoding()
    args = parse_args()
    try:
        version = args.version or read_version_from_package_json()
        validate_version(version)
    except (FileNotFoundError, ValueError) as exc:
        print(str(exc), file=sys.stderr)
        return 1

    zip_file_name = f"{ZIP_NAME_PREFIX}-{version}.zip"
    zip_file_path = BUILD_DIR / zip_file_name

    if zip_file_path.exists():
        print(f"削除: {zip_file_path}")
        zip_file_path.unlink()

    BUILD_DIR.mkdir(parents=True, exist_ok=True)

    if not SOURCE_DIR.exists():
        print(f"ソースディレクトリが見つかりません: {SOURCE_DIR}", file=sys.stderr)
        return 1

    staging_root: Path | None = None
    try:
        staging_source_dir = create_staging_source(SOURCE_DIR)
        staging_root = staging_source_dir.parent
        create_zip(zip_file_path, staging_source_dir)
    except (FileNotFoundError, OSError) as exc:
        print(str(exc), file=sys.stderr)
        return 1
    finally:
        if staging_root and staging_root.exists():
            shutil.rmtree(staging_root)

    print(f"ZIP作成完了: {zip_file_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
