# Release operations handbook

このディレクトリは、おちびちゃんズ変換ツールの**リリース運用の正本**です。
旧ドキュメントの分散をやめ、ここに手順と方針を統合しています。

## 目的
- バージョン更新・ZIP作成・公開前確認を一箇所で管理する
- `docs/`（ユーザー向け）と開発運用資料を明確に分離する
- `Assets/`（配布ソース）への運用資料混入を防ぐ

## 管理対象ファイル
- `update_version.py`: バージョン更新（`ToolVersion` / `package.json version` / `package.json url`）
- `create_vpm_zip.py`: VPM 配布 ZIP 作成
- `conversion-pipeline-flow.svg`: 開発者向け変換パイプライン図

## 開発運用資料の配置ルール
- `docs/` はユーザー向けドキュメント専用
- 開発運用資料は `Tools/release/` に配置する
- `Assets/Aramaa/OchibiChansConverterTool/` は配布ソースのため、運用資料は置かない

## docs のローカル確認について
- `Tools/release/` はリリース運用手順の正本です。
- docs サイトのローカル表示確認手順（`bundle exec jekyll serve` / `./docs/build-docs.sh`）は `docs/README.md` を参照してください。

## リリース手順（実運用）
1. 作業ブランチを作成する（`develop/x.y.z` 系の運用ルールに従う）
2. バージョン反映
   - `python Tools/release/update_version.py x.y.z`
3. 差分確認・コミット・プッシュ
4. プルリクエスト作成（`develop/x.y.z` 向け）
5. ZIP 作成
   - `python Tools/release/create_vpm_zip.py x.y.z`
6. VCC で動作確認
7. VPM リポジトリ更新（開発用→本番用）
8. `master` マージと告知


## ベータ版の開発・公開方針
- いきなり正式版を出さず、まずはベータ版（例: `0.5.3-beta.1`）を公開する
- ベータ版を公開後、数名に実際に使ってもらい、フィードバックを収集する
- 問題がなければ、収集した内容を反映して正式版（`0.5.3` など）へ進める
- ツールの更新通知は正式版（`x.y.z`）基準とし、`-beta` は通常ユーザー向けの最新通知対象にしない
- ベータ版の反映コマンド例:
  - `Tools/release/update_version.py 0.5.3-beta.1`
  - `python Tools/release/update_version.py 0.5.3-beta.1`

## よく使うコマンド
- バージョン更新のドライラン:
  - `python Tools/release/update_version.py 0.0.0 --dry-run`
- 公開前チェック（ライセンス/整合性/機密情報スキャン）:
  - `python Tools/release/validate_public_release.py`
- package.json から版を読んで ZIP 作成:
  - `python Tools/release/create_vpm_zip.py`
- 版を明示して ZIP 作成:
  - `python Tools/release/create_vpm_zip.py 0.0.0`

## 注意事項
- VRChat SDK 更新直後は Unity 側の設定修正通知が出ることがあり、再起動が必要になる場合がある
- そのため、更新直後の検証時は Unity 再起動を織り込んで作業する
- Windows で `validate_public_release.py` 実行時に文字化けする場合は、PowerShell / Windows Terminal で UTF-8 を使用する（例: `chcp 65001`）
