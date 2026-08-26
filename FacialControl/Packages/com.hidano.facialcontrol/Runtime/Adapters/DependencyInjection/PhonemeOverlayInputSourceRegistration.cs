using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.DependencyInjection
{
    internal static class PhonemeOverlayInputSourceRegistration
    {
        public static void RegisterDeclaredSlots(in AdapterBuildContext context)
        {
            var registry = context.InputSourceRegistry;
            var slots = context.Profile.Slots.Span;
            if (registry == null || slots.Length == 0)
            {
                return;
            }

            HashSet<string> registeredSlots = null;
            for (int i = 0; i < slots.Length; i++)
            {
                string slot = slots[i];
                if (!PhonemeOverlaySlots.IsReserved(slot))
                {
                    continue;
                }

                if (registeredSlots == null)
                {
                    registeredSlots = new HashSet<string>(StringComparer.Ordinal);
                }
                if (!registeredSlots.Add(slot))
                {
                    continue;
                }

                string inputSourceId = OverlayInputSource.ReservedIdPrefix + ":" + slot;
                if (registry.TryResolve(inputSourceId, out _))
                {
                    continue;
                }

                registry.Register(
                    AdapterSlug.Parse(OverlayInputSource.ReservedIdPrefix),
                    slot,
                    new OverlayInputSource(
                        InputSourceId.Parse(inputSourceId),
                        slot,
                        context.BlendShapeNames.Count,
                        context.BlendShapeNames,
                        context.Profile,
                        context.ActiveExpressionProvider,
                        "emotion"));
            }
        }
    }
}
