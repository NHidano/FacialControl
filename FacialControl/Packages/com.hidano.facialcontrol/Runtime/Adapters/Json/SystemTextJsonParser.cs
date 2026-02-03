using System;
using System.Collections.Generic;
using UnityEngine;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Json
{
    /// <summary>
    /// IJsonParser の実装。Unity の JsonUtility をベースに、
    /// DTO を介してドメインモデルとの変換を行う。
    /// schemaVersion チェックと不正 JSON 時の例外スローを提供する。
    /// </summary>
    public sealed class SystemTextJsonParser : IJsonParser
    {
        private const string SupportedSchemaVersion = "1.0";

        /// <inheritdoc/>
        public FacialProfile ParseProfile(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON 文字列を空にすることはできません。", nameof(json));

            ProfileDto dto;
            try
            {
                dto = JsonUtility.FromJson<ProfileDto>(json);
            }
            catch (Exception ex)
            {
                throw new FormatException("プロファイル JSON のパースに失敗しました。", ex);
            }

            if (dto == null)
                throw new FormatException("プロファイル JSON のパースに失敗しました。結果が null です。");

            if (string.IsNullOrEmpty(dto.schemaVersion))
                throw new FormatException("schemaVersion が指定されていません。");

            ValidateSchemaVersion(dto.schemaVersion);

            return ConvertToProfile(dto);
        }

        /// <inheritdoc/>
        public string SerializeProfile(FacialProfile profile)
        {
            var dto = ConvertToProfileDto(profile);
            return JsonUtility.ToJson(dto, true);
        }

        /// <inheritdoc/>
        public FacialControlConfig ParseConfig(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON 文字列を空にすることはできません。", nameof(json));

            ConfigDto dto;
            try
            {
                dto = JsonUtility.FromJson<ConfigDto>(json);
            }
            catch (Exception ex)
            {
                throw new FormatException("設定 JSON のパースに失敗しました。", ex);
            }

            if (dto == null)
                throw new FormatException("設定 JSON のパースに失敗しました。結果が null です。");

            if (string.IsNullOrEmpty(dto.schemaVersion))
                throw new FormatException("schemaVersion が指定されていません。");

            ValidateSchemaVersion(dto.schemaVersion);

            return ConvertToConfig(dto);
        }

        /// <inheritdoc/>
        public string SerializeConfig(FacialControlConfig config)
        {
            var dto = ConvertToConfigDto(config);
            return JsonUtility.ToJson(dto, true);
        }

        private static void ValidateSchemaVersion(string version)
        {
            if (version != SupportedSchemaVersion)
                throw new FormatException(
                    $"サポートされていないスキーマバージョンです: {version}（サポート対象: {SupportedSchemaVersion}）");
        }

        // --- Profile 変換 ---

        private static FacialProfile ConvertToProfile(ProfileDto dto)
        {
            var layers = ConvertLayers(dto.layers);
            var expressions = ConvertExpressions(dto.expressions);
            return new FacialProfile(dto.schemaVersion, layers, expressions);
        }

        private static LayerDefinition[] ConvertLayers(List<LayerDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<LayerDefinition>();

            var layers = new LayerDefinition[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                var exclusionMode = ParseExclusionMode(d.exclusionMode);
                layers[i] = new LayerDefinition(d.name, d.priority, exclusionMode);
            }
            return layers;
        }

        private static Expression[] ConvertExpressions(List<ExpressionDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<Expression>();

            var expressions = new Expression[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                var blendShapes = ConvertBlendShapeMappings(d.blendShapeValues);
                var layerSlots = ConvertLayerSlots(d.layerSlots);
                var curve = ConvertTransitionCurve(d.transitionCurve);

                expressions[i] = new Expression(
                    d.id,
                    d.name,
                    d.layer,
                    d.transitionDuration,
                    curve,
                    blendShapes,
                    layerSlots);
            }
            return expressions;
        }

        private static BlendShapeMapping[] ConvertBlendShapeMappings(List<BlendShapeMappingDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<BlendShapeMapping>();

            var mappings = new BlendShapeMapping[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                mappings[i] = new BlendShapeMapping(
                    d.name,
                    d.value,
                    string.IsNullOrEmpty(d.renderer) ? null : d.renderer);
            }
            return mappings;
        }

        private static LayerSlot[] ConvertLayerSlots(List<LayerSlotDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<LayerSlot>();

            var slots = new LayerSlot[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                var blendShapes = ConvertBlendShapeMappings(d.blendShapeValues);
                slots[i] = new LayerSlot(d.layer, blendShapes);
            }
            return slots;
        }

        private static TransitionCurve ConvertTransitionCurve(TransitionCurveDto dto)
        {
            if (dto == null)
                return TransitionCurve.Linear;

            var type = ParseTransitionCurveType(dto.type);
            var keys = ConvertCurveKeyFrames(dto.keys);
            return new TransitionCurve(type, keys);
        }

        private static CurveKeyFrame[] ConvertCurveKeyFrames(List<CurveKeyFrameDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<CurveKeyFrame>();

            var keys = new CurveKeyFrame[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                keys[i] = new CurveKeyFrame(
                    d.time, d.value, d.inTangent, d.outTangent,
                    d.inWeight, d.outWeight, d.weightedMode);
            }
            return keys;
        }

        private static ExclusionMode ParseExclusionMode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return ExclusionMode.LastWins;

            return value.ToLowerInvariant() switch
            {
                "lastwins" => ExclusionMode.LastWins,
                "blend" => ExclusionMode.Blend,
                _ => throw new FormatException($"不正な ExclusionMode 値: {value}")
            };
        }

        private static TransitionCurveType ParseTransitionCurveType(string value)
        {
            if (string.IsNullOrEmpty(value))
                return TransitionCurveType.Linear;

            return value.ToLowerInvariant() switch
            {
                "linear" => TransitionCurveType.Linear,
                "easein" => TransitionCurveType.EaseIn,
                "easeout" => TransitionCurveType.EaseOut,
                "easeinout" => TransitionCurveType.EaseInOut,
                "custom" => TransitionCurveType.Custom,
                _ => throw new FormatException($"不正な TransitionCurveType 値: {value}")
            };
        }

        // --- Profile → DTO 変換 ---

        private static ProfileDto ConvertToProfileDto(FacialProfile profile)
        {
            var dto = new ProfileDto
            {
                schemaVersion = profile.SchemaVersion,
                layers = new List<LayerDto>(),
                expressions = new List<ExpressionDto>()
            };

            var layerSpan = profile.Layers.Span;
            for (int i = 0; i < layerSpan.Length; i++)
            {
                dto.layers.Add(new LayerDto
                {
                    name = layerSpan[i].Name,
                    priority = layerSpan[i].Priority,
                    exclusionMode = SerializeExclusionMode(layerSpan[i].ExclusionMode)
                });
            }

            var exprSpan = profile.Expressions.Span;
            for (int i = 0; i < exprSpan.Length; i++)
            {
                dto.expressions.Add(ConvertToExpressionDto(exprSpan[i]));
            }

            return dto;
        }

        private static ExpressionDto ConvertToExpressionDto(Expression expr)
        {
            var dto = new ExpressionDto
            {
                id = expr.Id,
                name = expr.Name,
                layer = expr.Layer,
                transitionDuration = expr.TransitionDuration,
                transitionCurve = ConvertToTransitionCurveDto(expr.TransitionCurve),
                blendShapeValues = new List<BlendShapeMappingDto>(),
                layerSlots = new List<LayerSlotDto>()
            };

            var bsSpan = expr.BlendShapeValues.Span;
            for (int i = 0; i < bsSpan.Length; i++)
            {
                dto.blendShapeValues.Add(new BlendShapeMappingDto
                {
                    name = bsSpan[i].Name,
                    value = bsSpan[i].Value,
                    renderer = bsSpan[i].Renderer ?? ""
                });
            }

            var slotSpan = expr.LayerSlots.Span;
            for (int i = 0; i < slotSpan.Length; i++)
            {
                var slotDto = new LayerSlotDto
                {
                    layer = slotSpan[i].Layer,
                    blendShapeValues = new List<BlendShapeMappingDto>()
                };

                var slotBsSpan = slotSpan[i].BlendShapeValues.Span;
                for (int j = 0; j < slotBsSpan.Length; j++)
                {
                    slotDto.blendShapeValues.Add(new BlendShapeMappingDto
                    {
                        name = slotBsSpan[j].Name,
                        value = slotBsSpan[j].Value,
                        renderer = slotBsSpan[j].Renderer ?? ""
                    });
                }

                dto.layerSlots.Add(slotDto);
            }

            return dto;
        }

        private static TransitionCurveDto ConvertToTransitionCurveDto(TransitionCurve curve)
        {
            var dto = new TransitionCurveDto
            {
                type = SerializeTransitionCurveType(curve.Type),
                keys = new List<CurveKeyFrameDto>()
            };

            var keysSpan = curve.Keys.Span;
            for (int i = 0; i < keysSpan.Length; i++)
            {
                dto.keys.Add(new CurveKeyFrameDto
                {
                    time = keysSpan[i].Time,
                    value = keysSpan[i].Value,
                    inTangent = keysSpan[i].InTangent,
                    outTangent = keysSpan[i].OutTangent,
                    inWeight = keysSpan[i].InWeight,
                    outWeight = keysSpan[i].OutWeight,
                    weightedMode = keysSpan[i].WeightedMode
                });
            }

            return dto;
        }

        private static string SerializeExclusionMode(ExclusionMode mode)
        {
            return mode switch
            {
                ExclusionMode.LastWins => "lastWins",
                ExclusionMode.Blend => "blend",
                _ => "lastWins"
            };
        }

        private static string SerializeTransitionCurveType(TransitionCurveType type)
        {
            return type switch
            {
                TransitionCurveType.Linear => "linear",
                TransitionCurveType.EaseIn => "easeIn",
                TransitionCurveType.EaseOut => "easeOut",
                TransitionCurveType.EaseInOut => "easeInOut",
                TransitionCurveType.Custom => "custom",
                _ => "linear"
            };
        }

        // --- Config 変換 ---

        private static FacialControlConfig ConvertToConfig(ConfigDto dto)
        {
            var oscMappings = ConvertOscMappings(dto.osc?.mapping);
            var osc = new OscConfiguration(
                dto.osc?.sendPort ?? OscConfiguration.DefaultSendPort,
                dto.osc?.receivePort ?? OscConfiguration.DefaultReceivePort,
                dto.osc?.preset ?? OscConfiguration.DefaultPreset,
                oscMappings);

            var cache = new CacheConfiguration(
                dto.cache?.animationClipLruSize ?? CacheConfiguration.DefaultAnimationClipLruSize);

            return new FacialControlConfig(dto.schemaVersion, osc, cache);
        }

        private static OscMapping[] ConvertOscMappings(List<OscMappingDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<OscMapping>();

            var mappings = new OscMapping[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                mappings[i] = new OscMapping(d.oscAddress, d.blendShapeName, d.layer);
            }
            return mappings;
        }

        private static ConfigDto ConvertToConfigDto(FacialControlConfig config)
        {
            var dto = new ConfigDto
            {
                schemaVersion = config.SchemaVersion,
                osc = new OscConfigurationDto
                {
                    sendPort = config.Osc.SendPort,
                    receivePort = config.Osc.ReceivePort,
                    preset = config.Osc.Preset,
                    mapping = new List<OscMappingDto>()
                },
                cache = new CacheConfigurationDto
                {
                    animationClipLruSize = config.Cache.AnimationClipLruSize
                }
            };

            var mappingSpan = config.Osc.Mapping.Span;
            for (int i = 0; i < mappingSpan.Length; i++)
            {
                dto.osc.mapping.Add(new OscMappingDto
                {
                    oscAddress = mappingSpan[i].OscAddress,
                    blendShapeName = mappingSpan[i].BlendShapeName,
                    layer = mappingSpan[i].Layer
                });
            }

            return dto;
        }

        // ====================================================================
        // DTO 定義（JsonUtility 用の Serializable クラス）
        // ====================================================================

        [Serializable]
        private class ProfileDto
        {
            public string schemaVersion;
            public List<LayerDto> layers;
            public List<ExpressionDto> expressions;
        }

        [Serializable]
        private class LayerDto
        {
            public string name;
            public int priority;
            public string exclusionMode;
        }

        [Serializable]
        private class ExpressionDto
        {
            public string id;
            public string name;
            public string layer;
            public float transitionDuration = 0.25f;
            public TransitionCurveDto transitionCurve;
            public List<BlendShapeMappingDto> blendShapeValues;
            public List<LayerSlotDto> layerSlots;
        }

        [Serializable]
        private class TransitionCurveDto
        {
            public string type;
            public List<CurveKeyFrameDto> keys;
        }

        [Serializable]
        private class CurveKeyFrameDto
        {
            public float time;
            public float value;
            public float inTangent;
            public float outTangent;
            public float inWeight;
            public float outWeight;
            public int weightedMode;
        }

        [Serializable]
        private class BlendShapeMappingDto
        {
            public string name;
            public float value;
            public string renderer;
        }

        [Serializable]
        private class LayerSlotDto
        {
            public string layer;
            public List<BlendShapeMappingDto> blendShapeValues;
        }

        [Serializable]
        private class ConfigDto
        {
            public string schemaVersion;
            public OscConfigurationDto osc;
            public CacheConfigurationDto cache;
        }

        [Serializable]
        private class OscConfigurationDto
        {
            public int sendPort = OscConfiguration.DefaultSendPort;
            public int receivePort = OscConfiguration.DefaultReceivePort;
            public string preset = OscConfiguration.DefaultPreset;
            public List<OscMappingDto> mapping;
        }

        [Serializable]
        private class OscMappingDto
        {
            public string oscAddress;
            public string blendShapeName;
            public string layer;
        }

        [Serializable]
        private class CacheConfigurationDto
        {
            public int animationClipLruSize = CacheConfiguration.DefaultAnimationClipLruSize;
        }
    }
}
