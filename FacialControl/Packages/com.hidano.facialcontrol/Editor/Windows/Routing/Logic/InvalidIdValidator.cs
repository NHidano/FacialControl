using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;

namespace Hidano.FacialControl.Editor.Windows.Routing.Logic
{
    public readonly struct InvalidDeclarationRef : IEquatable<InvalidDeclarationRef>
    {
        public InvalidDeclarationRef(int layerIndex, int declarationIndex, string id)
        {
            LayerIndex = layerIndex;
            DeclarationIndex = declarationIndex;
            Id = id ?? string.Empty;
        }

        public int LayerIndex { get; }

        public int DeclarationIndex { get; }

        public string Id { get; }

        public bool Equals(InvalidDeclarationRef other)
        {
            return LayerIndex == other.LayerIndex
                && DeclarationIndex == other.DeclarationIndex
                && string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is InvalidDeclarationRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = LayerIndex;
                hash = (hash * 397) ^ DeclarationIndex;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Id);
                return hash;
            }
        }
    }

    public interface IInvalidIdValidator
    {
        IReadOnlyList<InvalidDeclarationRef> Validate(
            FacialCharacterProfileSO profile,
            ISet<string> validCanonicalIds);
    }

    public sealed class InvalidIdValidator : IInvalidIdValidator
    {
        public IReadOnlyList<InvalidDeclarationRef> Validate(
            FacialCharacterProfileSO profile,
            ISet<string> validCanonicalIds)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (validCanonicalIds == null)
            {
                throw new ArgumentNullException(nameof(validCanonicalIds));
            }

            var invalidDeclarations = new List<InvalidDeclarationRef>();
            IList<LayerDefinitionSerializable> layers = profile.Layers;
            if (layers == null)
            {
                return invalidDeclarations;
            }

            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                LayerDefinitionSerializable layer = layers[layerIndex];
                IList<InputSourceDeclarationSerializable> declarations = layer?.inputSources;
                if (declarations == null)
                {
                    continue;
                }

                for (int declarationIndex = 0; declarationIndex < declarations.Count; declarationIndex++)
                {
                    string id = declarations[declarationIndex]?.id ?? string.Empty;
                    if (validCanonicalIds.Contains(id))
                    {
                        continue;
                    }

                    invalidDeclarations.Add(new InvalidDeclarationRef(layerIndex, declarationIndex, id));
                }
            }

            return invalidDeclarations;
        }
    }
}
