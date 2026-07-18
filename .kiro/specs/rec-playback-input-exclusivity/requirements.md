# Requirements Document

## Project Description (Input)
REC 再生中の全入力排他（rec-playback-input-exclusivity）。REC 機能（com.hidano.facialcontrol.rec）の再生中、トリガー on/off は RecTriggerInjector がライブと同一の ExpressionTriggerInputSourceBase インスタンスを直接駆動する方式（既存 spec rec-recording-playback design.md:302/798 の「差し替えない」設計判断）のため、再生中のコントローラ等のライブ操作が混ざる。ライブの ExpressionInputSourceAdapter は sink をフィールド直参照で押すため registry Replace ではライブトリガーを遮断できず、core の ExpressionTriggerInputSourceBase へのライブ入力遮断ゲート新設が必須。改修方針: (1) core にゲート SuspendTriggerInput/ResumeTriggerInput（冪等 bool、Resume はスタック不変で Req 3.5 のシームレス引き継ぎ成立）+ ゲート迂回の注入面 InjectTriggerOn/Off（観測者へ通知し再生中の記録を維持。ゲート中のライブ TriggerOn/Off は無視かつ観測者非通知。ResetToExpressionStack はゲート非適用。未使用時は bool 分岐 1 個で Req 6.5 の挙動・性能不変を充足）を追加。(2) rec の ITriggerInjectionPort を IAnalogInjectionPort と対称の BeginInjection(baseline)/InjectTriggerOn/InjectTriggerOff/EndInjection 形に再形成（EstablishBaseline は BeginInjection に吸収）。(3) PlaybackUseCase は Start で trigger→analog の順に BeginInjection、Stop で trigger→analog の順に EndInjection。自然完了（Completed）では解放せず StopPlayback を唯一の解放点とする（アナログ既存挙動と対称）。(4) アナログ側の残余ギャップも塞ぐ: RecAnalogInjector の Replace 対象を registry 登録済みの全アナログソースに拡大し、記録ベースライン外のソース（記録後に追加されたデバイス等）も再生中は遮断する（ベースライン外ソースの seed 値の扱い＝現在消費値で凍結 vs 0 埋めは design フェーズで決定する論点）。排他は常時有効とし on/off オプションは設けない。既知制限として文書化: 再生中に新規登録されたソースは対象外（開始時スナップショット方式）、timeline パッケージ経由の TriggerOn/Off も遮断される、ExpressionInputSourceAdapter の Toggle バインドは遮断中も entry.IsActive が内部反転し続けるため再生後に空押し再同期が要る場合がある（拡張パッケージ無改修の制約下で許容）。制約: core は rec を知らない（Req 6.6）、拡張パッケージ（inputsystem/osc/lipsync/ifacialmocap）無改修（Req 6.7）、core 改修は観測面・注入面の追加に限定（Req 6.1）、毎フレーム GC ゼロ維持。テストは全て Fake のみで EditMode 配置、TDD 厳守。

## Introduction

本機能は、REC（`com.hidano.facialcontrol.rec`）の再生中に、コントローラ等のライブ入力（トリガー on/off・アナログ軸値・gaze）が再生結果へ混入しないようにする入力排他を追加する。既存 spec rec-recording-playback の設計判断（design.md:302/798「トリガーは入力ソースを差し替えず、ライブと同一の原本インスタンスを直接駆動する」）は原本直接駆動という点では維持しつつ、「差し替えないためライブ入力も素通しになる」という帰結を本 spec で上書きし、core にライブ入力の遮断面と、遮断を迂回する再生駆動用の注入面を追加する。あわせてアナログ / gaze 側の残余ギャップ（記録ベースライン外のソースが遮断されない問題）も塞ぐ。排他は常時有効であり、有効/無効を切り替えるオプションは設けない。

## Boundary Context

