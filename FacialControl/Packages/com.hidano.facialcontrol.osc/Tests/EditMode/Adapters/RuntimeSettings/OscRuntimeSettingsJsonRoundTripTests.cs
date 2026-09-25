using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// task 3.3 の観測可能完了条件: 全フィールドを設定した <see cref="OscRuntimeSettingsSO"/> を
    /// <c>ToJson</c> → 新規 SO に <c>FromJson</c> で復元したとき全フィールドが一致することを検証する。
    /// enum (FailSafeMode / BundleInterpretationMode) は JSON 上で文字列として書き出される
    /// (要件 5.3, 7.3)。
    /// </summary>
    [TestFixture]
    public class OscRuntimeSettingsJsonRoundTripTests
    {
        private OscRuntimeSettingsSO _source;
        private OscRuntimeSettingsSO _restored;

        [SetUp]
        public void SetUp()
        {
            _source = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            _restored = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_source != null)
            {
                Object.DestroyImmediate(_source);
                _source = null;
            }

            if (_restored != null)
            {
                Object.DestroyImmediate(_restored);
                _restored = null;
            }
        }

        [Test]
        public void ToJson_FromJson_PreservesAllFields()
        {
            AssignAllFields(
                _source,
                label: "prod-osc",
                schemaVersion: 1,
                receiverEnabled: false,
                listenEndpoint: "192.168.1.10",
                listenPort: 9100,
                stalenessSeconds: 1.5f,
                failSafeMode: FailSafeMode.HoldLastValue,
                consistencyCheckWarnLog: false,
                bundleMode: BundleInterpretationMode.IndividualMessage,
                bundleAccumulationTimeoutMs: 12.5f,
                senderEnabled: false,
                endpoints: new List<OscSenderEndpointConfig>
                {
                    new OscSenderEndpointConfig("10.0.0.1", 9200, true, AddressPresetKind.VRChat),
                    new OscSenderEndpointConfig("10.0.0.2", 9300, false, AddressPresetKind.ARKit),
                },
                heartbeatIntervalSeconds: 7.5f,
                suppressLoopback: false);

            string json = _source.ToJson();
            _restored.FromJson(json);

            Assert.AreEqual(_source.Label, _restored.Label);
            Assert.AreEqual(_source.SchemaVersion, _restored.SchemaVersion);
            Assert.AreEqual(_source.ReceiverEnabled, _restored.ReceiverEnabled);
            Assert.AreEqual(_source.ListenEndpoint, _restored.ListenEndpoint);
            Assert.AreEqual(_source.ListenPort, _restored.ListenPort);
            Assert.AreEqual(_source.StalenessSeconds, _restored.StalenessSeconds);
            Assert.AreEqual(_source.FailSafeMode, _restored.FailSafeMode);
            Assert.AreEqual(_source.ConsistencyCheckWarnLog, _restored.ConsistencyCheckWarnLog);
            Assert.AreEqual(_source.BundleMode, _restored.BundleMode);
            Assert.AreEqual(_source.BundleAccumulationTimeoutMs, _restored.BundleAccumulationTimeoutMs);
            Assert.AreEqual(_source.SenderEnabled, _restored.SenderEnabled);
            Assert.AreEqual(_source.HeartbeatIntervalSeconds, _restored.HeartbeatIntervalSeconds);
            Assert.AreEqual(_source.SuppressLoopback, _restored.SuppressLoopback);

            Assert.AreEqual(_source.Endpoints.Count, _restored.Endpoints.Count);
            for (int i = 0; i < _source.Endpoints.Count; i++)
            {
                Assert.AreEqual(_source.Endpoints[i].endpoint, _restored.Endpoints[i].endpoint);
                Assert.AreEqual(_source.Endpoints[i].port, _restored.Endpoints[i].port);
                Assert.AreEqual(_source.Endpoints[i].enabled, _restored.Endpoints[i].enabled);
                Assert.AreEqual(_source.Endpoints[i].preset, _restored.Endpoints[i].preset);
            }
        }

        [Test]
        public void ToJson_FailSafeMode_WrittenAsString()
        {
            AssignFailSafeMode(_source, FailSafeMode.HoldLastValue);

            string json = _source.ToJson();

            StringAssert.Contains("\"failSafeMode\": \"holdLastValue\"", json);
        }

        [Test]
        public void ToJson_BundleMode_WrittenAsString()
        {
            AssignBundleMode(_source, BundleInterpretationMode.IndividualMessage);

            string json = _source.ToJson();

            StringAssert.Contains("\"bundleMode\": \"individualMessage\"", json);
        }

        [Test]
        public void FromJson_InvalidEnumStrings_NormalizedToDefaults()
        {
            string json = "{\"failSafeMode\":\"unknown\",\"bundleMode\":\"unknown\"}";

            _restored.FromJson(json);

            Assert.AreEqual(FailSafeMode.RevertToBase, _restored.FailSafeMode);
            Assert.AreEqual(BundleInterpretationMode.AtomicSwap, _restored.BundleMode);
        }

        [Test]
        public void FromJson_InvalidNumericFields_NormalizedToDefaults()
        {
            string json =
                "{\"listenPort\":0,\"bundleAccumulationTimeoutMs\":-1," +
                "\"heartbeatIntervalSeconds\":-1,\"stalenessSeconds\":-1}";

            _restored.FromJson(json);

            Assert.AreEqual(OscConfiguration.DefaultReceivePort, _restored.ListenPort);
            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs,
                _restored.BundleAccumulationTimeoutMs);
            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultHeartbeatIntervalSeconds,
                _restored.HeartbeatIntervalSeconds);
            Assert.AreEqual(0f, _restored.StalenessSeconds);
        }

        [Test]
        public void FromJson_EmptyJson_AppliesDefaults()
        {
            _restored.FromJson(string.Empty);

            Assert.AreEqual(OscRuntimeSettingsSO.DefaultListenEndpoint, _restored.ListenEndpoint);
            Assert.AreEqual(OscConfiguration.DefaultReceivePort, _restored.ListenPort);
            Assert.AreEqual(FailSafeMode.RevertToBase, _restored.FailSafeMode);
            Assert.AreEqual(BundleInterpretationMode.AtomicSwap, _restored.BundleMode);
        }

        private static void AssignAllFields(
            OscRuntimeSettingsSO target,
            string label,
            int schemaVersion,
            bool receiverEnabled,
            string listenEndpoint,
            int listenPort,
            float stalenessSeconds,
            FailSafeMode failSafeMode,
            bool consistencyCheckWarnLog,
            BundleInterpretationMode bundleMode,
            float bundleAccumulationTimeoutMs,
            bool senderEnabled,
            IReadOnlyList<OscSenderEndpointConfig> endpoints,
            float heartbeatIntervalSeconds,
            bool suppressLoopback)
        {
            var so = new UnityEditor.SerializedObject(target);
            so.FindProperty("_label").stringValue = label;
            so.FindProperty("_schemaVersion").intValue = schemaVersion;
            so.FindProperty("_receiverEnabled").boolValue = receiverEnabled;
            so.FindProperty("_listenEndpoint").stringValue = listenEndpoint;
            so.FindProperty("_listenPort").intValue = listenPort;
            so.FindProperty("_stalenessSeconds").floatValue = stalenessSeconds;
            so.FindProperty("_failSafeMode").enumValueIndex = (int)failSafeMode;
            so.FindProperty("_consistencyCheckWarnLog").boolValue = consistencyCheckWarnLog;
            so.FindProperty("_bundleMode").enumValueIndex = (int)bundleMode;
            so.FindProperty("_bundleAccumulationTimeoutMs").floatValue = bundleAccumulationTimeoutMs;
            so.FindProperty("_senderEnabled").boolValue = senderEnabled;
            so.FindProperty("_heartbeatIntervalSeconds").floatValue = heartbeatIntervalSeconds;
            so.FindProperty("_suppressLoopback").boolValue = suppressLoopback;

            var endpointsProperty = so.FindProperty("_endpoints");
            endpointsProperty.arraySize = endpoints.Count;
            for (var i = 0; i < endpoints.Count; i++)
            {
                var element = endpointsProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("endpoint").stringValue = endpoints[i].endpoint;
                element.FindPropertyRelative("port").intValue = endpoints[i].port;
                element.FindPropertyRelative("enabled").boolValue = endpoints[i].enabled;
                element.FindPropertyRelative("preset").enumValueIndex = (int)endpoints[i].preset;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignFailSafeMode(OscRuntimeSettingsSO target, FailSafeMode mode)
        {
            var so = new UnityEditor.SerializedObject(target);
            so.FindProperty("_failSafeMode").enumValueIndex = (int)mode;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignBundleMode(OscRuntimeSettingsSO target, BundleInterpretationMode mode)
        {
            var so = new UnityEditor.SerializedObject(target);
            so.FindProperty("_bundleMode").enumValueIndex = (int)mode;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
