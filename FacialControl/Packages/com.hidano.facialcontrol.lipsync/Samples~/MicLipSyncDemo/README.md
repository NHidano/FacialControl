# MicLipSyncDemo

`ULipSyncAdapterBinding` を `FacialCharacterProfileSO._adapterBindings` に inline serialized で持つ、uLipSync マイク入力の最小サンプルです。

## 使い方

1. Package Manager から `MicLipSyncDemo` を Import します。
2. `Scenes/MicLipSyncDemo.unity` を開きます。
3. `Character` GameObject の下へ自分のモデルを配置します。
4. `Profiles/MicLipSyncDemoProfile.asset` の `uLipSync` binding で、使用するマイク名を現在の環境に合わせます。
5. モデル側の口 BlendShape 名が異なる場合は、A / I / U / E / O の各 entry の `BlendShapeName` を合わせます。

Scene 上の `Character` には、再生前に `uLipSync.uLipSync` / `uLipSyncMicrophone` / `AudioSource` は付いていません。再生時に `ULipSyncAdapterBinding` が Host GameObject へ動的に追加し、停止または Dispose で取り外します。

Analyzer Profile は同梱しません。`_analyzerProfile` を未指定のまま再生すると、パッケージ同梱の `Resources/FacialControl/LipSync/Default uLipSync Profile.asset` が `Resources.Load` でフォールバック適用されます。独自に学習させた Profile を使う場合のみ Inspector で割り当ててください。
