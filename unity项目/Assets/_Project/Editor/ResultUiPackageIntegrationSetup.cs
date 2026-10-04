#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRInterview.EditorTools
{
    public static class ResultUiPackageIntegrationSetup
    {
        private const string PrefabPath =
            "Assets/_Project/Prefabs/ui/ResultUI_New.prefab";

        private const string ScenePath =
            "Assets/Scenes/MainScene_AIInterview_VoiceTest_v1.unity";

        private const string ChineseFontGuid =
            "1ad41addaf41dc94c9ce393fb690d934";

        private static readonly Color MainText =
            new Color32(242, 248, 250, 255);

        private static readonly Color SecondaryText =
            new Color32(198, 216, 223, 255);

        private static readonly Color PanelFill =
            new Color32(47, 65, 76, 222);

        [MenuItem("Tools/VR Interview/应用新版结算报告 UI")]
        public static void Apply()
        {
            BuildPrefab();
            BindScene();
            Validate();
        }

        private static void BuildPrefab()
        {
            GameObject root =
                PrefabUtility.LoadPrefabContents(PrefabPath);

            if (root == null)
            {
                throw new InvalidOperationException(
                    "无法加载结果页 Prefab：" + PrefabPath
                );
            }

            try
            {
                TMP_FontAsset font = LoadChineseFont(root);

                Transform oldPackageLayout =
                    root.transform.Find("PackageResultLayout");

                if (oldPackageLayout != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        oldPackageLayout.gameObject
                    );
                }

                foreach (Transform child in root.transform)
                {
                    child.gameObject.SetActive(false);
                }

                RectTransform layout = CreateRect(
                    "PackageResultLayout",
                    root.transform,
                    new Vector2(1700f, 920f),
                    Vector2.zero
                );

                InterviewPanelGradient gradient =
                    layout.gameObject.AddComponent<
                        InterviewPanelGradient
                    >();
                gradient.raycastTarget = false;

                Outline outline =
                    layout.gameObject.AddComponent<Outline>();
                outline.effectColor =
                    new Color(0.72f, 0.87f, 0.91f, 0.78f);
                outline.effectDistance = new Vector2(3f, -3f);

                BuildDecor(layout);

                TMP_Text title = CreateText(
                    "ResultTitleText",
                    layout,
                    font,
                    "面试结果",
                    70f,
                    FontStyles.Bold,
                    MainText,
                    new Vector2(500f, 100f),
                    new Vector2(-120f, 350f),
                    TextAlignmentOptions.Center
                );

                CreateLine(
                    "TitleUnderline",
                    layout,
                    new Vector2(360f, 3f),
                    new Vector2(-120f, 296f),
                    new Color(0.67f, 0.82f, 0.86f, 0.72f)
                );

                InterviewScoreRing ring = BuildScoreSummary(
                    layout,
                    font,
                    out TMP_Text overallScore,
                    out TMP_Text positionLabel
                );

                TMP_Text languageScore = BuildMetricCard(
                    layout,
                    font,
                    "LanguageMetric",
                    "语言表达",
                    new Vector2(-120f, 196f),
                    "●"
                );

                TMP_Text reactionScore = BuildMetricCard(
                    layout,
                    font,
                    "ReactionMetric",
                    "临场反应",
                    new Vector2(-120f, 42f),
                    "◆"
                );

                TMP_Text professionalScore = BuildMetricCard(
                    layout,
                    font,
                    "ProfessionalMetric",
                    "专业匹配度",
                    new Vector2(-120f, -112f),
                    "▲"
                );

                TMP_Text demeanorScore = BuildMetricCard(
                    layout,
                    font,
                    "DemeanorMetric",
                    "仪态表现",
                    new Vector2(-120f, -266f),
                    "■"
                );

                BuildFeedbackPanel(
                    layout,
                    font,
                    out TMP_Text highlightsText,
                    out TMP_Text suggestionsText,
                    out ScrollRect highlightsScroll,
                    out ScrollRect suggestionsScroll
                );

                InterviewReferenceUI prefabReferenceUI =
                    root.GetComponent<InterviewReferenceUI>();

                if (prefabReferenceUI != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        prefabReferenceUI
                    );
                }

                TMP_Text notice = CreateText(
                    "ResultNotice",
                    layout,
                    font,
                    "",
                    24f,
                    FontStyles.Normal,
                    new Color32(255, 213, 128, 255),
                    new Vector2(720f, 50f),
                    new Vector2(350f, -438f),
                    TextAlignmentOptions.Center
                );
                notice.gameObject.SetActive(false);

                GameObject historyPanel = BuildHistoryPanel(
                    layout,
                    font,
                    out TMP_Text historyText
                );

                Button simulateAgain = CreateButton(
                    "SimulateAgain",
                    layout,
                    font,
                    "再次模拟",
                    new Vector2(235f, 88f),
                    new Vector2(402f, -385f),
                    new Color32(82, 108, 125, 255)
                );

                Button printReport = CreateButton(
                    "PrintReport",
                    layout,
                    font,
                    "打印报告",
                    new Vector2(235f, 88f),
                    new Vector2(680f, -385f),
                    new Color32(96, 116, 134, 255)
                );

                ClearPersistentCalls(simulateAgain);
                ClearPersistentCalls(printReport);

                title.raycastTarget = false;
                overallScore.raycastTarget = false;
                languageScore.raycastTarget = false;
                reactionScore.raycastTarget = false;
                professionalScore.raycastTarget = false;
                demeanorScore.raycastTarget = false;
                highlightsText.raycastTarget = false;
                suggestionsText.raycastTarget = false;
                simulateAgain.navigation = new Navigation
                {
                    mode = Navigation.Mode.None
                };
                printReport.navigation = new Navigation
                {
                    mode = Navigation.Mode.None
                };

                root.SetActive(false);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void BindScene()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (scene.path != ScenePath)
            {
                if (scene.isDirty)
                {
                    throw new InvalidOperationException(
                        "当前场景有未保存修改，无法自动切换。"
                    );
                }

                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single
                );
            }

            InterviewManager manager =
                UnityEngine.Object.FindObjectOfType<
                    InterviewManager
                >(true);

            if (manager == null || manager.resultUI == null)
            {
                throw new InvalidOperationException(
                    "主场景中找不到 InterviewManager 或结果页。"
                );
            }

            GameObject resultRoot = manager.resultUI;
            RectTransform resultRect =
                resultRoot.GetComponent<RectTransform>();

            if (resultRect == null)
            {
                throw new InvalidOperationException(
                    "结果页根对象缺少 RectTransform。"
                );
            }

            Vector2 resultPosition = resultRect.anchoredPosition;
            resultPosition.y = 1.286f;
            resultRect.anchoredPosition = resultPosition;

            Transform layout = resultRoot.transform.Find(
                "PackageResultLayout"
            );

            if (layout == null)
            {
                throw new InvalidOperationException(
                    "结果页实例尚未刷新出 PackageResultLayout。"
                );
            }

            manager.overallScoreText = RequireText(
                layout,
                "OverallScoreText"
            );
            manager.languageScoreText = RequireText(
                layout,
                "LanguageScoreText"
            );
            manager.reactionScoreText = RequireText(
                layout,
                "ReactionScoreText"
            );
            manager.professionalScoreText = RequireText(
                layout,
                "ProfessionalScoreText"
            );
            manager.demeanorScoreText = RequireText(
                layout,
                "DemeanorScoreText"
            );
            manager.highlightsText = RequireText(
                layout,
                "HighlightsText"
            );
            manager.suggestionsText = RequireText(
                layout,
                "SuggestionsText"
            );

            InterviewReferenceUI staleReferenceUI =
                resultRoot.GetComponent<InterviewReferenceUI>();

            if (staleReferenceUI != null)
            {
                UnityEngine.Object.DestroyImmediate(staleReferenceUI);
            }

            InterviewReferenceUI referenceUI =
                manager.GetComponent<InterviewReferenceUI>();

            if (referenceUI == null)
            {
                referenceUI =
                    manager.gameObject.AddComponent<
                        InterviewReferenceUI
                    >();
            }

            referenceUI.manager = manager;
            referenceUI.welcomeDecor = null;
            referenceUI.questionGroup = null;
            referenceUI.positionLabel = RequireText(
                layout,
                "PositionLabel"
            );
            referenceUI.notice = RequireText(
                layout,
                "ResultNotice"
            );
            referenceUI.historyText = RequireText(
                layout,
                "HistoryText"
            );
            referenceUI.historyPanel = FindRequired(
                layout,
                "HistoryPanel"
            ).gameObject;
            referenceUI.ring = FindRequired(
                layout,
                "ScoreRing"
            ).GetComponent<InterviewScoreRing>();
            referenceUI.highlightsScroll = FindRequired(
                layout,
                "HighlightsScroll"
            ).GetComponent<ScrollRect>();
            referenceUI.suggestionsScroll = FindRequired(
                layout,
                "SuggestionsScroll"
            ).GetComponent<ScrollRect>();

            Button simulateAgain = FindDeep(
                layout,
                "SimulateAgain"
            ).GetComponent<Button>();

            ClearPersistentCalls(simulateAgain);
            UnityEventTools.AddPersistentListener(
                simulateAgain.onClick,
                manager.RestartInterview
            );

            Button printReport = FindRequired(
                layout,
                "PrintReport"
            ).GetComponent<Button>();
            printReport.interactable = true;
            ClearPersistentCalls(printReport);
            UnityEventTools.AddPersistentListener(
                printReport.onClick,
                referenceUI.PrintReport
            );

            Button closeHistory = FindRequired(
                layout,
                "CloseHistory"
            ).GetComponent<Button>();
            ClearPersistentCalls(closeHistory);
            UnityEventTools.AddPersistentListener(
                closeHistory.onClick,
                referenceUI.CloseHistory
            );

            resultRoot.SetActive(false);
            EditorUtility.SetDirty(resultRoot);
            EditorUtility.SetDirty(resultRect);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(referenceUI);
            EditorUtility.SetDirty(simulateAgain);
            EditorUtility.SetDirty(printReport);
            EditorUtility.SetDirty(closeHistory);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/VR Interview/验证新版结算报告 UI")]
        public static void Validate()
        {
            InterviewManager manager =
                UnityEngine.Object.FindObjectOfType<
                    InterviewManager
                >(true);

            if (manager == null || manager.resultUI == null)
            {
                throw new InvalidOperationException(
                    "验证失败：找不到结果页管理器。"
                );
            }

            GameObject resultRoot = manager.resultUI;
            Transform layout = resultRoot.transform.Find(
                "PackageResultLayout"
            );
            InterviewReferenceUI referenceUI =
                manager.GetComponent<InterviewReferenceUI>();

            Button simulateAgain = layout == null
                ? null
                : FindDeep(layout, "SimulateAgain")
                    ?.GetComponent<Button>();
            Button printReport = layout == null
                ? null
                : FindDeep(layout, "PrintReport")
                    ?.GetComponent<Button>();
            Button closeHistory = layout == null
                ? null
                : FindDeep(layout, "CloseHistory")
                    ?.GetComponent<Button>();

            bool valid =
                layout != null
                && referenceUI != null
                && referenceUI.manager == manager
                && referenceUI.ring != null
                && referenceUI.positionLabel != null
                && referenceUI.highlightsScroll != null
                && referenceUI.suggestionsScroll != null
                && manager.overallScoreText != null
                && manager.languageScoreText != null
                && manager.reactionScoreText != null
                && manager.professionalScoreText != null
                && manager.demeanorScoreText != null
                && manager.highlightsText != null
                && manager.suggestionsText != null
                && simulateAgain != null
                && simulateAgain.onClick.GetPersistentEventCount() > 0
                && printReport != null
                && printReport.interactable
                && printReport.onClick.GetPersistentEventCount() > 0
                && closeHistory != null
                && closeHistory.onClick.GetPersistentEventCount() > 0
                && resultRoot.GetComponentsInChildren<
                    InterviewCapsuleGraphic
                >(true).Length >= 3
                && resultRoot.GetComponentInChildren<
                    InterviewPanelGradient
                >(true) != null;

            if (!valid)
            {
                throw new InvalidOperationException(
                    "新版结算页仍有组件或引用缺失。"
                );
            }

            Debug.Log(
                "[Result UI Validation] PASS：package 中的四个 UI 组件已接入；"
                + "环形评分、岗位、四项得分、亮点、建议、再次模拟和打印报告引用完整。"
            );
        }

        private static InterviewScoreRing BuildScoreSummary(
            RectTransform parent,
            TMP_FontAsset font,
            out TMP_Text overallScore,
            out TMP_Text positionLabel
        )
        {
            RectTransform ringRect = CreateRect(
                "ScoreRing",
                parent,
                new Vector2(300f, 300f),
                new Vector2(-642f, 246f)
            );
            InterviewScoreRing ring =
                ringRect.gameObject.AddComponent<
                    InterviewScoreRing
                >();
            ring.raycastTarget = false;

            overallScore = CreateText(
                "OverallScoreText",
                parent,
                font,
                "80",
                132f,
                FontStyles.Normal,
                MainText,
                new Vector2(260f, 160f),
                new Vector2(-650f, -4f),
                TextAlignmentOptions.Center
            );

            CreateText(
                "OverallOutOfText",
                parent,
                font,
                "/100",
                42f,
                FontStyles.Normal,
                SecondaryText,
                new Vector2(140f, 70f),
                new Vector2(-495f, -24f),
                TextAlignmentOptions.Left
            );

            CreateText(
                "OverallLabelText",
                parent,
                font,
                "综合得分",
                48f,
                FontStyles.Bold,
                MainText,
                new Vector2(330f, 70f),
                new Vector2(-620f, -112f),
                TextAlignmentOptions.Center
            );

            RectTransform positionCard = CreatePanel(
                "PositionCard",
                parent,
                new Vector2(410f, 160f),
                new Vector2(-622f, -268f),
                new Color32(73, 94, 105, 225),
                true
            );

            positionLabel = CreateText(
                "PositionLabel",
                positionCard,
                font,
                "UI设计师",
                43f,
                FontStyles.Normal,
                MainText,
                new Vector2(350f, 58f),
                new Vector2(0f, 31f),
                TextAlignmentOptions.Left
            );

            CreateText(
                "PositionCaption",
                positionCard,
                font,
                "当前岗位",
                30f,
                FontStyles.Normal,
                SecondaryText,
                new Vector2(350f, 46f),
                new Vector2(0f, -40f),
                TextAlignmentOptions.Left
            );

            return ring;
        }

        private static TMP_Text BuildMetricCard(
            RectTransform parent,
            TMP_FontAsset font,
            string objectName,
            string label,
            Vector2 position,
            string icon
        )
        {
            RectTransform card = CreatePanel(
                objectName,
                parent,
                new Vector2(530f, 126f),
                position,
                new Color32(50, 68, 79, 215),
                true
            );

            CreateText(
                objectName + "Label",
                card,
                font,
                label,
                31f,
                FontStyles.Bold,
                MainText,
                new Vector2(260f, 42f),
                new Vector2(-82f, 31f),
                TextAlignmentOptions.Left
            );

            string scoreName = objectName.Replace(
                "Metric",
                "ScoreText"
            );

            TMP_Text score = CreateText(
                scoreName,
                card,
                font,
                "80",
                55f,
                FontStyles.Bold,
                MainText,
                new Vector2(125f, 65f),
                new Vector2(-142f, -22f),
                TextAlignmentOptions.Left
            );

            CreateText(
                objectName + "OutOf",
                card,
                font,
                "/100",
                28f,
                FontStyles.Normal,
                SecondaryText,
                new Vector2(105f, 45f),
                new Vector2(-29f, -26f),
                TextAlignmentOptions.Left
            );

            CreateLine(
                objectName + "DottedLine",
                card,
                new Vector2(215f, 2f),
                new Vector2(105f, -23f),
                new Color(0.72f, 0.82f, 0.85f, 0.42f)
            );

            CreateText(
                objectName + "Icon",
                card,
                font,
                icon,
                34f,
                FontStyles.Normal,
                new Color32(208, 230, 235, 255),
                new Vector2(62f, 62f),
                new Vector2(218f, -8f),
                TextAlignmentOptions.Center
            );

            return score;
        }

        private static void BuildFeedbackPanel(
            RectTransform parent,
            TMP_FontAsset font,
            out TMP_Text highlightsText,
            out TMP_Text suggestionsText,
            out ScrollRect highlightsScroll,
            out ScrollRect suggestionsScroll
        )
        {
            RectTransform panel = CreatePanel(
                "FeedbackPanel",
                parent,
                new Vector2(555f, 650f),
                new Vector2(548f, 65f),
                PanelFill,
                true
            );

            CreateText(
                "HighlightsLabel",
                panel,
                font,
                "表现亮点",
                40f,
                FontStyles.Bold,
                MainText,
                new Vector2(475f, 56f),
                new Vector2(0f, 258f),
                TextAlignmentOptions.Left
            );

            highlightsScroll = CreateScrollText(
                "HighlightsScroll",
                panel,
                font,
                "HighlightsText",
                "● 亮点文本",
                new Vector2(475f, 180f),
                new Vector2(0f, 137f),
                out highlightsText,
                new Color32(217, 238, 225, 255)
            );

            CreateLine(
                "FeedbackDivider",
                panel,
                new Vector2(475f, 2f),
                new Vector2(0f, 20f),
                new Color(0.68f, 0.79f, 0.82f, 0.25f)
            );

            CreateText(
                "SuggestionsLabel",
                panel,
                font,
                "改进建议",
                40f,
                FontStyles.Bold,
                MainText,
                new Vector2(475f, 56f),
                new Vector2(0f, -32f),
                TextAlignmentOptions.Left
            );

            suggestionsScroll = CreateScrollText(
                "SuggestionsScroll",
                panel,
                font,
                "SuggestionsText",
                "◆ 建议文本",
                new Vector2(475f, 200f),
                new Vector2(0f, -175f),
                out suggestionsText,
                new Color32(215, 219, 255, 255)
            );
        }

        private static void BuildDecor(RectTransform parent)
        {
            CreateLine(
                "CenterDivider",
                parent,
                new Vector2(2f, 780f),
                new Vector2(205f, 0f),
                new Color(0.65f, 0.78f, 0.82f, 0.22f)
            );
        }

        private static GameObject BuildHistoryPanel(
            RectTransform parent,
            TMP_FontAsset font,
            out TMP_Text historyText
        )
        {
            RectTransform panel = CreatePanel(
                "HistoryPanel",
                parent,
                new Vector2(1450f, 760f),
                Vector2.zero,
                new Color32(30, 44, 55, 250),
                true
            );

            CreateText(
                "HistoryTitle",
                panel,
                font,
                "历史面试记录",
                54f,
                FontStyles.Bold,
                MainText,
                new Vector2(760f, 80f),
                new Vector2(0f, 318f),
                TextAlignmentOptions.Center
            );

            historyText = CreateText(
                "HistoryText",
                panel,
                font,
                "暂无历史记录。",
                30f,
                FontStyles.Normal,
                MainText,
                new Vector2(1240f, 540f),
                new Vector2(0f, -5f),
                TextAlignmentOptions.TopLeft
            );

            Button close = CreateButton(
                "CloseHistory",
                panel,
                font,
                "关闭",
                new Vector2(220f, 76f),
                new Vector2(0f, -330f),
                new Color32(84, 105, 122, 255)
            );

            ClearPersistentCalls(close);

            panel.gameObject.SetActive(false);
            return panel.gameObject;
        }

        private static ScrollRect CreateScrollText(
            string name,
            RectTransform parent,
            TMP_FontAsset font,
            string textName,
            string placeholder,
            Vector2 size,
            Vector2 position,
            out TMP_Text text,
            Color textColor
        )
        {
            RectTransform scrollRect = CreateRect(
                name,
                parent,
                size,
                position
            );
            Image hitArea =
                scrollRect.gameObject.AddComponent<Image>();
            hitArea.color = new Color(1f, 1f, 1f, 0.001f);

            ScrollRect scroll =
                scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            RectTransform viewport = CreateStretchRect(
                "Viewport",
                scrollRect,
                new Vector4(0f, 0f, 0f, 0f)
            );
            viewport.gameObject.AddComponent<RectMask2D>();

            text = CreateText(
                textName,
                viewport,
                font,
                placeholder,
                29f,
                FontStyles.Normal,
                textColor,
                new Vector2(size.x - 18f, size.y),
                Vector2.zero,
                TextAlignmentOptions.TopLeft
            );
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0.5f, 1f);
            textRect.anchorMax = new Vector2(0.5f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = Vector2.zero;
            text.enableAutoSizing = true;
            text.fontSizeMin = 21f;
            text.fontSizeMax = 29f;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.margin = new Vector4(8f, 4f, 8f, 4f);

            scroll.viewport = viewport;
            scroll.content = textRect;
            return scroll;
        }

        private static Button CreateButton(
            string name,
            RectTransform parent,
            TMP_FontAsset font,
            string label,
            Vector2 size,
            Vector2 position,
            Color color
        )
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                size,
                position
            );
            InterviewCapsuleGraphic graphic =
                rect.gameObject.AddComponent<
                    InterviewCapsuleGraphic
                >();
            graphic.color = color;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.82f, 0.88f, 0.92f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            button.colors = colors;

            CreateText(
                "Label",
                rect,
                font,
                label,
                34f,
                FontStyles.Normal,
                MainText,
                size,
                Vector2.zero,
                TextAlignmentOptions.Center
            );

            return button;
        }

        private static RectTransform CreatePanel(
            string name,
            RectTransform parent,
            Vector2 size,
            Vector2 position,
            Color color,
            bool outline
        )
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                size,
                position
            );
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;

            if (outline)
            {
                Outline border =
                    rect.gameObject.AddComponent<Outline>();
                border.effectColor =
                    new Color(0.65f, 0.78f, 0.82f, 0.35f);
                border.effectDistance = new Vector2(1.5f, -1.5f);
            }

            return rect;
        }

        private static TMP_Text CreateText(
            string name,
            RectTransform parent,
            TMP_FontAsset font,
            string content,
            float fontSize,
            FontStyles style,
            Color color,
            Vector2 size,
            Vector2 position,
            TextAlignmentOptions alignment
        )
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                size,
                position
            );
            TextMeshProUGUI text =
                rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void CreateLine(
            string name,
            RectTransform parent,
            Vector2 size,
            Vector2 position,
            Color color
        )
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                size,
                position
            );
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static RectTransform CreateRect(
            string name,
            Transform parent,
            Vector2 size,
            Vector2 position
        )
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform)
            );
            gameObject.layer = 5;
            RectTransform rect =
                gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static RectTransform CreateStretchRect(
            string name,
            Transform parent,
            Vector4 offsets
        )
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                Vector2.zero,
                Vector2.zero
            );
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(offsets.x, offsets.y);
            rect.offsetMax = new Vector2(-offsets.z, -offsets.w);
            return rect;
        }

        private static TMP_FontAsset LoadChineseFont(
            GameObject root
        )
        {
            string path = AssetDatabase.GUIDToAssetPath(
                ChineseFontGuid
            );
            TMP_FontAsset font =
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

            if (font == null)
            {
                TMP_Text existing =
                    root.GetComponentInChildren<TMP_Text>(true);
                font = existing != null ? existing.font : null;
            }

            if (font == null)
            {
                throw new InvalidOperationException(
                    "找不到支持中文的 TMP 字体资源。"
                );
            }

            return font;
        }

        private static TMP_Text RequireText(
            Transform root,
            string objectName
        )
        {
            Transform target = FindDeep(root, objectName);

            if (target == null)
            {
                throw new InvalidOperationException(
                    "结果页缺少文本对象：" + objectName
                );
            }

            TMP_Text text = target.GetComponent<TMP_Text>();

            if (text == null)
            {
                throw new InvalidOperationException(
                    objectName + " 缺少 TMP_Text。"
                );
            }

            return text;
        }

        private static Transform FindDeep(
            Transform root,
            string objectName
        )
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == objectName)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, objectName);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindRequired(
            Transform root,
            string objectName
        )
        {
            Transform found = FindDeep(root, objectName);

            if (found == null)
            {
                throw new InvalidOperationException(
                    "结果页缺少对象：" + objectName
                );
            }

            return found;
        }

        private static void ClearPersistentCalls(Button button)
        {
            if (button == null)
            {
                throw new InvalidOperationException(
                    "按钮引用为空。"
                );
            }

            while (button.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(
                    button.onClick,
                    0
                );
            }
        }
    }
}
#endif
