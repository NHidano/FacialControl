# Requirements Document

## Project Description (Input)
REC 記録（操作イベント時系列）を Unity Timeline 上で編集・再生可能にする独自 Track + ベイク書き出し機能。標準 AnimationTrack は使わず（クリップ重なりブレンドが線形固定でカスタムカーブ・「遷移中の再トリガーは現在値から開始」を表現できないため）、独自 Track の mixer が「もう一つの入力アダプター」として本物の入力パイプライン（TriggerOn/Off + アナログ/gaze 値）を駆動し、遷移計算をライブ操作と同一コードパスにする。スクラブ/ランダムアクセス対応はベイク済みカーブ併用: ベイク粒度はソース単位（遷移補間済み・レイヤー合成前、post-blend ではない。ライブのリップシンク等とレイヤー合成で共存させるため）。値はベイクカーブから、active 表情状態はイベント列から並行駆動する（音素 override/suppress 等の active 依存挙動のため。イベント側は値を出力しない）。ベイクは Aggregator 側の観測フック（Editor オフライン時のみ）で焼く。人間による Timeline 編集（クリップ移動・差し替え）が前提で、編集後の正本は Timeline のクリップ列に移り、ベイクは「クリップ列 + プロファイル」から再シミュレーションで再生成。陳腐化はハッシュで検知（自動再ベイク + ランタイム不一致警告）。gaze は正規化 Vector2(-1..1) のまま Keyframe カーブ化し、独自 Track 経由でランタイム gaze 解決パイプラインに流す（リグ非依存・プロファイル追従）。backlog M-1（Timeline 統合）と関連。rec-recording-playback spec（REC 記録+リアルタイム再生）の後続・依存 spec。

## Introduction

本機能は、REC 記録（操作イベント時系列）を Unity Timeline 上のクリップ列として編集・再生可能にする Timeline 統合機能を提供する。標準 AnimationTrack ではなく独自 Track を提供し、その mixer が「もう一つの入力アダプター」として本物の入力パイプラインを駆動することで、遷移計算（カスタムカーブ・「遷移中の再トリガーは現在の補間値から開始」）をライブ操作と同一コードパスで完全再現する。スクラブ・ランダムアクセス（ライブ本番中のキュー点ジャンプを含む）に対しては、Editor オフラインで生成するベイク済みカーブを併用し、「値はベイクカーブから・active 表情状態はイベント列から」の並行駆動でランタイム再生する。

本 spec は backlog M-1（Timeline 統合は将来対応 / requirements-qa.md Q9-1）の実体であり、`rec-recording-playback` spec（REC 記録 + Timeline 非経由のリアルタイム再生）の後続・依存 spec である。

## Boundary Context

- **In scope**: 独自 Timeline Track と mixer（入力パイプライン駆動）、REC 記録からクリップ列への書き出し、人間による Timeline 編集（クリップ移動・差し替え・調整）と正本のクリップ列への移行、ベイク済みカーブの生成（ソース単位・Editor オフライン・再シミュレーションによる再生成）、ベイク値 + 状態イベント並行駆動のランタイム再生、スクラブ/ランダムアクセス対応、ハッシュによる陳腐化検知（自動再ベイク + ランタイム不一致警告）、gaze の Timeline 統合（正規化 Vector2 カーブ）
- **Out of scope**: 操作イベントの記録機構、core へのイベント観測点追加、sidecar 永続化、Timeline 非経由のリアルタイム再生（以上は先行 spec `rec-recording-playback` で扱う）。音声解析・リップシンク音源の記録（既存方針どおりスコープ外）。ランタイム UI の提供
- **Adjacent expectations**: `rec-recording-playback` が生成する REC 記録データ（操作イベント時系列）を入力とする。core（`com.hidano.facialcontrol`）の既存入力パイプライン・遷移計算・レイヤー合成は変更しない（独自 Track は入力アダプターとして追加される側）。`com.unity.timeline` 1.8.9 は既存依存に含まれる

## Requirements

### Requirement 1: 独自 Timeline Track による入力パイプライン駆動

**Objective:** As a Unity エンジニア, I want REC 記録由来の表情操作を Unity Timeline の独自 Track で再生したい, so that ライブ操作と同一の遷移計算でタイムライン演出を制御できる

#### Acceptance Criteria

1. The Timeline 統合機能 shall 標準 AnimationTrack ではなく、表情トリガー・アナログ値・gaze を扱う独自 Track を提供する
2. While Timeline 再生中, the 独自 Track の mixer shall 「もう一つの入力アダプター」として本物の入力パイプライン（ExpressionTrigger の TriggerOn/TriggerOff およびアナログ/gaze 値の駆動）を駆動する
3. The Timeline 統合機能 shall 遷移計算（カスタムカーブ・イージング・「遷移中の再トリガーは現在の補間値から開始」）をライブ操作と同一のコードパスで実行させる
4. The Timeline 統合機能 shall 合成済み BlendShape 値を出力先へ直接書き込まない（レイヤー合成は既存パイプラインに委ねる）
5. When 同一のクリップ列を同一プロファイル・同一レイヤー設定で先頭から線形再生したとき, the Timeline 再生機能 shall ライブ操作で同一イベント列を与えた場合と同一のブレンド結果を再現する

