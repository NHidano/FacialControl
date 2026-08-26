using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// AnimationClip サンプリング由来の単一ボーン姿勢スナップショット値型。
    /// (BonePath, Position(X,Y,Z), Euler(X,Y,Z), Scale(X,Y,Z)) の 1 string + 9 float を
    /// 不変に保持する Domain 値型で、Unity 非依存（UnityEngine.* を参照しない）。
    /// <para>
    /// readonly struct + immutable string + float のため Domain レベルで防御コピーは不要。
    /// 配列としての防御コピー責務は ExpressionSnapshot 側が持つ。
    /// </para>
    /// </summary>
    public readonly struct BoneSnapshot : IEquatable<BoneSnapshot>
    {
        /// <summary>対象ボーンの Transform 階層パス（null は空文字に正規化）。</summary>
        public string BonePath { get; }

        /// <summary>X 軸ローカル位置。</summary>
        public float PositionX { get; }
        /// <summary>Y 軸ローカル位置。</summary>
        public float PositionY { get; }
        /// <summary>Z 軸ローカル位置。</summary>
        public float PositionZ { get; }

        /// <summary>X 軸オイラー角（度）。</summary>
        public float EulerX { get; }
        /// <summary>Y 軸オイラー角（度）。</summary>
        public float EulerY { get; }
        /// <summary>Z 軸オイラー角（度）。</summary>
        public float EulerZ { get; }

        /// <summary>X 軸ローカルスケール。</summary>
        public float ScaleX { get; }
        /// <summary>Y 軸ローカルスケール。</summary>
        public float ScaleY { get; }
        /// <summary>Z 軸ローカルスケール。</summary>
        public float ScaleZ { get; }

        /// <summary>
        /// BoneSnapshot を生成する。
        /// </summary>
        /// <param name="bonePath">ボーン階層パス（null は空文字扱い）</param>
        /// <param name="positionX">X 軸位置</param>
        /// <param name="positionY">Y 軸位置</param>
        /// <param name="positionZ">Z 軸位置</param>
        /// <param name="eulerX">X 軸オイラー角（度）</param>
        /// <param name="eulerY">Y 軸オイラー角（度）</param>
        /// <param name="eulerZ">Z 軸オイラー角（度）</param>
        /// <param name="scaleX">X 軸スケール</param>
        /// <param name="scaleY">Y 軸スケール</param>
        /// <param name="scaleZ">Z 軸スケール</param>
        public BoneSnapshot(
            string bonePath,
            float positionX, float positionY, float positionZ,
            float eulerX, float eulerY, float eulerZ,
            float scaleX, float scaleY, float scaleZ)
        {
            BonePath = bonePath ?? string.Empty;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            EulerX = eulerX;
            EulerY = eulerY;
            EulerZ = eulerZ;
            ScaleX = scaleX;
            ScaleY = scaleY;
            ScaleZ = scaleZ;
        }

        public bool Equals(BoneSnapshot other)
        {
            return string.Equals(BonePath, other.BonePath, StringComparison.Ordinal)
                && PositionX.Equals(other.PositionX)
                && PositionY.Equals(other.PositionY)
                && PositionZ.Equals(other.PositionZ)
                && EulerX.Equals(other.EulerX)
                && EulerY.Equals(other.EulerY)
                && EulerZ.Equals(other.EulerZ)
                && ScaleX.Equals(other.ScaleX)
                && ScaleY.Equals(other.ScaleY)
                && ScaleZ.Equals(other.ScaleZ);
        }

        public override bool Equals(object obj) => obj is BoneSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (BonePath != null ? StringComparer.Ordinal.GetHashCode(BonePath) : 0);
                hash = (hash * 31) + PositionX.GetHashCode();
                hash = (hash * 31) + PositionY.GetHashCode();
                hash = (hash * 31) + PositionZ.GetHashCode();
                hash = (hash * 31) + EulerX.GetHashCode();
                hash = (hash * 31) + EulerY.GetHashCode();
                hash = (hash * 31) + EulerZ.GetHashCode();
                hash = (hash * 31) + ScaleX.GetHashCode();
                hash = (hash * 31) + ScaleY.GetHashCode();
                hash = (hash * 31) + ScaleZ.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(BoneSnapshot left, BoneSnapshot right) => left.Equals(right);

        public static bool operator !=(BoneSnapshot left, BoneSnapshot right) => !left.Equals(right);
    }
}
