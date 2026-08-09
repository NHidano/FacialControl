using System;
using System.Collections;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    [TestFixture]
    public class GazeBindingConfigResolverTests
    {
        [TestCase(GazeSide.Shared, "eye")]
        [TestCase(GazeSide.Left, "eye.left")]
        [TestCase(GazeSide.Right, "eye.right")]
        public void ComposeSourceSub_AllSides_ReturnsConventionSub(GazeSide side, string expected)
        {
            Assert.That(
                GazeBindingConfigResolver.ComposeSourceSub("eye", side),
                Is.EqualTo(expected));
        }

        [TestCase(GazeSide.Shared, "receiver:eye")]
        [TestCase(GazeSide.Left, "receiver:eye.left")]
        [TestCase(GazeSide.Right, "receiver:eye.right")]
        public void ComposeSourceId_AllSides_ReturnsConventionId(GazeSide side, string expected)
        {
            Assert.That(
                GazeBindingConfigResolver.ComposeSourceId("receiver", "eye", side),
                Is.EqualTo(expected));
        }

        [Test]
        public void TryResolve_DefaultMode_MultipleLeftRightProviders_SelectsLexicographicallyFirstSlug()
        {
            var registry = new InputSourceRegistry();
            var zLeft = new FakeGazeSource("z-receiver:eye.left", 0.1f, 0.2f);
            var zRight = new FakeGazeSource("z-receiver:eye.right", 0.3f, 0.4f);
            var aLeft = new FakeGazeSource("a-receiver:eye.left", 0.5f, 0.6f);
            var aRight = new FakeGazeSource("a-receiver:eye.right", 0.7f, 0.8f);
            Register(registry, "z-receiver", "eye.left", zLeft);
            Register(registry, "z-receiver", "eye.right", zRight);
            Register(registry, "a-receiver", "eye.left", aLeft);
            Register(registry, "a-receiver", "eye.right", aRight);
            var config = new GazeBindingConfig { expressionId = "eye" };

            LogAssert.Expect(
                LogType.Warning,
                new Regex("expressionId 'eye'.*multiple binding slugs.*selected 'a-receiver'"));

            bool resolved = GazeBindingConfigResolver.TryResolve(
                config,
                registry,
                out ResolvedGazeInputSources sources);

            Assert.That(resolved, Is.True);
            Assert.That(sources.SelectedSlug, Is.EqualTo("a-receiver"));
            Assert.That(sources.LeftSource, Is.SameAs(aLeft));
            Assert.That(sources.RightSource, Is.SameAs(aRight));
        }

        [Test]
        public void TryResolve_DefaultMode_LeftOnlyProvider_FallsBackToBothEyes()
        {
            var registry = new InputSourceRegistry();
            var left = new FakeGazeSource("input:eye.left", -0.25f, 0.5f);
            Register(registry, "input", "eye.left", left);
            var config = new GazeBindingConfig { expressionId = "eye" };

            bool resolved = GazeBindingConfigResolver.TryResolve(
                config,
                registry,
                out ResolvedGazeInputSources sources);

            Assert.That(resolved, Is.True);
            Assert.That(sources.LeftSource, Is.SameAs(left));
            Assert.That(sources.RightSource, Is.SameAs(left));
        }

        [Test]
        public void TryResolve_DefaultMode_NoSideProviders_UsesSharedExpressionIdProvider()
        {
            var registry = new InputSourceRegistry();
            var shared = new FakeGazeSource("input:eye", 0.25f, -0.5f);
            Register(registry, "input", "eye", shared);
            var config = new GazeBindingConfig { expressionId = "eye" };

            bool resolved = GazeBindingConfigResolver.TryResolve(
                config,
                registry,
                out ResolvedGazeInputSources sources);

            Assert.That(resolved, Is.True);
            Assert.That(sources.LeftSource, Is.SameAs(shared));
            Assert.That(sources.RightSource, Is.SameAs(shared));
        }

        [Test]
        public void TryResolve_DistinctMode_UsesCompleteSlugStringsAndWarnsOnSingleSideFallback()
        {
            var registry = new InputSourceRegistry();
            var right = new FakeGazeSource("osc:eye.right", 0.8f, -0.2f);
            Register(registry, "osc", "eye.right", right);
            var config = new GazeBindingConfig
            {
                expressionId = "eye",
                useDistinctLeftRight = true,
                sourceIdLeft = "missing:eye.left",
                sourceIdRight = "osc:eye.right",
            };

            LogAssert.Expect(
                LogType.Warning,
                new Regex("useDistinctLeftRight=true.*expressionId 'eye'.*resolved only one side"));

            bool resolved = GazeBindingConfigResolver.TryResolve(
                config,
                registry,
                out ResolvedGazeInputSources sources);

            Assert.That(resolved, Is.True);
            Assert.That(sources.LeftSource, Is.SameAs(right));
            Assert.That(sources.RightSource, Is.SameAs(right));
        }

        [Test]
        public void TryResolve_DistinctMode_DoesNotAutoMatchExpressionId()
        {
            var registry = new InputSourceRegistry();
            Register(registry, "osc", "eye.left", new FakeGazeSource("osc:eye.left", -1f, 0f));
            Register(registry, "osc", "eye.right", new FakeGazeSource("osc:eye.right", 1f, 0f));
            var config = new GazeBindingConfig
            {
                expressionId = "eye",
                useDistinctLeftRight = true,
                sourceIdLeft = "other:eye.left",
                sourceIdRight = "other:eye.right",
            };

            bool resolved = GazeBindingConfigResolver.TryResolve(
                config,
                registry,
                out _);

            Assert.That(resolved, Is.False);
        }

        private static void Register(
            InputSourceRegistry registry,
            string slug,
            string sub,
            FakeGazeSource source)
        {
            registry.Register(AdapterSlug.Parse(slug), sub, source);
        }

        private sealed class FakeGazeSource : IInputSource, IAnalogInputSource
        {
            private readonly float _x;
            private readonly float _y;

            public FakeGazeSource(string id, float x, float y)
            {
                Id = id;
                _x = x;
                _y = y;
                ContributeMask = new BitArray(0);
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 0;
            public BitArray ContributeMask { get; }
            public bool IsValid => true;
            public int AxisCount => 2;

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output)
            {
                return false;
            }

            public bool TryReadScalar(out float value)
            {
                value = _x;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = _x;
                y = _y;
                return true;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length > 0)
                {
                    output[0] = _x;
                }
                if (output.Length > 1)
                {
                    output[1] = _y;
                }

                return true;
            }
        }
    }
}