- **In scope**: 再生中のライブトリガー入力の遮断（core への遮断面の新設）、遮断を迂回する再生駆動用のトリガー注入面（観測者通知あり＝再生中の記録維持）、rec 側トリガー注入ポートのアナログ注入ポートと対称なライフサイクルへの再形成、アナログ / gaze 遮断対象の registry 登録済み全ソースへの拡大（記録ベースライン外ソースを含む）、排他の確立・解放ライフサイクル（StopPlayback を唯一の解放点とする）、既知制限の文書化
- **Out of scope**: 排他の on/off オプション（常時有効のため設けない）、拡張パッケージ（inputsystem / osc / lipsync / ifacialmocap / timeline）の改修、再生中に新規登録された入力ソースの遮断（開始時スナップショット方式の既知制限）、Toggle バインドの内部状態（IsActive）の自動再同期、記録機能・永続化フォーマット自体の変更
- **Adjacent expectations**: rec-recording-playback Req 3.5（停止時のシームレスなライブ引き継ぎ）と Req 3.8（再生開始時の基準状態確立とライブ残存トリガー解除）を損なわないこと。core 改修は rec-recording-playback Req 6 の制約群（6.1 観測・注入面の追加に限定 / 6.5 未使用時の挙動・性能不変 / 6.6 core は rec を知らない / 6.7 拡張パッケージ無改修）の範囲内で行うこと

## Requirements

### Requirement 1: 再生中のライブトリガー入力の遮断

**Objective:** As a Unity エンジニア, I want REC 再生中にコントローラ等のライブトリガー操作が再生結果へ混入しないでほしい, so that 記録どおりのブレンドが再生中に乱されない

#### Acceptance Criteria

1. The core shall トリガー入力の遮断状態を制御する面（遮断の開始・解除）を提供し、開始・解除の操作はそれぞれ冪等とする（重複呼び出しで状態や副作用が変化しない）
2. While トリガー入力が遮断されている間, when ライブ入力源から TriggerOn または TriggerOff が呼び出されたとき, the core トリガー入力ソース shall 当該呼び出しを無視し、表情スタックを変更しない
3. While トリガー入力が遮断されている間, when ライブの TriggerOn / TriggerOff が無視されたとき, the core shall 登録された観測者へ当該イベントを通知しない（無視されたライブ操作を再生中の記録へ混入させない）
4. When トリガー入力の遮断が解除されたとき, the core トリガー入力ソース shall 表情スタックを解除時点の状態のまま維持する（rec-recording-playback Req 3.5 のシームレスなライブ引き継ぎを損なわない）
5. The core shall 基準状態確立のためのスタックリセット操作（ResetToExpressionStack 相当）を遮断の対象外とする（rec-recording-playback Req 3.8 の基準状態確立を遮断中も成立させる）
6. The 入力排他 shall 再生中は常時有効とし、排他を無効化する設定・オプションを提供しない

### Requirement 2: 再生駆動のための注入経路（遮断の迂回）

**Objective:** As a Unity エンジニア, I want 再生の駆動イベントは遮断中でも本物の入力パイプラインへ届いてほしい, so that ライブと同一コードパスでのブレンド再現と、再生中の記録（観測）が両立する

#### Acceptance Criteria

1. While トリガー入力が遮断されている間, when REC 再生サービスが注入経路経由でトリガー on / off を駆動したとき, the core shall 当該イベントをライブの TriggerOn / TriggerOff と同一の表情スタック処理で受理する
2. When 注入経路経由のトリガーイベントが受理されたとき, the core shall 登録された観測者へ当該イベント（expressionId・イベント種別）を通知する（再生中に記録セッションが有効な場合、再生イベントが記録に残る）
3. The core の注入経路 shall 遮断状態の有無に関わらず機能する
4. The rec トリガー注入ポート shall アナログ注入ポートと対称のライフサイクル（基準状態の確立を伴う注入開始 / トリガー on 注入 / トリガー off 注入 / 注入終了）を持ち、注入開始時に排他の確立と記録された基準状態の確立を一体の操作として行う

### Requirement 3: 再生中のライブアナログ / gaze 入力の遮断（全ソースへの拡大）

**Objective:** As a Unity エンジニア, I want 記録ベースラインに含まれないアナログ / gaze ソースも再生中は遮断されてほしい, so that 記録後に追加されたデバイス等のライブ入力が再生結果に混ざらない

#### Acceptance Criteria

