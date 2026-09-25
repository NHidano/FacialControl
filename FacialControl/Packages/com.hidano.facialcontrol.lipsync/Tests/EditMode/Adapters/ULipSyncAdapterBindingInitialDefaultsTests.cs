using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    public sealed class ULipSyncAdapterBindingInitialDefaultsTests
    {
        [Test]
        public void ApplyInitialDefaults_OnEmpty_GeneratesFiveExpressionEntries()
        {
            var binding = new ULipSyncAdapterBinding();

            binding.ApplyInitialDefaults();

            List<PhonemeEntryBase> entries = GetPhonemeEntries(binding);
            Assert.That(entries, Has.Count.EqualTo(5));

            string[] expectedPhonemeIds = { "A", "I", "U", "E", "O" };
            for (int i = 0; i < expectedPhonemeIds.Length; i++)
            {
                Assert.That(entries[i], Is.InstanceOf<ExpressionPhonemeEntry>());
                Assert.That(entries[i].PhonemeId, Is.EqualTo(expectedPhonemeIds[i]));
                Assert.That(entries[i].MaxWeight, Is.EqualTo(100f));
                Assert.That(((ExpressionPhonemeEntry)entries[i]).ExpressionId, Is.Empty);
            }
        }

        [Test]
        public void ApplyInitialDefaults_OnNonEmpty_DoesNothing()
        {
            var existingEntry = new BlendShapePhonemeEntry
            {
                PhonemeId = "A",
                MaxWeight = 80f,
                BlendShapeName = "Mouth_A",
            };
            var binding = new ULipSyncAdapterBinding();
            binding.Configure(default, null, new[] { existingEntry });

            binding.ApplyInitialDefaults();

            List<PhonemeEntryBase> entries = GetPhonemeEntries(binding);
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0], Is.SameAs(existingEntry));
        }

        private static List<PhonemeEntryBase> GetPhonemeEntries(ULipSyncAdapterBinding binding)
        {
            FieldInfo field = typeof(ULipSyncAdapterBinding).GetField(
                "_phonemeEntries",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            return (List<PhonemeEntryBase>)field.GetValue(binding);
        }
    }
}
