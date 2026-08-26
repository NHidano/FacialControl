using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.Common;

namespace Hidano.FacialControl.Editor.Inspector
{
    /// <summary>
    /// FacialController のカスタム Inspector。
    /// UI Toolkit で実装し、統合 SO 参照、SkinnedMeshRenderer リスト、
    /// プロファイル概要 (Schema / Layer 数 / Expression 数 / Snapshot 数) を表示する。
    /// OSC 設定はサブパッケージ <c>com.hidano.facialcontrol.osc</c> の専用 MonoBehaviour 側に移管されている。
    /// </summary>
    /// <remarks>
    /// 概要表示は AnimationClip 由来の Snapshot 数を表示する。
    /// </remarks>
    [CustomEditor(typeof(FacialController))]
    public class FacialControllerEditor : UnityEditor.Editor
    {
        // ---- セクション見出し ----
        public const string ProfileSectionLabel = "キャラクター SO";
        public const string RenderersSectionLabel = "SkinnedMeshRenderer";
        public const string ProfileInfoSectionLabel = "プロファイル情報";

        // ---- 概要ラベルのフォーマット (テストから参照可能にするため public) ----
        public const string SchemaVersionLabelFormat = "スキーマバージョン: {0}";
        public const string LayerCountLabelFormat = "レイヤー数: {0}";
        public const string ExpressionCountLabelFormat = "Expression 数: {0}";
        public const string SnapshotCountLabelFormat = "Snapshot 数: {0}";
        public const string EmptyValuePlaceholder = "---";

        // ---- 重複 FacialController 警告 ----
        public const string DuplicateWarningHelpBoxName = "facial-controller-duplicate-warning";
        public const string DuplicateWarningMessageFormat =
            "同じ階層に別の FacialController があります（'{0}'）。"
            + "両方が同じ SkinnedMeshRenderer を制御すると、入力を受けていない側が BlendShape を 0 で上書きし、"
            + "表情が動かなくなります。1 つのモデルに対して FacialController は 1 つだけにしてください。"
            + "\n実行時は階層上位の FacialController が優先され、もう一方は自動的に無効化されます。";

        // ---- ラベルの name 属性 (UI Toolkit Q<>) ----
        public const string SchemaVersionLabelName = "facial-controller-schema-version-label";
        public const string LayerCountLabelName = "facial-controller-layer-count-label";
        public const string ExpressionCountLabelName = "facial-controller-expression-count-label";
        public const string SnapshotCountLabelName = "facial-controller-snapshot-count-label";

        private Label _layerCountLabel;
        private Label _expressionCountLabel;
        private Label _snapshotCountLabel;
        private Label _schemaVersionLabel;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // ========================================
            // 重複 FacialController の警告（誤設定の早期発見）
            // ========================================
            AddDuplicateControllerWarning(root);

            // ========================================
            // 統合 SO セクション
            // ========================================
            var profileFoldout = new Foldout { text = ProfileSectionLabel, value = true };
            var profileField = new PropertyField(serializedObject.FindProperty("_characterSO"));
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
            // プロファイル情報セクション（読み取り専用）
            // ========================================
            var infoFoldout = new Foldout { text = ProfileInfoSectionLabel, value = true };

            _schemaVersionLabel = new Label(string.Format(SchemaVersionLabelFormat, EmptyValuePlaceholder))
            {
                name = SchemaVersionLabelName,
            };
            _schemaVersionLabel.AddToClassList(FacialControlStyles.InfoLabel);
            infoFoldout.Add(_schemaVersionLabel);

            _layerCountLabel = new Label(string.Format(LayerCountLabelFormat, EmptyValuePlaceholder))
            {
                name = LayerCountLabelName,
            };
            _layerCountLabel.AddToClassList(FacialControlStyles.InfoLabel);
            infoFoldout.Add(_layerCountLabel);

            _expressionCountLabel = new Label(string.Format(ExpressionCountLabelFormat, EmptyValuePlaceholder))
            {
                name = ExpressionCountLabelName,
            };
            _expressionCountLabel.AddToClassList(FacialControlStyles.InfoLabel);
            infoFoldout.Add(_expressionCountLabel);

            _snapshotCountLabel = new Label(string.Format(SnapshotCountLabelFormat, EmptyValuePlaceholder))
            {
                name = SnapshotCountLabelName,
            };
            _snapshotCountLabel.AddToClassList(FacialControlStyles.InfoLabel);
            infoFoldout.Add(_snapshotCountLabel);

