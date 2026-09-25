using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// InputAction 名と Expression ID を紐付けるイミュータブル値型。
    /// 押下時の動作モード (<see cref="TriggerMode"/>) も保持する。
    /// </summary>
    public readonly struct InputBinding : IEquatable<InputBinding>
    {
        public string ActionName { get; }
        public string ExpressionId { get; }
        public TriggerMode TriggerMode { get; }

        public InputBinding(string actionName, string expressionId)
            : this(actionName, expressionId, TriggerMode.Hold)
        {
        }

        public InputBinding(string actionName, string expressionId, TriggerMode triggerMode)
        {
            if (string.IsNullOrWhiteSpace(actionName))
            {
                throw new ArgumentException("actionName は null または空文字列にできません。", nameof(actionName));
            }

            if (string.IsNullOrWhiteSpace(expressionId))
            {
                throw new ArgumentException("expressionId は null または空文字列にできません。", nameof(expressionId));
            }

            ActionName = actionName;
            ExpressionId = expressionId;
            TriggerMode = triggerMode;
        }

        public bool Equals(InputBinding other)
        {
            return string.Equals(ActionName, other.ActionName, StringComparison.Ordinal)
                && string.Equals(ExpressionId, other.ExpressionId, StringComparison.Ordinal)
                && TriggerMode == other.TriggerMode;
        }

        public override bool Equals(object obj)
        {
            return obj is InputBinding other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (ActionName != null ? ActionName.GetHashCode() : 0);
                hash = hash * 31 + (ExpressionId != null ? ExpressionId.GetHashCode() : 0);
                hash = hash * 31 + (int)TriggerMode;
                return hash;
            }
        }

        public static bool operator ==(InputBinding left, InputBinding right) => left.Equals(right);

        public static bool operator !=(InputBinding left, InputBinding right) => !left.Equals(right);
    }
}
