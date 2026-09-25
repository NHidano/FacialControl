using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// task 3.2 の観測可能完了条件: <c>_receiverEnabled</c> / <c>_senderEnabled</c> トグルと
    /// <see cref="ISerializationCallbackReceiver.OnAfterDeserialize"/> による不正値補正・enum 正規化を検証する。
    /// </summary>
    [TestFixture]
    public class OscRuntimeSettingsSONormalizationTests
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
        public void ReceiverEnabled_OnFreshInstance_ReturnsTrue()
        {
            Assert.IsTrue(_instance.ReceiverEnabled);
        }

        [Test]
        public void SenderEnabled_OnFreshInstance_ReturnsTrue()
        {
            Assert.IsTrue(_instance.SenderEnabled);
        }

        [Test]
        public void OnAfterDeserialize_ListenPortZero_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_listenPort\":0}", _instance);

            Assert.AreEqual(OscConfiguration.DefaultReceivePort, _instance.ListenPort);
        }

        [Test]
        public void OnAfterDeserialize_ListenPortOutOfRange_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_listenPort\":70000}", _instance);

            Assert.AreEqual(OscConfiguration.DefaultReceivePort, _instance.ListenPort);
        }

        [Test]
        public void OnAfterDeserialize_BundleAccumulationTimeoutNegative_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_bundleAccumulationTimeoutMs\":-1}", _instance);

            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs,
                _instance.BundleAccumulationTimeoutMs);
        }

        [Test]
        public void OnAfterDeserialize_BundleAccumulationTimeoutZero_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_bundleAccumulationTimeoutMs\":0}", _instance);

            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs,
                _instance.BundleAccumulationTimeoutMs);
        }

        [Test]
        public void OnAfterDeserialize_HeartbeatIntervalNegative_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_heartbeatIntervalSeconds\":-1}", _instance);

            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultHeartbeatIntervalSeconds,
                _instance.HeartbeatIntervalSeconds);
        }

        [Test]
        public void OnAfterDeserialize_HeartbeatIntervalZero_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_heartbeatIntervalSeconds\":0}", _instance);

            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultHeartbeatIntervalSeconds,
                _instance.HeartbeatIntervalSeconds);
        }

        [Test]
        public void OnAfterDeserialize_StalenessNegative_NormalizesToZero()
        {
            JsonUtility.FromJsonOverwrite("{\"_stalenessSeconds\":-1}", _instance);

            Assert.AreEqual(0f, _instance.StalenessSeconds);
        }

        [Test]
        public void OnAfterDeserialize_ListenEndpointWhitespace_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_listenEndpoint\":\"   \"}", _instance);

            Assert.AreEqual(OscRuntimeSettingsSO.DefaultListenEndpoint, _instance.ListenEndpoint);
        }

        [Test]
        public void OnAfterDeserialize_ListenEndpointWithWhitespacePadding_TrimsWhitespace()
        {
            JsonUtility.FromJsonOverwrite("{\"_listenEndpoint\":\"  192.168.1.10  \"}", _instance);

            Assert.AreEqual("192.168.1.10", _instance.ListenEndpoint);
        }

        [Test]
        public void OnAfterDeserialize_FailSafeModeUndefined_NormalizesToRevertToBase()
        {
            JsonUtility.FromJsonOverwrite("{\"_failSafeMode\":999}", _instance);

            Assert.AreEqual(FailSafeMode.RevertToBase, _instance.FailSafeMode);
        }

        [Test]
        public void OnAfterDeserialize_BundleModeUndefined_NormalizesToAtomicSwap()
        {
            JsonUtility.FromJsonOverwrite("{\"_bundleMode\":999}", _instance);

            Assert.AreEqual(BundleInterpretationMode.AtomicSwap, _instance.BundleMode);
        }
    }
}
