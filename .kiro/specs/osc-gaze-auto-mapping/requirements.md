# Requirements Document

## Project Description (Input)
osc-gaze-auto-mapping: OSC 送信側が /_facialcontrol/gaze 専用アドレスで gaze の id と形式（VRChat_XY / ARKit_8BS）を広告し、受信側が gaze route / GazeVector2InputSource を自動生成する（backlog M-25 の実行）。あわせて調査で発覚した gaze 経路の潜在バグ 4 件（Custom preset + gaze の未捕捉例外 / gaze source 後発登録レース / Gaze_VRChat_XY + leftRightIndependent の見かけ倒し / gaze 読取ロジック重複）を回収し、実機の未解決不具合「OSC 受信端末で gaze だけ動かない」を根治する。詳細な背景・決定済み事項・スコープ・未決事項は .kiro/multi-spec/gaze-control-overhaul.md の「全体コンテクスト」「共通の決定済み事項」「Spec: osc-gaze-auto-mapping」の各節に記載されており、requirements 生成・dig インタビュー・design の前提として必ず読み込むこと。

## Requirements
<!-- Will be generated in /kiro-spec-requirements phase -->
