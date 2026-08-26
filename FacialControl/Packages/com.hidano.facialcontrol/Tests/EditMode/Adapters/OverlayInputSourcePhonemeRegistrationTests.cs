using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.DependencyInjection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using VContainer.Unity;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    public class OverlayInputSourcePhonemeRegistrationTests
    {
        private sealed class NoopAdapterBinding : AdapterBindingBase
        {
            public int OnStartCount;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                OnStartCount++;
            }
        }

        private GameObject _hostGameObject;

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
        public void Initialize_PhonemeSlotsDeclared_RegistersOverlayInputSourcePerSlot()
        {
            var registry = new InputSourceRegistry();
            var binding = new NoopAdapterBinding();
            var host = new AdapterBindingHost(
                binding,
                CreateContext(registry, new[] { "a", "i", "u", "e", "o" }));

            ((IInitializable)host).Initialize();

            CollectionAssert.AreEqual(
                new[] { "overlay:a", "overlay:i", "overlay:u", "overlay:e", "overlay:o" },
                registry.RegisteredIds);
            AssertOverlaySource(registry, "overlay:a");
            AssertOverlaySource(registry, "overlay:i");
            AssertOverlaySource(registry, "overlay:u");
            AssertOverlaySource(registry, "overlay:e");
            AssertOverlaySource(registry, "overlay:o");
            Assert.AreEqual(1, binding.OnStartCount);
        }

        [Test]
        public void Initialize_PhonemeSlotNotDeclared_DoesNotRegister()
        {
            var registry = new InputSourceRegistry();
            var host = new AdapterBindingHost(
                new NoopAdapterBinding(),
                CreateContext(registry, new[] { "blink" }));

            ((IInitializable)host).Initialize();

            Assert.IsFalse(registry.TryResolve("overlay:a", out var resolved));
            Assert.IsNull(resolved);
            Assert.AreEqual(0, registry.RegisteredIds.Count);
        }

        [Test]
        public void Initialize_NonPhonemeSlot_NotAffected()
        {
            var registry = new InputSourceRegistry();
            var host = new AdapterBindingHost(
                new NoopAdapterBinding(),
                CreateContext(registry, new[] { "a", "blink" }));

            ((IInitializable)host).Initialize();

            CollectionAssert.AreEqual(new[] { "overlay:a" }, registry.RegisteredIds);
            AssertOverlaySource(registry, "overlay:a");
            Assert.IsFalse(registry.TryResolve("overlay:blink", out var blink));
            Assert.IsNull(blink);
        }

        [Test]
        public void Initialize_MultipleHosts_DoNotDuplicatePhonemeOverlaySources()
        {
            var registry = new InputSourceRegistry();
            var context = CreateContext(registry, new[] { "a", "i" });
            var hostA = new AdapterBindingHost(new NoopAdapterBinding(), context);
            var hostB = new AdapterBindingHost(new NoopAdapterBinding(), context);

            ((IInitializable)hostA).Initialize();
            ((IInitializable)hostB).Initialize();

            CollectionAssert.AreEqual(new[] { "overlay:a", "overlay:i" }, registry.RegisteredIds);
        }

        private AdapterBuildContext CreateContext(
            IInputSourceRegistry registry,
            string[] slots)
        {
            _hostGameObject = new GameObject("OverlayInputSourcePhonemeRegistrationTestsHost");
            return new AdapterBuildContext(
                new FacialProfile("1.0", slots: slots),
                new List<string> { "JawOpen", "MouthSmile" },
                registry,
                new FacialOutputBus(),
                new ManualTimeProvider(),
                _hostGameObject,
                lipSyncProvider: null);
        }

        private static void AssertOverlaySource(InputSourceRegistry registry, string id)
        {
            Assert.IsTrue(registry.TryResolve(id, out var source), id + " must be registered.");
            Assert.IsInstanceOf<OverlayInputSource>(source);
        }
    }
}
