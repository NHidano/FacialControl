# Requirements Document

## Project Description (Input)
REC 記録（操作イベント時系列）を Unity Timeline 上で編集・再生可能にする独自 Track + ベイク書き出し機能。標準 AnimationTrack は使わず（クリップ重なりブレンドが線形固定でカスタムカーブ・「遷移中の再トリガーは現在値から開始」を表現できないため）、独自 Track の mixer が「もう一つの入力アダプター」として本物の入力パイプライン（TriggerOn/Off + アナログ/gaze 値）を駆動し、遷移計算をライブ操作と同一コードパスにする。スクラブ/ランダムアクセス対応はベイク済みカーブ併用: ベイク粒度はソース単位（遷移補間済み・レイヤー合成前、post-blend ではない。ライブのリップシンク等とレイヤー合成で共存させるため）。値はベイクカーブから、active 表情状態はイベント列から並行駆動する（音素 override/suppress 等の active 依存挙動のため。イベント側は値を出力しない）。ベイクは Aggregator 側の観測フック（Editor オフライン時のみ）で焼く。人間による Timeline 編集（クリップ移動・差し替え）が前提で、編集後の正本は Timeline のクリップ列に移り、ベイクは「クリップ列 + プロファイル」から再シミュレーションで再生成。陳腐化はハッシュで検知（自動再ベイク + ランタイム不一致警告）。gaze は正規化 Vector2(-1..1) のまま Keyframe カーブ化し、独自 Track 経由でランタイム gaze 解決パイプラインに流す（リグ非依存・プロファイル追従）。backlog M-1（Timeline 統合）と関連。rec-recording-playback spec（REC 記録+リアルタイム再生）の後続・依存 spec。

## Requirements
<!-- Will be generated in /kiro-spec-requirements phase -->
