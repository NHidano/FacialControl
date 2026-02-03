using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// P06-T03: PropertyStreamHandleCache の単体テスト。
    /// キャッシュ動作を検証する。
    /// </summary>
    [TestFixture]
    public class PropertyStreamHandleCacheTests
    {
        private PropertyStreamHandleCache _cache;

        [TearDown]
        public void TearDown()
        {
            _cache = null;
        }

        // ================================================================
        // コンストラクタ
        // ================================================================

        [Test]
        public void Constructor_Default_CreatesEmptyCache()
        {
            _cache = new PropertyStreamHandleCache();

            Assert.AreEqual(0, _cache.Count);
        }

        // ================================================================
        // EnsureHandles — 新規取得
        // ================================================================

        [Test]
        public void EnsureHandles_NewBlendShapes_InvokesFactoryForEach()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f),
                new BlendShapeMapping("eyeBlink_R", 1.0f)
            };

            int factoryCallCount = 0;
            _cache.EnsureHandles(mappings, (name, renderer) =>
            {
                factoryCallCount++;
                return default;
            });

            Assert.AreEqual(2, factoryCallCount);
        }

        [Test]
        public void EnsureHandles_NewBlendShapes_IncrementsCount()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f),
                new BlendShapeMapping("mouthOpen", 0.5f)
            };

            _cache.EnsureHandles(mappings, (name, renderer) => default);

            Assert.AreEqual(2, _cache.Count);
        }

        // ================================================================
        // EnsureHandles — キャッシュヒット
        // ================================================================

        [Test]
        public void EnsureHandles_AlreadyCachedBlendShapes_DoesNotInvokeFactory()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f)
            };

            _cache.EnsureHandles(mappings, (name, renderer) => default);

            int secondCallCount = 0;
            _cache.EnsureHandles(mappings, (name, renderer) =>
            {
                secondCallCount++;
                return default;
            });

            Assert.AreEqual(0, secondCallCount);
        }

        [Test]
        public void EnsureHandles_MixedCachedAndNew_InvokesFactoryOnlyForNew()
        {
            _cache = new PropertyStreamHandleCache();
            var firstMappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f)
            };
            _cache.EnsureHandles(firstMappings, (name, renderer) => default);

            var secondMappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 0.5f),
                new BlendShapeMapping("mouthOpen", 0.8f)
            };

            int factoryCallCount = 0;
            string lastFactoryName = null;
            _cache.EnsureHandles(secondMappings, (name, renderer) =>
            {
                factoryCallCount++;
                lastFactoryName = name;
                return default;
            });

            Assert.AreEqual(1, factoryCallCount);
            Assert.AreEqual("mouthOpen", lastFactoryName);
        }

        [Test]
        public void EnsureHandles_SameNameDifferentValue_DoesNotInvokeFactory()
        {
            _cache = new PropertyStreamHandleCache();
            var firstMappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f)
            };
            _cache.EnsureHandles(firstMappings, (name, renderer) => default);

            // 同じ名前・renderer で異なる値 → 既にキャッシュ済みなのでファクトリ不要
            var secondMappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 0.3f)
            };

            int factoryCallCount = 0;
            _cache.EnsureHandles(secondMappings, (name, renderer) =>
            {
                factoryCallCount++;
                return default;
            });

            Assert.AreEqual(0, factoryCallCount);
            Assert.AreEqual(1, _cache.Count);
        }

        // ================================================================
        // Renderer 区別
        // ================================================================

        [Test]
        public void EnsureHandles_SameNameDifferentRenderer_CachesSeparately()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings1 = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f, "Face")
            };
            _cache.EnsureHandles(mappings1, (name, renderer) => default);

            var mappings2 = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f, "Body")
            };

            int factoryCallCount = 0;
            _cache.EnsureHandles(mappings2, (name, renderer) =>
            {
                factoryCallCount++;
                return default;
            });

            Assert.AreEqual(1, factoryCallCount);
            Assert.AreEqual(2, _cache.Count);
        }

        [Test]
        public void EnsureHandles_NullRenderer_CachesSeparatelyFromNamedRenderer()
        {
            _cache = new PropertyStreamHandleCache();
            var mappingsNull = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f)
            };
            _cache.EnsureHandles(mappingsNull, (name, renderer) => default);

            var mappingsNamed = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f, "Face")
            };

            int factoryCallCount = 0;
            _cache.EnsureHandles(mappingsNamed, (name, renderer) =>
            {
                factoryCallCount++;
                return default;
            });

            Assert.AreEqual(1, factoryCallCount);
            Assert.AreEqual(2, _cache.Count);
        }

        // ================================================================
        // TryGetHandle
        // ================================================================

        [Test]
        public void TryGetHandle_ExistingEntry_ReturnsTrueAndHandle()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f)
            };
            _cache.EnsureHandles(mappings, (name, renderer) => default);

            bool found = _cache.TryGetHandle("eyeBlink_L", null, out var handle);

            Assert.IsTrue(found);
        }

        [Test]
        public void TryGetHandle_NonExistingEntry_ReturnsFalse()
        {
            _cache = new PropertyStreamHandleCache();

            bool found = _cache.TryGetHandle("nonExistent", null, out var handle);

            Assert.IsFalse(found);
        }

        [Test]
        public void TryGetHandle_WithRenderer_ReturnsCorrectHandle()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f, "Face")
            };
            _cache.EnsureHandles(mappings, (name, renderer) => default);

            Assert.IsTrue(_cache.TryGetHandle("eyeBlink_L", "Face", out _));
            Assert.IsFalse(_cache.TryGetHandle("eyeBlink_L", null, out _));
            Assert.IsFalse(_cache.TryGetHandle("eyeBlink_L", "Body", out _));
        }

        // ================================================================
        // ContainsKey
        // ================================================================

        [Test]
        public void ContainsKey_ExistingEntry_ReturnsTrue()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f)
            };
            _cache.EnsureHandles(mappings, (name, renderer) => default);

            Assert.IsTrue(_cache.ContainsKey("eyeBlink_L", null));
        }

        [Test]
        public void ContainsKey_NonExistingEntry_ReturnsFalse()
        {
            _cache = new PropertyStreamHandleCache();

            Assert.IsFalse(_cache.ContainsKey("nonExistent", null));
        }

        [Test]
        public void ContainsKey_WithRenderer_ChecksCorrectKey()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f, "Face")
            };
            _cache.EnsureHandles(mappings, (name, renderer) => default);

            Assert.IsTrue(_cache.ContainsKey("eyeBlink_L", "Face"));
            Assert.IsFalse(_cache.ContainsKey("eyeBlink_L", null));
        }

        // ================================================================
        // Clear
        // ================================================================

        [Test]
        public void Clear_EmptyCache_DoesNotThrow()
        {
            _cache = new PropertyStreamHandleCache();

            Assert.DoesNotThrow(() => _cache.Clear());
        }

        [Test]
        public void Clear_WithEntries_ResetsCountToZero()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f),
                new BlendShapeMapping("mouthOpen", 0.5f)
            };
            _cache.EnsureHandles(mappings, (name, renderer) => default);

            _cache.Clear();

            Assert.AreEqual(0, _cache.Count);
        }

        [Test]
        public void Clear_AfterClear_RequiresNewFactoryCalls()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f)
            };
            _cache.EnsureHandles(mappings, (name, renderer) => default);
            _cache.Clear();

            int factoryCallCount = 0;
            _cache.EnsureHandles(mappings, (name, renderer) =>
            {
                factoryCallCount++;
                return default;
            });

            Assert.AreEqual(1, factoryCallCount);
        }

        // ================================================================
        // EnsureHandles — エラーケース
        // ================================================================

        [Test]
        public void EnsureHandles_NullMappings_ThrowsArgumentNullException()
        {
            _cache = new PropertyStreamHandleCache();

            Assert.Throws<ArgumentNullException>(() =>
                _cache.EnsureHandles(null, (name, renderer) => default));
        }

        [Test]
        public void EnsureHandles_NullFactory_ThrowsArgumentNullException()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f)
            };

            Assert.Throws<ArgumentNullException>(() =>
                _cache.EnsureHandles(mappings, null));
        }

        [Test]
        public void EnsureHandles_EmptyMappings_DoesNotInvokeFactory()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = Array.Empty<BlendShapeMapping>();

            int factoryCallCount = 0;
            _cache.EnsureHandles(mappings, (name, renderer) =>
            {
                factoryCallCount++;
                return default;
            });

            Assert.AreEqual(0, factoryCallCount);
            Assert.AreEqual(0, _cache.Count);
        }

        // ================================================================
        // 大量エントリ
        // ================================================================

        [Test]
        public void EnsureHandles_ManyBlendShapes_CachesAll()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new BlendShapeMapping[100];
            for (int i = 0; i < 100; i++)
            {
                mappings[i] = new BlendShapeMapping($"blendShape_{i}", i / 100f);
            }

            _cache.EnsureHandles(mappings, (name, renderer) => default);

            Assert.AreEqual(100, _cache.Count);
        }

        [Test]
        public void EnsureHandles_DuplicateNamesInSameBatch_InvokesFactoryOnce()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f),
                new BlendShapeMapping("eyeBlink_L", 0.5f)
            };

            int factoryCallCount = 0;
            _cache.EnsureHandles(mappings, (name, renderer) =>
            {
                factoryCallCount++;
                return default;
            });

            // 同一バッチ内の重複は 1 回だけファクトリ呼び出し
            Assert.AreEqual(1, factoryCallCount);
            Assert.AreEqual(1, _cache.Count);
        }

        // ================================================================
        // 2バイト文字・特殊記号
        // ================================================================

        [Test]
        public void EnsureHandles_JapaneseBlendShapeName_CachesCorrectly()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("まばたき_左", 1.0f),
                new BlendShapeMapping("口開け", 0.5f)
            };

            _cache.EnsureHandles(mappings, (name, renderer) => default);

            Assert.AreEqual(2, _cache.Count);
            Assert.IsTrue(_cache.ContainsKey("まばたき_左", null));
            Assert.IsTrue(_cache.ContainsKey("口開け", null));
        }

        [Test]
        public void EnsureHandles_SpecialCharacterBlendShapeName_CachesCorrectly()
        {
            _cache = new PropertyStreamHandleCache();
            var mappings = new[]
            {
                new BlendShapeMapping("eye.blink-L (left)", 1.0f)
            };

            _cache.EnsureHandles(mappings, (name, renderer) => default);

            Assert.IsTrue(_cache.ContainsKey("eye.blink-L (left)", null));
        }
    }
}
