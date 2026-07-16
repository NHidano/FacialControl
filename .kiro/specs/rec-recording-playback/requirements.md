# Requirements Document

## Project Description (Input)
表情操作の記録・リアルタイム再生（REC）機能。トリガー on/off + expressionId + アナログ軸値 + gaze(-1..1 Vector2) の操作イベント時系列（秒ベースタイムスタンプ）を記録の正本とし、本物の入力パイプライン（ExpressionTriggerInputSourceBase の TriggerOn/Off + アナログ/gaze 値）を駆動して収録時プレビューと同一のブレンドを完全再現するリアルタイム再生を提供する。新規 UPM パッケージ com.hidano.facialcontrol.rec として追加し、core へはイベント観測点の小改修（数行規模）のみ。永続化は profile.json 同居ではなく sidecar ファイル（StreamingAssets/FacialControl/{assetName}/ 規約流用）。SystemTextJsonParser（実体 JsonUtility）は巨大配列に弱いため REC 時系列のフォーマット選定は要検討。gaze は BlendShape 経路と別チャネル（IAnalogInputSource、push 型 Publish(x,y)、-1..1）で記録・再生とも必須スコープ。

## Introduction

本機能は、FacialControl の表情操作（トリガー on/off、アナログ軸値、gaze）を操作イベントレベルの時系列として記録し、本物の入力パイプラインを駆動するリアルタイム再生によって収録時プレビューと同一のブレンドを完全再現する REC 機能を提供する。新規 UPM パッケージ `com.hidano.facialcontrol.rec` として配布し、core への改修は操作イベントの観測面と入力ソース差し替え（注入）面の正式な追加に限定する（既存コードパスの挙動は変更しない。当初は数行規模の観測点のみを想定していたが、gap 分析でアナログ/gaze の push 集約点が core に存在しないこと・消費側が入力ソースを構築時キャッシュすることが判明し、観測バス + Replace 再バインド伝搬を core の正式な面として追加する方針に改訂した）。記録の正本は合成後の BlendShape 値ではなく操作イベントであり、タイムスタンプは秒ベース（フレーム番号駆動は存在しない）とする。

## Boundary Context

- **In scope**: 操作イベント（トリガー on/off + expressionId + アナログ軸値 + gaze）の記録、sidecar ファイルへの永続化と読み込み、本物の入力パイプラインを駆動するリアルタイム再生（イベント駆動の完全経路）、core への観測面（トリガー観測フック + アナログ/gaze 観測バス）と注入面（Replace 再バインド伝搬）の追加、gaze チャネル（-1..1 Vector2）の記録・再生
- **Out of scope**: Timeline 独自 Track、ベイク済みカーブ書き出し、スクラブ対応、人間による Timeline 編集（後続 spec `rec-timeline-baking` で扱う）。音声解析・リップシンク音源の記録（既存方針どおりスコープ外）。ランタイム UI の提供
- **Adjacent expectations**: core（`com.hidano.facialcontrol`）の既存入力パイプライン（ExpressionTriggerInputSourceBase / IAnalogInputSource / Aggregator / 遷移計算）の既存挙動は変更しない（観測・注入面の追加のみ）。既存拡張パッケージ（osc / inputsystem / lipsync / ifacialmocap）は無改修のまま REC を知らない（core の観測面経由で入力元を問わず記録される）

## Requirements

### Requirement 1: 操作イベントの記録

**Objective:** As a Unity エンジニア, I want 表情操作の入力イベントを時系列で記録したい, so that 収録したパフォーマンスを後から完全に再現できる

#### Acceptance Criteria

1. While 記録セッションが有効な間, when ExpressionTrigger の on または off イベントが発生したとき, the REC 記録サービス shall expressionId とイベント種別（on/off）を秒ベースのタイムスタンプ付きで記録する
2. While 記録セッションが有効な間, when アナログ軸値が更新されたとき, the REC 記録サービス shall 入力ソースの識別子と軸値を秒ベースのタイムスタンプ付きで記録する
3. While 記録セッションが有効な間, when gaze 値の Publish(x, y) が発生したとき, the REC 記録サービス shall gaze の x/y 値（値域 -1..1）を秒ベースのタイムスタンプ付きで記録する
4. The REC 記録サービス shall 記録の正本として操作イベントレベルのデータ（トリガー on/off + expressionId + アナログ軸値 + gaze）のみを保持する（合成後の BlendShape 値を正本としない）
5. The REC 記録サービス shall タイムスタンプを記録開始時点を起点とする秒ベースの相対時間として記録する（フレーム番号に依存しない）
6. When 記録セッションが開始されたとき, the REC 記録サービス shall 開始時点で有効な入力状態（active なトリガー・アナログ値・gaze 値）を初期状態として捕捉する

### Requirement 2: 記録セッションの制御

**Objective:** As a Unity エンジニア, I want 記録の開始・停止を明示的に制御したい, so that 必要な区間だけを安全に収録できる

#### Acceptance Criteria