### Requirement 2: REC 記録からのクリップ列書き出し

**Objective:** As a Unity エンジニア, I want REC 記録を Timeline のクリップ列として書き出したい, so that 収録したパフォーマンスをタイムライン上で編集できる

#### Acceptance Criteria

1. When ユーザーが REC 記録の Timeline 書き出しを指示したとき, the Timeline 書き出し機能 shall 操作イベント時系列（トリガー on/off + expressionId + アナログ軸値 + gaze）を独自 Track 上のクリップ列へ変換する
2. The Timeline 書き出し機能 shall トリガー on/off の対を、開始時刻・終了時刻・expressionId を持つ表情クリップとして表現する
3. The Timeline 書き出し機能 shall アナログ軸値および gaze の連続値時系列を Keyframe カーブを持つクリップとして表現する
4. When 書き出しが完了したとき, the Timeline 書き出し機能 shall 生成物を Unity 標準の Timeline ウィンドウで編集可能な TimelineAsset として保存する
5. If 書き出し対象の REC 記録が現在のプロファイルに存在しない expressionId を参照しているとき, the Timeline 書き出し機能 shall 書き出し全体を失敗させずに該当イベントを安全に扱い、Unity 標準ログでユーザーへ通知する

### Requirement 3: 人間による Timeline 編集と正本の移行

**Objective:** As a Unity エンジニア, I want 書き出したクリップを Timeline 上で移動・差し替え・調整したい, so that 収録後にタイムライン演出を作り込める

#### Acceptance Criteria

1. The Timeline 統合機能 shall クリップの移動・長さ変更・削除・expressionId の差し替えを Unity 標準の Timeline 編集操作で行えるようにする
2. When Timeline 上のクリップ列が編集されたとき, the Timeline 統合機能 shall 編集後のクリップ列を正本として扱う（REC 原本のイベント列を正本とし続けない）
3. If クリップが現在のプロファイルに存在しない（削除済みの）expressionId を参照しているとき, the Timeline 統合機能 shall 再生・ベイクを破綻させずに該当クリップを安全に扱い、Unity 標準ログでユーザーへ通知する
4. If REC 原本からの再書き出しが人間による編集済みのクリップ列を上書きしようとしたとき, the Timeline 書き出し機能 shall 無警告での上書きを行わない

### Requirement 4: ベイク済みカーブの生成（ソース単位・Editor オフライン）

**Objective:** As a Unity エンジニア, I want クリップ列とプロファイルからベイク済みカーブを生成したい, so that スクラブ・ランダムアクセスでも決定的な表情値を得られる

#### Acceptance Criteria

1. The ベイク機能 shall 遷移補間済み・レイヤー合成前のソース単位の値をベイク済みカーブとして生成する（レイヤー合成後の post-blend 値をベイクしない）
2. The ベイク機能 shall ベイク値の取得点を Aggregator（LayerInputSourceAggregator）側の観測フックとする（post-blend の出力バスからは取得しない）
3. The ベイク機能 shall ベイク処理を Editor のオフライン処理としてのみ実行する（ランタイムでベイクを実行しない）
4. When ベイクが実行されたとき, the ベイク機能 shall 正本であるクリップ列とプロファイルからの再シミュレーションによりカーブを生成する（REC 原本イベント列からの直接ベイクを正とし続けない）
5. The ベイク機能 shall 同一のクリップ列・同一のプロファイルから決定的に同一のベイク済みカーブを生成する

### Requirement 5: ランタイム Timeline 再生（ベイク値 + 状態イベント並行駆動）

**Objective:** As a Unity エンジニア, I want ライブ本番中に Timeline をスクラブ・キュー点ジャンプ込みで再生したい, so that どの再生位置からでも破綻なく表情演出を出せる

#### Acceptance Criteria

1. While ランタイムで Timeline を再生している間, the Timeline 再生機能 shall 値をベイク済みカーブから供給し、active 表情状態をイベント列から並行駆動する
2. The Timeline 再生機能 shall 状態イベント列から値を出力しない（値の供給元はベイク済みカーブのみとする）
3. When 再生位置が任意の時刻へ移動（スクラブ・キュー点ジャンプ）したとき, the Timeline 再生機能 shall 移動先時刻の値と active 表情状態を再シミュレーションなしで確定する
4. While Timeline 再生中, the Timeline 再生機能 shall active 表情状態に依存する挙動（音素 override / suppress 等）をライブ操作時と同一に機能させる
5. While Timeline 再生中, the Timeline 再生機能 shall ライブの他入力（リップシンク等）とのレイヤー合成を既存の合成パイプラインで共存させる
6. Where Editor の Timeline ウィンドウでスクラブしたとき, the Timeline 統合機能 shall スクラブ位置に対応する表情プレビューを提示する

