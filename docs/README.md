# おちびちゃんズ化ツール（Ochibi-chans Converter Tool） Docs

このフォルダは おちびちゃんズ化ツール（Ochibi-chans Converter Tool） のドキュメントサイト (GitHub Pages) 用の Jekyll ソースです。

## ローカルで表示を確認する

### 1) Ruby/Jekyll が使える場合

```bash
bundle install
bundle exec jekyll serve --livereload
```

### 1-b) 付属スクリプトで起動する場合（推奨）

```bash
./serve_local.sh
```

- `Gemfile` の位置を自動判定して `bundle install` を実行後、`jekyll serve` を起動します。
- `--baseurl=""` を指定して起動するため、ローカル確認時にパス崩れが起きにくくなります。

### 2) Docker で確認する場合

```bash
./build-docs.sh
```

上記コマンドは、`../.docs-site` に静的サイトを出力します。
必要に応じて `python -m http.server` などでプレビューしてください。
