using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.IFacialMocap
{
    /// <summary>
    /// 単一 BlendShape の (名前, 値) サンプル。値は iFacialMocap のスケール (0〜100)。
    /// </summary>
    public struct IFacialMocapBlendShapeSample
    {
        public string Name;

        /// <summary>0〜100（v2 では負値もありうる）。</summary>
        public float Value;

        public IFacialMocapBlendShapeSample(string name, float value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// head / rightEye / leftEye の Transform サンプル。オイラー角は度。
    /// eye は euler のみ使用し position は 0。
    /// </summary>
    public struct IFacialMocapTransformSample
    {
        public bool HasValue;
        public float EulerX;
        public float EulerY;
        public float EulerZ;
        public float PositionX;
        public float PositionY;
        public float PositionZ;
    }

    /// <summary>
    /// 1 パケット分の解析結果を保持する再利用可能フレーム。
    /// <see cref="IFacialMocapPacketParser.TryParse"/> が <see cref="Clear"/> 後に書き込む。
    /// </summary>
    /// <remarks>
    /// 受信スレッドで生成し、main スレッドが読む構造のため、外部で適切に lock すること
    /// （本クラス自体はスレッドセーフではない）。
    /// </remarks>
    public sealed class IFacialMocapFrame
    {
        /// <summary>受信した BlendShape サンプル列（受信順）。</summary>
        public readonly List<IFacialMocapBlendShapeSample> BlendShapes = new List<IFacialMocapBlendShapeSample>(64);

        public IFacialMocapTransformSample Head;
        public IFacialMocapTransformSample RightEye;
        public IFacialMocapTransformSample LeftEye;

        /// <summary>解析結果を初期化する。</summary>
        public void Clear()
        {
            BlendShapes.Clear();
            Head = default;
            RightEye = default;
            LeftEye = default;
        }

        /// <summary>main スレッド側のスナップショットへ内容をコピーする（受信→読取のハンドオフ用）。</summary>
        public void CopyTo(IFacialMocapFrame destination)
        {
            destination.BlendShapes.Clear();
            destination.BlendShapes.AddRange(BlendShapes);
            destination.Head = Head;
            destination.RightEye = RightEye;
            destination.LeftEye = LeftEye;
        }
    }
}
