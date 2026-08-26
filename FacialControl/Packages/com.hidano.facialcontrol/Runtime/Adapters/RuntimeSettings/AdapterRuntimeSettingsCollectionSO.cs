using System.Collections.Generic;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.RuntimeSettings
{
    [CreateAssetMenu(
        menuName = "FacialControl/Adapter Runtime Settings Collection",
        fileName = "AdapterRuntimeSettingsCollection")]
    public sealed class AdapterRuntimeSettingsCollectionSO : UnityEngine.ScriptableObject
    {
        [SerializeField]
        private List<AdapterRuntimeSettingsBase> _items = new List<AdapterRuntimeSettingsBase>();

        public IReadOnlyList<AdapterRuntimeSettingsBase> Items => _items;

        public T TryFind<T>() where T : AdapterRuntimeSettingsBase
        {
            if (_items == null)
            {
                return null;
            }

            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i] is T typed)
                {
                    return typed;
                }
            }

            return null;
        }

        public T TryFind<T>(string label) where T : AdapterRuntimeSettingsBase
        {
            if (_items == null)
            {
                return null;
            }

            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i] is T typed && string.Equals(typed.Label, label, System.StringComparison.Ordinal))
                {
                    return typed;
                }
            }

            return null;
        }

        public int IndexOf(AdapterRuntimeSettingsBase item)
        {
            if (_items == null || item == null)
            {
                return -1;
            }

            for (var i = 0; i < _items.Count; i++)
            {
                if (ReferenceEquals(_items[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        private void OnEnable()
        {
            WarnOnNullItems();

            // Migration seam (要件 5.4-5.5):
            // 本 spec 時点では実装本体を持たない。将来 spec で対応レベル c (型削除/リネーム) を実装する際、
            // ここで `_schemaVersion` 別の互換変換を実行する。実装例:
            //   foreach (var item in _items)
            //   {
            //       if (item != null) { /* item ごとのマイグレーション */ }
            //   }
            MigrateOnLoad();
        }

        private void WarnOnNullItems()
        {
            if (_items == null)
            {
                return;
            }

            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null)
                {
                    Debug.LogWarning(
                        $"[AdapterRuntimeSettingsCollectionSO] '{name}' の _items[{i}] が null です。sub-asset 参照が欠落している可能性があります (要件 3.1-3.4)。");
                }
            }
        }

        // 本 spec 時点では未実装の拡張点。対応レベル c (型削除/リネーム時のマイグレーション) で
        // 本実装される予定 (要件 5.4-5.5)。Collection は sealed のためサブクラスからの override 経路はなく、
        // 将来同クラス内に直接マイグレーション処理を追加する。
        private void MigrateOnLoad()
        {
        }
    }
}
