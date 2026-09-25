using UnityEngine;

namespace Hidano.FacialControl.Adapters.RuntimeSettings
{
    public abstract class AdapterRuntimeSettingsBase : UnityEngine.ScriptableObject
    {
        [SerializeField]
        protected string _label = string.Empty;

        [SerializeField]
        protected int _schemaVersion = 1;

        public string Label => _label;

        public int SchemaVersion => _schemaVersion;

        protected virtual void OnEnable()
        {
            if (_schemaVersion <= 0)
            {
                _schemaVersion = 1;
            }
        }

        public virtual string ToJson()
        {
            Debug.LogWarning(
                $"[AdapterRuntimeSettingsBase] {GetType().Name} は ToJson() を override していません。空文字を返します。");
            return string.Empty;
        }

        public virtual void FromJson(string json)
        {
            Debug.LogWarning(
                $"[AdapterRuntimeSettingsBase] {GetType().Name} は FromJson(string) を override していません。値は変更されません。");
        }
    }
}
