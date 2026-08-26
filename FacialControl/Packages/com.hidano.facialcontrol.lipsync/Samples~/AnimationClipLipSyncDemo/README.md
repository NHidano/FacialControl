# AnimationClipLipSyncDemo

`ULipSyncAdapterBinding` の音素 entry に、BlendShape 直接指定と AnimationClip time 0 サンプリングを混在させるサンプルです。

## 使い方

1. Package Manager から `AnimationClipLipSyncDemo` を Import します。
2. `Scenes/AnimationClipLipSyncDemo.unity` を開きます。
3. `Character` GameObject の下へ自分のモデルを配置します。
4. モデルの口メッシュ GameObject 名が `Body` ではない場合は、`Profiles/AnimationClipLipSyncDemoProfile.asset` の `Target Mesh Hint` を変更します。
5. BlendShape 名が異なる場合は、A / E の `BlendShapeName` と、`Animations/` 内の I / U / O 用 AnimationClip の BlendShape カーブ名を合わせます。

Profile には A / E を BlendShape entry、I / U / O を AnimationClip entry として設定しています。AnimationClip entry は再生時間ではなく time 0 の BlendShape 値だけを初期化時にサンプリングします。

Analyzer Profile は同梱しません。`_analyzerProfile` を未指定のまま再生すると、パッケージ同梱の `Resources/FacialControl/LipSync/Default uLipSync Profile.asset` が `Resources.Load` でフォールバック適用されます。
