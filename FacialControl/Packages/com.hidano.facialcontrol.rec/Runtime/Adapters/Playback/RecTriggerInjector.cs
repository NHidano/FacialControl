using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.Playback
{
    /// <summary>
    /// Drives original trigger sources during playback without replacing the live pipeline.
    /// </summary>
    public sealed class RecTriggerInjector : ITriggerInjectionPort
    {
        private static readonly string[] EmptyExpressionIds = Array.Empty<string>();

        private readonly Func<string, ExpressionTriggerInputSourceBase> _resolveTriggerSource;
        private readonly Func<IReadOnlyList<ExpressionTriggerInputSourceBase>> _getAllTriggerSources;
        private readonly HashSet<string> _warnedMissingSourceIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _resolvedSourceIdsBuffer = new HashSet<string>(StringComparer.Ordinal);

        public RecTriggerInjector(
            Func<string, ExpressionTriggerInputSourceBase> resolveTriggerSource,
            Func<IReadOnlyList<ExpressionTriggerInputSourceBase>> getAllTriggerSources)
        {
            _resolveTriggerSource = resolveTriggerSource ?? throw new ArgumentNullException(nameof(resolveTriggerSource));
            _getAllTriggerSources = getAllTriggerSources ?? throw new ArgumentNullException(nameof(getAllTriggerSources));
        }

        public void EstablishBaseline(RecBaselineState baseline)
        {
            IReadOnlyList<ExpressionTriggerInputSourceBase> triggerSources = _getAllTriggerSources() ?? Array.Empty<ExpressionTriggerInputSourceBase>();
            RecBaselineState safeBaseline = baseline ?? RecBaselineState.Empty;

            _resolvedSourceIdsBuffer.Clear();

            for (int i = 0; i < triggerSources.Count; i++)
            {
                ExpressionTriggerInputSourceBase triggerSource = triggerSources[i];
                if (triggerSource == null)
                {
                    continue;
                }

                _resolvedSourceIdsBuffer.Add(triggerSource.Id);

                if (safeBaseline.TryGetTriggerStack(triggerSource.Id, out IReadOnlyList<string> expressionIds))
                {
                    triggerSource.ResetToExpressionStack(expressionIds);
                    continue;
                }

                triggerSource.ResetToExpressionStack(EmptyExpressionIds);
            }

            IReadOnlyList<RecBaselineState.TriggerEntry> triggerEntries = safeBaseline.TriggerEntries;
            for (int i = 0; i < triggerEntries.Count; i++)
            {
                string sourceId = triggerEntries[i].SourceId;
                if (!_resolvedSourceIdsBuffer.Contains(sourceId))
                {
                    WarnMissingSourceOnce(sourceId);
                }
            }

            _resolvedSourceIdsBuffer.Clear();
        }

        public void InjectTriggerOn(string sourceId, string expressionId)
        {
            if (!TryResolveSource(sourceId, out ExpressionTriggerInputSourceBase triggerSource))
            {
                return;
            }

            triggerSource.TriggerOn(expressionId);
        }

        public void InjectTriggerOff(string sourceId, string expressionId)
        {
            if (!TryResolveSource(sourceId, out ExpressionTriggerInputSourceBase triggerSource))
            {
                return;
            }

            triggerSource.TriggerOff(expressionId);
        }

        private bool TryResolveSource(string sourceId, out ExpressionTriggerInputSourceBase triggerSource)
        {
            triggerSource = null;

            if (string.IsNullOrEmpty(sourceId))
            {
                WarnMissingSourceOnce(sourceId);
                return false;
            }

            triggerSource = _resolveTriggerSource(sourceId);
            if (triggerSource != null)
            {
                return true;
            }

            WarnMissingSourceOnce(sourceId);
            return false;
        }

        private void WarnMissingSourceOnce(string sourceId)
        {
            string normalizedSourceId = string.IsNullOrEmpty(sourceId) ? "<null-or-empty>" : sourceId;
            if (!_warnedMissingSourceIds.Add(normalizedSourceId))
            {
                return;
            }

            Debug.LogWarning($"Playback skipped trigger injection because sourceId '{normalizedSourceId}' could not be resolved.");
        }
    }
}
