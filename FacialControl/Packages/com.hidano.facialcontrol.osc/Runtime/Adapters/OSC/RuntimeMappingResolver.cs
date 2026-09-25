using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    public static class RuntimeMappingResolver
    {
        private static readonly HashSet<string> _arkit52Names = new HashSet<string>(ARKitDetector.ARKit52Names);

        public readonly struct ResolveResult
        {
            public ResolveResult(
                OscMapping[] runtimeMappings,
                OscReceiverAdapterBinding.MappingOrigin[] origins,
                int manualCount,
                int heartbeatAutoCount)
            {
                RuntimeMappings = runtimeMappings ?? Array.Empty<OscMapping>();
                Origins = origins ?? Array.Empty<OscReceiverAdapterBinding.MappingOrigin>();
                ManualCount = manualCount;
                HeartbeatAutoCount = heartbeatAutoCount;
            }

            public OscMapping[] RuntimeMappings { get; }
            public OscReceiverAdapterBinding.MappingOrigin[] Origins { get; }
            public int ManualCount { get; }
            public int HeartbeatAutoCount { get; }
        }

        public static ResolveResult ResolveInitialMappings(IReadOnlyList<OscMappingEntry> manualEntries)
        {
            int manualCount = CountManualEntries(manualEntries);
            if (manualCount == 0)
            {
                return new ResolveResult(
                    Array.Empty<OscMapping>(),
                    Array.Empty<OscReceiverAdapterBinding.MappingOrigin>(),
                    0,
                    0);
            }

            var runtimeMappings = new OscMapping[manualCount];
            var origins = new OscReceiverAdapterBinding.MappingOrigin[manualCount];
            FillManualMappings(manualEntries, runtimeMappings, origins, out int written);

            return new ResolveResult(runtimeMappings, origins, written, 0);
        }

        public static ResolveResult MergeWithHeartbeat(
            IReadOnlyList<OscMappingEntry> manualEntries,
            IReadOnlyList<string> heartbeatBlendShapeNames,
            IReadOnlyList<string> meshBlendShapeNames,
            AddressPresetKind preset,
            string customPrefix,
            ref bool warnedOnEmptyIntersection,
            ref bool warnedOnAddressCollision)
        {
            ResolveResult manualOnly = ResolveInitialMappings(manualEntries);
            if (heartbeatBlendShapeNames == null || heartbeatBlendShapeNames.Count == 0 ||
                meshBlendShapeNames == null || meshBlendShapeNames.Count == 0)
            {
                return manualOnly;
            }

            var manualNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < manualOnly.RuntimeMappings.Length; i++)
            {
                manualNames.Add(manualOnly.RuntimeMappings[i].BlendShapeName);
            }

            var meshNames = new HashSet<string>(meshBlendShapeNames, StringComparer.Ordinal);
            var heartbeatAutoNames = new List<string>(heartbeatBlendShapeNames.Count);
            var seenHeartbeatNames = new HashSet<string>(StringComparer.Ordinal);
            bool hasIntersection = false;

            for (int i = 0; i < heartbeatBlendShapeNames.Count; i++)
            {
                string name = heartbeatBlendShapeNames[i];
                if (string.IsNullOrEmpty(name) || !meshNames.Contains(name))
                {
                    continue;
                }

                hasIntersection = true;
                if (manualNames.Contains(name) || !seenHeartbeatNames.Add(name))
                {
                    continue;
                }

                heartbeatAutoNames.Add(name);
            }

            if (!hasIntersection)
            {
                WarnEmptyIntersectionOnce(ref warnedOnEmptyIntersection);
                return manualOnly;
            }

            int totalCount = manualOnly.RuntimeMappings.Length + heartbeatAutoNames.Count;
            var runtimeMappings = new OscMapping[totalCount];
            var origins = new OscReceiverAdapterBinding.MappingOrigin[totalCount];
            Array.Copy(manualOnly.RuntimeMappings, runtimeMappings, manualOnly.RuntimeMappings.Length);
            Array.Copy(manualOnly.Origins, origins, manualOnly.Origins.Length);

            for (int i = 0; i < heartbeatAutoNames.Count; i++)
            {
                string name = heartbeatAutoNames[i];
                string address = FormatHeartbeatAddress(name, preset, customPrefix, ref warnedOnAddressCollision);
                int index = manualOnly.RuntimeMappings.Length + i;
                runtimeMappings[index] = new OscMapping(address, name, string.Empty);
                origins[index] = OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto;
            }

            return new ResolveResult(
                runtimeMappings,
                origins,
                manualOnly.ManualCount,
                heartbeatAutoNames.Count);
        }

        private static int CountManualEntries(IReadOnlyList<OscMappingEntry> entries)
        {
            if (entries == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsValidManualEntry(entries[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static void FillManualMappings(
            IReadOnlyList<OscMappingEntry> entries,
            OscMapping[] runtimeMappings,
            OscReceiverAdapterBinding.MappingOrigin[] origins,
            out int written)
        {
            written = 0;
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                OscMappingEntry entry = entries[i];
                if (!IsValidManualEntry(entry))
                {
                    continue;
                }

                runtimeMappings[written] = new OscMapping(entry.addressPattern, entry.expressionId, string.Empty);
                origins[written] = OscReceiverAdapterBinding.MappingOrigin.Manual;
                written++;
            }
        }

        private static bool IsValidManualEntry(OscMappingEntry entry)
        {
            return entry != null &&
                entry.mode == OscMappingMode.Normal_BlendShape &&
                !string.IsNullOrEmpty(entry.expressionId) &&
                !string.IsNullOrEmpty(entry.addressPattern);
        }

        private static string FormatHeartbeatAddress(
            string blendShapeName,
            AddressPresetKind preset,
            string customPrefix,
            ref bool warnedOnAddressCollision)
        {
            switch (preset)
            {
                case AddressPresetKind.ARKit:
                    return _arkit52Names.Contains(blendShapeName)
                        ? OscAddressFormatter.FormatBlendShapeAddress(AddressPresetKind.ARKit, blendShapeName)
                        : OscAddressFormatter.FormatBlendShapeAddress(AddressPresetKind.VRChat, blendShapeName);
                case AddressPresetKind.Custom:
                    if (!string.IsNullOrEmpty(customPrefix))
                    {
                        return OscAddressFormatter.FormatBlendShapeAddress(customPrefix, blendShapeName);
                    }

                    WarnAddressCollisionOnce(ref warnedOnAddressCollision);
                    return OscAddressFormatter.FormatBlendShapeAddress(AddressPresetKind.VRChat, blendShapeName);
                default:
                    return OscAddressFormatter.FormatBlendShapeAddress(AddressPresetKind.VRChat, blendShapeName);
            }
        }

        private static void WarnEmptyIntersectionOnce(ref bool warnedOnEmptyIntersection)
        {
            if (warnedOnEmptyIntersection)
            {
                return;
            }

            warnedOnEmptyIntersection = true;
            Debug.LogWarning("[RuntimeMappingResolver] heartbeat and mesh BlendShape intersection is empty; keeping manual mappings only.");
        }

        private static void WarnAddressCollisionOnce(ref bool warnedOnAddressCollision)
        {
            if (warnedOnAddressCollision)
            {
                return;
            }

            warnedOnAddressCollision = true;
            Debug.LogWarning("[RuntimeMappingResolver] custom preset address is unavailable; using VRChat address fallback.");
        }
    }
}
