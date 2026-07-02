using UnityEditor;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Editor.Common
{
    /// <summary>
    /// <see cref="ListView.showFoldoutHeader"/> 付き ListView のヘッダー Foldout 開閉状態を
    /// SessionState に保存・復元するユーティリティ。domain reload や Inspector 再構築を
    /// またいで直前の展開状態を再現する（Editor 再起動でリセット）。
    /// </summary>
    public static class ListViewFoldoutStatePersistence
    {
        private const string KeyPrefix = "Hidano.FacialControl.ListViewFoldout";

        /// <summary>
        /// 対象 array の SerializedProperty から SessionState 保存キーを組み立てる。
        /// targetObject の InstanceID + propertyPath で一意化する。
        /// </summary>
        public static string GetSessionStateKey(SerializedProperty listProperty)
        {
            var target = listProperty != null ? listProperty.serializedObject.targetObject : null;
            int id = target != null ? target.GetInstanceID() : 0;
            string path = listProperty != null ? listProperty.propertyPath : string.Empty;
            return $"{KeyPrefix}.{id}.{path}";
        }

        /// <summary>
        /// ListView のヘッダー Foldout に SessionState 永続化を仕込む。
        /// 保存済みの開閉状態があれば即時復元し、以後は開閉トグル時（ChangeEvent）と
        /// panel からの detach 時に現在値を保存する。
        /// </summary>
        public static void Register(
            ListView listView, SerializedProperty listProperty, bool defaultOpen = true)
        {
            if (listView == null)
            {
                return;
            }

            var foldout = listView.Q<Foldout>(className: BaseListView.foldoutHeaderUssClassName);
            if (foldout == null)
            {
                return;
            }

            string key = GetSessionStateKey(listProperty);
            foldout.value = SessionState.GetBool(key, defaultOpen);
            foldout.RegisterValueChangedCallback(evt =>
            {
                // 行内の Toggle / 子 Foldout の ChangeEvent<bool> も bubble してくるため、
                // ヘッダー Foldout 自身の開閉のみを保存する。
                if (evt.target == foldout)
                {
                    SessionState.SetBool(key, evt.newValue);
                }
            });
            // ChangeEvent は panel 未接続時にディスパッチされないため、detach 時にも
            // 現在値を保存して破棄直前の変更を取りこぼさない。
            listView.RegisterCallback<DetachFromPanelEvent>(_ => SaveState(listView, listProperty));
        }

        /// <summary>
        /// ヘッダー Foldout の現在の開閉状態を SessionState へ保存する。
        /// </summary>
        public static void SaveState(ListView listView, SerializedProperty listProperty)
        {
            var foldout = listView?.Q<Foldout>(className: BaseListView.foldoutHeaderUssClassName);
            if (foldout == null)
            {
                return;
            }

            SessionState.SetBool(GetSessionStateKey(listProperty), foldout.value);
        }
    }
}
