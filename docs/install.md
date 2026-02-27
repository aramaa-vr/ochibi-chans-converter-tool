---
title: 導入
description: VCC（VRChat Creator Companion）での導入手順と、ZIP/UnityPackage導入時の注意点。
nav_order: 3
---

# 導入

**VCC（推奨）** と **UnityPackage（非推奨）** で導入する方法を説明します。  
どちらの場合も関連するVCCをインストールする必要があります。

---

## プロジェクトの確認

- Unity: **2022.3.22f1**
- VCCで作成したアバタープロジェクト（例: `2022.3.22f1 avatar project`）
- VRChat SDK: **3.10.1 以上**

![VCCのUnityバージョン]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/vcc-unity-version.webp" | relative_url }})

---

## 関連するVCCをインストール

- [Modular Avatar](https://modular-avatar.nadena.dev/ja/)
- [Floor Adjuster](https://narazaka.booth.pm/items/5756378)
- [lilToon](https://lilxyzw.booth.pm/items/3087170)

---

## 方法1: VCCで導入（推奨）

<details open markdown="1">
<summary>詳細を表示</summary>

### 迷ったらこの4ステップ

1. Add to VCC でリポジトリ追加
2. VCCで対象プロジェクトを開く
3. `おちびちゃんズ化ツール（Ochibi-chans Converter Tool）` を検索して追加
4. 2回目以降はVCCで更新

> 目安時間: **3〜5分**

---

### 1) Add to VCC でリポジトリ追加

<a class="btn btn-primary" href="https://aramaa-vr.github.io/vpm-repos/redirect.html" target="_blank" rel="noopener noreferrer">➕ Add to VCC</a>

![VCCにリポジトリ追加する画面]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/vcc-add-repo.webp" | relative_url }})
![VCCリポジトリ追加の確認画面]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/vcc-add-repo-confirm.webp" | relative_url }})

#### 「すでに追加済み」と表示された場合

`You have already added this repository. You can't add it again.` と表示される場合は、  
既にVCCに aramaa リポジトリが **追加済み** です。  
そのまま **手順2へ進んでください。**

![VCCでリポジトリが追加済みと表示された画面]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/vcc-settings-repo-added-q75.webp" | relative_url }})

---

### 2) リポジトリの確認をする

- `Settings → Packages → Installed Repositories` で、**aramaa にチェックがある**ことを確認
- （チェックがないとパッケージ一覧に表示されません）

![VCCのInstalled Repositoriesでaramaaチェックを確認する画面]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/vrcc_repo_opt_q82.webp" | relative_url }})

---

### 3) 対象プロジェクトを開く

- `Projects -> 導入したいプロジェクト` の **Manage Project** を押す
- そのまま **Manage Project 画面の Packages タブ** で、次の手順の検索を行います

![VCCでManage Projectを開く画面]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/vcc-manage-project.webp" | relative_url }})

---

### 4) `おちびちゃんズ化ツール（Ochibi-chans Converter Tool）` を検索して追加

`おちびちゃんズ化ツール（Ochibi-chans Converter Tool）` を検索し、**「＋」** を押して追加します。

![VCCでochibi-chans-converter-toolを検索して追加する画面]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/vcc-search-package.webp" | relative_url }})
![VCCで追加確認ダイアログが表示された画面]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/vcc-confirm.webp" | relative_url }})

---

### 5) 2回目以降のアップデート

導入後は、VCCのManage Project画面から更新ボタンを押すだけでアップデートできます。

![VCCでアップデートする画面]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/vcc-update.webp" | relative_url }})

</details>

## 方法2: BoothのZIP / UnityPackageで導入（非推奨） 

<details close markdown="1">
<summary>詳細を表示</summary>

[Booth](https://aramaa.booth.pm/items/7906711) からダウンロードしたパッケージをUnityに直接インポートする方法です。  
**VCCが使える環境では方法1を推奨**します。

[あらまあ素敵なショップ unitypackage 導入ガイド](https://aramaa-vr.github.io/vpm-vpai-error-test/)

</details>

## アンインストール（削除）方法

次の手順で削除してください。

### VCCで導入した場合（推奨）

1. VCCを開き、`Projects` から対象プロジェクトの **Manage Project** を開く
2. `Packages` タブで `おちびちゃんズ化ツール（Ochibi-chans Converter Tool）` を探す
3. パッケージ右側の **「-」ボタン（Remove / 削除）** を押す
4. Unityプロジェクトを開き直し、エラーがないか確認する

![VCCでアップデートする画面（同じ場所の「-」ボタンから削除可能）]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/vcc-update.webp" | relative_url }})

> 補足: 依存関係として一緒に導入したパッケージ（Modular Avatar / Floor Adjuster / lilToon）も削除されてしまう場合は、お手数おかけしますが削除後に入れなおしてください。

## うまくいかないとき（よくあるケース）

まずは以下を確認してください。

- 必要なVCCがインストールされているか
- `Settings → Packages → Installed Repositories` で、**aramaa にチェックがある**か
- VRChat SDK が **3.10.1 以上** か
- 追加先が目的のプロジェクトか（Manage Projectを開き間違えていないか）
- VCCを再起動し、Manage Projectを開き直したか

### Modular Avatar不足エラー

`Modular Avatar` が入っていない場合、エラーが発生します。  
先にModular Avatarを導入・更新してから、再度ツールを追加してください。

![Modular Avatar バージョン不足エラーの例]({{ "/assets/img/ochibi-chans-converter-tool/pages/install/ma-version-error.webp" | relative_url }})

---

## 次に読む

- [使い方]({{ "/usage/" | relative_url }})
- [トラブルシューティング]({{ "/troubleshooting/" | relative_url }})
