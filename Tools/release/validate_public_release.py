#!/usr/bin/env python3
"""公開・無料配布前のリリース妥当性チェック。"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import zipfile
from pathlib import Path

SECRET_PATTERN = re.compile(
    r"(AKIA[0-9A-Z]{16}|ghp_[0-9A-Za-z]{36}|xoxb-[0-9A-Za-z-]{20,}|AIza[0-9A-Za-z\-_]{35}|BEGIN (?:RSA|OPENSSH|EC) PRIVATE KEY)",
    re.IGNORECASE,
)
SEMVER_IN_ZIP_URL = re.compile(
    r"/releases/download/(?P<version>[^/]+)/"
    r"jp\.aramaa\.ochibi-chans-converter-tool-(?P=version)\.zip$"
)

REQUIRED_FILES = [
    "LICENSE",
    "THIRD_PARTY_NOTICES.md",
    "CHANGELOG.md",
    "Assets/Aramaa/OchibiChansConverterTool/LICENSE.txt",
    "Assets/Aramaa/OchibiChansConverterTool/package.json",
]
TEXT_SCAN_EXCLUDE_SUFFIXES = {
    ".png", ".jpg", ".jpeg", ".webp", ".ico", ".pdf", ".meta", ".zip", ".svg"
}
TEXT_SCAN_EXCLUDE_PARTS = {"LicenseVN3/"}
SCAN_ALLOWLIST = {"Tools/release/validate_public_release.py"}


def log_info(message: str) -> None:
    print(f"[INFO] {message}")




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

class CheckResult:
    def __init__(self) -> None:
        self.errors: list[str] = []
        self.warnings: list[str] = []

    def error(self, message: str) -> None:
        self.errors.append(message)

    def warn(self, message: str) -> None:
        self.warnings.append(message)

    def ok(self) -> bool:
        return not self.errors


def find_repo_root(start: Path) -> Path:
    for candidate in [start, *start.parents]:
        if (candidate / "Assets/Aramaa/OchibiChansConverterTool/package.json").exists():
            return candidate
    raise FileNotFoundError("Repository root not found from script location")


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8").replace("\r\n", "\n").replace("\r", "\n")


def check_required_files(root: Path, result: CheckResult) -> None:
    log_info("必須ファイルの存在を確認します")
    for rel in REQUIRED_FILES:
        path = root / rel
        if not path.exists():
            result.error(f"必須ファイルが見つかりません: {rel}")
            continue
        log_info(f"必須ファイル OK: {rel}")


def check_license_docs(root: Path, result: CheckResult) -> None:
    log_info("LICENSE と THIRD_PARTY_NOTICES.md の内容を確認します")
    license_text = read_text(root / "LICENSE")
    if "VN3ライセンス" not in license_text:
        result.error("LICENSE に VN3ライセンスの記載がありません")
    else:
        log_info("LICENSE の VN3ライセンス記載を確認しました")

    third_party_text = read_text(root / "THIRD_PARTY_NOTICES.md")
    if "同梱" not in third_party_text:
        result.warn("THIRD_PARTY_NOTICES.md に同梱ポリシー記載が見当たりません")
    else:
        log_info("THIRD_PARTY_NOTICES.md の同梱ポリシー記載を確認しました")


def load_package_json(root: Path, result: CheckResult) -> dict:
    package_path = root / "Assets/Aramaa/OchibiChansConverterTool/package.json"
    rel_package_path = package_path.relative_to(root).as_posix()
    log_info(f"package.json を読み込みます: {rel_package_path}")
    try:
        package = json.loads(read_text(package_path))
        log_info("package.json の JSON 解析に成功しました")
        return package
    except json.JSONDecodeError as exc:
        result.error(f"package.json の解析に失敗しました: {exc}")
        return {}


def check_package_consistency(package: dict, result: CheckResult) -> None:
    log_info("package.json の version / url / license / licensesUrl 整合性を確認します")

    version = package.get("version")
    url = package.get("url")
    licenses_url = package.get("licensesUrl")
    license_name = package.get("license")

    if not isinstance(version, str) or not version:
        result.error("package.json の version が不正です")
    else:
        log_info(f"package.json version を確認しました: {version}")
    if not isinstance(url, str) or not url:
        result.error("package.json の url が不正です")
    elif isinstance(version, str):
        match = SEMVER_IN_ZIP_URL.search(url)
        if not match:
            result.error("package.json の url 形式が想定と一致しません")
        elif match.group("version") != version:
            result.error(
                "package.json の version と url 内バージョンが一致しません"
            )
        else:
            log_info("package.json の url と version の整合を確認しました")

    if license_name != "Custom":
        result.warn(f"package.json の license が Custom ではありません: {license_name}")
    else:
        log_info("package.json の license=Custom を確認しました")
    if licenses_url != "https://github.com/aramaa-vr/ochibi-chans-converter-tool/blob/master/LICENSE":
        result.warn("package.json の licensesUrl が想定値と異なります")
    else:
        log_info("package.json の licensesUrl を確認しました")


def check_changelog(root: Path, package: dict, result: CheckResult) -> None:
    version = package.get("version", "")
    log_info(f"CHANGELOG.md に version {version} の見出しがあるか確認します")
    changelog = read_text(root / "CHANGELOG.md")
    if f"## [{version}]" not in changelog:
        result.error(f"CHANGELOG.md に version {version} の見出しがありません")
    else:
        log_info(f"CHANGELOG.md の version {version} 見出しを確認しました")


def build_zip_tree_lines(names: list[str]) -> list[str]:
    root: dict[str, dict] = {}
    for raw_name in sorted(names):
        normalized = raw_name.rstrip("/")
        if not normalized:
            continue
        parts = [part for part in normalized.split("/") if part]
        cursor = root
        for part in parts:
            cursor = cursor.setdefault(part, {})

    lines: list[str] = []

    def append_lines(tree: dict[str, dict], prefix: str = "") -> None:
        keys = sorted(tree.keys())
        for idx, key in enumerate(keys):
            is_last = idx == len(keys) - 1
            connector = "└─ " if is_last else "├─ "
            child = tree[key]
            suffix = "/" if child else ""
            lines.append(f"{prefix}{connector}{key}{suffix}")
            next_prefix = f"{prefix}{'   ' if is_last else '│  '}"
            append_lines(child, next_prefix)

    append_lines(root)
    return lines


def check_build_zip_contents(root: Path, package: dict, result: CheckResult) -> None:
    version = package.get("version")
    if not isinstance(version, str) or not version:
        result.warn("Build ZIP の内容確認をスキップしました: package version が不正です")
        return

    zip_rel = Path(f"Build/jp.aramaa.ochibi-chans-converter-tool-{version}.zip")
    zip_path = root / zip_rel
    log_info(f"Build ZIP の内容を確認します: {zip_rel.as_posix()}")
    if not zip_path.exists():
        result.warn(f"Build ZIP が見つかりません: {zip_rel.as_posix()}")
        return

    try:
        with zipfile.ZipFile(zip_path) as zip_file:
            names = zip_file.namelist()
    except zipfile.BadZipFile:
        result.error(f"Build ZIP の読み込みに失敗しました: {zip_rel.as_posix()}")
        return

    if not names:
        result.warn(f"Build ZIP が空です: {zip_rel.as_posix()}")
        return

    log_info(f"Build ZIP エントリ数: {len(names)}")
    for line in build_zip_tree_lines(names):
        log_info(f"[ZIP] {line}")


def should_scan_file(path: Path) -> bool:
    rel = path.as_posix()
    if any(part in rel for part in TEXT_SCAN_EXCLUDE_PARTS):
        return False
    if path.suffix.lower() in TEXT_SCAN_EXCLUDE_SUFFIXES:
        return False
    return True


def check_secrets(root: Path, result: CheckResult) -> None:
    log_info("git 管理下ファイルに機密情報パターンがないか確認します")
    tracked = subprocess.run(
        ["git", "ls-files"],
        cwd=root,
        check=True,
        text=True,
        capture_output=True,
    ).stdout.splitlines()

    findings: list[str] = []
    scanned_files = 0
    for rel in tracked:
        if rel in SCAN_ALLOWLIST:
            continue
        path = root / rel
        if not path.exists() or not path.is_file() or not should_scan_file(path):
            continue
        scanned_files += 1
        try:
            content = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue

        for idx, line in enumerate(content.splitlines(), start=1):
            if SECRET_PATTERN.search(line):
                findings.append(f"{rel}:{idx}")

    if findings:
        result.error("機密情報の疑いがある文字列を検出しました: " + ", ".join(findings[:20]))
    else:
        log_info(f"機密情報チェック完了: スキャン対象 {scanned_files} ファイル")


def check_git_clean(root: Path, result: CheckResult) -> None:
    log_info("git 作業ツリーの未コミット差分を確認します")
    status = subprocess.run(
        ["git", "status", "--short"],
        cwd=root,
        check=True,
        text=True,
        capture_output=True,
    ).stdout.strip()
    if status:
        result.warn("作業ツリーに未コミット差分があります")
    else:
        log_info("git 作業ツリーはクリーンです")


def main() -> int:
    configure_console_encoding()
    root = find_repo_root(Path(__file__).resolve())
    result = CheckResult()

    check_required_files(root, result)
    if result.errors:
        for error in result.errors:
            print(f"[ERROR] {error}")
        return 1

    check_license_docs(root, result)
    package = load_package_json(root, result)
    check_package_consistency(package, result)
    check_changelog(root, package, result)
    check_build_zip_contents(root, package, result)
    check_secrets(root, result)
    check_git_clean(root, result)

    for warning in result.warnings:
        print(f"[WARN] {warning}")
    for error in result.errors:
        print(f"[ERROR] {error}")

    if result.ok():
        print("[OK] 公開前チェックに合格しました")
        return 0

    return 1


if __name__ == "__main__":
    raise SystemExit(main())
