using System;
using System.Reflection;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    public sealed class LegacyLipSyncInputSourceRemovalGuardTests
    {
        [Test]
        public void Codebase_DoesNotContainLegacyLipSyncInputSourceSymbol()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in GetLoadableTypes(assembly))
                {
                    Assert.That(
                        type.Name,
                        Is.Not.EqualTo("LipSyncInputSource"),
                        "Legacy LipSyncInputSource type remains in assembly: " + assembly.GetName().Name);
                }
            }
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                Type[] sourceTypes = ex.Types;
                int count = 0;
                for (int i = 0; i < sourceTypes.Length; i++)
                {
                    if (sourceTypes[i] != null)
                    {
                        count++;
                    }
                }

                var loadableTypes = new Type[count];
                int index = 0;
                for (int i = 0; i < sourceTypes.Length; i++)
                {
                    Type type = sourceTypes[i];
                    if (type != null)
                    {
                        loadableTypes[index] = type;
                        index++;
                    }
                }

                return loadableTypes;
            }
        }
    }
}
