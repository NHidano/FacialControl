# Spec 1 実装完了ゲートとテストベースライン

採取日: 2026-08-28 (JST)

## 前提ゲート

Spec 1 (`osc-gaze-auto-mapping`) の必須成果はすべてコードベースに存在することを確認した。

| 成果 | 確認位置 |
|---|---|
| 広告解決 | `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/GazeAdvertisementResolver.cs` |
| id 合成 helper | `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/ScriptableObject/GazeBindingConfigResolver.cs` (`ComposeSourceId`) |
| 受信側 GazeConfig 注入 Configure | `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscReceiverAdapterBinding.cs` (`Configure(IReadOnlyList<GazeBindingConfig>)`) |
| gaze 読取共通実装 | `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/InputSources/GazeInputReader.cs` |

Unity のプロジェクトバージョンは `6000.3.19f1` である。

## 変更前ベースライン

プロダクトコード・テストコードを変更せず、`D:/UnityEditors/6000.3.19f1/Editor/Unity.exe` の Unity Test Runner で採取した。

| テスト基盤 | 結果 |
|---|---:|
| EditMode | 2112 total / 2108 passed / 1 failed / 3 skipped |
| PlayMode | 429 total / 425 passed / 4 failed |

### 失敗の分類

- M-28 の pre-existing 赤: `SampleAssetsAreInSyncTests.ProfileJson_DevStreamingAssetsAndPackageSample_AreByteIdentical` (EditMode, 1件)。MultiSourceBlendDemo の `profile.json` drift。
- 既知フレーキー: `TenCharacterIsolationTests.TenIndependentBindings_OneSwap_DoesNotAffectOthers` (PlayMode, 1件)。
- S-21 系: PlayMode 3件が失敗。
  - `OscHeartbeatConsistencyTests.OnFixedTick_HeartbeatMissingReceiverBlendShape_LogsMismatchWarning`: 期待されない `[OscInputSource]` ログ。
  - `OscReceiverAdapterBindingAutoMappingIntegrationTests.HandleHeartbeat_HeartbeatHashUnchanged_DoesNotRebuildOscInputSource`: heartbeat hash の期待値 `1085723225` と実測値の不一致。
  - `OscReceiverGCAllocationTests.OnFixedTick_HeartbeatHashUnchanged100Frames_ZeroGCAllocation`: heartbeat hash の期待値 `1085723225` と実測値の不一致。

## 判定と引き継ぎ

前提シンボルは揃っているため実装着手条件は満たす。一方、S-21 系4件の全件緑という完了ゲートは満たさない（今回の実行では対象3件が失敗、残る1件は成功）。S-21 のログ期待値・hash 期待値の不一致を Spec 1 の分岐手順に従って後続で切り分けるまで、Spec 2 の実装完了判定は保留とする。

以降の FAIL 判定から除外できる既知の pre-existing 赤は、M-28 の1件と既知フレーキーの1件である。

