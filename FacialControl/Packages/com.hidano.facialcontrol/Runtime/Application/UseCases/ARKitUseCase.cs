using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Application.UseCases
{
    /// <summary>
    /// ARKit 52 / PerfectSync パラメータの検出と Expression・OSC マッピング自動生成を行うユースケース。
    /// Domain 層の ARKitDetector をラップし、検出から生成までのフローを提供する。
    /// </summary>
    public class ARKitUseCase
    {
        /// <summary>
        /// 検出 + Expression 自動生成の結果を保持する構造体。
        /// </summary>
        public readonly struct DetectResult
        {
            /// <summary>
            /// 検出された BlendShape パラメータ名の配列（ARKit 52 + PerfectSync）
            /// </summary>
            public string[] DetectedNames { get; }

            /// <summary>
            /// レイヤーグループ単位で自動生成された Expression の配列
            /// </summary>
            public Expression[] GeneratedExpressions { get; }

            public DetectResult(string[] detectedNames, Expression[] generatedExpressions)
            {
                DetectedNames = detectedNames;
                GeneratedExpressions = generatedExpressions;
            }
        }

        /// <summary>
        /// BlendShape 名配列から ARKit 52 / PerfectSync パラメータを検出し、
        /// レイヤーグループ単位で Expression を自動生成する。
        /// </summary>
        /// <param name="blendShapeNames">モデルの BlendShape 名配列</param>
        /// <returns>検出結果と生成された Expression</returns>
        public DetectResult DetectAndGenerate(string[] blendShapeNames)
        {
            if (blendShapeNames == null)
                throw new ArgumentNullException(nameof(blendShapeNames));

            var detectedNames = ARKitDetector.DetectAll(blendShapeNames);

            if (detectedNames.Length == 0)
                return new DetectResult(Array.Empty<string>(), Array.Empty<Expression>());

            var expressions = ARKitDetector.GenerateExpressions(detectedNames);

            return new DetectResult(detectedNames, expressions);
        }

        /// <summary>
        /// 検出されたパラメータ名から VRChat OSC 互換の OSC マッピングを自動生成する。
        /// 未知のパラメータ（レイヤーグループが取得できないもの）はスキップされる。
        /// OSC アドレス形式: /avatar/parameters/{blendShapeName}
        /// </summary>
        /// <param name="detectedNames">検出済みパラメータ名配列</param>
        /// <returns>生成された OscMapping の配列</returns>
        public OscMapping[] GenerateOscMapping(string[] detectedNames)
        {
            if (detectedNames == null)
                throw new ArgumentNullException(nameof(detectedNames));

            if (detectedNames.Length == 0)
                return Array.Empty<OscMapping>();

            var mappings = new List<OscMapping>();

            for (int i = 0; i < detectedNames.Length; i++)
            {
                string name = detectedNames[i];
                string layerGroup = ARKitDetector.GetLayerGroup(name);

                if (layerGroup == null)
                    continue;

                string oscAddress = $"/avatar/parameters/{name}";
                mappings.Add(new OscMapping(oscAddress, name, layerGroup));
            }

            return mappings.ToArray();
        }
    }
}
