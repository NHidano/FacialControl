using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;
using GazeBindingConfig = Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    /// <summary>
    /// <see cref="FacialCharacterProfileConverter"/> の SO → Domain 変換挙動を検証する。
    /// 特に bridge field <c>transitionDuration</c> が cachedSnapshot 存在時にも
    /// Inspector スライダー値（src.transitionDuration）として尊重されることを担保する。
    /// </summary>
    [TestFixture]
    public class FacialCharacterProfileConverterTests
    {
        [Test]
        public void ConvertExpressions_CachedSnapshotPresent_UsesInspectorTransitionDuration()
        {
            // Inspector スライダーで 0.6f に編集された Expression を再現する。
            // cachedSnapshot 側は AnimationEvent 由来の旧値 0.25f を保持しているケース。
            var layers = new List<LayerDefinitionSerializable>
            {
                new LayerDefinitionSerializable
                {
                    name = "emotion",
                    priority = 0,
                    exclusionMode = ExclusionMode.LastWins,
                },
            };
            var expressions = new List<ExpressionSerializable>
            {
                new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    transitionDuration = 0.6f,
                    cachedSnapshot = new ExpressionSnapshotDto
                    {
                        transitionDuration = 0.25f,
                        transitionCurvePreset = "Linear",
                        blendShapes = new List<BlendShapeSnapshotDto>
                        {
                            new BlendShapeSnapshotDto
                            {
                                rendererPath = "Body",
                                name = "笑い",
                                value = 1f,
                            },
                        },
                    },
                },
            };

            var profile = FacialCharacterProfileConverter.ToFacialProfile(
                "2.0",
                layers,
                expressions,
                new List<string> { "Body" });

            Assert.That(profile.Expressions.Length, Is.EqualTo(1));
            var expr = profile.Expressions.Span[0];
            Assert.That(
                expr.TransitionDuration,
                Is.EqualTo(0.6f).Within(1e-6f),
                "Inspector スライダー src.transitionDuration が cachedSnapshot より優先されるべき。");

            // BlendShape は引き続き cachedSnapshot から拾われていること。
            Assert.That(expr.BlendShapeValues.Length, Is.EqualTo(1));
            Assert.That(expr.BlendShapeValues.Span[0].Name, Is.EqualTo("笑い"));
        }

        [Test]
        public void ConvertExpressions_LayerOverrideMask_MapsToOverrideMaskBits()
        {
            // layers 宣言順: emotion(bit0), overlay(bit1)。
            // smile.layerOverrideMask=["overlay"] が OverrideMask=Bit1 に変換されることを検証する。
            var layers = new List<LayerDefinitionSerializable>
            {
                new LayerDefinitionSerializable { name = "emotion", priority = 0, exclusionMode = ExclusionMode.LastWins },
                new LayerDefinitionSerializable { name = "overlay", priority = 1, exclusionMode = ExclusionMode.LastWins },
            };
            var expressions = new List<ExpressionSerializable>
            {
                new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    transitionDuration = 0.1f,
                    layerOverrideMask = new List<string> { "overlay" },
                },
            };

            var profile = FacialCharacterProfileConverter.ToFacialProfile(
                "2.0", layers, expressions, new List<string> { "Body" });

            var expr = profile.Expressions.Span[0];
            Assert.That(expr.OverrideMask, Is.EqualTo(LayerOverrideMask.Bit1),
                "overlay は layers index1 なので Bit1 が立つべき。");
        }

        [Test]
        public void ConvertExpressions_EmptyLayerOverrideMask_OverrideMaskNone()
        {
            var layers = new List<LayerDefinitionSerializable>
            {
                new LayerDefinitionSerializable { name = "emotion", priority = 0, exclusionMode = ExclusionMode.LastWins },
            };
            var expressions = new List<ExpressionSerializable>
            {
                new ExpressionSerializable
                {
                    id = "smile", name = "Smile", layer = "emotion", transitionDuration = 0.1f,
                    layerOverrideMask = new List<string>(),
                },
            };

            var profile = FacialCharacterProfileConverter.ToFacialProfile(
                "2.0", layers, expressions, new List<string> { "Body" });

            Assert.That(profile.Expressions.Span[0].OverrideMask, Is.EqualTo(LayerOverrideMask.None));
        }

        [Test]
        public void ConvertExpressions_CachedSnapshotAbsent_UsesInspectorTransitionDuration()
        {
            // cachedSnapshot が無い場合も従来通り src.transitionDuration が使われる。
            var layers = new List<LayerDefinitionSerializable>
            {
                new LayerDefinitionSerializable
                {
                    name = "emotion",
                    priority = 0,
                    exclusionMode = ExclusionMode.LastWins,
                },
            };
            var expressions = new List<ExpressionSerializable>
            {
                new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    transitionDuration = 0.4f,
                },
            };

            var profile = FacialCharacterProfileConverter.ToFacialProfile(
                "2.0",
                layers,
                expressions,
                new List<string>());

            Assert.That(profile.Expressions.Length, Is.EqualTo(1));
            Assert.That(
                profile.Expressions.Span[0].TransitionDuration,
                Is.EqualTo(0.4f).Within(1e-6f));
        }

        [Test]
        public void ToFacialProfile_SlotsAndSerializableOverlayStates_MapsNewSchemaToDomain()
        {
            var layers = new List<LayerDefinitionSerializable>
            {
                new LayerDefinitionSerializable
                {
                    name = "emotion",
                    priority = 0,
                    exclusionMode = ExclusionMode.LastWins,
                },
            };
            var expressions = new List<ExpressionSerializable>
            {
                new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    transitionDuration = 0.2f,
                    overlays = new List<OverlaySlotBindingSerializable>
                    {
                        new OverlaySlotBindingSerializable { slot = "blink" },
                        new OverlaySlotBindingSerializable
                        {
                            slot = "blush",
                            suppress = true,
                            cachedSnapshot = CreateSnapshotDto("Face", "Ignored", 1f),
                        },
                        new OverlaySlotBindingSerializable
                        {
                            slot = "sparkle",
                            cachedSnapshot = CreateSnapshotDto("Face", "Sparkle", 0.75f),
                        },
                    },
                },
            };
            var defaultOverlays = new List<OverlaySlotBindingSerializable>
            {
                new OverlaySlotBindingSerializable
                {
                    slot = "blink",
                    cachedSnapshot = CreateSnapshotDto("Face", "Blink", 1f),
                },
            };

            var profile = FacialCharacterProfileConverter.ToFacialProfile(
                schemaVersion: SystemTextJsonParser.SchemaVersionV2,
                layers: layers,
                expressions: expressions,
                rendererPaths: new List<string> { "Face" },
                defaultOverlays: defaultOverlays,
                slots: new List<string> { "blink", "blush", "sparkle" });

            Assert.That(profile.Slots.Length, Is.EqualTo(3));
            Assert.That(profile.Slots.Span[0], Is.EqualTo("blink"));

            var overlays = profile.Expressions.Span[0].Overlays.Span;
            Assert.That(overlays.Length, Is.EqualTo(3));
            Assert.That(overlays[0].Slot, Is.EqualTo("blink"));
            Assert.That(overlays[0].IsDefaultFallback, Is.True);

            Assert.That(overlays[1].Slot, Is.EqualTo("blush"));
            Assert.That(overlays[1].Suppress, Is.True);
            Assert.That(overlays[1].Snapshot.HasValue, Is.False);

            Assert.That(overlays[2].Slot, Is.EqualTo("sparkle"));
            Assert.That(overlays[2].Suppress, Is.False);
            Assert.That(overlays[2].Snapshot.HasValue, Is.True);
            Assert.That(overlays[2].Snapshot.Value.Id, Is.EqualTo("sparkle"));
            Assert.That(overlays[2].Snapshot.Value.BlendShapes.Span[0].Name, Is.EqualTo("Sparkle"));

            var defaultOverlay = profile.DefaultOverlays.Span[0];
            Assert.That(defaultOverlay.Slot, Is.EqualTo("blink"));
            Assert.That(defaultOverlay.Snapshot.HasValue, Is.True);
            Assert.That(defaultOverlay.Snapshot.Value.BlendShapes.Span[0].Name, Is.EqualTo("Blink"));
        }

        [Test]
        public void ToProfileSnapshotDto_DomainOverlayStates_EmitsNewOverlayDtoSchema()
        {
            var overlaySnapshot = new ExpressionSnapshot(
                "sparkle",
                0.3f,
                TransitionCurvePreset.EaseOut,
                new[]
                {
                    new BlendShapeSnapshot("Face", "Sparkle", 0.75f),
                },
                null,
                new[] { "Face" });
            var expression = new Expression(
                "smile",
                "Smile",
                "emotion",
                transitionDuration: 0.2f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[] { new BlendShapeMapping("MouthSmile", 1f, "Face") },
                overlays: new[]
                {
                    new OverlaySlotBinding("blink", suppress: false, snapshot: null),
                    new OverlaySlotBinding("blush", suppress: true, snapshot: null),
                    new OverlaySlotBinding("sparkle", suppress: false, snapshot: overlaySnapshot),
                });
            var profile = new FacialProfile(
                SystemTextJsonParser.SchemaVersionV2,
                layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) },
                expressions: new[] { expression },
                rendererPaths: new[] { "Face" },
                slots: new[] { "blink", "blush", "sparkle" });

            ProfileSnapshotDto dto = FacialCharacterProfileConverter.ToProfileSnapshotDto(profile);

            Assert.That(dto.slots, Is.EqualTo(new List<string> { "blink", "blush", "sparkle" }));
            var overlayDtos = dto.expressions[0].snapshot.overlays;
            Assert.That(overlayDtos, Has.Count.EqualTo(3));
            Assert.That(overlayDtos[0].slot, Is.EqualTo("blink"));
            Assert.That(overlayDtos[0].suppress, Is.False);
            Assert.That(overlayDtos[0].snapshot, Is.Null);
            Assert.That(overlayDtos[1].slot, Is.EqualTo("blush"));
            Assert.That(overlayDtos[1].suppress, Is.True);
            Assert.That(overlayDtos[1].snapshot, Is.Null);
            Assert.That(overlayDtos[2].slot, Is.EqualTo("sparkle"));
            Assert.That(overlayDtos[2].suppress, Is.False);
            Assert.That(overlayDtos[2].snapshot, Is.Not.Null);
            Assert.That(overlayDtos[2].snapshot.blendShapes[0].name, Is.EqualTo("Sparkle"));

            string json = new SystemTextJsonParser().SerializeProfileSnapshot(dto);
            var reparsed = new SystemTextJsonParser().ParseProfile(json);
            Assert.That(reparsed.Slots.Length, Is.EqualTo(3));
            Assert.That(reparsed.Expressions.Span[0].Overlays.Span[2].Snapshot.HasValue, Is.True);
        }

        [Test]
        public void ToSORootGazeConfigs_RootDtoGazeConfigs_MapsEveryValueToRootConfig()
        {
            var dto = new ProfileSnapshotDto
            {
                gazeConfigs = new List<GazeBindingConfigDto>
                {
                    new GazeBindingConfigDto
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
                    },
                },
            };

            List<GazeBindingConfig> configs = FacialCharacterProfileConverter.ToSORootGazeConfigs(dto);

            Assert.That(configs, Has.Count.EqualTo(1));
            GazeBindingConfig config = configs[0];
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
            Assert.That(config.lookLeftClip, Is.Null);
            Assert.That(config.lookRightClip, Is.Null);
            Assert.That(config.lookUpClip, Is.Null);
            Assert.That(config.lookDownClip, Is.Null);
            Assert.That(config.lookLeftSamples, Is.Empty);
            Assert.That(config.lookRightSamples, Is.Empty);
            Assert.That(config.lookUpSamples, Is.Empty);
            Assert.That(config.lookDownSamples, Is.Empty);
        }

        [Test]
        public void ToGazeConfigDtos_DefaultAndDistinctModes_MapsEveryValueToDto()
        {
            var configs = new List<GazeBindingConfig>
            {
                new GazeBindingConfig
                {
                    expressionId = "eye_default",
                    useDistinctLeftRight = false,
                    leftEyeBonePath = "Head/LeftEye",
                    rightEyeBonePath = "Head/RightEye",
                },
                new GazeBindingConfig
                {
                    expressionId = "eye_distinct",
                    useDistinctLeftRight = true,
                    sourceIdLeft = "input:eye_distinct.left",
                    sourceIdRight = "osc:eye_distinct.right",
                    leftEyeBonePath = "Armature/LeftEye",
                    rightEyeBonePath = "Armature/RightEye",
                    lookUpAngle = 20f,
                    lookDownAngle = 10f,
                    outerYawAngle = 21f,
                    innerYawAngle = 11f,
                },
            };

            List<GazeBindingConfigDto> dtos = FacialCharacterProfileConverter.ToGazeConfigDtos(configs);

            Assert.That(dtos, Has.Count.EqualTo(2));
            Assert.That(dtos[0].expressionId, Is.EqualTo("eye_default"));
            Assert.That(dtos[0].useDistinctLeftRight, Is.False);
            Assert.That(dtos[0].sourceIdLeft, Is.EqualTo(string.Empty));
            Assert.That(dtos[0].sourceIdRight, Is.EqualTo(string.Empty));
            Assert.That(dtos[0].leftEyeBonePath, Is.EqualTo("Head/LeftEye"));
            Assert.That(dtos[0].rightEyeBonePath, Is.EqualTo("Head/RightEye"));
            Assert.That(dtos[1].expressionId, Is.EqualTo("eye_distinct"));
            Assert.That(dtos[1].useDistinctLeftRight, Is.True);
            Assert.That(dtos[1].sourceIdLeft, Is.EqualTo("input:eye_distinct.left"));
            Assert.That(dtos[1].sourceIdRight, Is.EqualTo("osc:eye_distinct.right"));
            Assert.That(dtos[1].lookUpAngle, Is.EqualTo(20f));
            Assert.That(dtos[1].lookDownAngle, Is.EqualTo(10f));
            Assert.That(dtos[1].outerYawAngle, Is.EqualTo(21f));
            Assert.That(dtos[1].innerYawAngle, Is.EqualTo(11f));
        }

        [Test]
        public void ToSORootGazeConfigs_NullOrEmptyRootDto_ReturnsEmptyList()
        {
            Assert.That(FacialCharacterProfileConverter.ToSORootGazeConfigs((ProfileSnapshotDto)null), Is.Empty);
            Assert.That(FacialCharacterProfileConverter.ToSORootGazeConfigs(new ProfileSnapshotDto()), Is.Empty);
        }

        private static OverlaySnapshotDto CreateSnapshotDto(
            string rendererPath,
            string blendShapeName,
            float value)
        {
            return new OverlaySnapshotDto
            {
                transitionDuration = 0.1f,
                transitionCurvePreset = "EaseIn",
                rendererPaths = new List<string> { rendererPath },
                blendShapes = new List<BlendShapeSnapshotDto>
                {
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = rendererPath,
                        name = blendShapeName,
                        value = value,
                    },
                },
            };
        }
    }
}
