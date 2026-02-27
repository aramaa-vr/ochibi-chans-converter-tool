---
title: 使い方
nav_order: 4
---

# 使い方

このページでは、ツールウィンドウの**3手順（変換元→変換先→実行）**を説明します。

---

## 全体フロー

![変換フロー]({{ "/assets/img/ochibi-chans-converter-tool/pages/usage/flow.svg" | relative_url }})

---

## ツールを開く

1. Hierarchy で変換したいアバターを 1 つ選びます。
2. **右クリック → Aramaa → おちびちゃんズ化ツール（Ochibi-chans Converter Tool）** を開きます。

※ ツール最上段「変換元アバター」には、選択中のアバターが自動で入ります。

![おちびちゃんズ化ツールのメニュー]({{ "/assets/img/ochibi-chans-converter-tool/pages/usage/unity-tool-menu.webp" | relative_url }})

---

## ツールウィンドウ

![おちびちゃんズ化ツールのウィンドウ]({{ "/assets/img/ochibi-chans-converter-tool/pages/usage/ochibi-chans-converter-tool.webp" | relative_url }})

---

## ツールウィンドウの読み方（上から順）

### ① 変換元アバター

- 最上段「変換元アバター」に、Hierarchy で選んだアバターが入っていることを確認します。
- 変換元は **Scene 上のアバター1体**を指定してください（Project の Prefab アセットは不可）。

> ダイアログ「Hierarchy で元のアバターを 1 つ選んでください。」が出るときは、変換元アバターが未指定です。
{: .warning }

### ② 変換先おちびちゃんズ

- 2段目のプルダウンで、変換先のおちびちゃんズを選びます。
- 候補は `Assets/夕時茶屋` から自動検出されます。
- 候補が出ない場合は「おちびちゃんズ Prefab（手動指定）」にドラッグ＆ドロップしてください。

> 候補が出ないときは [トラブルシューティング]({{ "/troubleshooting/" | relative_url }}) の「候補が出ない」を確認してください。
{: .tip }

### ③ 実行（コピー→変換）

- 「実行（コピー→変換）」で、元アバターを複製してから変換します（元データは変更しません）。
- 処理内容は以下です。

1. 元アバターを複製
2. （任意）MA Bone Proxy の補正
3. 複製側の Blueprint ID をクリア
4. 変換先おちびちゃんズの設定を反映

> 変換後にアップロードが新規扱いになるのは、Blueprint ID クリア仕様による正常な動作です。
{: .tip }

> だこちてギミックを入れたい場合は、おちびちゃんズに変換した後に別途ツールを利用してアバターにギミックを入れてください。
>
> （変換前のアバターにギミックを追加してしまうとうまく動かなくなります。既に入れている場合は別途ツールを利用してアバターにギミックを入れてください。）
{: .warning }


## 補足（必要なときだけ）

### オプション: MA Bone Proxy のずれ対策

「**MA Bone Proxyで設定している髪・小物がずれる場合に合わせる**」は初期状態で ON です。実行すると、複製後に MA Bone Proxy 処理を行い、ずれを軽減します。

<details close markdown="1">
<summary>チェックを付けた場合の動作詳細はこちら</summary>

MA Bone Proxy を疑似的に実行してから **調整** を行うようになります。
  - 複製後、アクセサリー Armature をターゲット骨（例: `Head`）配下へ移動します。
  - その後、**調整** を行います。

![ON/OFFで起きること図の見方]({{ "/assets/img/ochibi-chans-converter-tool/pages/usage/check-behavior.webp" | relative_url }})

</details>

> MA Bone Proxy が含まれるアバターでは、ON のまま実行するのがおすすめです。OFF で変換した場合は、変換後に位置・スケールを調整してください。
{: .warning }

> MA Bone Proxy が含まれないアバターでは、ON/OFF どちらでも問題ありません。
{: .note }

### おちびちゃんズ Prefab（手動指定）

- Project から **おちびちゃんズ Prefab をドラッグ＆ドロップ**します。

### オプション: ログを表示する

「**ログを表示する**」を ON にすると、処理後にログウィンドウが開きます。

実行内容（スキップ含む）やエラーをまとめて確認でき、Discord で相談するときにもそのまま共有できます。

---

## よくある操作パターン（必要なときだけ）

- **まずは1回変換したい**: ①変換元を確認 → ②変換先を選択 → ③実行 の順で進めます。
- **髪・小物がずれやすい**: 「MA Bone Proxy のずれ対策」を ON にしてから③を実行します。

---

## 次に読む

- [対応アバター]({{ "/support/" | relative_url }})
- [FAQ]({{ "/faq/" | relative_url }})
- [トラブルシューティング]({{ "/troubleshooting/" | relative_url }})
