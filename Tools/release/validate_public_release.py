#!/usr/bin/env python3
"""公開・無料配布前のリリース妥当性チェック。"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
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
    for rel in REQUIRED_FILES:
        path = root / rel
        if not path.exists():
            result.error(f"必須ファイルが見つかりません: {rel}")


def check_license_docs(root: Path, result: CheckResult) -> None:
    license_text = read_text(root / "LICENSE")
    if "VN3ライセンス" not in license_text:
        result.error("LICENSE に VN3ライセンスの記載がありません")

    third_party_text = read_text(root / "THIRD_PARTY_NOTICES.md")
    if "同梱" not in third_party_text:
        result.warn("THIRD_PARTY_NOTICES.md に同梱ポリシー記載が見当たりません")


def load_package_json(root: Path, result: CheckResult) -> dict:
    package_path = root / "Assets/Aramaa/OchibiChansConverterTool/package.json"
    try:
        return json.loads(read_text(package_path))
    except json.JSONDecodeError as exc:
        result.error(f"package.json の解析に失敗しました: {exc}")
        return {}


def check_package_consistency(package: dict, result: CheckResult) -> None:

    version = package.get("version")
    url = package.get("url")
    licenses_url = package.get("licensesUrl")
    license_name = package.get("license")

    if not isinstance(version, str) or not version:
        result.error("package.json の version が不正です")
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

    if license_name != "Custom":
        result.warn(f"package.json の license が Custom ではありません: {license_name}")
    if licenses_url != "https://github.com/aramaa-vr/ochibi-chans-converter-tool/blob/master/LICENSE":
        result.warn("package.json の licensesUrl が想定値と異なります")


def check_changelog(root: Path, package: dict, result: CheckResult) -> None:
    version = package.get("version", "")
    changelog = read_text(root / "CHANGELOG.md")
    if f"## [{version}]" not in changelog:
        result.error(f"CHANGELOG.md に version {version} の見出しがありません")


def should_scan_file(path: Path) -> bool:
    rel = path.as_posix()
    if any(part in rel for part in TEXT_SCAN_EXCLUDE_PARTS):
        return False
    if path.suffix.lower() in TEXT_SCAN_EXCLUDE_SUFFIXES:
        return False
    return True


def check_secrets(root: Path, result: CheckResult) -> None:
    tracked = subprocess.run(
        ["git", "ls-files"],
        cwd=root,
        check=True,
        text=True,
        capture_output=True,
    ).stdout.splitlines()

    findings: list[str] = []
    for rel in tracked:
        if rel in SCAN_ALLOWLIST:
            continue
        path = root / rel
        if not path.exists() or not path.is_file() or not should_scan_file(path):
            continue
        try:
            content = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue

        for idx, line in enumerate(content.splitlines(), start=1):
            if SECRET_PATTERN.search(line):
                findings.append(f"{rel}:{idx}")

    if findings:
        result.error("機密情報の疑いがある文字列を検出しました: " + ", ".join(findings[:20]))


def check_git_clean(root: Path, result: CheckResult) -> None:
    status = subprocess.run(
        ["git", "status", "--short"],
        cwd=root,
        check=True,
        text=True,
        capture_output=True,
    ).stdout.strip()
    if status:
        result.warn("作業ツリーに未コミット差分があります")


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
