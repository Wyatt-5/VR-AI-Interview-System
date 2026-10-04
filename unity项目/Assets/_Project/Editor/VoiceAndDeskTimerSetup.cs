#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRInterview.EditorTools
{
    public static class VoiceAndDeskTimerSetup
    {
        private const string ScenePath =
            "Assets/Scenes/MainScene_AIInterview_VoiceTest_v1.unity";

        private const string ChineseFontGuid =
            "1ad41addaf41dc94c9ce393fb690d934";

        private const string TimerRootName =
            "DeskTimerAnchor_桌面计时器";

        private const string ModelSocketName =
            "ModelSocket_外壳模型挂这里";

        private const string TimerCanvasName =
            "TimerCanvas_UI勿删除";

        private static readonly Vector3 TimerWorldPosition =
            new Vector3(0.27f, 0.865f, 0.48f);

        private static readonly Quaternion TimerWorldRotation =
            Quaternion.Euler(0f, -90f, 0f);

        [MenuItem("Tools/VR Interview/应用稳重男声与桌面计时器")]
        public static void Apply()
        {
            Scene scene = OpenMainScene();
            InterviewManager interviewManager =
                UnityEngine.Object.FindObjectOfType<InterviewManager>(true);
            QuestionSpeechManager speechManager =
                UnityEngine.Object.FindObjectOfType<QuestionSpeechManager>(true);

            if (interviewManager == null || speechManager == null)
            {
                throw new InvalidOperationException(
                    "主场景缺少 InterviewManager 或 QuestionSpeechManager。"
                );
            }

            ApplyVoiceSettings(speechManager);
            InterviewDeskTimer timer = BuildDeskTimer(
                scene,
                interviewManager
            );

            EditorUtility.SetDirty(interviewManager);
            EditorUtility.SetDirty(speechManager);
            EditorUtility.SetDirty(timer);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            ValidateOrThrow(scene);

            Selection.activeGameObject = timer.gameObject;
            Debug.Log(
                "[Voice + Desk Timer Setup] PASS："
                + "已使用成熟稳重的 Yunyang 男声，"
                + "桌面计时器已放到右侧桌面并预留外壳模型插槽。"
            );
        }

        public static void ApplyFromCommandLine()
        {
            Apply();
        }

        [MenuItem("Tools/VR Interview/验证稳重男声与桌面计时器")]
        public static void Validate()
        {
            Scene scene = OpenMainScene();
            ValidateOrThrow(scene);
            Debug.Log(
                "[Voice + Desk Timer Validation] PASS："
                + "音色、计时事件、空间位置、模型插槽和 UI 引用均正确。"
            );
        }

        [MenuItem("Tools/VR Interview/运行时快速测试桌面计时器")]
        public static void RuntimeTimerSmokeTest()
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "请先点击 Unity 顶部的 Play，再运行此测试。"
                );
            }

            InterviewManager interviewManager =
                UnityEngine.Object.FindObjectOfType<InterviewManager>(true);
            InterviewDeskTimer timer =
                UnityEngine.Object.FindObjectOfType<InterviewDeskTimer>(true);

            if (interviewManager == null || timer == null)
            {
                throw new InvalidOperationException(
                    "运行中的场景缺少面试管理器或桌面计时器。"
                );
            }

            interviewManager.targetPositionInput.text =
                "软件开发工程师";
            interviewManager.StartInterview();

            if (!timer.IsRunning)
            {
                throw new InvalidOperationException(
                    "StartInterview 已执行，但计时器没有开始。"
                );
            }

            Debug.Log(
                "[Voice + Desk Timer Runtime] PASS："
                + "正式面试事件已启动桌面计时器。"
            );
        }

        [MenuItem(
            "Tools/VR Interview/运行时快速测试桌面计时器",
            true
        )]
        private static bool CanRunRuntimeTimerSmokeTest()
        {
            return Application.isPlaying;
        }

        private static void ApplyVoiceSettings(
            QuestionSpeechManager speechManager
        )
        {
            Undo.RecordObject(
                speechManager,
                "配置成熟稳重的面试官男声"
            );
            speechManager.enableSpeech = true;
            speechManager.voice = "zh-CN-YunyangNeural";
            speechManager.rate = "-8%";
            speechManager.volume = "+0%";
            speechManager.pitch = "-4Hz";
            speechManager.useNeuralVoice = true;
            speechManager.requestTimeoutSeconds = 30;
            speechManager.failureCooldownSeconds = 15f;
        }

        private static InterviewDeskTimer BuildDeskTimer(
            Scene scene,
            InterviewManager interviewManager
        )
        {
            GameObject root = FindRoot(scene, TimerRootName);

            if (root == null)
            {
                root = new GameObject(TimerRootName);
                Undo.RegisterCreatedObjectUndo(
                    root,
                    "创建桌面计时器"
                );
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            root.transform.SetPositionAndRotation(
                TimerWorldPosition,
                TimerWorldRotation
            );
            root.transform.localScale = Vector3.one;

            Transform modelSocket = root.transform.Find(ModelSocketName);

            if (modelSocket == null)
            {
                GameObject socketObject = new GameObject(ModelSocketName);
                socketObject.transform.SetParent(root.transform, false);
                modelSocket = socketObject.transform;
            }

            modelSocket.localPosition = Vector3.zero;
            modelSocket.localRotation = Quaternion.identity;
            modelSocket.localScale = Vector3.one;

            Transform oldCanvas = root.transform.Find(TimerCanvasName);

            if (oldCanvas != null)
            {
                UnityEngine.Object.DestroyImmediate(oldCanvas.gameObject);
            }

            TMP_FontAsset font = LoadChineseFont();
            GameObject canvasObject = new GameObject(
                TimerCanvasName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(CanvasGroup)
            );
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(root.transform, false);

            RectTransform canvasRect =
                canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(190f, 76f);
            canvasRect.localPosition = new Vector3(0f, 0f, -0.006f);
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.001f;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 18;

            CanvasScaler scaler =
                canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.referencePixelsPerUnit = 100f;
            scaler.dynamicPixelsPerUnit = 12f;

            CanvasGroup group =
                canvasObject.GetComponent<CanvasGroup>();
            group.alpha = 0.92f;
            group.interactable = false;
            group.blocksRaycasts = false;

            RectTransform background = CreateRect(
                "TimerBackground",
                canvasRect,
                new Vector2(190f, 76f),
                Vector2.zero
            );
            InterviewCapsuleGraphic backgroundGraphic =
                background.gameObject.AddComponent<InterviewCapsuleGraphic>();
            backgroundGraphic.color = new Color32(47, 65, 76, 244);
            backgroundGraphic.raycastTarget = false;

            Shadow shadow = background.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
            shadow.effectDistance = new Vector2(3f, -3f);

            Outline outline = background.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color32(166, 203, 212, 126);
            outline.effectDistance = new Vector2(1.4f, -1.4f);

            CreateLine(
                "AccentLine",
                background,
                new Vector2(112f, 2f),
                new Vector2(18f, 19f),
                new Color32(91, 190, 191, 205)
            );

            RectTransform indicatorRect = CreateRect(
                "StateIndicator",
                background,
                new Vector2(9f, 9f),
                new Vector2(-73f, 28f)
            );
            InterviewCapsuleGraphic indicator =
                indicatorRect.gameObject.AddComponent<InterviewCapsuleGraphic>();
            indicator.color = new Color32(126, 170, 181, 255);
            indicator.raycastTarget = false;

            TMP_Text stateText = CreateText(
                "TimerStateText",
                background,
                font,
                "准备就绪",
                14f,
                FontStyles.Normal,
                new Color32(198, 216, 223, 255),
                new Vector2(132f, 22f),
                new Vector2(8f, 28f),
                TextAlignmentOptions.MidlineLeft
            );
            stateText.enableWordWrapping = false;

            TMP_Text timeText = CreateText(
                "TimerValueText",
                background,
                font,
                "00:00",
                44f,
                FontStyles.Bold,
                new Color32(242, 248, 250, 255),
                new Vector2(172f, 47f),
                new Vector2(0f, -12f),
                TextAlignmentOptions.Center
            );
            timeText.enableAutoSizing = true;
            timeText.fontSizeMin = 29f;
            timeText.fontSizeMax = 44f;
            timeText.characterSpacing = 4f;

            SetLayerRecursively(canvasObject, 5);

            InterviewDeskTimer timer =
                root.GetComponent<InterviewDeskTimer>();

            if (timer == null)
            {
                timer = root.AddComponent<InterviewDeskTimer>();
            }

            timer.interviewManager = interviewManager;
            timer.timeText = timeText;
            timer.stateText = stateText;
            timer.timerCanvasGroup = group;
            timer.stateIndicator = indicator;
            timer.hideWhenFinished = true;

            return timer;
        }

        private static void ValidateOrThrow(Scene scene)
        {
            QuestionSpeechManager speechManager =
                UnityEngine.Object.FindObjectOfType<QuestionSpeechManager>(true);
            GameObject root = FindRoot(scene, TimerRootName);
            InterviewDeskTimer timer =
                root != null
                    ? root.GetComponent<InterviewDeskTimer>()
                    : null;

            bool voiceValid =
                speechManager != null
                && speechManager.enableSpeech
                && speechManager.useNeuralVoice
                && speechManager.voice == "zh-CN-YunyangNeural"
                && speechManager.rate == "-8%"
                && speechManager.pitch == "-4Hz";

            bool timerValid =
                root != null
                && timer != null
                && timer.interviewManager != null
                && timer.timeText != null
                && timer.stateText != null
                && timer.timerCanvasGroup != null
                && timer.stateIndicator != null
                && root.transform.Find(ModelSocketName) != null
                && root.transform.Find(TimerCanvasName) != null
                && Vector3.Distance(
                    root.transform.position,
                    TimerWorldPosition
                ) < 0.001f;

            if (!voiceValid || !timerValid)
            {
                throw new InvalidOperationException(
                    "稳重男声或桌面计时器验证失败："
                    + $"voice={voiceValid}，timer={timerValid}。"
                );
            }
        }

        private static Scene OpenMainScene()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (!scene.IsValid() || scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single
                );
            }

            return scene;
        }

        private static GameObject FindRoot(
            Scene scene,
            string objectName
        )
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                {
                    return root;
                }
            }

            return null;
        }

        private static TMP_FontAsset LoadChineseFont()
        {
            string path = AssetDatabase.GUIDToAssetPath(
                ChineseFontGuid
            );
            TMP_FontAsset font =
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

            if (font == null)
            {
                throw new InvalidOperationException(
                    "找不到支持中文的 TMP 字体资源。"
                );
            }

            return font;
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

        private static TMP_Text CreateText(
            string name,
            Transform parent,
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
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void CreateLine(
            string name,
            Transform parent,
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

        private static void SetLayerRecursively(
            GameObject root,
            int layer
        )
        {
            root.layer = layer;

            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
#endif