1. When 再生が開始されたとき, the REC 再生サービス shall 再生開始時点で registry に登録済みの全アナログ / gaze 入力ソース（記録ベースラインに含まれないソースを含む）を遮断対象とする
2. While 再生中, when 遮断対象ソースに対するライブのアナログ / gaze 値更新が発生したとき, the REC 再生サービス shall 当該値を入力パイプラインのブレンドへ反映させない
3. When 再生が開始されたとき, the REC 再生サービス shall 記録ベースライン外の遮断対象ソースに対して確定的な初期値（seed 値）を確立する（seed 値の方式＝現在消費値での凍結か 0 埋めかは設計フェーズで決定し、本要件では方式を固定しない）
4. The 入力排他 shall 遮断対象を再生開始時点のスナップショットに基づいて決定する（再生中に新規登録されたソースは遮断対象に追加しない）

### Requirement 4: 排他の確立・解放ライフサイクル

**Objective:** As a Unity エンジニア, I want 排他の確立と解放のタイミングが明確であってほしい, so that 停止時のライブ引き継ぎと自然完了後の状態が予測可能になる

#### Acceptance Criteria

1. When 再生が開始されたとき, the REC 再生サービス shall 時系列イベントの発火を開始する前に、トリガーとアナログ / gaze の両方の入力排他を確立する
2. When 再生停止（StopPlayback）が指示されたとき, the REC 再生サービス shall トリガーとアナログ / gaze の両方の入力排他を解放し、ライブ操作へ引き継げる状態にする
3. When 再生が記録の最終イベントに到達して自然完了（Completed）したとき, the REC 再生サービス shall 入力排他を解放せず維持する（StopPlayback を唯一の解放点とし、アナログ側の既存挙動と対称にする）
4. The REC 再生サービス shall トリガーとアナログ / gaze の排他確立・解放を一貫した順序で行い、部分的な排他状態（片方のみ確立・解放された状態）を定常状態として残さない

### Requirement 5: core 制約・性能・テスト容易性の維持

**Objective:** As a ライブラリ開発者, I want 排他機構を rec-recording-playback Req 6 の制約群と性能基準の範囲内で追加したい, so that core の独立性・既存挙動・GC ゼロ目標を守ったまま排他を成立させられる

#### Acceptance Criteria

1. The core 改修 shall 観測面・注入面（ライブ入力の遮断面を含む）の追加に限定し、既存コードパスの挙動を変更しない（rec-recording-playback Req 6.1 の範囲内）
2. If トリガー入力の遮断が一度も有効化されず注入経路も使用されていないとき, the core shall 既存の挙動・性能を一切変更しない（rec-recording-playback Req 6.5 を維持）
3. The core shall rec パッケージへの依存を持たない（rec-recording-playback Req 6.6 を維持）
4. The 本機能 shall 拡張パッケージ（inputsystem / osc / lipsync / ifacialmocap / timeline）を無改修のまま成立させ、これらの既存挙動を変更しない（rec-recording-playback Req 6.7 を維持）
5. While 入力排他が有効な間, the core および REC 再生サービス shall 毎フレームの定常処理でヒープ確保を発生させない
6. The 本機能の受け入れテスト shall Fake のみで検証可能な構造とし、EditMode テストとして配置できるようにする

### Requirement 6: 既知制限の文書化

**Objective:** As a ライブラリ利用者, I want 入力排他の既知制限を事前に把握したい, so that 再生前後の運用（デバイス追加・Timeline 併用・Toggle 再同期）で想定外の挙動に混乱しない

#### Acceptance Criteria

1. The 本機能のドキュメント shall 再生開始後に新規登録された入力ソースが遮断対象外であること（開始時スナップショット方式）を既知制限として文書化する
2. The 本機能のドキュメント shall timeline パッケージ経由の TriggerOn / TriggerOff も再生中は遮断されることを既知制限として文書化する
3. The 本機能のドキュメント shall ExpressionInputSourceAdapter の Toggle バインドが遮断中も内部状態（entry.IsActive）を反転し続けるため、再生終了後に空押しによる再同期が必要になる場合があること（拡張パッケージ無改修の制約下で許容する挙動）を既知制限として文書化する

## Open Questions（設計フェーズで決定する残論点）

1. **記録ベースライン外ソースの seed 値方式**: 現在消費値での凍結か 0 埋めか（Requirement 3.3。ユーザー体験＝再生開始時の表情の跳びと、再現性のどちらを優先するかを設計フェーズで比較して決定する）
