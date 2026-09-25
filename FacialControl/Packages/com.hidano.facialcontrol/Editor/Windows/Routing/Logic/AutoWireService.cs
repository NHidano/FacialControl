using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using UnityEditor;

namespace Hidano.FacialControl.Editor.Windows.Routing.Logic
{
    public interface IAutoWireService
    {
        void AutoWire(SerializedObject serializedObject, AdapterBindingBase binding, IReadOnlyList<string> allLayerNames);
    }

    public sealed class AutoWireService : IAutoWireService
    {
        private const string LayersPropertyName = "_layers";
        private const string LayerNamePropertyName = "name";
        private const string InputSourcesPropertyName = "inputSources";
        private const string DeclarationIdPropertyName = "id";
        private const string OverlayLayerName = "overlay";
        private const string UndoGroupName = "Auto Wire Routing";

        private readonly ISourcePortEnumerator _sourcePortEnumerator;
        private readonly IPhonemeSlotInitializer _phonemeSlotInitializer;
        private readonly IWiringSerializedMapper _wiringSerializedMapper;

        public AutoWireService()
            : this(new SourcePortEnumerator(), new PhonemeSlotInitializer(), new WiringSerializedMapper())
        {
        }

        public AutoWireService(
            ISourcePortEnumerator sourcePortEnumerator,
            IPhonemeSlotInitializer phonemeSlotInitializer,
            IWiringSerializedMapper wiringSerializedMapper)
        {
            _sourcePortEnumerator = sourcePortEnumerator ?? throw new ArgumentNullException(nameof(sourcePortEnumerator));
            _phonemeSlotInitializer = phonemeSlotInitializer ?? throw new ArgumentNullException(nameof(phonemeSlotInitializer));
            _wiringSerializedMapper = wiringSerializedMapper ?? throw new ArgumentNullException(nameof(wiringSerializedMapper));
        }

        public void AutoWire(
            SerializedObject serializedObject,
            AdapterBindingBase binding,
            IReadOnlyList<string> allLayerNames)
        {
            if (serializedObject == null)
            {
                throw new ArgumentNullException(nameof(serializedObject));
            }

            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            if (allLayerNames == null)
            {
                throw new ArgumentNullException(nameof(allLayerNames));
            }

            if (binding is not IAdapterBindingDefaultLayerInputs)
            {
                return;
            }

            var additions = CollectMissingDeclarations(serializedObject, binding, allLayerNames, out bool needsReservedSlots);
            if (additions.Count == 0 && !needsReservedSlots)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoGroupName);
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                if (needsReservedSlots)
                {
                    _phonemeSlotInitializer.EnsureReservedSlots(serializedObject);
                }

                for (int i = 0; i < additions.Count; i++)
                {
                    PendingDeclaration addition = additions[i];
                    _wiringSerializedMapper.AddDeclaration(serializedObject, addition.LayerIndex, addition.CanonicalId, 1f);
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private List<PendingDeclaration> CollectMissingDeclarations(
            SerializedObject serializedObject,
            AdapterBindingBase binding,
            IReadOnlyList<string> allLayerNames,
            out bool needsReservedSlots)
        {
            serializedObject.Update();
            SerializedProperty layersProperty = serializedObject.FindProperty(LayersPropertyName);
            if (layersProperty == null || !layersProperty.isArray)
            {
                throw new InvalidOperationException($"SerializedObject does not contain array property '{LayersPropertyName}'.");
            }

            var additions = new List<PendingDeclaration>();
            needsReservedSlots = false;
            var seenLayerNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < allLayerNames.Count; i++)
            {
                string layerName = allLayerNames[i];
                if (string.IsNullOrEmpty(layerName) || !seenLayerNames.Add(layerName))
                {
                    continue;
                }

                IReadOnlyList<SourcePort> ports = _sourcePortEnumerator.EnumerateForLayer(binding, layerName);
                if (ports.Count == 0)
                {
                    continue;
                }

                if (string.Equals(layerName, OverlayLayerName, StringComparison.Ordinal))
                {
                    needsReservedSlots = true;
                }

                for (int layerIndex = 0; layerIndex < layersProperty.arraySize; layerIndex++)
                {
                    SerializedProperty layerProperty = layersProperty.GetArrayElementAtIndex(layerIndex);
                    SerializedProperty nameProperty = layerProperty?.FindPropertyRelative(LayerNamePropertyName);
                    if (nameProperty == null
                        || !string.Equals(nameProperty.stringValue, layerName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ISet<string> existingIds = ReadDeclaredIds(layerProperty);
                    for (int portIndex = 0; portIndex < ports.Count; portIndex++)
                    {
                        string canonicalId = ports[portIndex].CanonicalId;
                        if (existingIds.Add(canonicalId))
                        {
                            additions.Add(new PendingDeclaration(layerIndex, canonicalId));
                        }
                    }
                }
            }

            return additions;
        }

        private static ISet<string> ReadDeclaredIds(SerializedProperty layerProperty)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            SerializedProperty inputSourcesProperty = layerProperty?.FindPropertyRelative(InputSourcesPropertyName);
            if (inputSourcesProperty == null || !inputSourcesProperty.isArray)
            {
                return result;
            }

            for (int i = 0; i < inputSourcesProperty.arraySize; i++)
            {
                SerializedProperty declarationProperty = inputSourcesProperty.GetArrayElementAtIndex(i);
                SerializedProperty idProperty = declarationProperty?.FindPropertyRelative(DeclarationIdPropertyName);
                string id = idProperty?.stringValue;
                if (!string.IsNullOrEmpty(id))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        private readonly struct PendingDeclaration
        {
            public PendingDeclaration(int layerIndex, string canonicalId)
            {
                LayerIndex = layerIndex;
                CanonicalId = canonicalId;
            }

            public int LayerIndex { get; }

            public string CanonicalId { get; }
        }
    }
}
