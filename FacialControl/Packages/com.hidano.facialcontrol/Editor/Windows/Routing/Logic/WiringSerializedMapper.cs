using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Windows.Routing.Logic
{
    public interface IWiringSerializedMapper
    {
        void AddDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight);

        void RemoveDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId);

        void SetWeight(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight);

        void SetLayerProperties(
            SerializedObject serializedObject,
            int layerIndex,
            string layerName,
            int priority,
            ExclusionMode exclusionMode,
            IReadOnlyList<string> overrideMask);

        void BeginContinuousWeight(SerializedObject serializedObject, int layerIndex, string canonicalId);

        void SetWeightContinuous(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight);

        void EndContinuousWeight();
    }

    public sealed class WiringSerializedMapper : IWiringSerializedMapper
    {
        private const string LayersPropertyName = "_layers";
        private const string InputSourcesPropertyName = "inputSources";
        private const string IdPropertyName = "id";
        private const string WeightPropertyName = "weight";
        private const string OptionsJsonPropertyName = "optionsJson";
        private const string LayerNamePropertyName = "name";
        private const string PriorityPropertyName = "priority";
        private const string ExclusionModePropertyName = "exclusionMode";
        private const string LayerOverrideMaskPropertyName = "layerOverrideMask";
        private const string WeightUndoGroupName = "Routing Weight Drag";
        private const string DeclarationUndoName = "Edit Routing Declaration";
        private const string LayerUndoName = "Edit Routing Layer";

        private int _continuousUndoGroup = -1;
        private UnityEngine.Object _continuousTargetObject;
        private string _continuousCanonicalId = string.Empty;
        private int _continuousLayerIndex = -1;

        public void AddDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
        {
            ValidateDeclarationInputs(serializedObject, layerIndex, canonicalId);

            ApplyWithUndo(serializedObject, DeclarationUndoName, so =>
            {
                SerializedProperty declarationsProperty = GetInputSourcesProperty(so, layerIndex);
                RemoveDuplicateDeclarations(declarationsProperty, canonicalId, out string preservedOptionsJson);

                int insertIndex = declarationsProperty.arraySize;
                declarationsProperty.InsertArrayElementAtIndex(insertIndex);
                SerializedProperty declarationProperty = declarationsProperty.GetArrayElementAtIndex(insertIndex);
                WriteDeclaration(declarationProperty, canonicalId, ClampWeight(weight), preservedOptionsJson);
            });
        }

        public void RemoveDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId)
        {
            ValidateDeclarationInputs(serializedObject, layerIndex, canonicalId);

            ApplyWithUndo(serializedObject, DeclarationUndoName, so =>
            {
                SerializedProperty declarationsProperty = GetInputSourcesProperty(so, layerIndex);
                RemoveDuplicateDeclarations(declarationsProperty, canonicalId, out _);
            });
        }

        public void SetWeight(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
        {
            ValidateDeclarationInputs(serializedObject, layerIndex, canonicalId);

            ApplyWithUndo(serializedObject, DeclarationUndoName, so =>
            {
                SerializedProperty declarationProperty = FindDeclarationProperty(so, layerIndex, canonicalId);
                if (declarationProperty == null)
                {
                    throw new InvalidOperationException($"Declaration '{canonicalId}' was not found on layer {layerIndex}.");
                }

                SerializedProperty weightProperty = declarationProperty.FindPropertyRelative(WeightPropertyName);
                if (weightProperty != null)
                {
                    weightProperty.floatValue = ClampWeight(weight);
                }
            });
        }

        public void SetLayerProperties(
            SerializedObject serializedObject,
            int layerIndex,
            string layerName,
            int priority,
            ExclusionMode exclusionMode,
            IReadOnlyList<string> overrideMask)
        {
            if (serializedObject == null)
            {
                throw new ArgumentNullException(nameof(serializedObject));
            }

            ApplyWithUndo(serializedObject, LayerUndoName, so =>
            {
                SerializedProperty layerProperty = GetLayerProperty(so, layerIndex);

                SerializedProperty nameProperty = layerProperty.FindPropertyRelative(LayerNamePropertyName);
                if (nameProperty != null)
                {
                    nameProperty.stringValue = layerName ?? string.Empty;
                }

                SerializedProperty priorityProperty = layerProperty.FindPropertyRelative(PriorityPropertyName);
                if (priorityProperty != null)
                {
                    priorityProperty.intValue = Mathf.Max(0, priority);
                }

                SerializedProperty exclusionModeProperty = layerProperty.FindPropertyRelative(ExclusionModePropertyName);
                if (exclusionModeProperty != null)
                {
                    exclusionModeProperty.enumValueIndex = (int)exclusionMode;
                }

                SerializedProperty overrideMaskProperty = layerProperty.FindPropertyRelative(LayerOverrideMaskPropertyName);
                if (overrideMaskProperty != null && overrideMaskProperty.isArray)
                {
                    overrideMaskProperty.ClearArray();
                    int count = overrideMask?.Count ?? 0;
                    for (int i = 0; i < count; i++)
                    {
                        overrideMaskProperty.InsertArrayElementAtIndex(i);
                        SerializedProperty itemProperty = overrideMaskProperty.GetArrayElementAtIndex(i);
                        if (itemProperty != null)
                        {
                            itemProperty.stringValue = overrideMask[i] ?? string.Empty;
                        }
                    }
                }
            });
        }

        public void BeginContinuousWeight(SerializedObject serializedObject, int layerIndex, string canonicalId)
        {
            ValidateDeclarationInputs(serializedObject, layerIndex, canonicalId);

            EndContinuousWeight();

            _continuousTargetObject = serializedObject.targetObject;
            _continuousLayerIndex = layerIndex;
            _continuousCanonicalId = canonicalId;
            _continuousUndoGroup = BeginUndoGroup(WeightUndoGroupName);
            Undo.RecordObject(_continuousTargetObject, WeightUndoGroupName);
        }

        public void SetWeightContinuous(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
        {
            ValidateDeclarationInputs(serializedObject, layerIndex, canonicalId);
            if (_continuousTargetObject == null
                || !ReferenceEquals(_continuousTargetObject, serializedObject.targetObject)
                || _continuousLayerIndex != layerIndex
                || !string.Equals(_continuousCanonicalId, canonicalId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Continuous weight edit has not been started for the specified declaration.");
            }

            serializedObject.Update();
            SerializedProperty declarationProperty = FindDeclarationProperty(serializedObject, layerIndex, canonicalId);
            if (declarationProperty == null)
            {
                throw new InvalidOperationException($"Declaration '{canonicalId}' was not found on layer {layerIndex}.");
            }

            SerializedProperty weightProperty = declarationProperty.FindPropertyRelative(WeightPropertyName);
            if (weightProperty != null)
            {
                weightProperty.floatValue = ClampWeight(weight);
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        public void EndContinuousWeight()
        {
            if (_continuousUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(_continuousUndoGroup);
            }

            _continuousUndoGroup = -1;
            _continuousTargetObject = null;
            _continuousCanonicalId = string.Empty;
            _continuousLayerIndex = -1;
        }

        private static void ValidateDeclarationInputs(SerializedObject serializedObject, int layerIndex, string canonicalId)
        {
            if (serializedObject == null)
            {
                throw new ArgumentNullException(nameof(serializedObject));
            }

            if (!InputSourceId.TryParse(canonicalId, out _))
            {
                throw new ArgumentException($"Invalid canonical id '{canonicalId ?? "<null>"}'.", nameof(canonicalId));
            }

            GetLayerProperty(serializedObject, layerIndex);
        }

        private static void ApplyWithUndo(SerializedObject serializedObject, string undoName, Action<SerializedObject> editAction)
        {
            if (serializedObject == null)
            {
                throw new ArgumentNullException(nameof(serializedObject));
            }

            if (editAction == null)
            {
                throw new ArgumentNullException(nameof(editAction));
            }

            serializedObject.Update();
            Undo.RecordObject(serializedObject.targetObject, undoName);
            editAction(serializedObject);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        private static SerializedProperty GetLayerProperty(SerializedObject serializedObject, int layerIndex)
        {
            SerializedProperty layersProperty = serializedObject.FindProperty(LayersPropertyName);
            if (layersProperty == null || !layersProperty.isArray)
            {
                throw new InvalidOperationException($"SerializedObject does not contain array property '{LayersPropertyName}'.");
            }

            if (layerIndex < 0 || layerIndex >= layersProperty.arraySize)
            {
                throw new ArgumentOutOfRangeException(nameof(layerIndex), layerIndex, "Layer index is out of range.");
            }

            SerializedProperty layerProperty = layersProperty.GetArrayElementAtIndex(layerIndex);
            if (layerProperty == null)
            {
                throw new InvalidOperationException($"Layer at index {layerIndex} could not be resolved.");
            }

            return layerProperty;
        }

        private static SerializedProperty GetInputSourcesProperty(SerializedObject serializedObject, int layerIndex)
        {
            SerializedProperty layerProperty = GetLayerProperty(serializedObject, layerIndex);
            SerializedProperty inputSourcesProperty = layerProperty.FindPropertyRelative(InputSourcesPropertyName);
            if (inputSourcesProperty == null || !inputSourcesProperty.isArray)
            {
                throw new InvalidOperationException($"Layer at index {layerIndex} does not contain array property '{InputSourcesPropertyName}'.");
            }

            return inputSourcesProperty;
        }

        private static SerializedProperty FindDeclarationProperty(SerializedObject serializedObject, int layerIndex, string canonicalId)
        {
            SerializedProperty declarationsProperty = GetInputSourcesProperty(serializedObject, layerIndex);
            for (int i = 0; i < declarationsProperty.arraySize; i++)
            {
                SerializedProperty declarationProperty = declarationsProperty.GetArrayElementAtIndex(i);
                SerializedProperty idProperty = declarationProperty?.FindPropertyRelative(IdPropertyName);
                if (idProperty != null && string.Equals(idProperty.stringValue, canonicalId, StringComparison.Ordinal))
                {
                    return declarationProperty;
                }
            }

            return null;
        }

        private static void RemoveDuplicateDeclarations(
            SerializedProperty declarationsProperty,
            string canonicalId,
            out string preservedOptionsJson)
        {
            preservedOptionsJson = string.Empty;

            for (int i = declarationsProperty.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty declarationProperty = declarationsProperty.GetArrayElementAtIndex(i);
                SerializedProperty idProperty = declarationProperty?.FindPropertyRelative(IdPropertyName);
                if (idProperty == null || !string.Equals(idProperty.stringValue, canonicalId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(preservedOptionsJson))
                {
                    SerializedProperty optionsProperty = declarationProperty.FindPropertyRelative(OptionsJsonPropertyName);
                    preservedOptionsJson = optionsProperty?.stringValue ?? string.Empty;
                }

                declarationsProperty.DeleteArrayElementAtIndex(i);
            }
        }

        private static void WriteDeclaration(
            SerializedProperty declarationProperty,
            string canonicalId,
            float weight,
            string optionsJson)
        {
            if (declarationProperty == null)
            {
                throw new ArgumentNullException(nameof(declarationProperty));
            }

            SerializedProperty idProperty = declarationProperty.FindPropertyRelative(IdPropertyName);
            if (idProperty != null)
            {
                idProperty.stringValue = canonicalId;
            }

            SerializedProperty weightProperty = declarationProperty.FindPropertyRelative(WeightPropertyName);
            if (weightProperty != null)
            {
                weightProperty.floatValue = weight;
            }

            SerializedProperty optionsProperty = declarationProperty.FindPropertyRelative(OptionsJsonPropertyName);
            if (optionsProperty != null)
            {
                optionsProperty.stringValue = optionsJson ?? string.Empty;
            }
        }

        private static int BeginUndoGroup(string groupName)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(groupName);
            return Undo.GetCurrentGroup();
        }

        private static float ClampWeight(float weight)
        {
            return Mathf.Clamp01(weight);
        }
    }
}