1. When ユーザーが記録開始を指示したとき, the REC 記録サービス shall 新しい記録セッションを開始し、以降の操作イベントの捕捉を開始する
2. When ユーザーが記録停止を指示したとき, the REC 記録サービス shall 記録セッションを終了し、操作イベントの捕捉を停止する
3. If 記録セッションが有効な状態で記録開始が指示されたとき, the REC 記録サービス shall 二重開始を拒否し、既存セッションを継続したまま Unity 標準ログで警告する
4. While 記録セッションが停止している間, the REC 記録サービス shall 操作イベントを記録しない
5. While 記録セッションが有効な間, the REC 記録サービス shall ライブの表情出力（遷移計算・ブレンド結果）に影響を与えない（記録は観測のみ）

### Requirement 3: リアルタイム再生（ブレンドの完全再現）

**Objective:** As a Unity エンジニア, I want 記録した操作イベントを本物の入力パイプライン経由で再生したい, so that 収録時プレビューと同一のブレンドを完全再現できる

#### Acceptance Criteria

1. When 再生が開始されたとき, the REC 再生サービス shall 記録された操作イベントを本物の入力パイプライン（ExpressionTriggerInputSourceBase の TriggerOn/TriggerOff およびアナログ/gaze 値の駆動）に対して時系列どおりに発火する
2. The REC 再生サービス shall 遷移計算・レイヤー合成をライブ操作と同一のコードパスで実行させる（Timeline を経由しない、合成済み値の直接書き込みをしない）
3. When 同一の記録を同一プロファイル・同一レイヤー設定で再生したとき, the REC 再生サービス shall 収録時プレビューと同一のブレンド結果を再現する
4. When 記録イベントのタイムスタンプ（秒）に経過時間が到達したとき, the REC 再生サービス shall 該当イベントを発火する（収録時と再生時のフレームレートが異なってもイベントの順序と時刻を維持する）
5. When 再生停止が指示されたとき, the REC 再生サービス shall 停止時点の駆動中入力状態（on のままのトリガー・最終アナログ/gaze 値）をそのまま保持し、ライブ操作へシームレスに引き継げるようにする（自動解除は行わない）
6. When 再生が記録の最終イベントに到達したとき, the REC 再生サービス shall 再生を終了し、終了状態を利用側から検知可能にする
7. If 再生中に再生開始が指示されたとき, the REC 再生サービス shall 二重再生を拒否し Unity 標準ログで警告する

### Requirement 4: Gaze チャネルの記録・再生

**Objective:** As a Unity エンジニア, I want gaze（視線）を表情と一体で記録・再生したい, so that 収録時の視線を含めた表情パフォーマンス全体を再現できる

#### Acceptance Criteria

1. The REC パッケージ shall gaze を BlendShape 経路（0..1）とは別チャネル（IAnalogInputSource、push 型 Publish(x, y)）として記録・再生の必須スコープに含める
2. When 記録中に gaze の Publish(x, y) が発生したとき, the REC 記録サービス shall 値域 -1..1 のまま正規化せずに記録する
3. When 再生中に gaze イベントのタイムスタンプに到達したとき, the REC 再生サービス shall 記録時と同一の x/y 値を Publish で入力パイプラインへ駆動する
4. The REC パッケージ shall gaze の記録・再生において Vector2 の 2 軸（AxisCount == 2）を欠落なく扱う

### Requirement 5: sidecar ファイルによる永続化

**Objective:** As a Unity エンジニア, I want 記録を profile.json とは別の sidecar ファイルとして保存・読み込みしたい, so that プロファイルを汚さずに記録を管理・差し替えできる

#### Acceptance Criteria

1. The REC 永続化機能 shall 記録を `StreamingAssets/FacialControl/{assetName}/` 規約に従う sidecar ファイルとして保存する（記録中の順次ストリーミング書き出しも同規約の保存先に対して行う）
2. The REC 永続化機能 shall 記録データを profile.json に同居させない
3. When 記録セッションが停止されたとき, the REC 永続化機能 shall ストリーミング書き出し中の sidecar ファイルを完結（ファイナライズ）し、読み込み可能な状態にする
4. When 保存済み記録の読み込みが指示されたとき, the REC 永続化機能 shall sidecar ファイルから記録を復元し、再生可能な状態にする
5. If 指定された sidecar ファイルが存在しない、または解釈できないとき, the REC 永続化機能 shall Unity 標準ログでエラーを出力し、再生を開始しない
6. The REC 永続化フォーマット shall 大量の操作イベント時系列を JsonUtility の制約（巨大配列・Dictionary への弱さ）に抵触せず、かつ記録中の順次追記（ストリーミング書き出し）に適した方式で格納できる
7. The REC 永続化機能 shall Editor 上とビルド後ランタイムの両方で記録の保存・読み込みを可能にする

### Requirement 6: core への観測・注入面の追加

**Objective:** As a ライブラリ開発者, I want core に操作イベントの観測面と入力ソース差し替えの注入面を正式に追加したい, so that REC を確実に動作させつつ、後続 spec（rec-timeline-baking）や将来機能も同じ面を再利用できる

