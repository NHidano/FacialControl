using System;
using System.Collections;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.LipSync.Adapters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    public class ULipSyncAdapterBindingConflictDetectionTests
    {
        private const string Slug = "ulipsync";
        private const string BlendShapeName = "Mouth_A";

        private InputSourceRegistry _registry;
        private GameObject _hostGameObject;

        [SetUp]
        public void SetUp()
        {
            _registry = new InputSourceRegistry();
            _hostGameObject = new GameObject("ULipSyncAdapterBindingConflictDetectionTestsHost");
        }

        [TearDown]
        public void TearDown()
        {
            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }
        }

        [Test]
        public void OnStart_LegacyLipSyncSourceAlsoRegistered_LogsConflictWarningOnce()
        {
            _registry.Register(AdapterSlug.Parse(Slug), new StubInputSource(Slug));
            AdapterBuildContext ctx = CreateContext();

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*Legacy LipSync.*ulipsync.*slot 'a'.*double-write.*migrate"));

            bool warned = ULipSyncAdapterBinding.WarnIfLegacyLipSyncSourceAlsoRegistered(
                in ctx,
                AdapterSlug.Parse(Slug));

            Assert.That(warned, Is.True);
            Assert.That(_registry.TryResolve(Slug, out IInputSource legacy), Is.True);
            Assert.That(legacy, Is.InstanceOf<StubInputSource>());
        }

        private AdapterBuildContext CreateContext()
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("1.0", slots: new[] { PhonemeOverlaySlots.A }),
                blendShapeNames: new[] { BlendShapeName },
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _hostGameObject,
                lipSyncProvider: null);
        }

        private sealed class StubInputSource : IInputSource
        {
            public StubInputSource(string id)
            {
                Id = id;
                Type = InputSourceType.ValueProvider;
                BlendShapeCount = 1;
                ContributeMask = new BitArray(BlendShapeCount, true);
            }

            public string Id { get; }
            public InputSourceType Type { get; }
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }

            public bool TryWriteValues(Span<float> output)
            {
                if (output.Length > 0)
                {
                    output[0] = 1f;
                }

                return true;
            }
        }
    }
}
