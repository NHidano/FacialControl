using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Editor.Tools
{
    /// <summary>
    /// Editor 向け ARKit 検出 API。
    /// Domain 層の ARKitDetector をラップし、SkinnedMeshRenderer からの BlendShape 名取得と
    /// 検出結果の JSON 保存機能を提供する。
    /// </summary>
    public sealed class ARKitEditorService
    {
        private readonly ARKitUseCase _useCase;
        private readonly IJsonParser _parser;

        public ARKitEditorService()
        {
            _useCase = new ARKitUseCase();
            _parser = new SystemTextJsonParser();
        }

        public ARKitEditorService(ARKitUseCase useCase, IJsonParser parser)
        {
            _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        /// <summary>
        /// SkinnedMeshRenderer から全 BlendShape 名を取得する。
        /// </summary>
        /// <param name="renderer">対象の SkinnedMeshRenderer</param>
        /// <returns>BlendShape 名の配列。BlendShape が存在しない場合は空配列</returns>
        public string[] GetBlendShapeNames(SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer));

            var mesh = renderer.sharedMesh;
            if (mesh == null)
                return Array.Empty<string>();

            int count = mesh.blendShapeCount;
            if (count == 0)
                return Array.Empty<string>();

            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = mesh.GetBlendShapeName(i);
            }
            return names;
        }

        /// <summary>
        /// SkinnedMeshRenderer の BlendShape に対して ARKit 52 / PerfectSync 検出と Expression 自動生成を実行する。
        /// </summary>
        /// <param name="renderer">対象の SkinnedMeshRenderer</param>
        /// <returns>検出結果と生成された Expression</returns>
        public ARKitUseCase.DetectResult DetectFromRenderer(SkinnedMeshRenderer renderer)
        {
            var blendShapeNames = GetBlendShapeNames(renderer);
            return _useCase.DetectAndGenerate(blendShapeNames);
        }

        /// <summary>
        /// 検出された BlendShape 名から OSC マッピングを生成する。
        /// </summary>
        /// <param name="detectedNames">検出済みパラメータ名配列</param>
        /// <returns>生成された OscMapping 配列</returns>
        public OscMapping[] GenerateOscMapping(string[] detectedNames)
        {
            return _useCase.GenerateOscMapping(detectedNames);
        }

        /// <summary>
        /// 検出された Expression をプロファイル JSON として保存する。
        /// レイヤー定義は Expression のレイヤー参照から自動生成される。
        /// </summary>
        /// <param name="expressions">保存対象の Expression 配列</param>
        /// <param name="path">保存先ファイルパス</param>
        public void SaveExpressionsAsProfileJson(Expression[] expressions, string path)
        {
            if (expressions == null)
                throw new ArgumentNullException(nameof(expressions));
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("保存先パスを空にすることはできません。", nameof(path));

            // Expression のレイヤー参照からレイヤー定義を構築
            var layerNames = new HashSet<string>();
            for (int i = 0; i < expressions.Length; i++)
            {
                layerNames.Add(expressions[i].Layer);
            }

            var layers = new List<LayerDefinition>();
            int priority = 0;
            foreach (var name in layerNames)
            {
                layers.Add(new LayerDefinition(name, priority, ExclusionMode.LastWins));
                priority++;
            }

            var profile = new FacialProfile("1.0", layers.ToArray(), expressions);
            var json = _parser.SerializeProfile(profile);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// OSC マッピングを config.json として保存する。
        /// </summary>
        /// <param name="mappings">保存対象の OscMapping 配列</param>
        /// <param name="path">保存先ファイルパス</param>
        public void SaveOscMappingAsConfigJson(OscMapping[] mappings, string path)
        {
            if (mappings == null)
                throw new ArgumentNullException(nameof(mappings));
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("保存先パスを空にすることはできません。", nameof(path));

            var oscConfig = new OscConfiguration(mapping: mappings);
            var config = new FacialControlConfig("1.0", oscConfig);
            var json = _parser.SerializeConfig(config);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }
    }
}
