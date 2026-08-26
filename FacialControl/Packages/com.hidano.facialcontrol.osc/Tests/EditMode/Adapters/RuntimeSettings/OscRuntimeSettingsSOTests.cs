using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// task 3.1 の観測可能完了条件: <see cref="OscRuntimeSettingsSO"/> に Receiver / Sender
    /// 両セクションのフィールドと getter が定義され、<c>JsonUtility.ToJson</c> が両セクションの
    /// 値を含むことを検証する。
    /// </summary>
    [TestFixture]
    public class OscRuntimeSettingsSOTests
    {
        private OscRuntimeSettingsSO _instance;

        [SetUp]
        public void SetUp()
        {
            _instance = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null)
            {
                Object.DestroyImmediate(_instance);
                _instance = null;
            }
        }

        [Test]
        public void Defaults_OnFreshInstance_ReturnsExpectedReceiverDefaults()
        {
            Assert.AreEqual(OscRuntimeSettingsSO.DefaultListenEndpoint, _instance.ListenEndpoint);
            Assert.AreEqual(9001, _instance.ListenPort);
            Assert.AreEqual(0f, _instance.StalenessSeconds);
            Assert.AreEqual(FailSafeMode.RevertToBase, _instance.FailSafeMode);
            Assert.IsTrue(_instance.ConsistencyCheckWarnLog);
            Assert.AreEqual(BundleInterpretationMode.AtomicSwap, _instance.BundleMode);
            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs,
                _instance.BundleAccumulationTimeoutMs);
        }

        [Test]
        public void Defaults_OnFreshInstance_ReturnsExpectedSenderDefaults()
        {
            Assert.IsNotNull(_instance.Endpoints);
            Assert.AreEqual(0, _instance.Endpoints.Count);
            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultHeartbeatIntervalSeconds,
                _instance.HeartbeatIntervalSeconds);
            Assert.IsTrue(_instance.SuppressLoopback);
        }

        [Test]
        public void Getters_AfterSerializedFieldAssignment_ReturnAssignedValues()
        {
            AssignReceiverFields(
                listenEndpoint: "192.168.1.10",
                listenPort: 9100,
                stalenessSeconds: 1.5f,
                failSafeMode: FailSafeMode.HoldLastValue,
                consistencyCheckWarnLog: false,
                bundleMode: BundleInterpretationMode.IndividualMessage,
                bundleAccumulationTimeoutMs: 12.5f);
            AssignSenderFields(
                endpoints: new List<OscSenderEndpointConfig>
                {
                    new OscSenderEndpointConfig("10.0.0.1", 9200),
                },
                heartbeatIntervalSeconds: 7.5f,
                suppressLoopback: false);

            Assert.AreEqual("192.168.1.10", _instance.ListenEndpoint);
            Assert.AreEqual(9100, _instance.ListenPort);
            Assert.AreEqual(1.5f, _instance.StalenessSeconds);
            Assert.AreEqual(FailSafeMode.HoldLastValue, _instance.FailSafeMode);
            Assert.IsFalse(_instance.ConsistencyCheckWarnLog);
            Assert.AreEqual(BundleInterpretationMode.IndividualMessage, _instance.BundleMode);
            Assert.AreEqual(12.5f, _instance.BundleAccumulationTimeoutMs);

            Assert.AreEqual(1, _instance.Endpoints.Count);
            Assert.AreEqual("10.0.0.1", _instance.Endpoints[0].endpoint);
            Assert.AreEqual(9200, _instance.Endpoints[0].port);
            Assert.AreEqual(7.5f, _instance.HeartbeatIntervalSeconds);
            Assert.IsFalse(_instance.SuppressLoopback);
        }

        [Test]
        public void JsonUtility_ToJsonAndFromJsonOverwrite_PreservesReceiverAndSenderFields()
        {
            AssignReceiverFields(
                listenEndpoint: "192.168.1.10",
                listenPort: 9100,
                stalenessSeconds: 1.5f,
                failSafeMode: FailSafeMode.HoldLastValue,
                consistencyCheckWarnLog: false,
                bundleMode: BundleInterpretationMode.IndividualMessage,
                bundleAccumulationTimeoutMs: 12.5f);
            AssignSenderFields(
                endpoints: new List<OscSenderEndpointConfig>
                {
                    new OscSenderEndpointConfig("10.0.0.1", 9200, true, AddressPresetKind.VRChat),
                    new OscSenderEndpointConfig("10.0.0.2", 9300, false, AddressPresetKind.ARKit),
                },
                heartbeatIntervalSeconds: 7.5f,
                suppressLoopback: false);

            var json = JsonUtility.ToJson(_instance);
            var restored = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            try
            {
                JsonUtility.FromJsonOverwrite(json, restored);

                Assert.AreEqual(_instance.ListenEndpoint, restored.ListenEndpoint);
                Assert.AreEqual(_instance.ListenPort, restored.ListenPort);
                Assert.AreEqual(_instance.StalenessSeconds, restored.StalenessSeconds);
                Assert.AreEqual(_instance.FailSafeMode, restored.FailSafeMode);
                Assert.AreEqual(_instance.ConsistencyCheckWarnLog, restored.ConsistencyCheckWarnLog);
                Assert.AreEqual(_instance.BundleMode, restored.BundleMode);
                Assert.AreEqual(_instance.BundleAccumulationTimeoutMs, restored.BundleAccumulationTimeoutMs);

                Assert.AreEqual(_instance.Endpoints.Count, restored.Endpoints.Count);
                for (int i = 0; i < _instance.Endpoints.Count; i++)
                {
                    Assert.AreEqual(_instance.Endpoints[i].endpoint, restored.Endpoints[i].endpoint);
                    Assert.AreEqual(_instance.Endpoints[i].port, restored.Endpoints[i].port);
                    Assert.AreEqual(_instance.Endpoints[i].enabled, restored.Endpoints[i].enabled);
                    Assert.AreEqual(_instance.Endpoints[i].preset, restored.Endpoints[i].preset);
                }
                Assert.AreEqual(_instance.HeartbeatIntervalSeconds, restored.HeartbeatIntervalSeconds);
                Assert.AreEqual(_instance.SuppressLoopback, restored.SuppressLoopback);
            }
            finally
            {
                Object.DestroyImmediate(restored);
            }
        }

        [Test]
        public void JsonUtility_ToJson_ContainsReceiverAndSenderFieldValues()
        {
            AssignReceiverFields(
                listenEndpoint: "192.168.1.10",
                listenPort: 9100,
                stalenessSeconds: 1.5f,
                failSafeMode: FailSafeMode.HoldLastValue,
                consistencyCheckWarnLog: false,
                bundleMode: BundleInterpretationMode.IndividualMessage,
                bundleAccumulationTimeoutMs: 12.5f);
            AssignSenderFields(
                endpoints: new List<OscSenderEndpointConfig>
                {
                    new OscSenderEndpointConfig("10.0.0.1", 9200),
                },
                heartbeatIntervalSeconds: 7.5f,
                suppressLoopback: false);

            var json = JsonUtility.ToJson(_instance);

            // JsonUtility は SerializeField 名 (_camelCase) をそのまま JSON キーに用いる。
            // task 3.3 の ToJson override で `listenPort` 等の正規化キーに変換する予定。
            // Receiver section
            StringAssert.Contains("\"_listenEndpoint\":\"192.168.1.10\"", json);
            StringAssert.Contains("\"_listenPort\":9100", json);
            StringAssert.Contains("\"_stalenessSeconds\":1.5", json);
            // FailSafeMode は本タスク時点では int で出力される (task 3.3 で文字列化を実装)。
            StringAssert.Contains("\"_failSafeMode\":1", json);
            StringAssert.Contains("\"_consistencyCheckWarnLog\":false", json);
            StringAssert.Contains("\"_bundleMode\":1", json);
            StringAssert.Contains("\"_bundleAccumulationTimeoutMs\":12.5", json);

            // Sender section
            StringAssert.Contains("\"_endpoints\":[", json);
            StringAssert.Contains("\"endpoint\":\"10.0.0.1\"", json);
            StringAssert.Contains("\"port\":9200", json);
            StringAssert.Contains("\"_heartbeatIntervalSeconds\":7.5", json);
            StringAssert.Contains("\"_suppressLoopback\":false", json);
        }

        private void AssignReceiverFields(
            string listenEndpoint,
            int listenPort,
            float stalenessSeconds,
            FailSafeMode failSafeMode,
            bool consistencyCheckWarnLog,
            BundleInterpretationMode bundleMode,
            float bundleAccumulationTimeoutMs)
        {
            var so = new UnityEditor.SerializedObject(_instance);
            so.FindProperty("_listenEndpoint").stringValue = listenEndpoint;
            so.FindProperty("_listenPort").intValue = listenPort;
            so.FindProperty("_stalenessSeconds").floatValue = stalenessSeconds;
            so.FindProperty("_failSafeMode").enumValueIndex = (int)failSafeMode;
            so.FindProperty("_consistencyCheckWarnLog").boolValue = consistencyCheckWarnLog;
            so.FindProperty("_bundleMode").enumValueIndex = (int)bundleMode;
            so.FindProperty("_bundleAccumulationTimeoutMs").floatValue = bundleAccumulationTimeoutMs;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private void AssignSenderFields(
            IReadOnlyList<OscSenderEndpointConfig> endpoints,
            float heartbeatIntervalSeconds,
            bool suppressLoopback)
        {
            var so = new UnityEditor.SerializedObject(_instance);
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

            so.FindProperty("_heartbeatIntervalSeconds").floatValue = heartbeatIntervalSeconds;
            so.FindProperty("_suppressLoopback").boolValue = suppressLoopback;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
