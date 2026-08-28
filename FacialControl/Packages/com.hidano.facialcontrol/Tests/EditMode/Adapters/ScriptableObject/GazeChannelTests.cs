using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using GazeChannel = Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    public sealed class GazeChannelTests
    {
        private sealed class TestProfile : FacialCharacterProfileSO
        {
            public List<GazeChannel> WritableGazeChannels => _gazeChannels;
            public List<LegacyGazeConfigEntry> WritableLegacyGazeConfigs { get => _legacyGazeConfigs; set => _legacyGazeConfigs = value; }
        }

        [Test]
        public void GazeChannels_NewProfile_ProvidesDefaultChannel()
        {
            var profile = ScriptableObject.CreateInstance<TestProfile>();
            try
            {
                Assert.That(profile.GazeChannels, Has.Count.EqualTo(1));
                Assert.That(profile.GazeChannels[0].id, Is.EqualTo("gaze"));
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void GazeChannels_InvalidRootState_RepairsDefaultChannel()
        {
            var profile = ScriptableObject.CreateInstance<TestProfile>();
            try
            {
                profile.WritableGazeChannels.Clear();
                Assert.That(profile.GazeChannels[0].id, Is.EqualTo("gaze"));

                profile.WritableGazeChannels[0].id = "custom";
                Assert.That(profile.GazeChannels[0].id, Is.EqualTo("gaze"));
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void ExpressionSerializable_IsGaze_IsNotSerialized()
        {
            var profile = ScriptableObject.CreateInstance<TestProfile>();
            try
            {
                profile.Expressions.Add(new ExpressionSerializable { id = "expression" });
                var serialized = new SerializedObject(profile);
                var expression = serialized.FindProperty("_expressions").GetArrayElementAtIndex(0);
                Assert.That(expression.FindPropertyRelative("isGaze"), Is.Null);
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void LegacyGazeConfigs_AreDetectedAndMigratedToChannels()
        {
            var profile = ScriptableObject.CreateInstance<TestProfile>();
            try
            {
                profile.WritableLegacyGazeConfigs = new List<LegacyGazeConfigEntry>
                {
                    new LegacyGazeConfigEntry
                    {
                        expressionId = "lookLeft",
                        leftEyeBonePath = "Head/LeftEye",
                        lookUpAngle = 23f
                    }
                };
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("旧 _gazeConfigs"));
                profile.OnAfterDeserialize();

                Assert.That(profile.HasLegacyGazeConfigs, Is.True);
                Assert.That(profile.LegacyGazeConfigCount, Is.EqualTo(1));
                Assert.That(profile.LegacyGazeConfigIds[0], Is.EqualTo("lookLeft"));
                Assert.That(profile.GazeChannels, Has.Count.EqualTo(2));
                Assert.That(profile.GazeChannels[0].id, Is.EqualTo("gaze"));
                Assert.That(profile.GazeChannels[1].id, Is.EqualTo("lookLeft"));
                Assert.That(profile.GazeChannels[1].leftEyeBonePath, Is.EqualTo("Head/LeftEye"));
                Assert.That(profile.GazeChannels[1].lookUpAngle, Is.EqualTo(23f));
                Assert.That(profile.WritableLegacyGazeConfigs, Is.Empty);
                Assert.That(new SerializedObject(profile).FindProperty("_legacyGazeConfigs").arraySize, Is.EqualTo(0));
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void NewProfile_HasNoLegacyGazeConfigs()
        {
            var profile = ScriptableObject.CreateInstance<TestProfile>();
            try
            {
                Assert.That(profile.HasLegacyGazeConfigs, Is.False);
                Assert.That(profile.LegacyGazeConfigCount, Is.EqualTo(0));
            }
            finally { Object.DestroyImmediate(profile); }
        }
    }
}
