# S-21 ベースライン失敗メッセージ

タスク 1.1 の変更前ベースライン。採取日: 2026-08-09（JST）

実行条件:

- Unity `6000.3.19f1`
- `-batchmode -nographics -runTests -testPlatform PlayMode`
- 対象は S-21 の4件のみ。プロダクトコード・テストコードの変更前に実行。

## 結果

| テスト | 結果 | 原因系統 | 失敗メッセージ / 観測値 |
|---|---|---|---|
| `OscHeartbeatConsistencyTests.OnFixedTick_HeartbeatMissingReceiverBlendShape_LogsMismatchWarning` | Failed | gaze 未設定ログの LogAssert 未追従 | `Unhandled log message: '[Log] [OscReceiverAdapterBinding] gaze mapping が未設定のため Gaze 受信は無効です（heartbeat auto-map は gaze route を生成しません）。目線を反映するには受信側マッピングに gaze エントリ（mode=Gaze_*, expressionId, addressPattern）を明示設定してください。'` |
| `OscReceiverAdapterBindingAutoMappingIntegrationTests.HandleHeartbeat_HeartbeatHashUnchanged_DoesNotRebuildOscInputSource` | Failed | heartbeat ハッシュ期待値のハードコードずれ | `Expected: 1085723225` / `But was: 122830949`。同じ gaze 未設定ログも出力された。 |
| `OscReceiverAdapterBindingAutoMappingIntegrationTests.OnStart_EmptyMappingsAndNoHeartbeat_DoesNotRegisterOscInputSourceOrChangeRenderer` | Failed | gaze 未設定ログの LogAssert 未追従 | 上記の `gaze mapping が未設定のため...` ログに対する `LogAssert.Expect` 不足。 |
| `OscReceiverGCAllocationTests.OnFixedTick_HeartbeatHashUnchanged100Frames_ZeroGCAllocation` | Failed | heartbeat ハッシュ期待値のハードコードずれ | `Expected: 1085723225` / `But was: 133301657`。同じ gaze 未設定ログも出力された。 |

## 後続タスクへの引き継ぎ

- ログ系2件は、`LogAssert` の追加ではなく、広告駆動導入後に前提が虚偽となる gaze 未設定ログの削除・改稿で判定する。
- ハッシュ系2件は、ログ対応後も残る場合、テスト側のハードコード期待値を再計算または値非依存化して判定する。
- Unity Test Runner の XML / ログは一時採取物であり、リポジトリには記録本文のみを残す。
