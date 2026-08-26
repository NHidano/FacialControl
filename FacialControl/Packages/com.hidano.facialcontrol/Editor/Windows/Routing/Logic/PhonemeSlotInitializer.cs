using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using UnityEditor;

namespace Hidano.FacialControl.Editor.Windows.Routing.Logic
{
    public interface IPhonemeSlotInitializer
    {
        IReadOnlyList<string> GetMissingReservedSlots(IReadOnlyList<string> declaredSlots);

        bool EnsureReservedSlots(SerializedObject serializedObject);
    }

    public sealed class PhonemeSlotInitializer : IPhonemeSlotInitializer
    {
        public IReadOnlyList<string> GetMissingReservedSlots(IReadOnlyList<string> declaredSlots)
        {
            var missingSlots = new List<string>();
            var declared = new HashSet<string>(StringComparer.Ordinal);

            if (declaredSlots != null)
            {
                for (int i = 0; i < declaredSlots.Count; i++)
                {
                    string slot = declaredSlots[i];
                    if (!string.IsNullOrEmpty(slot))
                    {
                        declared.Add(slot);
                    }
                }
            }

            foreach (string reservedSlot in PhonemeOverlaySlots.ReservedNames)
            {
                if (!declared.Contains(reservedSlot))
                {
                    missingSlots.Add(reservedSlot);
                }
            }

            return missingSlots;
        }

        public bool EnsureReservedSlots(SerializedObject serializedObject)
        {
            if (serializedObject == null)
            {
                throw new ArgumentNullException(nameof(serializedObject));
            }

            SerializedProperty slotsProperty = serializedObject.FindProperty("_slots");
            if (slotsProperty == null)
            {
                return false;
            }

            serializedObject.Update();

            IReadOnlyList<string> missingSlots = GetMissingReservedSlots(ReadSlots(slotsProperty));
            if (missingSlots.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < missingSlots.Count; i++)
            {
                int index = slotsProperty.arraySize;
                slotsProperty.InsertArrayElementAtIndex(index);
                SerializedProperty slotProperty = slotsProperty.GetArrayElementAtIndex(index);
                if (slotProperty != null)
                {
                    slotProperty.stringValue = missingSlots[i];
                }
            }

            serializedObject.ApplyModifiedProperties();
            return true;
        }

        private static IReadOnlyList<string> ReadSlots(SerializedProperty slotsProperty)
        {
            var slots = new List<string>(slotsProperty.arraySize);
            for (int i = 0; i < slotsProperty.arraySize; i++)
            {
                SerializedProperty slotProperty = slotsProperty.GetArrayElementAtIndex(i);
                slots.Add(slotProperty?.stringValue ?? string.Empty);
            }

            return slots;
        }
    }
}
