# OpenUPM 登録手順

## 概要

FacialControl パッケージを OpenUPM に登録するための設定ファイルと手順書。

## 前提条件

1. GitHub リポジトリ `NHidano/FacialControl` が公開されていること
2. GitHub リポジトリ `NHidano/uOsc` が公開されていること
3. 各リポジトリに SemVer 準拠のタグ（`v0.1.0-preview.1` 等）が付与されていること

## 登録順序

**uOsc を先に登録する必要がある**（FacialControl が依存するため）。

### 1. uOsc フォーク（com.hidano.uosc）の登録

#### リリース確認チェックリスト

- [ ] `NHidano/uOsc` リポジトリが GitHub 上で公開されている
- [ ] ルートまたはサブディレクトリに有効な `package.json` が存在する
  - `"name": "com.hidano.uosc"`
  - `"version": "1.0.0"` 以上
- [ ] MIT ライセンスファイルが含まれている
- [ ] Git タグ `v1.0.0` が作成されている
- [ ] タグのコミットで Unity プロジェクトがコンパイル可能

#### 登録手順

1. [openupm/openupm](https://github.com/openupm/openupm) リポジトリをフォーク
2. `data/packages/com.hidano.uosc.yml` に本ディレクトリの `com.hidano.uosc.yml` をコピー
3. PR を作成（タイトル: `Add package com.hidano.uosc`）
4. OpenUPM チームのレビューを待つ
5. マージ後、`https://openupm.com/packages/com.hidano.uosc/` で公開を確認

### 2. FacialControl（com.hidano.facialcontrol）の登録

#### リリース確認チェックリスト

- [ ] `NHidano/FacialControl` リポジトリが GitHub 上で公開されている
- [ ] `FacialControl/Packages/com.hidano.facialcontrol/package.json` が有効
  - `"name": "com.hidano.facialcontrol"`
  - `"version": "0.1.0-preview.1"`
- [ ] MIT ライセンスファイルが含まれている
- [ ] Git タグ `v0.1.0-preview.1` が作成されている
- [ ] 依存パッケージ `com.hidano.uosc` が OpenUPM に登録済み
- [ ] CI テスト（EditMode / PlayMode）が通過している

#### 登録手順

1. [openupm/openupm](https://github.com/openupm/openupm) リポジトリをフォーク（uOsc で使用済みなら同じフォークを更新）
2. `data/packages/com.hidano.facialcontrol.yml` に本ディレクトリの `com.hidano.facialcontrol.yml` をコピー
3. PR を作成（タイトル: `Add package com.hidano.facialcontrol`）
4. OpenUPM チームのレビューを待つ
5. マージ後、`https://openupm.com/packages/com.hidano.facialcontrol/` で公開を確認

## ユーザー側のインストール方法

### OpenUPM CLI 経由

```bash
openupm add com.hidano.facialcontrol
```

### manifest.json 手動編集

```json
{
    "scopedRegistries": [
        {
            "name": "OpenUPM",
            "url": "https://package.openupm.com",
            "scopes": [
                "com.hidano"
            ]
        }
    ],
    "dependencies": {
        "com.hidano.facialcontrol": "0.1.0-preview.1"
    }
}
```

`com.hidano` スコープにより、`com.hidano.uosc` も自動的に OpenUPM レジストリから解決される。

## ファイル一覧

| ファイル | 説明 |
|---------|------|
| `com.hidano.facialcontrol.yml` | FacialControl パッケージの OpenUPM 登録用 YAML |
| `com.hidano.uosc.yml` | uOsc フォークの OpenUPM 登録用 YAML |
| `README.md` | 本ドキュメント |
