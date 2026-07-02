# Phoneme Overlay Slots 移行ガイド

このガイドは、`phoneme-overlay-slots` spec に基づき、uLipSync の A / I / U / E / O 出力を旧 `lipsync` Layer 直書きから Expression Overlay 経由へ移行する手順をまとめる。

preview 段階の破壊的変更として扱うため、自動マイグレーションは提供しない。既存 `FacialCharacterProfileSO`、同梱 sample、`StreamingAssets/FacialControl/**/*.json` はこの手順に沿って手動で更新する。

## 1. Slots に phoneme 予約名を宣言する

`FacialCharacterProfileSO.Slots` に以下の 5 slot を追加する。

| Slot | 対応する uLipSync phoneme |
|------|---------------------------|
| `a` | A |
| `i` | I |
| `u` | U |
| `e` | E |
| `o` | O |

Inspector に `Phoneme slots を初期化 (a/i/u/e/o)` ボタンがある場合は、そのボタンで未登録の予約名だけを追加する。手動編集する場合は小文字の `a` / `i` / `u` / `e` / `o` をそのまま使い、大文字 (`A` など) や別名 (`aa`、`silence` など) は使わない。

この spec の対象は 5 予約名のみである。custom phoneme set、言語別 phoneme、追加母音、子音 slot は out-of-scope とし、必要になった場合は別 spec で扱う。

## 2. Expression overlay を編集する

各 Expression の `overlays` に、必要な phoneme slot の `OverlaySlotBinding` を追加する。

- Override: `slot` に `a` / `i` / `u` / `e` / `o` のいずれかを指定し、`AnimationClip` または bake 済み `cachedSnapshot` を設定する。Expression が有効な間、その slot の口形状 snapshot が uLipSync 既定の snapshot の**代わりに**使われる（差し替え）。駆動 weight（音素 weight × 音量）は既定出力と同じ値を使い回すため、無音・当該音素が検出されないフレームでは override 中でも何も出力されない。静的なオーバーレイ出力（表情中に snapshot が常時 100% 出力される挙動）にはならない。
- Suppress: `Suppress` を有効にする。Expression が有効な間、その slot は何も出力しない。
- Default fallback: `Suppress = false` かつ snapshot 未設定のままにする、または該当 slot の overlay entry を作らない。この場合は `ULipSyncAdapterBinding` の `BlendShapePhonemeEntry` / `AnimationClipPhonemeEntry` が従来どおり既定出力として使われる。

`Suppress = true` と snapshot が同時に存在する場合は validation warning の対象になり、runtime は `Suppress = true` を優先する。これは `overlay-clip-redesign` で確立した 3 状態 Overlay モデルに合わせる。

## 3. Layer.inputSources を多重化する

旧構成では `lipsync` Layer が `ulipsync` などの単一入力を直接参照していた。移行後は phoneme slot ごとに `overlay:{slot}` と `lipsync-overlay:{slot}` を `overlay` Layer に並べ、Expression 側の overlay と uLipSync 既定出力を同じ解決点へ集約する。

```json
{
  "layers": [
    {
      "name": "overlay",
      "priority": 10,
      "exclusionMode": "blend",
      "inputSources": [
        { "id": "overlay:a", "weight": 1.0 },
        { "id": "lipsync-overlay:a", "weight": 1.0 },
        { "id": "overlay:i", "weight": 1.0 },
        { "id": "lipsync-overlay:i", "weight": 1.0 },
        { "id": "overlay:u", "weight": 1.0 },
        { "id": "lipsync-overlay:u", "weight": 1.0 },
        { "id": "overlay:e", "weight": 1.0 },
        { "id": "lipsync-overlay:e", "weight": 1.0 },
        { "id": "overlay:o", "weight": 1.0 },
        { "id": "lipsync-overlay:o", "weight": 1.0 }
      ]
    }
  ],
  "slots": ["a", "i", "u", "e", "o"]
}
```

旧 `lipsync` Layer に残っている `ulipsync` input source は削除する。`ULipSyncAdapterBinding` 自体と phoneme entry は削除しない。`BlendShapePhonemeEntry` / `AnimationClipPhonemeEntry` は、Override や Suppress が無い場合の default fallback として使われる。

## 4. 旧 lipsync Layer 互換動作と warning の解釈

preview 移行期間中、外部 binding や古い profile により同じ Layer に旧 slug `ulipsync` が残っている場合、起動時に `Debug.LogWarning` が 1 回出力されることがある。

この warning は「同じ phoneme BlendShape に旧 `lipsync` 直書き経路と新 `overlay` 経路が同時に作用する可能性がある」ことを示す。warning 内に slot 名や migration step が含まれる場合は、該当 slot の `Slots` 宣言、Expression overlay、`Layer.inputSources` を確認し、旧 `ulipsync` 参照を撤去する。

warning を消すために `ULipSyncAdapterBinding` を削除してはいけない。削除すべきなのは旧 Layer / input source 参照であり、binding は `lipsync-overlay:{slot}` の default fallback 供給元として残す。

## 5. Precedence と実装乖離の記録欄

設計上の優先順は以下のとおり。

1. Expression の phoneme Override
2. Expression の phoneme Suppress
3. Expression または Profile の default overlay fallback
4. `ULipSyncAdapterBinding` の default phoneme output

実装が `.kiro/specs/phoneme-overlay-slots/design.md` の precedence と異なる場合は、以下に rationale を追記する。

| 日付 | 対象実装 | design.md との差分 | rationale | 追跡 issue / spec |
|------|----------|--------------------|-----------|-------------------|
| 2026-07-03 | `LipSyncPhonemeOverlayInputSource` / `OverlayInputSource` | design.md は「`[overlay:{slot}, lipsync-overlay:{slot}]` の並び順で Aggregator が LastWins 相当に preempt する」とするが、Aggregator は加重和のみで並び順 preemption を持たず、`OverlayInputSource` の静的出力では表情中に snapshot が常時 100% 出力されてしまう | Override の意図は「音素の口形状 snapshot の差し替え（駆動 weight = 音素 weight × 音量は既定出力と同じ値を使い回す）」。差し替え合成を `LipSyncPhonemeOverlayInputSource` 側（Override/Suppress/DefaultOverlays の解決込み）へ実装し、予約音素 slot の `OverlayInputSource` は不活性化した（`overlay:{slot}` のレイヤー宣言は互換のため残置可） | 本ガイド §2 / `PhonemeOverlayPreemptionTests` |

## 6. 関連資料

- origin: [`docs/backlog.md` Backlog S-17](../../../../docs/backlog.md)
- spec: [`.kiro/specs/phoneme-overlay-slots/`](../../../../.kiro/specs/phoneme-overlay-slots/)
- 3 状態 Overlay モデル: [`.kiro/specs/overlay-clip-redesign/design.md`](../../../../.kiro/specs/overlay-clip-redesign/design.md)
- Adapter Binding 全体移行: [`com.hidano.facialcontrol/Documentation~/migration-guide.md`](../../com.hidano.facialcontrol/Documentation~/migration-guide.md)
