using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.AutoExport;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.AutoExport
{
    [TestFixture]
    public class FacialCharacterProfileExporter_GazeConfigsTests
    {
        private sealed class TestCharacterSO : FacialCharacterProfileSO
        {
            public List<GazeBindingConfig> WritableGazeConfigs => _gazeConfigs;
        }

        [Test]
        public void ExportProfileJson_SORootGazeConfigs_WritesRootGazeConfigsOnly()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<TestCharacterSO>();
            string assetName = "ExporterGazeConfigs_" + Guid.NewGuid().ToString("N");
            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
            string profileDirectory = Path.GetDirectoryName(profilePath);

            try
            {
                so.name = assetName;
                so.WritableGazeConfigs.Add(new GazeBindingConfig
                {
                    expressionId = "eye_look",
                    useDistinctLeftRight = true,
                    sourceIdLeft = "input:eye_look.left",
                    sourceIdRight = "osc:eye_look.right",
                    leftEyeBonePath = "Armature/Hips/Head/LeftEye",
                    leftEyeInitialRotation = new Vector3(1f, 2f, 3f),
                    leftEyeYawAxisLocal = new Vector3(0f, 1f, 0f),
                    leftEyePitchAxisLocal = new Vector3(1f, 0f, 0f),
                    rightEyeBonePath = "Armature/Hips/Head/RightEye",
                    rightEyeInitialRotation = new Vector3(4f, 5f, 6f),
                    rightEyeYawAxisLocal = new Vector3(0f, 0.75f, 0.25f),
                    rightEyePitchAxisLocal = new Vector3(0.5f, 0f, 0.5f),
                    lookUpAngle = 16f,
                    lookDownAngle = 8f,
                    outerYawAngle = 17f,
                    innerYawAngle = 12f,
                    lookLeftClip = new AnimationClip { name = "LookLeftShouldRemainInSO" },
                    lookRightClip = new AnimationClip { name = "LookRightShouldRemainInSO" },
                    lookUpClip = new AnimationClip { name = "LookUpShouldRemainInSO" },
                    lookDownClip = new AnimationClip { name = "LookDownShouldRemainInSO" },
                    lookLeftSamples = new List<GazeBlendShapeSampleEntry>
                    {
                        new GazeBlendShapeSampleEntry { blendShapeName = "LookLeft", weight = 100f },
                    },
                });

                bool exported = FacialCharacterProfileExporter.ExportProfileJson(so);

                Assert.That(exported, Is.True);
                Assert.That(File.Exists(profilePath), Is.True);

                string json = File.ReadAllText(profilePath);
                StringAssert.Contains("\"gaze_configs\"", json);
                StringAssert.Contains("\"useDistinctLeftRight\"", json);
                StringAssert.Contains("\"sourceIdLeft\"", json);
                StringAssert.Contains("\"sourceIdRight\"", json);
                StringAssert.DoesNotContain("\"_gazeConfigs\"", json);
                StringAssert.DoesNotContain("\"lookLeftClip\"", json);
                StringAssert.DoesNotContain("\"lookRightClip\"", json);
                StringAssert.DoesNotContain("\"lookUpClip\"", json);
                StringAssert.DoesNotContain("\"lookDownClip\"", json);
                StringAssert.DoesNotContain("\"lookLeftSamples\"", json);
                StringAssert.DoesNotContain("LookLeftShouldRemainInSO", json);
                StringAssert.DoesNotContain("LookLeft", json);

                var dto = new SystemTextJsonParser().ParseProfileSnapshotV2(json);

                Assert.That(dto.gazeConfigs, Has.Count.EqualTo(1));
                var config = dto.gazeConfigs[0];
                Assert.That(config.expressionId, Is.EqualTo("eye_look"));
                Assert.That(config.useDistinctLeftRight, Is.True);
                Assert.That(config.sourceIdLeft, Is.EqualTo("input:eye_look.left"));
                Assert.That(config.sourceIdRight, Is.EqualTo("osc:eye_look.right"));
                Assert.That(config.leftEyeBonePath, Is.EqualTo("Armature/Hips/Head/LeftEye"));
                Assert.That(config.leftEyeInitialRotation, Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(config.leftEyeYawAxisLocal, Is.EqualTo(new Vector3(0f, 1f, 0f)));
                Assert.That(config.leftEyePitchAxisLocal, Is.EqualTo(new Vector3(1f, 0f, 0f)));
                Assert.That(config.rightEyeBonePath, Is.EqualTo("Armature/Hips/Head/RightEye"));
                Assert.That(config.rightEyeInitialRotation, Is.EqualTo(new Vector3(4f, 5f, 6f)));
                Assert.That(config.rightEyeYawAxisLocal, Is.EqualTo(new Vector3(0f, 0.75f, 0.25f)));
                Assert.That(config.rightEyePitchAxisLocal, Is.EqualTo(new Vector3(0.5f, 0f, 0.5f)));
                Assert.That(config.lookUpAngle, Is.EqualTo(16f));
                Assert.That(config.lookDownAngle, Is.EqualTo(8f));
                Assert.That(config.outerYawAngle, Is.EqualTo(17f));
                Assert.That(config.innerYawAngle, Is.EqualTo(12f));
            }
            finally
            {
                if (Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, true);
                }

                Object.DestroyImmediate(so);
            }
        }
    }
}
