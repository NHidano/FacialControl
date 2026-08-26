using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    public static class AddressPresetEstimator
    {
        public const string PresetVrChat = "vrchat";
        public const string PresetArKit = "arkit";
        public const string PresetCustom = "custom";

        private static readonly HashSet<string> _arkit52Names = new HashSet<string>(ARKitDetector.ARKit52Names);

        public readonly struct EstimationResult
        {
            public EstimationResult(AddressPresetKind preset, string customPrefix)
            {
                Preset = preset;
                CustomPrefix = customPrefix;
            }

            public AddressPresetKind Preset { get; }
            public string CustomPrefix { get; }
        }

        public static EstimationResult Estimate(
            string presetName,
            string presetCustomPrefix,
            IReadOnlyList<string> blendShapeNames,
            ref bool warnedOnUnknownPreset,
            ref bool warnedOnMissingCustomPrefix)
        {
            if (presetName != null)
            {
                if (string.Equals(presetName, PresetVrChat, StringComparison.OrdinalIgnoreCase))
                {
                    return new EstimationResult(AddressPresetKind.VRChat, null);
                }

                if (string.Equals(presetName, PresetArKit, StringComparison.OrdinalIgnoreCase))
                {
                    return new EstimationResult(AddressPresetKind.ARKit, null);
                }

                if (string.Equals(presetName, PresetCustom, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(presetCustomPrefix))
                    {
                        return new EstimationResult(AddressPresetKind.Custom, presetCustomPrefix);
                    }

                    WarnMissingCustomPrefixOnce(ref warnedOnMissingCustomPrefix);
                    return new EstimationResult(AddressPresetKind.VRChat, null);
                }

                WarnUnknownPresetOnce(presetName, ref warnedOnUnknownPreset);
            }

            return new EstimationResult(EstimateByBlendShapeNames(blendShapeNames), null);
        }

        private static AddressPresetKind EstimateByBlendShapeNames(IReadOnlyList<string> blendShapeNames)
        {
            if (blendShapeNames == null || blendShapeNames.Count == 0)
            {
                return AddressPresetKind.VRChat;
            }

            int arkitNameCount = 0;
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                string name = blendShapeNames[i];
                if (name != null && _arkit52Names.Contains(name))
                {
                    arkitNameCount++;
                }
            }

            return arkitNameCount * 2 >= ARKitDetector.ARKit52Names.Length
                ? AddressPresetKind.ARKit
                : AddressPresetKind.VRChat;
        }

        private static void WarnUnknownPresetOnce(string presetName, ref bool warnedOnUnknownPreset)
        {
            if (warnedOnUnknownPreset)
            {
                return;
            }

            warnedOnUnknownPreset = true;
            Debug.LogWarning($"[AddressPresetEstimator] unknown preset '{presetName}'; estimating address preset from BlendShape names.");
        }

        private static void WarnMissingCustomPrefixOnce(ref bool warnedOnMissingCustomPrefix)
        {
            if (warnedOnMissingCustomPrefix)
            {
                return;
            }

            warnedOnMissingCustomPrefix = true;
            Debug.LogWarning("[AddressPresetEstimator] custom preset is missing prefix; using VRChat fallback.");
        }
    }
}
