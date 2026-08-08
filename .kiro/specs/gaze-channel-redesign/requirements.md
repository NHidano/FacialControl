# Requirements Document

## Project Description (Input)
gaze-channel-redesign: gaze の識別を規約 id "gaze" 1 本の既定チャネルに再設計し、isGaze ダミー Expression + GazeConfigs の二重管理を廃止して Profile 直下の「Gaze セクション」へ統合する。各 binding は gaze source を宣言インターフェース（IGazeSourceProvider 仮称）で公開し、ユーザーは Gaze セクションの入力ソース選択（ドロップダウン）だけで手法（コントローラ / iFacialMocap / OSC 受信など）を切り替えられる。GazeSourceIdConvention ヘルパーで id 合成・パースを一元化し（iFacialMocap の gaze.left/right ハードコード、InputSystem のエイリアス後付け、5 箇所に散った .left/.right リテラルを吸収）、目ボーン設定は参照モデル割当時に自動解決、ボーン path はフルパス化（backlog S-1 同時解消）する。詳細な背景・決定済み事項・スコープ・未決事項は .kiro/multi-spec/gaze-control-overhaul.md の「全体コンテクスト」「共通の決定済み事項」「Spec: gaze-channel-redesign」の各節に記載されており、requirements 生成・dig インタビュー・design の前提として必ず読み込むこと。関連 spec: .kiro/specs/osc-gaze-auto-mapping/（先行 Spec 1。広告プロトコル /_facialcontrol/gaze と受信側 gaze 動的登録経路を確立済み。本 spec では広告される id が規約定数 "gaze" になるだけでプロトコルは変更しない）も参照。

## Requirements
<!-- Will be generated in /kiro-spec-requirements phase -->
