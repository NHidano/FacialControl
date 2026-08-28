using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Editor.Inspector;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector
{
    public sealed class GazeProviderEnumeratorTests
    {
        private sealed class DeclaringBinding : AdapterBindingBase, IGazeSourceProvider
        {
            public bool Wildcard;
            public IEnumerable<GazeSourceDeclaration> GetGazeSourceDeclarations()
            {
                yield return new GazeSourceDeclaration(Wildcard ? null : "gaze", true);
            }
        }

        private sealed class NonDeclaringBinding : AdapterBindingBase { }

        [Test]
        public void Enumerate_DeclaredAndWildcardProviders_IncludesAutomaticAndMatchingProviders()
        {
            var declared = new DeclaringBinding { Slug = "controller" };
            var wildcard = new DeclaringBinding { Slug = "camera", Wildcard = true };
            var choices = new GazeProviderEnumerator().Enumerate(
                new AdapterBindingBase[] { declared, new NonDeclaringBinding { Slug = "none" }, wildcard }, "gaze");

            Assert.That(choices[0].Slug, Is.EqualTo(string.Empty));
            Assert.That(choices.Select(c => c.Slug), Is.EquivalentTo(new[] { "", "controller", "camera" }));
        }

        [Test]
        public void Enumerate_NonMatchingDeclaration_ExcludesProvider()
        {
            var binding = new DeclaringBinding { Slug = "provider" };
            var choices = new GazeProviderEnumerator().Enumerate(new[] { binding }, "other");
            Assert.That(choices, Has.Count.EqualTo(1));
        }
    }
}
