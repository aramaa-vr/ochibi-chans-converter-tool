# 公開・無料配布前チェックリスト

このチェックリストは「リポジトリ公開」と「無料配布リリース」の直前確認用です。

## 1. 配布物・ライセンス
- [ ] `LICENSE` が存在し、利用規約（VN3）への導線がある
- [ ] `Assets/Aramaa/OchibiChansConverterTool/LICENSE.txt` が存在し、配布ZIP内導線がある
- [ ] `THIRD_PARTY_NOTICES.md` に第三者依存と同梱なし方針が明記されている
- [ ] `package.json` の `license` / `licensesUrl` が実態と一致している

## 2. 機密情報・不要ファイル
- [ ] `python Tools/release/validate_public_release.py` を実行し、機密情報スキャンを含む必須チェックが通る
- [ ] 購入必須アセット本体を誤って同梱していない（本ツールは前提商品のみ参照）
- [ ] 生成物（`Build/` 等）が不要にコミットされていない

## 3. バージョンと配布整合
- [ ] `python Tools/release/validate_public_release.py` を実行し、`package.json` / `CHANGELOG.md` の整合チェックが通る
- [ ] `python3 Tools/release/update_version.py <version> --dry-run` が成功する
- [ ] `python3 Tools/release/create_vpm_zip.py <version>` が成功する

## 4. ドキュメント公開前確認
- [ ] `README.md` から説明書サイトへの導線が有効
- [ ] `docs/` が必要ならローカルビルド確認（`docs/build-docs.sh`）を実施
- [ ] 不可の場合は CI か別環境での docs ビルド手順を用意

## 5. 公開可否判定（運用メモ）
- ベータ版（例: `x.y.z-beta.n`）は「プレリリース」として公開
- 正式版にする場合は `-beta` を外した版で再検証
- 告知文には「非公式ファンメイド」「前提商品は別途購入」を明記
