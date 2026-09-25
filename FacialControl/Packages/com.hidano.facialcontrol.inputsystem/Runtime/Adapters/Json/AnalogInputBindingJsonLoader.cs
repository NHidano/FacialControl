using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Json
{
    /// <summary>
    /// <see cref="AnalogInputBindingProfile"/> の JSON 永続化（Load / Save, 6.7, 6.8, 9.6）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>JsonUtility.FromJson&lt;AnalogInputBindingProfileDto&gt;</c> でデシリアライズ後、
    /// 各エントリを <see cref="AnalogBindingEntry"/> へ変換する。
    /// 不正エントリ（未知の <c>targetKind</c> / 欠損 <c>targetIdentifier</c> 等）は
    /// <see cref="Debug.LogWarning"/> + skip + 残余ロード継続。
    /// JSON パース自体に失敗した場合も警告ログを出して空プロファイルを返し、例外伝播はしない。
    /// </para>
    /// <para>
    /// targetKind / targetAxis の文字列値は大小無視で解釈する。
    /// dead-zone / scale / offset / curve / invert / clamp の値変換は
    /// Adapters 側 InputProcessor 経路で扱う。
    /// </para>
    /// </remarks>
    public static class AnalogInputBindingJsonLoader
    {
        /// <summary>
        /// JSON 文字列を <see cref="AnalogInputBindingProfile"/> へ変換する。
        /// </summary>
        /// <param name="json">JSON 文字列（null / 空 / 全空白は空プロファイルを返す）。</param>
        public static AnalogInputBindingProfile Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AnalogInputBindingProfile(string.Empty, Array.Empty<AnalogBindingEntry>());
            }

            AnalogInputBindingProfileDto dto;
            try
            {
                dto = JsonUtility.FromJson<AnalogInputBindingProfileDto>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"AnalogInputBindingJsonLoader: JSON のパースに失敗しました: {ex.Message}");
                return new AnalogInputBindingProfile(string.Empty, Array.Empty<AnalogBindingEntry>());
            }

            if (dto == null)
            {
                return new AnalogInputBindingProfile(string.Empty, Array.Empty<AnalogBindingEntry>());
            }

            var version = dto.version ?? string.Empty;

            if (dto.bindings == null || dto.bindings.Count == 0)
            {
                return new AnalogInputBindingProfile(version, Array.Empty<AnalogBindingEntry>());
            }

            var entries = new List<AnalogBindingEntry>(dto.bindings.Count);
            for (int i = 0; i < dto.bindings.Count; i++)
            {
                var entryDto = dto.bindings[i];
                if (TryConvertEntry(entryDto, i, out var entry))
                {
                    entries.Add(entry);
                }
            }

            return new AnalogInputBindingProfile(version, entries.ToArray());
        }

        /// <summary>
        /// <see cref="AnalogInputBindingProfile"/> を JSON 文字列へ変換する。
        /// </summary>
        /// <param name="profile">永続化対象プロファイル。</param>
        /// <param name="prettyPrint">JsonUtility の pretty print 指定（既定 true）。</param>
        public static string Save(in AnalogInputBindingProfile profile, bool prettyPrint = true)
        {
            var dto = new AnalogInputBindingProfileDto
            {
                version = profile.Version,
                bindings = new List<AnalogBindingEntryDto>(profile.Bindings.Length)
            };

            var bindings = profile.Bindings.Span;
            for (int i = 0; i < bindings.Length; i++)
            {
                dto.bindings.Add(ConvertEntryToDto(bindings[i]));
            }

            return JsonUtility.ToJson(dto, prettyPrint);
        }

        private static bool TryConvertEntry(AnalogBindingEntryDto dto, int index, out AnalogBindingEntry entry)
        {
            entry = default;

            if (dto == null)
            {
                Debug.LogWarning(
                    $"AnalogInputBindingJsonLoader: bindings[{index}] が null のため skip します。");
                return false;
            }

            if (string.IsNullOrWhiteSpace(dto.targetIdentifier))
            {
                Debug.LogWarning(
                    $"AnalogInputBindingJsonLoader: bindings[{index}] の targetIdentifier が空のため skip します。");
                return false;
            }

            if (dto.sourceAxis < 0)
            {
                Debug.LogWarning(
                    $"AnalogInputBindingJsonLoader: bindings[{index}] の sourceAxis が負 ({dto.sourceAxis}) のため skip します。");
                return false;
            }

            if (!TryParseTargetKind(dto.targetKind, out var targetKind))
            {
                Debug.LogWarning(
                    $"AnalogInputBindingJsonLoader: bindings[{index}] の targetKind '{dto.targetKind}' が未知のため skip します。");
                return false;
            }

            if (!TryParseTargetAxis(dto.targetAxis, out var targetAxis))
            {
                Debug.LogWarning(
                    $"AnalogInputBindingJsonLoader: bindings[{index}] の targetAxis '{dto.targetAxis}' が未知のため skip します。");
                return false;
            }

            // scale / direction は旧スキーマ JSON では欠落するので default に fallback する。
            // JsonUtility は欠落 float に 0 を入れるため、0 は「未設定」とみなして 1f に補正する。
            float scale = dto.scale == 0f ? 1f : dto.scale;
            if (!TryParseDirection(dto.direction, out var direction))
            {
                Debug.LogWarning(
                    $"AnalogInputBindingJsonLoader: bindings[{index}] の direction '{dto.direction}' が未知のため Bipolar として扱います。");
                direction = AnalogBindingDirection.Bipolar;
            }

            try
            {
                entry = new AnalogBindingEntry(
                    sourceId: dto.sourceId ?? string.Empty,
                    sourceAxis: dto.sourceAxis,
                    targetKind: targetKind,
                    targetIdentifier: dto.targetIdentifier,
                    targetAxis: targetAxis,
                    scale: scale,
                    direction: direction);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"AnalogInputBindingJsonLoader: bindings[{index}] の構築に失敗したため skip します: {ex.Message}");
                return false;
            }
        }

        private static bool TryParseDirection(string value, out AnalogBindingDirection result)
        {
            // 空・null は Bipolar 既定（旧スキーマ互換）。
            if (string.IsNullOrWhiteSpace(value))
            {
                result = AnalogBindingDirection.Bipolar;
                return true;
            }

            switch (value.ToLowerInvariant())
            {
                case "bipolar":
                    result = AnalogBindingDirection.Bipolar;
                    return true;
                case "positive":
                    result = AnalogBindingDirection.Positive;
                    return true;
                case "negative":
                    result = AnalogBindingDirection.Negative;
                    return true;
                default:
                    result = AnalogBindingDirection.Bipolar;
                    return false;
            }
        }

        private static bool TryParseTargetKind(string value, out AnalogBindingTargetKind result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = default;
                return false;
            }

            switch (value.ToLowerInvariant())
            {
                case "blendshape":
                    result = AnalogBindingTargetKind.BlendShape;
                    return true;
                case "bonepose":
                    result = AnalogBindingTargetKind.BonePose;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool TryParseTargetAxis(string value, out AnalogTargetAxis result)
        {
            // BlendShape ターゲットでは TargetAxis は無視されるため、null / 空は X 既定で許容する。
            if (string.IsNullOrWhiteSpace(value))
            {
                result = AnalogTargetAxis.X;
                return true;
            }

            switch (value.ToLowerInvariant())
            {
                case "x":
                    result = AnalogTargetAxis.X;
                    return true;
                case "y":
                    result = AnalogTargetAxis.Y;
                    return true;
                case "z":
                    result = AnalogTargetAxis.Z;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static AnalogBindingEntryDto ConvertEntryToDto(in AnalogBindingEntry entry)
        {
            return new AnalogBindingEntryDto
            {
                sourceId = entry.SourceId ?? string.Empty,
                sourceAxis = entry.SourceAxis,
                targetKind = SerializeTargetKind(entry.TargetKind),
                targetIdentifier = entry.TargetIdentifier,
                targetAxis = SerializeTargetAxis(entry.TargetAxis),
                scale = entry.Scale,
                direction = SerializeDirection(entry.Direction),
            };
        }

        private static string SerializeDirection(AnalogBindingDirection direction)
        {
            return direction switch
            {
                AnalogBindingDirection.Bipolar => "bipolar",
                AnalogBindingDirection.Positive => "positive",
                AnalogBindingDirection.Negative => "negative",
                _ => "bipolar"
            };
        }

        private static string SerializeTargetKind(AnalogBindingTargetKind kind)
        {
            return kind switch
            {
                AnalogBindingTargetKind.BlendShape => "blendshape",
                AnalogBindingTargetKind.BonePose => "bonepose",
                _ => "blendshape"
            };
        }

        private static string SerializeTargetAxis(AnalogTargetAxis axis)
        {
            return axis switch
            {
                AnalogTargetAxis.X => "X",
                AnalogTargetAxis.Y => "Y",
                AnalogTargetAxis.Z => "Z",
                _ => "X"
            };
        }
    }
}
