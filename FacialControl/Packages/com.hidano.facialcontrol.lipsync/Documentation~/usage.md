# uLipSync Adapter 使用手順

`com.hidano.facialcontrol.lipsync` は、uLipSync の解析結果を FacialControl の `lipsync` 入力ソースへ接続するための Windows 向け UPM パッケージです。設定は `FacialCharacterProfileSO` の `ULipSyncAdapterBinding` に inline serialized で保存され、再生時に `AudioSource`、`uLipSync.uLipSync`、入力コンポーネントを Host GameObject へ動的に追加します。

> **マイクデバイス指定の保存先**: マシン依存値である Device 名 / Disambiguator Index は、spec
> [`adapter-runtime-settings`](../../../../.kiro/specs/adapter-runtime-settings/) で導入された
> `LipSyncDeviceStore` 経由で PlayerPrefs (キー
> `Hidano.FacialControl.LipSync.MicDevice.Name` /
> `Hidano.FacialControl.LipSync.MicDevice.Disambiguator`) に保存され、git 管理外になります。
> Inspector の DeviceDescriptorPopup は内部で `LipSyncDeviceStore.Load()` / `Save()` を呼びます。
> 詳細は [`com.hidano.facialcontrol/Documentation~/adapter-runtime-settings.md`](../../com.hidano.facialcontrol/Documentation~/adapter-runtime-settings.md)
> の「5. PlayerPrefs キー名一覧」節を参照してください。

## AnimationClip 形式 entry の time-0 サンプリング

AnimationClip 形式 entry は、初期化時に `AnimationClip.SampleAnimation(host, 0f)` 相当の処理で **time 0 の BlendShape weight だけ**を読み取ります。読み取った値は音素ごとの固定スナップショットとして保持され、再生中は `phonemeRatio * volume * snapshotWeight` として合算されます。

この entry は AnimationClip の時間軸を再生しません。たとえば 0.0 秒で口閉じ、0.5 秒で口開けになる Clip を指定しても、使用されるのは 0.0 秒時点の値だけです。口開け遷移、イージング、途中キー、ループ、Clip 長は評価対象になりません。

複合的な口形状を使いたい場合は、対象 Clip の 0.0 秒に必要な BlendShape weight をすべて設定してください。A / I / U / E / O のような音素ごとに 1 枚の Clip を用意し、それぞれの 0.0 秒フレームに完成形の値を置く運用を推奨します。

サンプリング時は Host GameObject 配下の `SkinnedMeshRenderer` の BlendShape weight を一時的に変更しますが、`ULipSyncAdapterBinding` は全 SMR の値を退避し、サンプリング後に復元します。この処理は `OnStart` の初期化コストであり、毎フレームのホットパスには含まれません。

## ASIO 入力の利用手順

ASIO 入力を使う場合も、Inspector で ASIO / Mic を切り替えるトグルはありません。`ULipSyncAdapterBinding` は設定されたデバイス名を ASIO ドライバ一覧に先に照合し、一致した場合は `uLipSyncAsioInput` を選択します。一致しない場合だけ `UnityEngine.Microphone.devices` の通常マイクとして解決します。

1. 使用する ASIO ドライバを OS にインストールします。オーディオインターフェース固有の公式 ASIO ドライバを優先し、必要な場合のみ ASIO4ALL などの汎用ドライバを使用します。
2. Unity Editor を起動し、対象 Profile の `ULipSyncAdapterBinding` を Inspector で開きます。
3. Device 欄に、ASIO ドライバ一覧へ表示される名前をそのまま入力します。候補に出ない環境では手動 override のテキスト欄にプロジェクト固有のドライバ名を入力します。
4. 同名デバイスが複数ある環境では `Disambiguator Index` に列挙順の 0 始まりインデックスを指定します。通常は `0` のままで問題ありません。
5. Analyzer Profile と phoneme entry を設定し、Scene を Play して Console に未解決デバイスの `LogError` が出ていないことを確認します。

ASIO ドライバ名は PC、ドライバのバージョン、ASIO4ALL の構成、接続中の機器によって変わります。そのため本パッケージは ASIO Sample Scene を同梱しません。特定の開発環境でしか解決できないドライバ名をサンプルに固定すると、Package Manager から Import したユーザー環境では再現性がありません。ASIO の確認は、この手順に従って各プロジェクトで実際のドライバ名を設定して行ってください。
