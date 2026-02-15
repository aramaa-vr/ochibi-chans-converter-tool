#!/usr/bin/env bash
set -euo pipefail

# このスクリプトが置かれている場所
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Gemfile のある場所（docs/ or 直下）を自動判定
if [[ -f "${SCRIPT_DIR}/Gemfile" ]]; then
  SITE_DIR="${SCRIPT_DIR}"
elif [[ -f "${SCRIPT_DIR}/docs/Gemfile" ]]; then
  SITE_DIR="${SCRIPT_DIR}/docs"
else
  echo "ERROR: Gemfile が見つかりません。"
  echo "  直下: ${SCRIPT_DIR}"
  echo "  docs : ${SCRIPT_DIR}/docs"
  echo "Gemfile があるフォルダにこのファイルを置くか、リポジトリ直下で実行してください。"
  exit 1
fi

cd "${SITE_DIR}"

if ! command -v ruby >/dev/null 2>&1; then
  echo "ERROR: ruby が見つかりません。Ruby をインストールしてから再実行してください。"
  exit 1
fi

if ! command -v bundle >/dev/null 2>&1; then
  echo "ERROR: bundle が見つかりません。Bundler をインストールしてから再実行してください。"
  exit 1
fi

echo "== Site dir: ${SITE_DIR} =="
echo "Ruby:   $(ruby -v)"
echo "Bundler: $(bundle -v)"

# 依存を揃える（初回・Gemfile更新時に必要）
bundle install

# ローカル起動（GitHub Pages の baseurl 設定があっても崩れにくい）
bundle exec jekyll serve --livereload --baseurl="" --trace
