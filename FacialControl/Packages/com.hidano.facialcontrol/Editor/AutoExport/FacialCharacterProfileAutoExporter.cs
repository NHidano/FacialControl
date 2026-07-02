using System;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.Sampling;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hidano.FacialControl.Editor.AutoExport
{
    /// <summary>
    /// Play モード突入時 / ビルド開始時に、プロジェクト内の全 <see cref="FacialCharacterProfileSO"/> を
    /// 再サンプリング + <c>StreamingAssets/FacialControl/{SO 名}/profile.json</c> エクスポートして
    /// ランタイムが読む JSON を最新化する Editor 専用フック。
    /// <para>
    /// 既存の自動保存は Inspector 編集の <c>TrackSerializedObjectValue</c> 起点のみのため、
    /// 「クリップだけ差し替えてエクスポートを忘れる」「パッケージ更新で旧 profile.json が残る」といった
    /// ケースで profile.json が古いまま Play / ビルドに進み得る。本フックがその穴を塞ぐ。
    /// </para>
    /// <para>
    /// エクスポートは冪等（内容が既に最新なら同一バイトを書くだけ）なので、データが正しければ
    /// ファイル差分は出ない。SO の <c>cachedSnapshot</c> はインメモリで再サンプリングするのみで
    /// アセットを dirty にしない（profile.json の最新化だけを目的とし、余計な保存・再インポートを避ける）。
    /// ただし既に dirty な（未保存編集を持つ）SO は、編集消失を防ぐためエクスポート前に .asset へ保存する。
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class FacialCharacterProfileAutoExporter
    {
        static FacialCharacterProfileAutoExporter()
        {
            // ドメインリロードごとに静的コンストラクタが走るため、二重登録を避けてから登録する。
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Edit モード終了（= Play 突入直前）でのみ実行する。
            // EnteredPlayMode で行うと既にランタイムが JSON を読んだ後になり得るため ExitingEditMode を採用する。
            if (change != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            ExportAll("playmode");
        }

        /// <summary>
        /// プロジェクト内の全 <see cref="FacialCharacterProfileSO"/>（派生型含む）を再サンプリング +
        /// profile.json エクスポートする。個別 SO の失敗は警告ログ + skip で、全体は継続する。
        /// </summary>
        /// <param name="trigger">ログ用のトリガー識別子（<c>"playmode"</c> / <c>"build"</c> 等）。</param>
        /// <returns>profile.json を書き出せた SO 数。</returns>
        public static int ExportAll(string trigger)
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(FacialCharacterProfileSO));
            if (guids == null || guids.Length == 0)
            {
                return 0;
            }

            var sampler = new AnimationClipExpressionSampler();
            int exported = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var so = AssetDatabase.LoadAssetAtPath<FacialCharacterProfileSO>(path);
                if (so == null)
                {
                    continue;
                }

                try
                {
                    // 未保存の編集（Inspector 破棄で自動保存 delayCall が失われた場合等）を
                    // Play / ビルド前に .asset へ確定する。メモリ上の SO とディスクの .asset が
                    // 不整合のままだと、以後ディスクへ書かれる契機がなく編集が失われ得る。
                    // 再サンプリング（下記）より前に呼ぶことで、保存対象をユーザー編集分に限定する。
                    AssetDatabase.SaveAssetIfDirty(so);

                    // クリップを ÷100 正規化して cachedSnapshot に焼き直し（インメモリ）、その値で JSON を書き出す。
                    FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(so, sampler);
                    if (FacialCharacterProfileExporter.ExportProfileJson(so))
                    {
                        exported++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[FacialCharacterProfileAutoExporter] '{path}' の自動エクスポート ({trigger}) に失敗しました: {ex.Message}");
                }
            }

            return exported;
        }
    }

    /// <summary>
    /// ビルド開始時に全 <see cref="FacialCharacterProfileSO"/> を profile.json へ再エクスポートする
    /// <see cref="IPreprocessBuildWithReport"/> 実装。出荷ビルドに含まれる StreamingAssets を常に最新化する。
    /// </summary>
    public sealed class FacialCharacterProfileBuildExporter : IPreprocessBuildWithReport
    {
        /// <inheritdoc />
        public int callbackOrder => 0;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            FacialCharacterProfileAutoExporter.ExportAll("build");
        }
    }
}
