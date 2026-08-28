using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using Hidano.FacialControl.Editor.Sampling;
using Hidano.FacialControl.Editor.Windows.Routing;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;

namespace Hidano.FacialControl.Editor.Inspector
{
    /// <summary>
    /// <see cref="FacialCharacterProfileSO"/> 邉ｻ繧｢繧ｻ繝・ヨ縺ｮ豎守畑 UI Toolkit 繧ｫ繧ｹ繧ｿ繝 Inspector縲・
    /// </summary>
    /// <remarks>
    /// <para>
    /// 蜈･蜉帶婿蠑・(InputSystem / OSC / ARKit 遲・ 縺ｫ縺ｯ萓晏ｭ倥＠縺ｪ縺・ｱ守畑 UI 繧呈署萓帙☆繧九・
    /// Layers / Expressions / Reference Model / Debug 陦ｨ遉ｺ縺ｨ閾ｪ蜍穂ｿ晏ｭ假ｼ・rofile.json・峨ｒ諡・ｽ薙☆繧九・
    /// </para>
    /// <para>
    /// 豢ｾ逕溘け繝ｩ繧ｹ縺ｯ <c>OnResolveDerivedSerializedProperties</c> / <c>OnBuildPreLayersSections</c> /
    /// <c>FlushAutoExport</c> 繧偵が繝ｼ繝舌・繝ｩ繧､繝峨＠縺ｦ蜈･蜉帶ｺ仙崋譛・UI 繧・ｿ晏ｭ伜・逅・ｒ霑ｽ蜉縺吶ｋ縲・
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(FacialCharacterProfileSO), editorForChildClasses: true)]
    public class FacialCharacterProfileSOInspector : UnityEditor.Editor
    {
        // ====================================================================
        // VisualElement 縺ｮ name 螳壽焚・域ｱ守畑驛ｨ蛻・ｼ・
        // ====================================================================

        public const string LayersFoldoutName = "facial-character-layers-foldout";
        public const string BaseExpressionFoldoutName = "facial-character-base-expression-foldout";
        public const string BaseExpressionClipFieldName = "facial-character-base-expression-clip-field";
        public const string BaseExpressionUnsetHelpName = "facial-character-base-expression-unset-help";
        public const string GazeConfigsFoldoutName = "facial-character-gaze-configs-foldout";
        public const string DebugFoldoutName = "facial-character-debug-foldout";
        /// <summary>
        /// Adapter Bindings 繧ｻ繧ｯ繧ｷ繝ｧ繝ｳ縺ｮ繝ｫ繝ｼ繝郁ｦ∫ｴ蜷阪よ釜繧翫◆縺溘∩縺ｯ蟒・ｭ｢縺励◆縺溘ａ
        /// <see cref="Foldout"/> 縺ｧ縺ｯ縺ｪ縺冗ｴ縺ｮ <see cref="VisualElement"/> 縺ｫ莉倥￥・亥錐蜑阪・莠呈鋤縺ｮ縺溘ａ謐ｮ縺育ｽｮ縺搾ｼ峨・
        /// </summary>
        public const string AdapterBindingsFoldoutName = "facial-character-adapter-bindings-foldout";

        public const string TabViewName = "facial-character-tabview";
        public const string TabExpressionLibraryName = "facial-character-tab-expression-library";
        public const string TabLayersName = "facial-character-tab-layers";
        public const string TabBaseExpressionName = "facial-character-tab-base-expression";
        public const string TabGazeName = "facial-character-tab-gaze";
        public const string TabAdapterBindingsName = "facial-character-tab-adapter-bindings";
        public const string TabDebugName = "facial-character-tab-debug";
        public const string TabExpressionsName = TabExpressionLibraryName;

        public const string SlotsDeclarationFoldoutName = "facial-character-slots-declaration-foldout";
        public const string SlotsInitPhonemeButtonName = "slots-init-phoneme-button";
        public const string DefaultOverlaysFoldoutName = "facial-character-default-overlays-foldout";
        public const string ExpressionLibraryFoldoutName = "facial-character-expression-library-foldout";
        public const string ExpressionLibraryAddButtonName = "facial-character-expression-library-add-button";
        public const string RoutingEditorOpenButtonName = "facial-character-routing-editor-open-button";
        public const string ExpressionOverlaysSectionName = "expression-row-overlays-section";
        public const string ExpressionPhonemeOverlaysFoldoutName = "expression-row-phoneme-overlays-foldout";
        public const string ExpressionPhonemeOverlaysSummaryName = "expression-row-phoneme-overlays-summary";
        public const string ExpressionPhonemeOverlayUndeclaredSlotHelpName = "expression-row-phoneme-overlay-undeclared-slot-help";
        public const string DefaultOverlaySlotDropdownName = "default-overlay-slot-dropdown";
        public const string DefaultOverlayAnimationClipFieldName = "default-overlay-animation-clip-field";
        public const string DefaultOverlayUndeclaredSlotHelpName = "default-overlay-undeclared-slot-help";
        public const string ExpressionOverlayStateDropdownName = "expression-overlay-state-dropdown";
        public const string ExpressionOverlayAnimationClipFieldName = "expression-overlay-animation-clip-field";
        public const string ExpressionOverlayUndeclaredSlotHelpName = "expression-overlay-undeclared-slot-help";

        /// <summary>縲檎岼邱壹阪ち繝悶・陦ｨ遉ｺ譁・ｭ怜・・亥盾辣ｧ繝｢繝・Ν螟画峩譎ゅ↓繧｢繧ｹ繧ｿ繝ｪ繧ｹ繧ｯ繧剃ｻ倅ｸ弱☆繧句渕貅門､・峨・/summary>
        public const string GazeTabBaseLabel = "逶ｮ邱・;

        /// <summary>縲檎岼邱壹阪ち繝悶↓莉倅ｸ弱☆繧九梧悴遒ｺ隱阪阪・繝ｼ繧ｫ繝ｼ・亥盾辣ｧ繝｢繝・Ν縺悟､峨ｏ縺｣縺溽峩蠕後↓陦ｨ遉ｺ・峨・/summary>
        public const string GazeTabAttentionMarker = " *";

        public const string ReferenceModelDirectFieldName = "facial-character-reference-model-field";

        public const string SaveStatusBarName = "facial-character-save-status-bar";
        public const string SaveStatusLabelName = "facial-character-save-status";
        public const string ExpressionsValidationHelpName = "facial-character-expressions-validation";
        public const string DebugExpressionIdMappingTitleName = "debug-expression-id-mapping-title";
        public const string DebugExpressionIdMappingName = "debug-expression-id-mapping";
        public const string DebugExpressionIdMappingRowName = "debug-expression-id-mapping-row";
        public const string DebugExpressionIdMappingNameCellName = "debug-expression-id-mapping-name";
        public const string DebugExpressionIdMappingExpressionIdCellName = "debug-expression-id-mapping-expression-id";
        public const string DebugExpressionIdMappingKindCellName = "debug-expression-id-mapping-kind";
        public const string DebugExpressionIdMappingLayerCellName = "debug-expression-id-mapping-layer";

        public const string ReferenceModelFoldoutName = "facial-character-reference-model-foldout";

        public const string ExpressionRowNameFieldName = "expression-row-name-field";
        public const string ExpressionRowLayerDropdownName = "expression-row-layer-dropdown";
        public const string ExpressionRowIsGazeToggleName = "expression-row-is-gaze-toggle";
        public const string ExpressionRowClipFieldName = "expression-row-clip-field";
        public const string ExpressionRowRendererSummaryName = "expression-row-renderer-summary";
        public const string ExpressionRowValidationHelpName = "expression-row-validation-help";
        public const string ExpressionRowTransitionDurationFieldName = "expression-row-transition-duration-field";
        public const string ExpressionRowGazeAutoAssignButtonName = "expression-row-gaze-auto-assign-button";
        public const string GazeConfigAddDropdownName = "gaze-config-add-dropdown";
        public const string GazeConfigBulkResolveButtonName = "gaze-config-bulk-resolve-button";
        public const string GazeConfigBulkRegenerateButtonName = "gaze-config-bulk-regenerate-button";
        public const string GazeConfigNoCandidatesLabel = "霑ｽ蜉縺ｧ縺阪ｋ逶ｮ邱壽桃菴懊・陦ｨ諠・・縺ゅｊ縺ｾ縺帙ｓ";
        public const string GazeConfigRowName = "gaze-config-row";
        public const string GazeConfigExpressionNameLabelName = "gaze-config-expression-name";
        public const string GazeConfigLeftBonePathFieldName = "gaze-config-left-bone-path";
        public const string GazeConfigRightBonePathFieldName = "gaze-config-right-bone-path";
        public const string GazeConfigLookUpAngleFieldName = "gaze-config-look-up-angle";
        public const string GazeConfigLookDownAngleFieldName = "gaze-config-look-down-angle";
        public const string GazeConfigOuterYawAngleFieldName = "gaze-config-outer-yaw-angle";
        public const string GazeConfigInnerYawAngleFieldName = "gaze-config-inner-yaw-angle";
        public const string GazeConfigAutoAssignButtonName = "gaze-config-auto-assign-button";
        public const string GazeConfigRemoveButtonName = "gaze-config-remove-button";
        public const string GazeInputSourceDropdownName = "gaze-input-source-dropdown";
        public const string GazeLegacyHelpName = "facial-character-gaze-legacy-help";
        public const string GazeLegacyClearButtonName = "facial-character-gaze-legacy-clear-button";
        public const string GazeChannelsListName = "facial-character-gaze-channels-list";
        public const string GazeChannelRowName = "facial-character-gaze-channel-row";
        public const string GazeChannelIdFieldName = "facial-character-gaze-channel-id";
        public const string GazeChannelProviderDropdownName = "facial-character-gaze-channel-provider";
        public const string GazeChannelAdvancedFoldoutName = "facial-character-gaze-channel-advanced";
        public const string GazeChannelAddButtonName = "facial-character-gaze-channel-add-button";
        public const string GazeChannelIdValidationName = "facial-character-gaze-channel-id-validation";
        public const string GazeChannelDistinctToggleName = "facial-character-gaze-channel-distinct";
        public const string GazeChannelSourceIdLeftName = "facial-character-gaze-channel-source-left";
        public const string GazeChannelSourceIdRightName = "facial-character-gaze-channel-source-right";
        public const string GazeChannelLeftBonePathName = "facial-character-gaze-channel-left-bone";
        public const string GazeChannelRightBonePathName = "facial-character-gaze-channel-right-bone";
        public const string GazeChannelAutoAssignButtonName = "facial-character-gaze-channel-auto-assign";
        public const string GazeChannelBoneResolutionHelpName = "facial-character-gaze-channel-bone-resolution";

        // ====================================================================
        // 蜈ｱ騾壹せ繧ｿ繧､繝ｫ螳壽焚
        // ====================================================================

        protected const int HelpBoxFontSize = 12;
        protected const int SectionFoldoutFontSize = 13;

        // ====================================================================
        // SerializedProperty・域ｱ守畑驛ｨ蛻・ｼ・
        // ====================================================================

        protected SerializedProperty _layersProperty;
        protected SerializedProperty _expressionsProperty;
        protected SerializedProperty _baseExpressionProperty;
        protected SerializedProperty _schemaVersionProperty;
        protected SerializedProperty _adapterBindingsProperty;
        protected SerializedProperty _slotsProperty;
        protected SerializedProperty _defaultOverlaysProperty;
        protected SerializedProperty _gazeChannelsProperty;

#if UNITY_EDITOR
        protected SerializedProperty _referenceModelProperty;
#endif

        /// <summary>SO 繝ｫ繝ｼ繝育峩荳九・ <c>_legacyGazeConfigs</c> SerializedProperty縲・/summary>
        protected SerializedProperty _rootGazeConfigsProperty;

        // ====================================================================
        // VisualElement 繧ｭ繝｣繝・す繝･
        // ====================================================================

        private Label _debugSchemaVersionLabel;
        private Label _debugLayerCountLabel;
        private Label _debugExpressionCountLabel;
        private Label _debugJsonPathLabel;
        private VisualElement _debugExpressionIdMappingContainer;
        private HelpBox _expressionsValidationHelp;
        private Label _saveStatusLabel;
        private VisualElement _layersContainer;
        private VisualElement _expressionLibraryContainer;
        private VisualElement _legacyGazeConfigsContainer;
        private Tab _gazeTab;
        // CreateInspectorGUI 縺瑚ｿ斐☆繝ｫ繝ｼ繝郁ｦ∫ｴ縲Ｐverlay 邱ｨ髮・ｒ bind 譖ｴ譁ｰ繧ｵ繧､繧ｯ繝ｫ縺ｨ陦晉ｪ√＆縺帙↑縺・ｈ縺・
        // 谺｡繝・ぅ繝・け縺ｸ驕・ｻｶ螳溯｡後☆繧矩圀縺ｮ schedule / panel 蛻､螳壹↓菴ｿ逕ｨ縺吶ｋ縲・
        private VisualElement _rootElement;
        private readonly List<DropdownField> _slotDropdowns = new List<DropdownField>();
        private readonly List<ExpressionLayerDropdownField> _expressionLayerDropdowns = new List<ExpressionLayerDropdownField>();

        // ====================================================================
        // 蛟呵｣懊Μ繧ｹ繝・
        // ====================================================================

        protected readonly List<string> _layerNameChoices = new List<string>();
        protected readonly List<string> _slotNameChoices = new List<string>();
        private static readonly IPhonemeSlotInitializer s_phonemeSlotInitializer = new PhonemeSlotInitializer();

        protected IExpressionAnimationClipSampler _sampler;
        private bool _autoSavePending;

        // panel attach 譎ゅ↓谺｡繝・ぅ繝・け縺ｸ驕・ｻｶ縺励◆ overlay 邱ｨ髮・ｼ・pplyDefaultOverlayClipCore /
        // ApplyExpressionOverlayClipCore 遲会ｼ峨ｒ菫晄戟縺吶ｋ縲る≦蟒ｶ繝・ぅ繝・け縺梧擂繧句燕縺ｫ Play 遯∝・縺吶ｋ縺ｨ縲・
        // 繝峨Γ繧､繝ｳ繝ｪ繝ｭ繝ｼ繝峨〒 panel 縺檎ｴ譽・＆繧・schedule.Execute 縺悟ｮ溯｡後＆繧後★縲∫ｷｨ髮・′遒ｺ螳壹＠縺ｪ縺・∪縺ｾ螟ｱ繧上ｌ繧・
        // ・医い繧ｵ繧､繝ｳ逶ｴ蠕後↓ Play 縺吶ｋ縺ｨ Default Overlays 縺ｮ clip 縺悟､悶ｌ繧倶ｸ榊・蜷医・譬ｹ蝗・峨・
        // ExitingEditMode / OnDisable 縺ｧ縺薙・繝ｪ繧ｹ繝医ｒ繝輔Λ繝・す繝･縺励※遒ｺ螳溘↓ SerializedProperty 縺ｸ遒ｺ螳壹☆繧九・
        private readonly List<Action> _pendingOverlayEdits = new List<Action>();

        // SessionState 豌ｸ邯壼喧蟇ｾ雎｡縺ｮ Foldout・磯幕髢臥憾諷九ｒ OnDisable 縺ｧ荳諡ｬ菫晏ｭ倥☆繧具ｼ峨・
        private readonly List<(Foldout foldout, string key)> _persistedFoldouts =
            new List<(Foldout foldout, string key)>();
#if UNITY_EDITOR
        private GameObject _lastReferenceModel;
        private bool _isAutoAssigningGazeBones;
#endif

        // ====================================================================
        // Editor lifecycle
        // ====================================================================

        protected virtual void OnEnable()
        {
            // Play 遯∝・逶ｴ蜑阪↓菫晉蕗荳ｭ縺ｮ overlay 邱ｨ髮・ｒ遒ｺ螳壹☆繧九◆繧・playModeStateChanged 繧定ｳｼ隱ｭ縺吶ｋ縲・
            // 莠碁㍾逋ｻ骭ｲ繧帝∩縺代※縺九ｉ逋ｻ骭ｲ縺吶ｋ縲・
            EditorApplication.playModeStateChanged -= OnPlayModeStateChangedFlushOverlayEdits;
            EditorApplication.playModeStateChanged += OnPlayModeStateChangedFlushOverlayEdits;
        }

        protected virtual void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChangedFlushOverlayEdits;

            // 遐ｴ譽・燕縺ｫ Foldout 縺ｮ髢矩哩迥ｶ諷九ｒ菫晏ｭ倥＠縲∝・讒狗ｯ画凾縺ｫ逶ｴ蜑阪・陦ｨ遉ｺ迥ｶ諷九ｒ蜀咲樟縺ｧ縺阪ｋ繧医≧縺ｫ縺吶ｋ縲・
            SaveFoldoutViewStates();

            // Inspector 遐ｴ譽・/ 蛻･繧ｪ繝悶ず繧ｧ繧ｯ繝磯∈謚樊凾縺ｫ繧ゆｿ晉蕗荳ｭ縺ｮ overlay 邱ｨ髮・ｒ蜿悶ｊ縺薙⊂縺輔↑縺・・
            FlushPendingOverlayEdits();

            // 莠育ｴ・ｸ医∩縺ｮ閾ｪ蜍穂ｿ晏ｭ倥ｂ遐ｴ譽・燕縺ｫ蜷梧悄遒ｺ螳壹☆繧九らｴ譽・ｾ後↓ delayCall 縺ｧ FlushAutoSave 縺・
            // 逋ｺ轣ｫ縺励※繧・target 縺・null 縺ｨ縺ｪ繧贋ｽ輔ｂ菫晏ｭ倥＆繧後★縲《uppress/override 邱ｨ髮・′繝｡繝｢繝ｪ荳翫・
            // SO 縺ｫ縺縺第ｮ九▲縺ｦ .asset / profile.json 縺悟商縺・∪縺ｾ謾ｾ鄂ｮ縺輔ｌ繧・
            // ・育ｷｨ髮・峩蠕後↓蛻･繧ｪ繝悶ず繧ｧ繧ｯ繝医ｒ驕ｸ謚槭☆繧九→菫晏ｭ倥′螟ｱ繧上ｌ繧倶ｸ榊・蜷医・譬ｹ蝗・峨・
            if (_autoSavePending)
            {
                EditorApplication.delayCall -= FlushAutoSave;
                FlushAutoSave();
            }
        }

        /// <summary>
        /// Edit 繝｢繝ｼ繝臥ｵゆｺ・ｼ・ Play 遯∝・逶ｴ蜑搾ｼ峨↓縲∵ｬ｡繝・ぅ繝・け縺ｸ驕・ｻｶ縺輔ｌ縺・overlay 邱ｨ髮・ｒ遒ｺ螳壹＠縲・
        /// 莠育ｴ・ｸ医∩縺ｮ閾ｪ蜍穂ｿ晏ｭ倥ｒ蜊ｳ繝輔Λ繝・す繝･縺吶ｋ縲・c>schedule.Execute</c> 縺ｯ繝峨Γ繧､繝ｳ繝ｪ繝ｭ繝ｼ繝峨↓繧医ｋ
        /// panel 遐ｴ譽・〒螳溯｡後＆繧後↑縺・◆繧√√％縺薙〒繝輔Λ繝・す繝･縺励↑縺・→繧｢繧ｵ繧､繝ｳ逶ｴ蠕後・ clip 縺・
        /// .asset / profile.json 縺ｫ菫晏ｭ倥＆繧後★螟悶ｌ繧九・
        /// </summary>
        private void OnPlayModeStateChangedFlushOverlayEdits(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;

            FlushPendingOverlayEdits();
            if (_autoSavePending)
            {
                FlushAutoSave();
            }
        }

        /// <summary>
        /// <see cref="RunOverlayEditDeferredOrImmediate"/> 縺ｧ谺｡繝・ぅ繝・け縺ｸ驕・ｻｶ縺励◆ overlay 邱ｨ髮・
        /// ・・see cref="_pendingOverlayEdits"/>・峨ｒ蜊ｳ譎ゅ↓螳溯｡後＠縺ｦ遒ｺ螳壹☆繧九・
        /// 蜷・ｷｨ髮・さ繧｢縺ｯ蜀・Κ縺ｧ overlayIndex / slot 繧貞・讀懆ｨｼ縺吶ｋ縺溘ａ縲∽ｺ碁㍾螳溯｡後＆繧後※繧ょｮ牙・縲・
        /// </summary>
        private void FlushPendingOverlayEdits()
        {
            if (_pendingOverlayEdits.Count == 0) return;

            // 螳溯｡御ｸｭ縺ｫ蜀榊ｺｦ驕・ｻｶ縺檎ｩ阪∪繧後※繧ょｮ牙・縺ｪ繧医≧縲√せ繝翫ャ繝励す繝ｧ繝・ヨ繧貞叙繧・Clear 縺励※縺九ｉ螳溯｡後☆繧九・
            var pending = _pendingOverlayEdits.ToArray();
            _pendingOverlayEdits.Clear();
            for (int i = 0; i < pending.Length; i++)
            {
                if (this == null || target …43061 tokens truncated…ｉ繧ょ茜逕ｨ・・
        // ====================================================================

        protected ListView BuildArrayListView(
            SerializedProperty arrayProperty,
            float itemHeight,
            Func<VisualElement> makeItem,
            Action<VisualElement, int> bindItem)
        {
            var indexProxy = new List<int>();
            for (int i = 0; i < arrayProperty.arraySize; i++) indexProxy.Add(i);

            var listView = new ListView
            {
                fixedItemHeight = itemHeight,
                itemsSource = indexProxy,
                showAddRemoveFooter = true,
                showBorder = true,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                selectionType = SelectionType.Single,
                showFoldoutHeader = false,
                makeItem = makeItem,
                bindItem = bindItem,
            };
            listView.style.marginTop = 4;
            listView.style.minHeight = 80f;

            listView.itemsAdded += indices =>
            {
                serializedObject.Update();
                int addCount = 0;
                foreach (var _ in indices) addCount++;
                arrayProperty.arraySize += addCount;
                serializedObject.ApplyModifiedProperties();
                RebuildIndexProxy(indexProxy, arrayProperty);
                listView.Rebuild();
            };
            listView.itemsRemoved += indices =>
            {
                serializedObject.Update();
                var sorted = new List<int>(indices);
                sorted.Sort();
                for (int i = sorted.Count - 1; i >= 0; i--)
                {
                    var removeIndex = sorted[i];
                    if (removeIndex >= 0 && removeIndex < arrayProperty.arraySize)
                    {
                        arrayProperty.DeleteArrayElementAtIndex(removeIndex);
                    }
                }
                serializedObject.ApplyModifiedProperties();
                RebuildIndexProxy(indexProxy, arrayProperty);
                listView.Rebuild();
            };

            return listView;
        }

        private static void RebuildIndexProxy(List<int> indexProxy, SerializedProperty arrayProperty)
        {
            indexProxy.Clear();
            for (int i = 0; i < arrayProperty.arraySize; i++) indexProxy.Add(i);
        }

        // ====================================================================
        // 蛟呵｣懊く繝｣繝・す繝･譖ｴ譁ｰ
        // ====================================================================

        protected void RefreshLayerNameChoices()
        {
            _layerNameChoices.Clear();
            if (_layersProperty == null) return;
            for (int i = 0; i < _layersProperty.arraySize; i++)
            {
                var elem = _layersProperty.GetArrayElementAtIndex(i);
                var nameProp = elem.FindPropertyRelative("name");
                var name = nameProp != null ? nameProp.stringValue : null;
                if (!string.IsNullOrEmpty(name)) _layerNameChoices.Add(name);
            }
        }

        protected void RefreshSlotNameChoices()
        {
            _slotNameChoices.Clear();
            if (_slotsProperty == null) return;

            serializedObject.Update();
            for (int i = 0; i < _slotsProperty.arraySize; i++)
            {
                var elem = _slotsProperty.GetArrayElementAtIndex(i);
                var name = elem != null ? elem.stringValue : null;
                if (!string.IsNullOrEmpty(name) && !_slotNameChoices.Contains(name))
                {
                    _slotNameChoices.Add(name);
                }
            }
        }

        private List<string> BuildLayerDropdownChoices(string currentValue)
        {
            var choices = new List<string>(_layerNameChoices.Count + 1);
            for (int i = 0; i < _layerNameChoices.Count; i++)
            {
                if (!choices.Contains(_layerNameChoices[i]))
                {
                    choices.Add(_layerNameChoices[i]);
                }
            }

            if (!string.IsNullOrEmpty(currentValue) && !choices.Contains(currentValue))
            {
                choices.Add(currentValue);
            }

            return choices;
        }

        private void OnLayersPropertyChanged()
        {
            RefreshLayerNameChoices();
            RefreshAllExpressionLayerDropdownChoices();
        }

        private void RegisterExpressionLayerDropdown(ExpressionLayerDropdownField dropdown, string currentValue)
        {
            if (dropdown == null) return;
            ApplyExpressionLayerDropdownChoices(dropdown, currentValue);
            if (!_expressionLayerDropdowns.Contains(dropdown))
            {
                _expressionLayerDropdowns.Add(dropdown);
            }
        }

        private void RefreshAllExpressionLayerDropdownChoices()
        {
            for (int i = _expressionLayerDropdowns.Count - 1; i >= 0; i--)
            {
                var dropdown = _expressionLayerDropdowns[i];
                if (dropdown == null || dropdown.parent == null)
                {
                    _expressionLayerDropdowns.RemoveAt(i);
                    continue;
                }

                ApplyExpressionLayerDropdownChoices(dropdown, dropdown.value);
            }
        }

        private void ApplyExpressionLayerDropdownChoices(ExpressionLayerDropdownField dropdown, string currentValue)
        {
            var choices = BuildLayerDropdownChoices(currentValue);
            dropdown.choices = choices;
            dropdown.SetValueWithoutNotify(currentValue ?? string.Empty);
            dropdown.SetEnabled(choices.Count > 0);
        }

        private void OnSlotsPropertyChanged()
        {
            RefreshSlotNameChoices();
            RefreshAllSlotDropdownChoices();
        }

        private void RegisterSlotDropdown(DropdownField dropdown, string currentValue)
        {
            if (dropdown == null) return;
            ApplySlotDropdownChoices(dropdown, currentValue);
            if (!_slotDropdowns.Contains(dropdown))
            {
                _slotDropdowns.Add(dropdown);
            }
        }

        private void RefreshAllSlotDropdownChoices()
        {
            for (int i = _slotDropdowns.Count - 1; i >= 0; i--)
            {
                var dropdown = _slotDropdowns[i];
                if (dropdown == null || dropdown.parent == null)
                {
                    _slotDropdowns.RemoveAt(i);
                    continue;
                }

                ApplySlotDropdownChoices(dropdown, dropdown.value);
            }
        }

        private void ApplySlotDropdownChoices(DropdownField dropdown, string currentValue)
        {
            var choices = BuildSlotDropdownChoices(currentValue);
            dropdown.choices = choices;

            string value = currentValue ?? string.Empty;
            if (choices.Count > 0 && !choices.Contains(value))
            {
                value = choices[0];
            }
            dropdown.SetValueWithoutNotify(value);
            dropdown.SetEnabled(choices.Count > 0);
        }

        private List<string> BuildSlotDropdownChoices(string currentValue)
        {
            var choices = new List<string>(_slotNameChoices.Count + 1);
            for (int i = 0; i < _slotNameChoices.Count; i++)
            {
                if (!choices.Contains(_slotNameChoices[i]))
                {
                    choices.Add(_slotNameChoices[i]);
                }
            }

            if (!string.IsNullOrEmpty(currentValue) && !choices.Contains(currentValue))
            {
                choices.Add(currentValue);
            }

            return choices;
        }

        private string GenerateUniqueSlotName()
        {
            const string baseName = "slot";
            if (!_slotNameChoices.Contains(baseName))
            {
                return baseName;
            }

            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + i;
                if (!_slotNameChoices.Contains(candidate))
                {
                    return candidate;
                }
            }

            return Guid.NewGuid().ToString("N");
        }

        private bool IsDeclaredSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return false;
            for (int i = 0; i < _slotNameChoices.Count; i++)
            {
                if (string.Equals(_slotNameChoices[i], slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static int FindOverlayBindingIndex(SerializedProperty overlaysProp, string slot)
        {
            if (overlaysProp == null || !overlaysProp.isArray) return -1;

            for (int i = 0; i < overlaysProp.arraySize; i++)
            {
                var binding = overlaysProp.GetArrayElementAtIndex(i);
                var slotProp = binding.FindPropertyRelative("slot");
                var candidate = slotProp != null ? slotProp.stringValue ?? string.Empty : string.Empty;
                if (string.Equals(candidate, slot ?? string.Empty, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        protected List<string> CollectExpressionIds()
        {
            var ids = new List<string>();
            if (_expressionsProperty == null) return ids;
            for (int i = 0; i < _expressionsProperty.arraySize; i++)
            {
                var elem = _expressionsProperty.GetArrayElementAtIndex(i);
                var idProp = elem.FindPropertyRelative("id");
                if (idProp != null && !string.IsNullOrEmpty(idProp.stringValue))
                {
                    ids.Add(idProp.stringValue);
                }
            }
            return ids;
        }

        protected static List<string> BuildSafeChoices(IReadOnlyList<string> baseChoices, string currentValue)
        {
            var result = new List<string>(baseChoices.Count + 2);
            result.Add(string.Empty);
            for (int i = 0; i < baseChoices.Count; i++)
            {
                if (!result.Contains(baseChoices[i])) result.Add(baseChoices[i]);
            }
            if (!string.IsNullOrEmpty(currentValue) && !result.Contains(currentValue))
            {
                result.Add(currentValue);
            }
            return result;
        }

        // ====================================================================
        // Mask <-> List<string> 螟画鋤
        // ====================================================================

        protected static int ReadMaskValueFromSerializedList(SerializedProperty listProp, IReadOnlyList<string> orderedLayerNames)
        {
            if (listProp == null || !listProp.isArray || orderedLayerNames == null || orderedLayerNames.Count == 0) return 0;
            int result = 0;
            int maxBits = orderedLayerNames.Count > 32 ? 32 : orderedLayerNames.Count;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var name = elem.stringValue;
                if (string.IsNullOrEmpty(name)) continue;
                for (int b = 0; b < maxBits; b++)
                {
                    if (string.Equals(orderedLayerNames[b], name, StringComparison.Ordinal))
                    {
                        result |= 1 << b;
                        break;
                    }
                }
            }
            return result;
        }

        protected static void WriteMaskValueToSerializedList(SerializedProperty listProp, int maskValue, IReadOnlyList<string> orderedLayerNames)
        {
            if (listProp == null || !listProp.isArray) return;
            listProp.ClearArray();
            if (orderedLayerNames == null || orderedLayerNames.Count == 0 || maskValue == 0) return;
            int maxBits = orderedLayerNames.Count > 32 ? 32 : orderedLayerNames.Count;
            for (int b = 0; b < maxBits; b++)
            {
                if ((maskValue & (1 << b)) != 0)
                {
                    listProp.InsertArrayElementAtIndex(listProp.arraySize);
                    var elem = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
                    elem.stringValue = orderedLayerNames[b];
                }
            }
        }

        // ====================================================================
        // RendererPath mismatch 蛻､螳・(AnimationClip)
        // ====================================================================

        private string BuildRendererPathMismatchMessage(AnimationClip clip)
        {
            if (clip == null || _sampler == null) return null;
            var profileSO = target as FacialCharacterProfileSO;
            var referenceModel = profileSO != null ? profileSO.ReferenceModel : null;
            if (referenceModel == null) return null;

            HashSet<string> modelPaths;
            try { modelPaths = CollectReferenceModelRendererPaths(referenceModel); }
            catch (Exception) { return null; }
            if (modelPaths.Count == 0)
            {
                return $"蜿ら・繝｢繝・Ν '{referenceModel.name}' 縺ｫ SkinnedMeshRenderer 縺瑚ｦ九▽縺九ｊ縺ｾ縺帙ｓ縲・;
            }

            List<string> rendererPaths;
            try
            {
                var summary = _sampler.SampleSummary(clip);
                rendererPaths = summary.RendererPaths != null ? new List<string>(summary.RendererPaths) : new List<string>();
            }
            catch (Exception) { return null; }

            var invalid = new List<string>();
            for (int i = 0; i < rendererPaths.Count; i++)
            {
                var path = rendererPaths[i] ?? string.Empty;
                if (!modelPaths.Contains(path)) invalid.Add(string.IsNullOrEmpty(path) ? "(繝ｫ繝ｼ繝・" : path);
            }
            if (invalid.Count == 0) return null;

            return $"AnimationClip 縺ｮ RendererPath [{string.Join(", ", invalid)}] 縺悟盾辣ｧ繝｢繝・Ν蜀・・ SkinnedMeshRenderer 縺ｨ荳閾ｴ縺励∪縺帙ｓ縲・
                + $" 蜿ら・繝｢繝・Ν蛟呵｣・ [{string.Join(", ", modelPaths)}]";
        }

        private static HashSet<string> CollectReferenceModelRendererPaths(GameObject model)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (model == null) return result;
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var smr = renderers[i];
                if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount == 0) continue;
                var path = AnimationUtility.CalculateTransformPath(smr.transform, model.transform) ?? string.Empty;
                result.Add(path);
            }
            return result;
        }

        // ====================================================================
        // AnimationClip 陦ｨ遉ｺ逕ｨ ObjectField
        // ====================================================================

        private sealed class OverlayStateDropdownField : DropdownField
        {
            public Action<string> OnValueAssigned;

            public override string value
            {
                get => base.value;
                set
                {
                    base.value = value;
                    OnValueAssigned?.Invoke(value);
                }
            }
        }

        private sealed class OverlayAnimationClipObjectField : ObjectField
        {
            public Action<AnimationClip> OnValueAssigned;

            public OverlayAnimationClipObjectField(string label)
                : base(label)
            {
            }

            public override UnityEngine.Object value
            {
                get => base.value;
                set
                {
                    var previous = base.value;
                    base.value = value;
                    if (!ReferenceEquals(previous, value))
                    {
                        OnValueAssigned?.Invoke(value as AnimationClip);
                    }
                }
            }
        }

        private sealed class ExpressionLayerDropdownField : DropdownField
        {
            public Action<string> OnValueAssigned;

            public override string value
            {
                get => base.value;
                set
                {
                    var previous = base.value;
                    base.value = value;
                    if (!string.Equals(previous, value, StringComparison.Ordinal))
                    {
                        OnValueAssigned?.Invoke(value);
                    }
                }
            }
        }

        /// <summary>
        /// AnimationClip 蜷阪〒縺ｯ縺ｪ縺上√・繝ｭ繧ｸ繧ｧ繧ｯ繝医・繝輔ぃ繧､繝ｫ蜷搾ｼ域僑蠑ｵ蟄舌↑縺暦ｼ峨ｒ陦ｨ遉ｺ縺吶ｋ繧ｫ繧ｹ繧ｿ繝 ObjectField縲・
        /// AnimationClip 繧定､・｣ｽ繝ｻ邱ｨ髮・＠縺溷ｴ蜷医↓ object 蜷阪′繝輔ぃ繧､繝ｫ蜷阪→荳閾ｴ縺励↑縺上↑繧・Unity 縺ｮ謖吝虚繧貞屓驕ｿ縺吶ｋ縲・
        /// </summary>
        protected sealed class ExpressionClipObjectField : ObjectField
        {
            public Action<UnityEngine.Object> OnValueAssigned;

            public override void SetValueWithoutNotify(UnityEngine.Object newValue)
            {
                var previous = value;
                base.SetValueWithoutNotify(newValue);
                if (!ReferenceEquals(previous, newValue))
                {
                    MarkDirtyRepaint();
                    OnValueAssigned?.Invoke(newValue);
                }
                RefreshDisplayLabel();
            }

            public void RefreshDisplayLabel()
            {
                var clip = value as AnimationClip;
                var labelToSet = ResolveDisplayLabel(clip);
                if (string.IsNullOrEmpty(labelToSet)) return;

                // ObjectField 蜀・Κ縺ｮ陦ｨ遉ｺ繝・く繧ｹ繝郁ｦ∫ｴ繧・".unity-object-field-display__label" 縺ｧ蜿門ｾ励＠縺ｦ荳頑嶌縺・
                var displayLabel = this.Q<Label>(className: "unity-object-field-display__label");
                if (displayLabel != null)
                {
                    displayLabel.text = labelToSet;
                }
            }

            private static string ResolveDisplayLabel(AnimationClip clip)
            {
                if (clip == null) return string.Empty;
                var path = AssetDatabase.GetAssetPath(clip);
                if (string.IsNullOrEmpty(path)) return clip.name;
                var fileName = Path.GetFileNameWithoutExtension(path);
                return string.IsNullOrEmpty(fileName) ? clip.name : fileName;
            }
        }
    }
}


