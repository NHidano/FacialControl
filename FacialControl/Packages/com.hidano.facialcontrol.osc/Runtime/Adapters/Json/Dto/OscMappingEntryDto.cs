using System;
using Hidano.FacialControl.Adapters.OSC;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    [Serializable]
    public sealed class OscMappingEntryDto : ISerializationCallbackReceiver
    {
        public const string ModeBlendShape = "blendShape";
        public const string ModeGazeVrchatXy = "gazeVrchatXy";
        public const string ModeGazeArkit8Bs = "gazeArkit8Bs";

        public string mode = ModeBlendShape;
        public string expressionId = string.Empty;
        public string addressPattern = string.Empty;
        public string sourceIdLeft = string.Empty;
        public string sourceIdRight = string.Empty;
        public bool leftRightIndependent;

        public static OscMappingEntryDto FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            OscMappingEntryDto dto = JsonUtility.FromJson<OscMappingEntryDto>(json);
            if (dto == null)
            {
                return null;
            }

            return dto.NormalizeForRuntime(logWarnings: true) ? dto : null;
        }

        public string ToJson(bool prettyPrint = true)
        {
            NormalizeForSerialization();
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public bool TryToMappingEntry(out OscMappingEntry entry)
        {
            return TryToMappingEntry(logWarnings: true, out entry);
        }

        internal bool TryToMappingEntry(bool logWarnings, out OscMappingEntry entry)
        {
            if (!NormalizeForRuntime(logWarnings))
            {
                entry = null;
                return false;
            }

            entry = new OscMappingEntry
            {
                mode = ToOscMappingMode(mode),
                expressionId = expressionId ?? string.Empty,
                addressPattern = addressPattern ?? string.Empty,
                sourceIdLeft = sourceIdLeft ?? string.Empty,
                sourceIdRight = sourceIdRight ?? string.Empty,
                leftRightIndependent = leftRightIndependent
            };
            return true;
        }

        public static OscMappingEntryDto FromMappingEntry(OscMappingEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new OscMappingEntryDto
            {
                mode = ToModeString(entry.mode),
                expressionId = entry.expressionId ?? string.Empty,
                addressPattern = entry.mode == OscMappingMode.Gaze_ARKit_8BS
                    ? string.Empty
                    : entry.addressPattern ?? string.Empty,
                sourceIdLeft = entry.sourceIdLeft ?? string.Empty,
                sourceIdRight = entry.sourceIdRight ?? string.Empty,
                leftRightIndependent = entry.leftRightIndependent
            };
        }

        public static string ToModeString(OscMappingMode mappingMode)
        {
            switch (mappingMode)
            {
                case OscMappingMode.Gaze_VRChat_XY:
                    return ModeGazeVrchatXy;
                case OscMappingMode.Gaze_ARKit_8BS:
                    return ModeGazeArkit8Bs;
                case OscMappingMode.Normal_BlendShape:
                default:
                    return ModeBlendShape;
            }
        }

        public static OscMappingMode ToOscMappingMode(string mode)
        {
            string normalized = NormalizeModeString(mode);
            if (string.Equals(normalized, ModeGazeVrchatXy, StringComparison.Ordinal))
            {
                return OscMappingMode.Gaze_VRChat_XY;
            }

            if (string.Equals(normalized, ModeGazeArkit8Bs, StringComparison.Ordinal))
            {
                return OscMappingMode.Gaze_ARKit_8BS;
            }

            return OscMappingMode.Normal_BlendShape;
        }

        public void OnBeforeSerialize()
        {
            NormalizeForSerialization();
        }

        public void OnAfterDeserialize()
        {
            NormalizeCommon();
        }

        internal bool NormalizeForRuntime(bool logWarnings)
        {
            NormalizeCommon();

            OscMappingMode mappingMode = ToOscMappingMode(mode);
            mode = ToModeString(mappingMode);

            if (string.IsNullOrEmpty(expressionId))
            {
                if (logWarnings)
                {
                    Debug.LogWarning("[OscMappingEntryDto] expressionId is required. Skipping OSC mapping entry.");
                }

                return false;
            }

            if (mappingMode == OscMappingMode.Normal_BlendShape && string.IsNullOrEmpty(addressPattern))
            {
                if (logWarnings)
                {
                    Debug.LogWarning(
                        $"[OscMappingEntryDto] blendShape entry '{expressionId}' requires a complete addressPattern. Skipping.");
                }

                return false;
            }

            if (mappingMode == OscMappingMode.Gaze_VRChat_XY && string.IsNullOrEmpty(addressPattern))
            {
                if (logWarnings)
                {
                    Debug.LogWarning(
                        $"[OscMappingEntryDto] gazeVrchatXy entry '{expressionId}' requires a base addressPattern. Skipping.");
                }

                return false;
            }

            if (mappingMode == OscMappingMode.Gaze_ARKit_8BS && !string.IsNullOrEmpty(addressPattern))
            {
                if (logWarnings)
                {
                    Debug.Log(
                        $"[OscMappingEntryDto] gazeArkit8Bs entry '{expressionId}' addressPattern is ignored.");
                }

                addressPattern = string.Empty;
            }

            if (IsGazeMode(mappingMode)
                && leftRightIndependent
                && (string.IsNullOrEmpty(sourceIdLeft) || string.IsNullOrEmpty(sourceIdRight)))
            {
                if (logWarnings)
                {
                    Debug.LogWarning(
                        "[OscMappingEntryDto] leftRightIndependent=true requires sourceIdLeft/sourceIdRight. "
                        + $"Skipping gaze entry '{expressionId}'.");
                }

                return false;
            }

            return true;
        }

        private void NormalizeForSerialization()
        {
            NormalizeCommon();
            mode = NormalizeModeString(mode);
            if (ToOscMappingMode(mode) == OscMappingMode.Gaze_ARKit_8BS)
            {
                addressPattern = string.Empty;
            }
        }

        private void NormalizeCommon()
        {
            mode = NormalizeModeString(mode);
            expressionId = expressionId ?? string.Empty;
            addressPattern = addressPattern ?? string.Empty;
            sourceIdLeft = sourceIdLeft ?? string.Empty;
            sourceIdRight = sourceIdRight ?? string.Empty;
        }

        private static string NormalizeModeString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return ModeBlendShape;
            }

            string normalized = value.Trim();
            if (string.Equals(normalized, ModeGazeVrchatXy, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, nameof(OscMappingMode.Gaze_VRChat_XY), StringComparison.OrdinalIgnoreCase))
            {
                return ModeGazeVrchatXy;
            }

            if (string.Equals(normalized, ModeGazeArkit8Bs, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, nameof(OscMappingMode.Gaze_ARKit_8BS), StringComparison.OrdinalIgnoreCase))
            {
                return ModeGazeArkit8Bs;
            }

            return ModeBlendShape;
        }

        private static bool IsGazeMode(OscMappingMode mappingMode)
        {
            return mappingMode == OscMappingMode.Gaze_VRChat_XY
                || mappingMode == OscMappingMode.Gaze_ARKit_8BS;
        }
    }
}
