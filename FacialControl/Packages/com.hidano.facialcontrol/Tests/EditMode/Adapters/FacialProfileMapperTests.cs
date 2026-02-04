using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Adapters.ScriptableObject;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    public class FacialProfileMapperTests
    {
        // --- Fake 実装 ---

        private class FakeJsonParser : IJsonParser
        {
            public FacialProfile? ParseResult { get; set; }
            public string SerializeResult { get; set; } = "{}";
            public bool ThrowOnParse { get; set; }
            public string LastParsedJson { get; private set; }

            public FacialProfile ParseProfile(string json)
            {
                LastParsedJson = json;
                if (ThrowOnParse)
                    throw new FormatException("パースエラー");
                if (ParseResult.HasValue)
                    return ParseResult.Value;
                return new FacialProfile("1.0");
            }

            public string SerializeProfile(FacialProfile profile)
            {
                return SerializeResult;
            }

            public FacialControlConfig ParseConfig(string json)
            {
                return new FacialControlConfig("1.0");
            }

            public string SerializeConfig(FacialControlConfig config)
            {
                return "{}";
            }
        }

        private class FakeProfileRepository : IProfileRepository
        {
            public FacialProfile? LoadResult { get; set; }
            public bool ThrowOnLoad { get; set; }
            public string LastLoadedPath { get; private set; }
            public string LastSavedPath { get; private set; }
            public FacialProfile? LastSavedProfile { get; private set; }

            public FacialProfile LoadProfile(string path)
            {
                LastLoadedPath = path;
                if (ThrowOnLoad)
                    throw new FileNotFoundException("ファイルが見つかりません", path);
                if (LoadResult.HasValue)
                    return LoadResult.Value;
                return new FacialProfile("1.0");
            }

            public void SaveProfile(string path, FacialProfile profile)
            {
                LastSavedPath = path;
                LastSavedProfile = profile;
            }
        }

        // --- ヘルパー ---

        private FakeProfileRepository _repository;
        private FacialProfileMapper _mapper;

        private static FacialProfile CreateTestProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
            var expressions = new[]
            {
                new Expression("expr-1", "笑顔", "emotion"),
                new Expression("expr-2", "悲しみ", "emotion"),
                new Expression("expr-3", "あ", "lipsync")
            };
            return new FacialProfile("1.0", layers, expressions);
        }

        private FacialProfileSO CreateSO(string jsonFilePath = "profiles/test.json")
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialProfileSO>();
            so.JsonFilePath = jsonFilePath;
            return so;
        }

        [SetUp]
        public void SetUp()
        {
            _repository = new FakeProfileRepository();
            _mapper = new FacialProfileMapper(_repository);
        }

        [TearDown]
        public void TearDown()
        {
            // SO インスタンスのクリーンアップはテスト内で行う
        }

        // --- コンストラクタ ---

        [Test]
        public void Constructor_NullRepository_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new FacialProfileMapper(null));
        }

        [Test]
        public void Constructor_ValidRepository_CreatesInstance()
        {
            var mapper = new FacialProfileMapper(_repository);

            Assert.IsNotNull(mapper);
        }

        // --- ToProfile (SO → FacialProfile) ---

        [Test]
        public void ToProfile_ValidSO_ReturnsProfile()
        {
            var so = CreateSO("profiles/test.json");
            var expectedProfile = CreateTestProfile();
            _repository.LoadResult = expectedProfile;

            var result = _mapper.ToProfile(so);

            Assert.AreEqual("1.0", result.SchemaVersion);
            Assert.AreEqual(3, result.Layers.Length);
            Assert.AreEqual(3, result.Expressions.Length);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void ToProfile_ValidSO_PassesJsonFilePathToRepository()
        {
            var so = CreateSO("profiles/myprofile.json");

            _mapper.ToProfile(so);

            Assert.AreEqual("profiles/myprofile.json", _repository.LastLoadedPath);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void ToProfile_NullSO_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _mapper.ToProfile(null));
        }

        [Test]
        public void ToProfile_NullJsonFilePath_ThrowsArgumentException()
        {
            var so = CreateSO(null);

            Assert.Throws<ArgumentException>(() =>
                _mapper.ToProfile(so));
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void ToProfile_EmptyJsonFilePath_ThrowsArgumentException()
        {
            var so = CreateSO("");

            Assert.Throws<ArgumentException>(() =>
                _mapper.ToProfile(so));
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void ToProfile_WhitespaceJsonFilePath_ThrowsArgumentException()
        {
            var so = CreateSO("   ");

            Assert.Throws<ArgumentException>(() =>
                _mapper.ToProfile(so));
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void ToProfile_RepositoryThrows_PropagatesException()
        {
            var so = CreateSO("profiles/nonexistent.json");
            _repository.ThrowOnLoad = true;

            Assert.Throws<FileNotFoundException>(() =>
                _mapper.ToProfile(so));
            UnityEngine.Object.DestroyImmediate(so);
        }

        // --- UpdateSO (FacialProfile → SO の表示フィールド更新) ---

        [Test]
        public void UpdateSO_ValidProfile_UpdatesSchemaVersion()
        {
            var so = CreateSO();
            var profile = CreateTestProfile();

            _mapper.UpdateSO(so, profile);

            Assert.AreEqual("1.0", so.SchemaVersion);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void UpdateSO_ValidProfile_UpdatesLayerCount()
        {
            var so = CreateSO();
            var profile = CreateTestProfile();

            _mapper.UpdateSO(so, profile);

            Assert.AreEqual(3, so.LayerCount);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void UpdateSO_ValidProfile_UpdatesExpressionCount()
        {
            var so = CreateSO();
            var profile = CreateTestProfile();

            _mapper.UpdateSO(so, profile);

            Assert.AreEqual(3, so.ExpressionCount);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void UpdateSO_EmptyProfile_SetsZeroCounts()
        {
            var so = CreateSO();
            var emptyProfile = new FacialProfile("1.0");

            _mapper.UpdateSO(so, emptyProfile);

            Assert.AreEqual(0, so.LayerCount);
            Assert.AreEqual(0, so.ExpressionCount);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void UpdateSO_NullSO_ThrowsArgumentNullException()
        {
            var profile = CreateTestProfile();

            Assert.Throws<ArgumentNullException>(() =>
                _mapper.UpdateSO(null, profile));
        }

        [Test]
        public void UpdateSO_DoesNotModifyJsonFilePath()
        {
            var so = CreateSO("original/path.json");
            var profile = CreateTestProfile();

            _mapper.UpdateSO(so, profile);

            Assert.AreEqual("original/path.json", so.JsonFilePath);
            UnityEngine.Object.DestroyImmediate(so);
        }

        // --- LoadAndUpdateSO (SO のパスからロード → SO 表示フィールド更新) ---

        [Test]
        public void LoadAndUpdateSO_ValidSO_LoadsAndUpdates()
        {
            var so = CreateSO("profiles/test.json");
            var expectedProfile = CreateTestProfile();
            _repository.LoadResult = expectedProfile;

            var result = _mapper.LoadAndUpdateSO(so);

            Assert.AreEqual("1.0", result.SchemaVersion);
            Assert.AreEqual(3, result.Layers.Length);
            Assert.AreEqual("1.0", so.SchemaVersion);
            Assert.AreEqual(3, so.LayerCount);
            Assert.AreEqual(3, so.ExpressionCount);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void LoadAndUpdateSO_NullSO_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _mapper.LoadAndUpdateSO(null));
        }

        [Test]
        public void LoadAndUpdateSO_InvalidPath_ThrowsArgumentException()
        {
            var so = CreateSO("");

            Assert.Throws<ArgumentException>(() =>
                _mapper.LoadAndUpdateSO(so));
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void LoadAndUpdateSO_RepositoryThrows_PropagatesException()
        {
            var so = CreateSO("profiles/missing.json");
            _repository.ThrowOnLoad = true;

            Assert.Throws<FileNotFoundException>(() =>
                _mapper.LoadAndUpdateSO(so));
            UnityEngine.Object.DestroyImmediate(so);
        }

        // --- SaveFromSO (Profile を SO のパスに保存) ---

        [Test]
        public void SaveFromSO_ValidArgs_SavesProfileToPath()
        {
            var so = CreateSO("profiles/output.json");
            var profile = CreateTestProfile();

            _mapper.SaveFromSO(so, profile);

            Assert.AreEqual("profiles/output.json", _repository.LastSavedPath);
            Assert.IsTrue(_repository.LastSavedProfile.HasValue);
            Assert.AreEqual("1.0", _repository.LastSavedProfile.Value.SchemaVersion);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void SaveFromSO_ValidArgs_UpdatesSOFields()
        {
            var so = CreateSO("profiles/output.json");
            var profile = CreateTestProfile();

            _mapper.SaveFromSO(so, profile);

            Assert.AreEqual("1.0", so.SchemaVersion);
            Assert.AreEqual(3, so.LayerCount);
            Assert.AreEqual(3, so.ExpressionCount);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void SaveFromSO_NullSO_ThrowsArgumentNullException()
        {
            var profile = CreateTestProfile();

            Assert.Throws<ArgumentNullException>(() =>
                _mapper.SaveFromSO(null, profile));
        }

        [Test]
        public void SaveFromSO_NullJsonFilePath_ThrowsArgumentException()
        {
            var so = CreateSO(null);
            var profile = CreateTestProfile();

            Assert.Throws<ArgumentException>(() =>
                _mapper.SaveFromSO(so, profile));
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void SaveFromSO_EmptyJsonFilePath_ThrowsArgumentException()
        {
            var so = CreateSO("");
            var profile = CreateTestProfile();

            Assert.Throws<ArgumentException>(() =>
                _mapper.SaveFromSO(so, profile));
            UnityEngine.Object.DestroyImmediate(so);
        }

        // --- P17-T05: RendererPaths の SO ↔ Profile 同期 ---

        [Test]
        public void UpdateSO_ProfileWithRendererPaths_SyncsRendererPathsToSO()
        {
            var so = CreateSO();
            var profile = new FacialProfile("1.0", rendererPaths: new[] { "Armature/Body", "Armature/Face" });

            _mapper.UpdateSO(so, profile);

            Assert.IsNotNull(so.RendererPaths);
            Assert.AreEqual(2, so.RendererPaths.Length);
            Assert.AreEqual("Armature/Body", so.RendererPaths[0]);
            Assert.AreEqual("Armature/Face", so.RendererPaths[1]);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void UpdateSO_ProfileWithEmptyRendererPaths_SetsEmptyArray()
        {
            var so = CreateSO();
            var profile = new FacialProfile("1.0");

            _mapper.UpdateSO(so, profile);

            Assert.IsNotNull(so.RendererPaths);
            Assert.AreEqual(0, so.RendererPaths.Length);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void LoadAndUpdateSO_ProfileWithRendererPaths_SyncsRendererPathsToSO()
        {
            var so = CreateSO("profiles/test.json");
            var profile = new FacialProfile("1.0", rendererPaths: new[] { "Armature/Body" });
            _repository.LoadResult = profile;

            _mapper.LoadAndUpdateSO(so);

            Assert.IsNotNull(so.RendererPaths);
            Assert.AreEqual(1, so.RendererPaths.Length);
            Assert.AreEqual("Armature/Body", so.RendererPaths[0]);
            UnityEngine.Object.DestroyImmediate(so);
        }

        [Test]
        public void SaveFromSO_ProfileWithRendererPaths_SyncsRendererPathsToSO()
        {
            var so = CreateSO("profiles/output.json");
            var profile = new FacialProfile("1.0", rendererPaths: new[] { "Armature/Body", "Armature/Head" });

            _mapper.SaveFromSO(so, profile);

            Assert.IsNotNull(so.RendererPaths);
            Assert.AreEqual(2, so.RendererPaths.Length);
            Assert.AreEqual("Armature/Body", so.RendererPaths[0]);
            Assert.AreEqual("Armature/Head", so.RendererPaths[1]);
            UnityEngine.Object.DestroyImmediate(so);
        }
    }
}
