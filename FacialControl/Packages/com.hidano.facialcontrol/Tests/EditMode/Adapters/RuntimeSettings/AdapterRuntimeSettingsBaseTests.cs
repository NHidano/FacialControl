using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// task 2.1 の観測可能完了条件: <see cref="AdapterRuntimeSettingsBase"/> 派生型を生成し、
    /// <c>_schemaVersion == 1</c>、<c>Label</c> getter、ToJson/FromJson 既定実装の warning 出力を検証する。
    /// </summary>
    [TestFixture]
    public class AdapterRuntimeSettingsBaseTests
    {
        public sealed class FakeAdapterRuntimeSettings : AdapterRuntimeSettingsBase
        {
        }

        private FakeAdapterRuntimeSettings _instance;

        [SetUp]
        public void SetUp()
        {
            _instance = ScriptableObject.CreateInstance<FakeAdapterRuntimeSettings>();
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
        public void SchemaVersion_OnFreshInstance_ReturnsOne()
        {
            Assert.AreEqual(1, _instance.SchemaVersion);
        }

        [Test]
        public void Label_OnFreshInstance_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, _instance.Label);
        }

        [Test]
        public void Label_AfterSerializedFieldAssignment_ReturnsAssignedValue()
        {
            var so = new UnityEditor.SerializedObject(_instance);
            so.FindProperty("_label").stringValue = "primary";
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual("primary", _instance.Label);
        }

        [Test]
        public void ToJson_WithoutOverride_LogsWarningAndReturnsEmptyString()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"ToJson\(\)\s*を override していません"));

            var json = _instance.ToJson();

            Assert.AreEqual(string.Empty, json);
        }

        [Test]
        public void FromJson_WithoutOverride_LogsWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"FromJson\(string\)\s*を override していません"));

            Assert.DoesNotThrow(() => _instance.FromJson("{}"));
        }
    }
}
