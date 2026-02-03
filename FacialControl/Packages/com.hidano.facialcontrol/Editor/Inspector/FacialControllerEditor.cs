using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;

namespace Hidano.FacialControl.Editor.Inspector
{
    /// <summary>
    /// FacialController のカスタム Inspector。
    /// UI Toolkit で実装し、プロファイル参照、SkinnedMeshRenderer リスト、
    /// OSC ポート設定、プロファイル概要を表示する。
    /// </summary>
    [CustomEditor(typeof(FacialController))]
    public class FacialControllerEditor : UnityEditor.Editor
    {
        private const string ProfileSectionLabel = "プロファイル";
        private const string RenderersSectionLabel = "SkinnedMeshRenderer";
        private const string OscSectionLabel = "OSC 設定";
        private const string ProfileInfoSectionLabel = "プロファイル情報";

        private Label _layerCountLabel;
        private Label _expressionCountLabel;
        private Label _schemaVersionLabel;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            // ========================================
            // プロファイルセクション
            // ========================================
            var profileFoldout = new Foldout { text = ProfileSectionLabel, value = true };
            var profileField = new PropertyField(serializedObject.FindProperty("_profileSO"));
            profileField.RegisterValueChangeCallback(_ => UpdateProfileInfo());
            profileFoldout.Add(profileField);
            root.Add(profileFoldout);

            // ========================================
            // SkinnedMeshRenderer セクション
            // ========================================
            var renderersFoldout = new Foldout { text = RenderersSectionLabel, value = true };
            var renderersField = new PropertyField(serializedObject.FindProperty("_skinnedMeshRenderers"));
            renderersField.tooltip = "空の場合は子オブジェクトから自動検索されます";
            renderersFoldout.Add(renderersField);
            root.Add(renderersFoldout);

            // ========================================
            // OSC 設定セクション
            // ========================================
            var oscFoldout = new Foldout { text = OscSectionLabel, value = true };

            var sendPortField = new PropertyField(serializedObject.FindProperty("_oscSendPort"), "送信ポート");
            oscFoldout.Add(sendPortField);

            var receivePortField = new PropertyField(serializedObject.FindProperty("_oscReceivePort"), "受信ポート");
            oscFoldout.Add(receivePortField);

            root.Add(oscFoldout);

            // ========================================
            // プロファイル情報セクション（読み取り専用）
            // ========================================
            var infoFoldout = new Foldout { text = ProfileInfoSectionLabel, value = true };

            _schemaVersionLabel = new Label("スキーマバージョン: ---");
            _schemaVersionLabel.style.marginLeft = 4;
            infoFoldout.Add(_schemaVersionLabel);

            _layerCountLabel = new Label("レイヤー数: ---");
            _layerCountLabel.style.marginLeft = 4;
            infoFoldout.Add(_layerCountLabel);

            _expressionCountLabel = new Label("Expression 数: ---");
            _expressionCountLabel.style.marginLeft = 4;
            infoFoldout.Add(_expressionCountLabel);

            root.Add(infoFoldout);

            // 初回更新
            root.schedule.Execute(UpdateProfileInfo);

            return root;
        }

        /// <summary>
        /// ProfileSO から概要情報を読み取って表示を更新する
        /// </summary>
        private void UpdateProfileInfo()
        {
            var controller = target as FacialController;
            if (controller == null)
                return;

            var profileSO = controller.ProfileSO;
            if (profileSO != null)
            {
                string version = !string.IsNullOrEmpty(profileSO.SchemaVersion)
                    ? profileSO.SchemaVersion
                    : "---";
                int layers = profileSO.LayerCount;
                int expressions = profileSO.ExpressionCount;

                if (_schemaVersionLabel != null)
                    _schemaVersionLabel.text = $"スキーマバージョン: {version}";
                if (_layerCountLabel != null)
                    _layerCountLabel.text = $"レイヤー数: {layers}";
                if (_expressionCountLabel != null)
                    _expressionCountLabel.text = $"Expression 数: {expressions}";
            }
            else
            {
                if (_schemaVersionLabel != null)
                    _schemaVersionLabel.text = "スキーマバージョン: ---";
                if (_layerCountLabel != null)
                    _layerCountLabel.text = "レイヤー数: ---";
                if (_expressionCountLabel != null)
                    _expressionCountLabel.text = "Expression 数: ---";
            }
        }
    }
}
