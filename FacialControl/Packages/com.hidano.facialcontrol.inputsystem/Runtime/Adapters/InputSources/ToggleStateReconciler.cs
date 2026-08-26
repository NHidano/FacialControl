using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.InputSources
{
    internal interface IToggleStateEntry
    {
        string ExpressionId { get; }
        bool IsActive { get; set; }
    }

    internal static class ToggleStateReconciler
    {
        public static bool TryFlip(bool isTriggerInputSuspended, IToggleStateEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (isTriggerInputSuspended)
            {
                return false;
            }

            entry.IsActive = !entry.IsActive;
            return true;
        }

        public static void SyncWithStack(IReadOnlyList<string> activeExpressionIds, IToggleStateEntry entry)
        {
            if (activeExpressionIds == null) throw new ArgumentNullException(nameof(activeExpressionIds));
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            string expressionId = entry.ExpressionId;
            bool isActive = false;
            for (int i = 0; i < activeExpressionIds.Count; i++)
            {
                if (string.Equals(activeExpressionIds[i], expressionId, StringComparison.Ordinal))
                {
                    isActive = true;
                    break;
                }
            }

            entry.IsActive = isActive;
        }
    }
}
