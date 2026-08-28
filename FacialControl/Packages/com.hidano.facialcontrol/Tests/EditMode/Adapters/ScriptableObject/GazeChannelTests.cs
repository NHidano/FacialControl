using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using GazeChannel = Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    public sealed class GazeChannelTests
    {
        private sealed class TestProfile : FacialCharacterProfileSO
        {
            public List<GazeChannel> WritableGazeChannels => _gazeChannels;
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
    }
}