#### Acceptance Criteria

1. The core（`com.hidano.facialcontrol`）改修 shall 操作イベントの観測面と入力ソース差し替え（注入）面の追加に限定し、既存コードパスの挙動を変更しない
2. When ExpressionTriggerInputSourceBase の TriggerOn / TriggerOff が呼び出されたとき, the core shall 登録された観測者へ該当イベント（expressionId・イベント種別）を通知可能にする
3. The core shall アナログ軸値・gaze 値を登録された観測者へ通知可能にする共通の観測面を提供する（Publish 実装が拡張パッケージ側に分散しているため、core 側の共通面で観測を成立させる）
4. When 入力ソースがレジストリ上で差し替え（Replace）されたとき, the core shall 差し替え後のソースを消費側（レイヤー入力・gaze 解決）へ再バインドする（消費側の構築時キャッシュが旧ソースを読み続けないことを保証する）
5. If 観測者が 1 つも登録されておらず入力ソースの差し替えも行われていないとき, the core shall 既存の挙動・性能を一切変更しない
6. The core shall REC パッケージへの依存を持たない（core は rec を知らない）
7. The core 改修 shall 既存拡張パッケージ（osc / inputsystem / lipsync / ifacialmocap）側の改修なしで観測・注入を成立させ、これらの既存挙動を変更しない

### Requirement 7: UPM パッケージ構成

**Objective:** As a ライブラリ利用者, I want REC 機能を独立した UPM パッケージとして導入したい, so that 必要なプロジェクトだけに REC を追加できる

#### Acceptance Criteria

1. The REC 機能 shall 新規 UPM パッケージ `com.hidano.facialcontrol.rec` として提供する
2. The rec パッケージ shall 標準パッケージ構成（Runtime/Domain・Application・Adapters + Editor + Tests + Samples~）とクリーンアーキテクチャの依存方向（asmdef 強制）に従う
3. The rec パッケージ shall core（`com.hidano.facialcontrol`）のみに依存し、OSC / InputSystem パッケージへの依存を持たない
4. When core がインストール済みの環境に rec パッケージを追加したとき, the rec パッケージ shall 追加の必須設定なしに記録・再生機能を利用可能にする

### Requirement 8: 性能（GC ゼロ目標・ストリーミング記録）

**Objective:** As a Unity エンジニア, I want 記録・再生中も GC スパイクや I/O 起因の停止を発生させたくない, so that 配信・収録中のフレーム落ちや収録中断を防げる

#### Acceptance Criteria

1. While 記録セッションが有効な間, the REC 記録サービス shall 毎フレームの定常処理でヒープ確保を発生させない（事前確保バッファへの書き込みで記録する）
2. While 再生中, the REC 再生サービス shall 毎フレームの定常処理でヒープ確保を発生させない
3. The REC 記録サービス shall 記録イベントをメモリへ全量蓄積せず、別スレッドで sidecar ファイルへ順次ストリーミング書き出しする（メモリ常駐は書き出し待ちバッファの最低限に留める）
4. The REC 記録サービス shall 記録側（操作イベントを捕捉するスレッド）の処理をストレージ I/O の完了待ちでブロックさせない（I/O 遅延・スワップ等が発生しても操作イベントの捕捉を停止・欠落させない）
5. If 書き出し待ちバッファが飽和したとき, the REC 記録サービス shall イベントを欠落させずに記録を継続する（この場合に限り一時的なヒープ確保による拡張を許容する）
6. The REC パッケージ shall 保存・読み込み等の非毎フレーム処理を除き、時間計測・イベント駆動を GC アロケーションなしで行う

### Requirement 9: エラーハンドリングと記録の検証

**Objective:** As a Unity エンジニア, I want 不整合な記録を再生してもシステムが破綻しないでほしい, so that プロファイル変更後も安心して過去の記録を扱える

#### Acceptance Criteria

1. If 記録が現在のプロファイルに存在しない（削除済みの）expressionId を参照しているとき, the REC 再生サービス shall 再生全体を停止させずに該当イベントを安全に扱い、Unity 標準ログでユーザーへ通知する
2. When 記録の読み込みが完了したとき, the REC パッケージ shall 記録が参照する expressionId と現在のプロファイルとの整合性を検証可能にする
3. The REC パッケージ shall エラー・警告の通知に Unity 標準ログ（Debug.Log/Warning/Error）のみを使用し、標準例外を超えるカスタム例外型を追加しない

## Open Questions（設計フェーズで決定する残論点）

1. **sidecar フォーマットの選定**: JSON DTO かバイナリか（Requirement 5.6 の制約を満たす方式を設計フェーズで決定する。順次追記＝ストリーミング書き出しに適することが必須条件）
2. **削除済み expressionId を参照する記録の検証 UX**: スキップ + 警告の粒度、事前検証 API の提供形態など（Requirement 9.1 / 9.2 の詳細）
