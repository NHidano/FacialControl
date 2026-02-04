# npmjs.com 公開手順

## 概要

FacialControl パッケージを npmjs.com に公開するための手順書。

## 前提条件

1. npmjs.com アカウントを作成済みであること
2. `npm login` で認証済みであること
3. GitHub リポジトリ `NHidano/FacialControl` が公開されていること
4. GitHub リポジトリ `NHidano/uOsc` が公開されていること
5. 各リポジトリに SemVer 準拠のタグ（`v0.1.0-preview.1` 等）が付与されていること

## 公開順序

**uOsc を先に公開する必要がある**（FacialControl が依存するため）。

### 1. uOsc フォーク（com.hidano.uosc）の公開

#### リリース確認チェックリスト

- [ ] `NHidano/uOsc` リポジトリが GitHub 上で公開されている
- [ ] ルートまたはサブディレクトリに有効な `package.json` が存在する
  - `"name": "com.hidano.uosc"`
  - `"version": "1.0.0"` 以上
- [ ] MIT ライセンスファイルが含まれている
- [ ] Git タグ `v1.0.0` が作成されている
- [ ] タグのコミットで Unity プロジェクトがコンパイル可能

#### 公開手順

```bash
# uOsc の package.json があるディレクトリに移動
cd <uOsc-package-dir>

# パッケージを公開
npm publish
```

公開後、`https://www.npmjs.com/package/com.hidano.uosc` で確認。

### 2. FacialControl（com.hidano.facialcontrol）の公開

#### リリース確認チェックリスト

- [ ] `NHidano/FacialControl` リポジトリが GitHub 上で公開されている
- [ ] `FacialControl/Packages/com.hidano.facialcontrol/package.json` が有効
  - `"name": "com.hidano.facialcontrol"`
  - `"version": "0.1.0-preview.1"`
- [ ] MIT ライセンスファイルが含まれている
- [ ] Git タグ `v0.1.0-preview.1` が作成されている
- [ ] 依存パッケージ `com.hidano.uosc` が npmjs.com に公開済み
- [ ] CI テスト（EditMode / PlayMode）が通過している

#### 公開手順

```bash
# FacialControl の package.json があるディレクトリに移動
cd FacialControl/Packages/com.hidano.facialcontrol

# パッケージを公開
npm publish
```

公開後、`https://www.npmjs.com/package/com.hidano.facialcontrol` で確認。

## ユーザー側のインストール方法

### manifest.json 手動編集

```json
{
    "scopedRegistries": [
        {
            "name": "npmjs",
            "url": "https://registry.npmjs.org",
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

`com.hidano` スコープにより、`com.hidano.uosc` も自動的に npm レジストリから解決される。

## ファイル一覧

| ファイル | 説明 |
|---------|------|
| `README.md` | 本ドキュメント |
