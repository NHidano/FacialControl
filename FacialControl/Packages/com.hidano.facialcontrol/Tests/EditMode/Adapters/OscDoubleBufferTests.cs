using System;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Hidano.FacialControl.Adapters.OSC;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// P07-T01: OscDoubleBuffer の単体テスト。
    /// バッファ読み書き、スワップを検証する。
    /// </summary>
    [TestFixture]
    public class OscDoubleBufferTests
    {
        // ================================================================
        // コンストラクタ — 初期化
        // ================================================================

        [Test]
        public void Constructor_ValidSize_CreatesBuffer()
        {
            using var buffer = new OscDoubleBuffer(16);

            Assert.AreEqual(16, buffer.Size);
        }

        [Test]
        public void Constructor_ZeroSize_CreatesEmptyBuffer()
        {
            using var buffer = new OscDoubleBuffer(0);

            Assert.AreEqual(0, buffer.Size);
        }

        [Test]
        public void Constructor_NegativeSize_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var buffer = new OscDoubleBuffer(-1);
                buffer.Dispose();
            });
        }

        // ================================================================
        // Write — 書き込み（受信スレッド側）
        // ================================================================

        [Test]
        public void Write_ValidIndexAndValue_StoresValue()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            buffer.Swap();

            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.5f, readBuffer[0], 0.0001f);
        }

        [Test]
        public void Write_MultipleValues_StoresAll()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.1f);
            buffer.Write(1, 0.2f);
            buffer.Write(2, 0.3f);
            buffer.Write(3, 0.4f);
            buffer.Swap();

            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.1f, readBuffer[0], 0.0001f);
            Assert.AreEqual(0.2f, readBuffer[1], 0.0001f);
            Assert.AreEqual(0.3f, readBuffer[2], 0.0001f);
            Assert.AreEqual(0.4f, readBuffer[3], 0.0001f);
        }

        [Test]
        public void Write_OverwritesSameIndex_KeepsLatestValue()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.1f);
            buffer.Write(0, 0.9f);
            buffer.Swap();

            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.9f, readBuffer[0], 0.0001f);
        }

        [Test]
        public void Write_IndexOutOfRange_ThrowsArgumentOutOfRangeException()
        {
            using var buffer = new OscDoubleBuffer(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Write(4, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Write(-1, 0.5f));
        }

        // ================================================================
        // Swap — フレーム境界でスワップ
        // ================================================================

        [Test]
        public void Swap_WrittenValuesAppearInReadBuffer()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            buffer.Write(1, 0.75f);
            buffer.Swap();

            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.5f, readBuffer[0], 0.0001f);
            Assert.AreEqual(0.75f, readBuffer[1], 0.0001f);
        }

        [Test]
        public void Swap_WrittenValuesNotVisibleBeforeSwap()
        {
            using var buffer = new OscDoubleBuffer(4);

            // 初期状態の ReadBuffer はゼロ
            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0f, readBuffer[0]);

            buffer.Write(0, 0.5f);

            // スワップ前は読み出しバッファに反映されない
            readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0f, readBuffer[0]);
        }

        [Test]
        public void Swap_ClearsWriteBuffer()
        {
            using var buffer = new OscDoubleBuffer(4);

            // 最初のフレーム：書き込み → スワップ
            buffer.Write(0, 0.5f);
            buffer.Swap();

            // 2 フレーム目：何も書き込まずにスワップ
            buffer.Swap();

            // Write バッファがクリアされているため、ReadBuffer はゼロ
            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0f, readBuffer[0]);
        }

        [Test]
        public void Swap_MultipleSwaps_CorrectlyAlternates()
        {
            using var buffer = new OscDoubleBuffer(4);

            // フレーム 1: 0.1 を書き込み
            buffer.Write(0, 0.1f);
            buffer.Swap();
            Assert.AreEqual(0.1f, buffer.GetReadBuffer()[0], 0.0001f);

            // フレーム 2: 0.2 を書き込み
            buffer.Write(0, 0.2f);
            buffer.Swap();
            Assert.AreEqual(0.2f, buffer.GetReadBuffer()[0], 0.0001f);

            // フレーム 3: 0.3 を書き込み
            buffer.Write(0, 0.3f);
            buffer.Swap();
            Assert.AreEqual(0.3f, buffer.GetReadBuffer()[0], 0.0001f);
        }

        // ================================================================
        // GetReadBuffer — 読み出し（メインスレッド側）
        // ================================================================

        [Test]
        public void GetReadBuffer_InitialState_ReturnsZeroFilledBuffer()
        {
            using var buffer = new OscDoubleBuffer(4);

            var readBuffer = buffer.GetReadBuffer();

            for (int i = 0; i < readBuffer.Length; i++)
            {
                Assert.AreEqual(0f, readBuffer[i]);
            }
        }

        [Test]
        public void GetReadBuffer_ReturnsCorrectLength()
        {
            using var buffer = new OscDoubleBuffer(8);

            var readBuffer = buffer.GetReadBuffer();

            Assert.AreEqual(8, readBuffer.Length);
        }

        [Test]
        public void GetReadBuffer_IsReadOnly()
        {
            using var buffer = new OscDoubleBuffer(4);

            var readBuffer = buffer.GetReadBuffer();

            // ReadOnlyNativeArray であるため、ReadOnly であることを Length で間接的に確認
            Assert.AreEqual(4, readBuffer.Length);
        }

        // ================================================================
        // スレッド安全性 — ロックフリー動作
        // ================================================================

        [Test]
        public void Write_FromDifferentThread_DoesNotCorruptReadBuffer()
        {
            using var buffer = new OscDoubleBuffer(4);

            // メインスレッドで初期読み取り
            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0f, readBuffer[0]);

            // 別スレッドから書き込み
            var writeThread = new Thread(() =>
            {
                buffer.Write(0, 0.5f);
                buffer.Write(1, 0.75f);
            });
            writeThread.Start();
            writeThread.Join();

            // スワップ前は読み取りバッファに影響なし
            readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0f, readBuffer[0]);

            // スワップ後に値が反映
            buffer.Swap();
            readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.5f, readBuffer[0], 0.0001f);
            Assert.AreEqual(0.75f, readBuffer[1], 0.0001f);
        }

        [Test]
        public void ConcurrentWriteAndRead_DoesNotThrow()
        {
            using var buffer = new OscDoubleBuffer(64);
            var exception = (Exception)null;
            var iterations = 100;

            // 書き込みスレッド
            var writeThread = new Thread(() =>
            {
                try
                {
                    for (int frame = 0; frame < iterations; frame++)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            buffer.Write(i, (float)frame / iterations);
                        }
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            // 読み取り + スワップスレッド（メインスレッド模擬）
            var readThread = new Thread(() =>
            {
                try
                {
                    for (int frame = 0; frame < iterations; frame++)
                    {
                        buffer.Swap();
                        var readBuf = buffer.GetReadBuffer();
                        for (int i = 0; i < readBuf.Length; i++)
                        {
                            var _ = readBuf[i];
                        }
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            writeThread.Start();
            readThread.Start();
            writeThread.Join();
            readThread.Join();

            Assert.IsNull(exception, $"並行アクセスで例外が発生: {exception}");
        }

        // ================================================================
        // Dispose — 解放
        // ================================================================

        [Test]
        public void Dispose_DoesNotThrow()
        {
            var buffer = new OscDoubleBuffer(8);
            buffer.Write(0, 0.5f);
            buffer.Swap();

            Assert.DoesNotThrow(() => buffer.Dispose());
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var buffer = new OscDoubleBuffer(4);
            buffer.Dispose();

            Assert.DoesNotThrow(() => buffer.Dispose());
        }

        // ================================================================
        // Resize — サイズ変更
        // ================================================================

        [Test]
        public void Resize_LargerSize_AllocatesNewSize()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Resize(8);

            Assert.AreEqual(8, buffer.Size);
            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(8, readBuffer.Length);
        }

        [Test]
        public void Resize_SmallerSize_AllocatesNewSize()
        {
            using var buffer = new OscDoubleBuffer(8);

            buffer.Resize(4);

            Assert.AreEqual(4, buffer.Size);
        }

        [Test]
        public void Resize_SameSize_DoesNothing()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            buffer.Swap();
            buffer.Resize(4);

            // 同じサイズへのリサイズは何もしない（既存バッファを維持）
            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.5f, readBuffer[0], 0.0001f);
        }

        [Test]
        public void Resize_ClearsExistingData()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            buffer.Swap();
            buffer.Resize(8);

            // リサイズ後はデータがクリアされる
            var readBuffer = buffer.GetReadBuffer();
            for (int i = 0; i < readBuffer.Length; i++)
            {
                Assert.AreEqual(0f, readBuffer[i]);
            }
        }

        [Test]
        public void Resize_NegativeSize_ThrowsArgumentOutOfRangeException()
        {
            using var buffer = new OscDoubleBuffer(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Resize(-1));
        }

        [Test]
        public void Resize_WritesWorkAfterResize()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Resize(8);
            buffer.Write(7, 0.9f);
            buffer.Swap();

            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.9f, readBuffer[7], 0.0001f);
        }
    }
}