            root.Add(infoFoldout);

            // 初回更新
            root.schedule.Execute(UpdateProfileInfo);

            return root;
        }

        /// <summary>
        /// 親子関係にある別の FacialController を検出し、見つかったら警告 HelpBox を差し込む。
        /// </summary>
        private void AddDuplicateControllerWarning(VisualElement root)
        {
            var controller = target as FacialController;
            var duplicate = FindHierarchyDuplicate(controller);
            if (duplicate == null)
                return;

            var helpBox = new HelpBox(
                string.Format(DuplicateWarningMessageFormat, GetHierarchyPath(duplicate.transform)),
                HelpBoxMessageType.Warning)
            {
                name = DuplicateWarningHelpBoxName,
            };
            root.Add(helpBox);
        }

        /// <summary>
        /// <paramref name="controller"/> と親子関係にある別の <see cref="FacialController"/> を返す。
        /// </summary>
        /// <remarks>
        /// 兄弟関係（複数キャラクターをそれぞれのモデルルートで制御する正当な構成）は対象外。
        /// 祖先・子孫にある場合のみ SkinnedMeshRenderer の自動検索範囲が重なるため、そこだけを見る。
        /// </remarks>
        /// <returns>見つかった重複。無ければ null。</returns>
        public static FacialController FindHierarchyDuplicate(FacialController controller)
        {
            if (controller == null)
                return null;

            var parents = controller.GetComponentsInParent<FacialController>(includeInactive: true);
            for (int i = 0; i < parents.Length; i++)
            {
                if (parents[i] != null && parents[i] != controller)
                    return parents[i];
            }

            var children = controller.GetComponentsInChildren<FacialController>(includeInactive: true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i] != controller)
                    return children[i];
            }

            return null;
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
                return "(missing)";

            var builder = new System.Text.StringBuilder(target.name);
            Transform current = target.parent;
            while (current != null)
            {
                builder.Insert(0, '/');
                builder.Insert(0, current.name);
                current = current.parent;
            }

            return builder.ToString();
        }

        /// <summary>
        /// CharacterSO から概要情報を読み取って表示を更新する。
        /// </summary>
        private void UpdateProfileInfo()
        {
            var controller = target as FacialController;
            if (controller == null)
                return;

            var so = controller.CharacterSO;
            if (so != null)
            {
                string version = !string.IsNullOrEmpty(so.SchemaVersion)
                    ? so.SchemaVersion
                    : EmptyValuePlaceholder;
                int layers = so.Layers != null ? so.Layers.Count : 0;
                int expressions = so.Expressions != null ? so.Expressions.Count : 0;
                int snapshots = CountSnapshots(so);

                if (_schemaVersionLabel != null)
                    _schemaVersionLabel.text = string.Format(SchemaVersionLabelFormat, version);
                if (_layerCountLabel != null)
                    _layerCountLabel.text = string.Format(LayerCountLabelFormat, layers);
                if (_expressionCountLabel != null)
                    _expressionCountLabel.text = string.Format(ExpressionCountLabelFormat, expressions);
                if (_snapshotCountLabel != null)
                    _snapshotCountLabel.text = string.Format(SnapshotCountLabelFormat, snapshots);
            }
            else
            {
                if (_schemaVersionLabel != null)
                    _schemaVersionLabel.text = string.Format(SchemaVersionLabelFormat, EmptyValuePlaceholder);
                if (_layerCountLabel != null)
                    _layerCountLabel.text = string.Format(LayerCountLabelFormat, EmptyValuePlaceholder);
                if (_expressionCountLabel != null)
                    _expressionCountLabel.text = string.Format(ExpressionCountLabelFormat, EmptyValuePlaceholder);
                if (_snapshotCountLabel != null)
                    _snapshotCountLabel.text = string.Format(SnapshotCountLabelFormat, EmptyValuePlaceholder);
            }
        }

        private static int CountSnapshots(FacialCharacterProfileSO so)
        {
            var expressions = so.Expressions;
            if (expressions == null)
                return 0;
            int count = 0;
            for (int i = 0; i < expressions.Count; i++)
            {
                if (expressions[i] != null && expressions[i].cachedSnapshot != null)
                    count++;
            }
            return count;
        }
    }
}
