using System;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// basis bone の <c>localRotation</c> と顔相対 Euler から最終 <c>localRotation</c> を合成する Domain サービス。
    /// Unity の <c>Quaternion.Euler(x, y, z)</c> と数学的等価な Z-X-Y Tait-Bryan 順（intrinsic Y-X-Z 相当）を採用する。
    /// Domain 層配置のため <c>UnityEngine.Quaternion</c> を使わず float 4 タプル (qx, qy, qz, qw) で実装する。
    /// pure function、副作用なし、ヒープ確保なし（hot path 仕様）。
    /// </summary>
    public static class BonePoseComposer
    {
        private const float DegToRad = (float)(Math.PI / 180.0);

        /// <summary>
        /// Euler degrees → quaternion (qx, qy, qz, qw) を計算する。
        /// 適用順は Unity と同じく「Z 軸 → X 軸 → Y 軸」（ベクトルへの作用としては R = Ry * Rx * Rz）。
        /// </summary>
        public static void EulerToQuaternion(
            float eulerXDeg, float eulerYDeg, float eulerZDeg,
            out float qx, out float qy, out float qz, out float qw)
        {
            float hx = eulerXDeg * DegToRad * 0.5f;
            float hy = eulerYDeg * DegToRad * 0.5f;
            float hz = eulerZDeg * DegToRad * 0.5f;

            float sx = (float)Math.Sin(hx);
            float cx = (float)Math.Cos(hx);
            float sy = (float)Math.Sin(hy);
            float cy = (float)Math.Cos(hy);
            float sz = (float)Math.Sin(hz);
            float cz = (float)Math.Cos(hz);

            // q = qY * qX * qZ
            qx = cy * sx * cz + sy * cx * sz;
            qy = sy * cx * cz - cy * sx * sz;
            qz = cy * cx * sz - sy * sx * cz;
            qw = cy * cx * cz + sy * sx * sz;
        }

        /// <summary>
        /// (basisX, basisY, basisZ, basisW) × Euler(eulerXDeg, eulerYDeg, eulerZDeg) を Hamilton 積で合成する。
        /// 結果は basis 相対の最終 <c>localRotation</c>。順序は basis * offset で固定（非可換）。
        /// </summary>
        public static void Compose(
            float basisX, float basisY, float basisZ, float basisW,
            float eulerXDeg, float eulerYDeg, float eulerZDeg,
            out float outX, out float outY, out float outZ, out float outW)
        {
            EulerToQuaternion(
                eulerXDeg, eulerYDeg, eulerZDeg,
                out float ex, out float ey, out float ez, out float ew);

            // basis * offset（Hamilton product）
            outX = basisW * ex + basisX * ew + basisY * ez - basisZ * ey;
            outY = basisW * ey - basisX * ez + basisY * ew + basisZ * ex;
            outZ = basisW * ez + basisX * ey - basisY * ex + basisZ * ew;
            outW = basisW * ew - basisX * ex - basisY * ey - basisZ * ez;
        }
    }
}
