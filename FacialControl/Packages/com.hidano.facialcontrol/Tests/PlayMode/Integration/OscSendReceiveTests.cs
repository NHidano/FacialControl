using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// P07-T03: OSC 送受信テスト。
    /// OscReceiver の初期化、メッセージ処理、アドレスパターン解析、
    /// ダブルバッファリング動作を検証する。
    /// 実 UDP 送受信テストは OscSender 実装後に追加。
    /// </summary>
    [TestFixture]
    public class OscSendReceiveTests
    {
        private GameObject _receiverObj;
        private OscReceiver _receiver;
        private OscDoubleBuffer _buffer;

        [SetUp]
        public void SetUp()
        {
            _receiverObj = new GameObject("OscReceiverTest");
            _receiver = _receiverObj.AddComponent<OscReceiver>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }
            if (_receiverObj != null)
            {
                Object.DestroyImmediate(_receiverObj);
            }
        }

        // ================================================================
        // ExtractBlendShapeName — アドレスパターン解析
        // ================================================================

        [Test]
        public void ExtractBlendShapeName_VRChatAddress_ReturnsParameterName()
        {
            string result = OscReceiver.ExtractBlendShapeName("/avatar/parameters/Fcl_ALL_Joy");

            Assert.AreEqual("Fcl_ALL_Joy", result);
        }

        [Test]
        public void ExtractBlendShapeName_ARKitAddress_ReturnsBlendShapeName()
        {
            string result = OscReceiver.ExtractBlendShapeName("/ARKit/eyeBlinkLeft");

            Assert.AreEqual("eyeBlinkLeft", result);
        }

        [Test]
        public void ExtractBlendShapeName_UnknownPrefix_ReturnsNull()
        {
            string result = OscReceiver.ExtractBlendShapeName("/unknown/prefix/name");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBlendShapeName_EmptyString_ReturnsNull()
        {
            string result = OscReceiver.ExtractBlendShapeName("");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBlendShapeName_Null_ReturnsNull()
        {
            string result = OscReceiver.ExtractBlendShapeName(null);

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBlendShapeName_VRChatPrefixOnly_ReturnsNull()
        {
            string result = OscReceiver.ExtractBlendShapeName("/avatar/parameters/");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBlendShapeName_ARKitPrefixOnly_ReturnsNull()
        {
            string result = OscReceiver.ExtractBlendShapeName("/ARKit/");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBlendShapeName_VRChatJapanese_ReturnsName()
        {
            string result = OscReceiver.ExtractBlendShapeName("/avatar/parameters/笑顔");

            Assert.AreEqual("笑顔", result);
        }

        [Test]
        public void ExtractBlendShapeName_ARKitWithSpecialChars_ReturnsName()
        {
            string result = OscReceiver.ExtractBlendShapeName("/ARKit/jaw_Open.L");

            Assert.AreEqual("jaw_Open.L", result);
        }

        // ================================================================
        // Initialize — 初期化
        // ================================================================

        [Test]
        public void Initialize_ValidParameters_SetsBufferAndMappings()
        {
            _buffer = new OscDoubleBuffer(2);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion"),
                new OscMapping("/avatar/parameters/Angry", "Angry", "emotion")
            };

            _receiver.Initialize(_buffer, mappings);

            Assert.AreEqual(_buffer, _receiver.Buffer);
        }

        [Test]
        public void Initialize_NullBuffer_ThrowsArgumentNullException()
        {
            var mappings = new OscMapping[] { };

            Assert.Throws<System.ArgumentNullException>(() =>
                _receiver.Initialize(null, mappings));
        }

        [Test]
        public void Initialize_NullMappings_ThrowsArgumentNullException()
        {
            _buffer = new OscDoubleBuffer(2);

            Assert.Throws<System.ArgumentNullException>(() =>
                _receiver.Initialize(_buffer, (OscMapping[])null));
        }

        [Test]
        public void Initialize_WithOscConfiguration_SetsPortFromConfig()
        {
            _buffer = new OscDoubleBuffer(2);
            var config = new OscConfiguration(
                sendPort: 9000,
                receivePort: 9999,
                preset: "vrchat",
                mapping: new[]
                {
                    new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
                });

            _receiver.Initialize(_buffer, config);

            Assert.AreEqual(9999, _receiver.Port);
        }

        // ================================================================
        // HandleOscMessage — メッセージ処理
        // ================================================================

        [Test]
        public void HandleOscMessage_MatchingAddress_WritesToBuffer()
        {
            _buffer = new OscDoubleBuffer(2);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion"),
                new OscMapping("/avatar/parameters/Angry", "Angry", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            var message = new uOSC.Message("/avatar/parameters/Joy", 0.75f);
            _receiver.HandleOscMessage(message);
            _buffer.Swap();

            var readBuffer = _buffer.GetReadBuffer();
            Assert.AreEqual(0.75f, readBuffer[0], 0.0001f);
        }

        [Test]
        public void HandleOscMessage_SecondMapping_WritesToCorrectIndex()
        {
            _buffer = new OscDoubleBuffer(2);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion"),
                new OscMapping("/avatar/parameters/Angry", "Angry", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            var message = new uOSC.Message("/avatar/parameters/Angry", 0.5f);
            _receiver.HandleOscMessage(message);
            _buffer.Swap();

            var readBuffer = _buffer.GetReadBuffer();
            Assert.AreEqual(0f, readBuffer[0], 0.0001f);
            Assert.AreEqual(0.5f, readBuffer[1], 0.0001f);
        }

        [Test]
        public void HandleOscMessage_IntValue_ConvertedToFloat()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Toggle", "Toggle", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            var message = new uOSC.Message("/avatar/parameters/Toggle", 1);
            _receiver.HandleOscMessage(message);
            _buffer.Swap();

            var readBuffer = _buffer.GetReadBuffer();
            Assert.AreEqual(1f, readBuffer[0], 0.0001f);
        }

        [Test]
        public void HandleOscMessage_UnmatchedAddress_DoesNotWriteToBuffer()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            var message = new uOSC.Message("/avatar/parameters/Unknown", 0.5f);
            _receiver.HandleOscMessage(message);
            _buffer.Swap();

            var readBuffer = _buffer.GetReadBuffer();
            Assert.AreEqual(0f, readBuffer[0]);
        }

        [Test]
        public void HandleOscMessage_EmptyAddress_Ignored()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            var message = new uOSC.Message("", 0.5f);
            _receiver.HandleOscMessage(message);
            _buffer.Swap();

            var readBuffer = _buffer.GetReadBuffer();
            Assert.AreEqual(0f, readBuffer[0]);
        }

        [Test]
        public void HandleOscMessage_NoValues_Ignored()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            var message = new uOSC.Message("/avatar/parameters/Joy");
            _receiver.HandleOscMessage(message);
            _buffer.Swap();

            var readBuffer = _buffer.GetReadBuffer();
            Assert.AreEqual(0f, readBuffer[0]);
        }

        [Test]
        public void HandleOscMessage_StringValue_Ignored()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            var message = new uOSC.Message("/avatar/parameters/Joy", "text");
            _receiver.HandleOscMessage(message);
            _buffer.Swap();

            var readBuffer = _buffer.GetReadBuffer();
            Assert.AreEqual(0f, readBuffer[0]);
        }

        [Test]
        public void HandleOscMessage_BeforeInitialize_DoesNotThrow()
        {
            // 初期化前にメッセージが来ても例外にならない
            var message = new uOSC.Message("/avatar/parameters/Joy", 0.5f);

            Assert.DoesNotThrow(() => _receiver.HandleOscMessage(message));
        }

        // ================================================================
        // HandleOscMessage — BlendShape 名による名前ベースの解決
        // ================================================================

        [Test]
        public void HandleOscMessage_ARKitAddressMatchesByBlendShapeName_WritesToBuffer()
        {
            _buffer = new OscDoubleBuffer(1);
            // マッピングは VRChat アドレスだが、BlendShape 名は eyeBlinkLeft
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/eyeBlinkLeft", "eyeBlinkLeft", "eye")
            };
            _receiver.Initialize(_buffer, mappings);

            // ARKit 形式のアドレスで送信 — アドレスは一致しないが BlendShape 名で解決
            var message = new uOSC.Message("/ARKit/eyeBlinkLeft", 0.8f);
            _receiver.HandleOscMessage(message);
            _buffer.Swap();

            var readBuffer = _buffer.GetReadBuffer();
            Assert.AreEqual(0.8f, readBuffer[0], 0.0001f);
        }

        // ================================================================
        // HandleOscMessage — 複数メッセージの連続処理
        // ================================================================

        [Test]
        public void HandleOscMessage_MultipleMessagesInOneFrame_AllWritten()
        {
            _buffer = new OscDoubleBuffer(3);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion"),
                new OscMapping("/avatar/parameters/Angry", "Angry", "emotion"),
                new OscMapping("/avatar/parameters/Sad", "Sad", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            _receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/Joy", 0.1f));
            _receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/Angry", 0.2f));
            _receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/Sad", 0.3f));
            _buffer.Swap();

            var readBuffer = _buffer.GetReadBuffer();
            Assert.AreEqual(0.1f, readBuffer[0], 0.0001f);
            Assert.AreEqual(0.2f, readBuffer[1], 0.0001f);
            Assert.AreEqual(0.3f, readBuffer[2], 0.0001f);
        }

        [Test]
        public void HandleOscMessage_SameAddressMultipleTimes_KeepsLatestValue()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            _receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/Joy", 0.1f));
            _receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/Joy", 0.9f));
            _buffer.Swap();

            var readBuffer = _buffer.GetReadBuffer();
            Assert.AreEqual(0.9f, readBuffer[0], 0.0001f);
        }

        // ================================================================
        // DoubleBuffering — フレーム境界でのバッファスワップ
        // ================================================================

        [Test]
        public void HandleOscMessage_AfterSwap_NewFrameStartsClean()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            // フレーム 1: 値を書き込み
            _receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/Joy", 0.5f));
            _buffer.Swap();
            Assert.AreEqual(0.5f, _buffer.GetReadBuffer()[0], 0.0001f);

            // フレーム 2: 何も書き込まずにスワップ
            _buffer.Swap();
            Assert.AreEqual(0f, _buffer.GetReadBuffer()[0], 0.0001f);
        }

        // ================================================================
        // Port — ポート設定
        // ================================================================

        [Test]
        public void Port_DefaultValue_Is9001()
        {
            Assert.AreEqual(OscConfiguration.DefaultReceivePort, _receiver.Port);
        }

        [Test]
        public void Port_SetValue_UpdatesPort()
        {
            _receiver.Port = 12345;

            Assert.AreEqual(12345, _receiver.Port);
        }

        // ================================================================
        // IsRunning — 初期状態
        // ================================================================

        [Test]
        public void IsRunning_BeforeStart_ReturnsFalse()
        {
            Assert.IsFalse(_receiver.IsRunning);
        }

        // ================================================================
        // 実 UDP 送受信テスト（OscSender 実装後に追加予定）
        // ================================================================

        [UnityTest]
        public IEnumerator StartReceiving_CreatesUOscServer()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            _receiver.StartReceiving();
            yield return null;

            Assert.IsTrue(_receiver.IsRunning);

            _receiver.StopReceiving();
            yield return null;
        }

        [UnityTest]
        public IEnumerator StopReceiving_StopsServer()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            _receiver.StartReceiving();
            yield return null;

            _receiver.StopReceiving();
            yield return null;

            Assert.IsFalse(_receiver.IsRunning);
        }

        [UnityTest]
        public IEnumerator RealUdpReceive_WithUOscClient_ReceivesValue()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Port = 19001; // テスト用ポート
            _receiver.Initialize(_buffer, mappings);
            _receiver.StartReceiving();

            // サーバー起動の安定化待ち
            yield return new WaitForSeconds(0.2f);

            // uOscClient で送信
            var clientObj = new GameObject("OscClientTest");
            var client = clientObj.AddComponent<uOSC.uOscClient>();
            client.address = "127.0.0.1";
            client.port = 19001;
            client.StartClient();

            yield return new WaitForSeconds(0.2f);

            // 複数回送信して到着確率を上げる
            bool received = false;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                client.Send("/avatar/parameters/Joy", 0.75f);
                yield return new WaitForSeconds(0.1f);

                _buffer.Swap();
                var readBuf = _buffer.GetReadBuffer();
                if (readBuf[0] > 0.01f)
                {
                    received = true;
                    Assert.AreEqual(0.75f, readBuf[0], 0.01f);
                    break;
                }
            }

            Assert.IsTrue(received, "OSC メッセージがタイムアウト内に受信されませんでした。");

            client.StopClient();
            _receiver.StopReceiving();
            Object.DestroyImmediate(clientObj);
            yield return null;
        }
    }
}
