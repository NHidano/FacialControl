using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// <c>IFacialMocapRuntimeSettingsSO</c> の JSON 表現（canonical フィールド）。
    /// サンプル JSON / ドキュメント / SO のラウンドトリップで共有する単一の真実。
    /// </summary>
    /// <remarks>
    /// enum は文字列で表現する（<c>dataVersion</c>: <c>standard</c>/<c>v2</c>、
    /// <c>failSafeMode</c>: <c>revertToBase</c>/<c>holdLastValue</c>）。フィールド名は
    /// アンダースコア無し camelCase。
    /// </remarks>
    [Serializable]
    public sealed class IFacialMocapOptionsDto
    {
        public int schemaVersion = 1;
        public string label = string.Empty;

        public bool receiverEnabled = true;
        public int listenPort = 49983;
        public string deviceAddress = string.Empty;
        public bool sendHandshake = false;
        public string dataVersion = "standard";
        public float handshakeIntervalSeconds = 1f;
        public float stalenessSeconds = 0f;
        public string failSafeMode = "revertToBase";

        public bool enableGaze = true;
        public float eyeMaxYawDegrees = 30f;
        public float eyeMaxPitchDegrees = 25f;

        public bool enableHead = true;
        public bool includeHeadPosition = false;

        /// <summary>本 DTO を JSON 文字列へ整形する。</summary>
        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        /// <summary>JSON 文字列を DTO に復元する。空/null/失敗時は既定値の新規インスタンス。</summary>
        public static IFacialMocapOptionsDto FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new IFacialMocapOptionsDto();
            }

            return JsonUtility.FromJson<IFacialMocapOptionsDto>(json) ?? new IFacialMocapOptionsDto();
        }
    }
}