### Requirement 6: ベイク陳腐化のハッシュ検知と再ベイク

**Objective:** As a Unity エンジニア, I want 正本（クリップ列 + プロファイル）とベイクの不一致を自動検知したい, so that 編集後の古いベイクを本番で再生してしまう事故を防げる

#### Acceptance Criteria

1. When ベイクが生成されたとき, the ベイク機能 shall 正本（クリップ列 + プロファイル）から導出したハッシュをベイク成果物に記録する
2. When Editor 上でクリップ列またはプロファイルの変更によりハッシュ不一致を検知したとき, the ベイク機能 shall 自動再ベイクを実行する
3. If ランタイム再生時に正本とベイク成果物のハッシュ不一致を検知したとき, the Timeline 再生機能 shall Unity 標準ログで警告した上で再生を継続する（本番中の再生を停止させない）
4. If ベイク成果物が存在しない状態でランタイム再生が開始されたとき, the Timeline 再生機能 shall Unity 標準ログでユーザーへ通知する

### Requirement 7: Gaze の Timeline 統合（リグ非依存）

**Objective:** As a Unity エンジニア, I want gaze を表情と一体で Timeline 編集・再生したい, so that リグ構成に依存せず視線演出をプロファイル追従で再現できる

#### Acceptance Criteria

1. The Timeline 統合機能 shall gaze を正規化 Vector2（値域 -1..1）のまま Keyframe カーブとして独自 Track 上で扱う
2. While Timeline 再生中, the Timeline 再生機能 shall gaze カーブの値をランタイムの gaze 解決パイプラインへ流す（解決後のボーン回転を直接出力しない）
3. The ベイク機能 shall gaze のベイクにおいて解決後のボーン回転を焼かず、正規化 Vector2 の値のみを保持する
4. When プロファイルまたはリグ構成が変更されたとき, the Timeline 再生機能 shall gaze の再生結果を変更後のプロファイル・リグの gaze 解決に追従させる

### Requirement 8: 性能（GC ゼロ）と時間管理

**Objective:** As a Unity エンジニア, I want Timeline 再生中も GC スパイクを発生させたくない, so that 配信・本番中のフレーム落ちを防げる

#### Acceptance Criteria

1. While ランタイムで Timeline を再生している間, the Timeline 再生機能 shall 毎フレームの定常処理でヒープ確保を発生させない
2. The Timeline 統合機能 shall 時間管理を既存の時間規約（deltaTime 累積、絶対時刻は ITimeProvider.UnscaledTimeSeconds）と整合させ、独自の絶対時刻取得手段を追加しない
3. Where ベイク処理（Editor オフライン）を実行するとき, the ベイク機能 shall ランタイム GC ゼロ制約の対象外としてヒープ確保を許容する

### Requirement 9: 既存パイプラインとの統合制約

**Objective:** As a ライブラリ開発者, I want Timeline 統合を既存アーキテクチャの正道に沿って追加したい, so that デッドコードへの相乗りや既存利用者への影響なく機能を提供できる

#### Acceptance Criteria

1. The Timeline 統合機能 shall 入力パイプラインへの接続に現行の正道である AdapterBindingBase 継承 + [FacialAdapterBinding] 方式を使用する
2. The Timeline 統合機能 shall デッドコードである PlayableGraph / OscReceiverPlayable 経路に依存しない（相乗りしない）
3. The Timeline 統合機能 shall core（`com.hidano.facialcontrol`）の既存入力パイプライン・遷移計算・レイヤー合成のコードパスを変更しない
4. The Timeline 統合機能 shall `com.unity.timeline`（1.8.9 系）への依存を Timeline 統合を導入した利用者にのみ課す（Timeline を使わない core / rec 利用者へ依存を波及させない）
5. The Timeline 統合機能 shall 先行 spec `rec-recording-playback` の成果物（REC 記録データ）を入力として受け取れる

## Open Questions（設計フェーズで決定する残論点）

1. **遷移時間の所有権**: クリップの遷移時間はプロファイル値の read-only 参照とするか、per-clip での上書きを許可するか
2. **削除済み expressionId を参照するクリップの検証 UX**: 警告の粒度・事前検証の提供形態など（Requirement 2.5 / 3.3 の詳細）
3. **連続値クリップの Keyframe 直接編集の許容範囲**: アナログ/gaze クリップの Keyframe をどこまで人間が直接編集できるようにするか
4. **REC 原本からの再書き出し運用**: 編集済みクリップ列を上書きしない具体的な運用（別 Track/Asset 生成、明示確認ダイアログ等。Requirement 3.4 の詳細）
5. **パッケージ配置**: `com.hidano.facialcontrol.rec` 内に含めるか、Timeline 依存を分離した新規パッケージとするか（Requirement 9.4 の実現形態）
