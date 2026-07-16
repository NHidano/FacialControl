# Requirements Document

## Project Description (Input)
表情操作の記録・リアルタイム再生（REC）機能。トリガー on/off + expressionId + アナログ軸値 + gaze(-1..1 Vector2) の操作イベント時系列（秒ベースタイムスタンプ）を記録の正本とし、本物の入力パイプライン（ExpressionTriggerInputSourceBase の TriggerOn/Off + アナログ/gaze 値）を駆動して収録時プレビューと同一のブレンドを完全再現するリアルタイム再生を提供する。新規 UPM パッケージ com.hidano.facialcontrol.rec として追加し、core へはイベント観測点の小改修（数行規模）のみ。永続化は profile.json 同居ではなく sidecar ファイル（StreamingAssets/FacialControl/{assetName}/ 規約流用）。SystemTextJsonParser（実体 JsonUtility）は巨大配列に弱いため REC 時系列のフォーマット選定は要検討。gaze は BlendShape 経路と別チャネル（IAnalogInputSource、push 型 Publish(x,y)、-1..1）で記録・再生とも必須スコープ。

## Requirements
<!-- Will be generated in /kiro-spec-requirements phase -->
