---
title: トラブルシューティング
nav_order: 6
---

# トラブルシューティング

> **どうすれば治るか**をまとめたページです。<br>
> 短い答えは [FAQ]({{ "/faq/" | relative_url }}),
> 既知バグの確認は [既知の問題]({{ "/known-issues/" | relative_url }}) を参照してください。
{: .note }

---

## まず確認（共通）

> 🚨 **重要：Modular Avatar は `1.16.2` の利用を推奨しています。**  \
> それ以外のバージョンでは、想定どおりに動作しない場合があります。
{: .warning }

1. VCCでツールが Installed
2. Unity再起動
3. Consoleの赤エラー解消
4. Hierarchy上のアバタールートを選択

---

## 「Hierarchy で元のアバターを 1 つ選んでください。」

- Hierarchyでアバタールートを1つ選び直して再実行

## 「Project でおちびちゃんズの Prefab を 1 つ選んでください。」

- 変換先Prefabを手動指定（ドラッグ&ドロップ）

---

## プルダウンが出ない / 間違う

1. `Assets/夕時茶屋` 配下にあるか確認
2. 手動指定で進める
3. キャッシュを削除して再生成

キャッシュ:
- `Library/Aramaa/OchibiChansConverterTool/FaceMeshCache.v7.json`

---

## 髪・小物がズレる

1. MA Bone Proxy調整オプションをON
2. 再実行して確認
3. 直らなければ [既知の問題]({{ "/known-issues/" | relative_url }}) を確認

---

## ログを添えて相談したい

- 「ログを表示する」をONで実行
- ログをコピーしてDiscordへ
- 報告テンプレは [既知の問題]({{ "/known-issues/" | relative_url }}) にあります
