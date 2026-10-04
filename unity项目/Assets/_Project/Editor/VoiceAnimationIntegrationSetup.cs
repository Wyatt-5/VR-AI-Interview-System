#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRInterview.Characters;

namespace VRInterview.EditorTools
{
    public static class VoiceAnimationIntegrationSetup
    {
        private const string ScenePath =
            "Assets/Scenes/MainScene_AIInterview_VoiceTest_v1.unity";

        private const string CharacterPath =
            "Assets/_Project/Models/Character/MetaHumanInterviewer/BP_NewMetaHumanCharacter.fbx";

        private const string ControllerPath =
            "Assets/_Project/Models/Character/MetaHumanInterviewer/Interviewer.controller";

        [MenuItem("Tools/VR Interview/接入纯语音与面试官动画")]
        public static void Setup()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (scene.path != ScenePath)
            {
                if (scene.isDirty)
                {
                    throw new InvalidOperationException(
                        "当前场景有未保存修改，已停止自动切换场景。请先保存后再重试。"
                    );
                }

                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single
                );
            }

            GameObject interviewManagerObject =
                GameObject.Find("Manager/InterviewManager")
                ?? GameObject.Find("InterviewManager");

            InterviewManager interviewManager =
                interviewManagerObject != null
                    ? interviewManagerObject.GetComponent<InterviewManager>()
                    : UnityEngine.Object.FindObjectOfType<InterviewManager>(
                        true
                    );

            VoiceInterviewManager voiceManager =
                UnityEngine.Object.FindObjectOfType<VoiceInterviewManager>(
                    true
                );

            QuestionSpeechManager speechManager =
                UnityEngine.Object.FindObjectOfType<QuestionSpeechManager>(
                    true
                );

            if (
                interviewManager == null
                || voiceManager == null
                || speechManager == null
            )
            {
                throw new InvalidOperationException(
                    "主场景管理器查找失败："
                    + $"InterviewManager={interviewManager != null}，"
                    + $"VoiceInterviewManager={voiceManager != null}，"
                    + $"QuestionSpeechManager={speechManager != null}。"
                );
            }

            GameObject legacy =
                GameObject.Find("Model/interviewer")
                ?? GameObject.Find("Model/interviewer_Legacy_Hidden");

            Transform parent = legacy != null
                ? legacy.transform.parent
                : GameObject.Find("Model")?.transform;

            if (parent == null)
            {
                throw new InvalidOperationException(
                    "主场景中找不到 Model 节点。"
                );
            }

            GameObject interviewer =
                GameObject.Find("Model/AI_Interviewer");

            if (interviewer == null)
            {
                GameObject characterPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        CharacterPath
                    );

                if (characterPrefab == null)
                {
                    throw new InvalidOperationException(
                        "找不到面试官模型：" + CharacterPath
                    );
                }

                interviewer = PrefabUtility.InstantiatePrefab(
                    characterPrefab,
                    parent
                ) as GameObject;

                if (interviewer == null)
                {
                    throw new InvalidOperationException(
                        "面试官模型实例化失败。"
                    );
                }

                interviewer.name = "AI_Interviewer";
                interviewer.transform.SetSiblingIndex(
                    legacy != null
                        ? legacy.transform.GetSiblingIndex() + 1
                        : parent.childCount - 1
                );

            }

            // 新 FBX 的正面轴与旧角色相反。每次接入都以旧角色为定位基准，
            // 再绕 Y 轴翻转 180 度，避免重复执行时继续累加旋转。
            AlignCharacterToLegacy(interviewer, legacy);

            RuntimeAnimatorController runtimeController =
                AssetDatabase.LoadAssetAtPath<
                    RuntimeAnimatorController
                >(ControllerPath);

            if (runtimeController == null)
            {
                throw new InvalidOperationException(
                    "找不到动画控制器：" + ControllerPath
                );
            }

            Animator animator =
                interviewer.GetComponentInChildren<Animator>(true);

            if (animator == null)
            {
                animator = interviewer.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = runtimeController;
            animator.applyRootMotion = false;

            InterviewerAnimationController animationController =
                animator.GetComponent<InterviewerAnimationController>();

            if (animationController == null)
            {
                animationController = animator.gameObject.AddComponent<
                    InterviewerAnimationController
                >();
            }

            if (legacy != null)
            {
                legacy.name = "interviewer_Legacy_Hidden";

                foreach (
                    Renderer renderer
                    in legacy.GetComponentsInChildren<Renderer>(true)
                )
                {
                    renderer.enabled = false;
                }

                Animator legacyAnimator =
                    legacy.GetComponentInChildren<Animator>(true);

                if (legacyAnimator != null)
                {
                    legacyAnimator.enabled = false;
                }

                EditorUtility.SetDirty(legacy);
            }

            GameObject sceneTelevision =
                FindSceneGameObject("television")
                ?? FindSceneGameObject(
                    "television_Obstruction_Hidden"
                );

            // 电视是面试房间原有的环境模型，不属于问题 UI。
            // 结果页的遮挡应通过隐藏 InterviewTestCanvas 处理，
            // 不能再关闭任何房间模型。
            if (sceneTelevision != null)
            {
                sceneTelevision.name = "television";
                sceneTelevision.SetActive(true);

                foreach (
                    Renderer renderer
                    in sceneTelevision.GetComponentsInChildren<
                        Renderer
                    >(true)
                )
                {
                    renderer.enabled = true;
                    EditorUtility.SetDirty(renderer);
                }

                EditorUtility.SetDirty(sceneTelevision);
            }

            GameObject interviewUIRoot =
                FindSceneGameObject("InterviewTestCanvas");

            if (interviewUIRoot == null)
            {
                throw new InvalidOperationException(
                    "主场景中找不到 InterviewTestCanvas。"
                );
            }

            ConfigureInterviewQuestionUi(
                interviewUIRoot,
                interviewManager.questionText,
                interviewManager.statusText
            );

            interviewManager.interviewUIRoot = interviewUIRoot;

            interviewManager.interviewerAnimationController =
                animationController;
            speechManager.animationController = animationController;

            voiceManager.interviewManager = interviewManager;
            voiceManager.questionSpeechManager = speechManager;
            voiceManager.interviewerAnimationController =
                animationController;
            voiceManager.handsFreeMode = true;
            voiceManager.automaticRecordingDelay = 0.35f;
            voiceManager.voiceActivityThreshold = 0.012f;
            voiceManager.silenceToFinishSeconds = 1.2f;
            voiceManager.initialSilenceTimeoutSeconds = 8f;
            voiceManager.maxAutomaticRetries = 1;
            voiceManager.automaticSubmitDelay = 0.35f;

            EditorUtility.SetDirty(interviewer);
            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(animationController);
            EditorUtility.SetDirty(interviewManager);
            EditorUtility.SetDirty(speechManager);
            EditorUtility.SetDirty(voiceManager);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[VR Interview Setup] 纯语音面试与四段面试官动画已接入主场景。"
            );

            ValidateCurrentScene();
        }

        [MenuItem("Tools/VR Interview/验证纯语音与动画接入")]
        public static void ValidateCurrentScene()
        {
            InterviewManager interviewManager =
                UnityEngine.Object.FindObjectOfType<InterviewManager>(true);
            VoiceInterviewManager voiceManager =
                UnityEngine.Object.FindObjectOfType<VoiceInterviewManager>(
                    true
                );
            QuestionSpeechManager speechManager =
                UnityEngine.Object.FindObjectOfType<QuestionSpeechManager>(
                    true
                );
            InterviewerAnimationController animationController =
                UnityEngine.Object.FindObjectOfType<
                    InterviewerAnimationController
                >(true);
            GameObject interviewer =
                FindSceneGameObject("AI_Interviewer");
            GameObject legacy =
                FindSceneGameObject("interviewer_Legacy_Hidden");
            GameObject sceneTelevision =
                FindSceneGameObject("television")
                ?? FindSceneGameObject(
                    "television_Obstruction_Hidden"
                );
            GameObject interviewUIRoot =
                FindSceneGameObject("InterviewTestCanvas");

            bool interviewerFacesCandidate =
                interviewer != null
                && (
                    legacy == null
                    || Quaternion.Angle(
                        interviewer.transform.localRotation,
                        legacy.transform.localRotation
                    ) >= 170f
                );

            bool televisionVisible =
                sceneTelevision != null
                && sceneTelevision.activeInHierarchy
                && AreRenderersVisible(sceneTelevision);

            bool resultUiSeparated =
                interviewManager != null
                && interviewUIRoot != null
                && interviewManager.interviewUIRoot
                    == interviewUIRoot
                && interviewManager.resultUI != null
                && !interviewManager.resultUI.transform.IsChildOf(
                    interviewUIRoot.transform
                );

            TMP_Text questionText =
                interviewManager != null
                    ? interviewManager.questionText
                    : null;

            bool questionLayoutValid =
                questionText != null
                && questionText.enableAutoSizing
                && questionText.enableWordWrapping
                && questionText.overflowMode
                    == TextOverflowModes.Ellipsis
                && questionText.rectTransform.rect.width >= 800f
                && questionText.rectTransform.rect.height >= 200f;

            bool valid =
                interviewManager != null
                && voiceManager != null
                && speechManager != null
                && animationController != null
                && voiceManager.handsFreeMode
                && voiceManager.interviewManager == interviewManager
                && voiceManager.questionSpeechManager == speechManager
                && voiceManager.interviewerAnimationController
                    == animationController
                && interviewManager.interviewerAnimationController
                    == animationController
                && speechManager.animationController
                    == animationController
                && animationController.GetComponent<Animator>()
                    ?.runtimeAnimatorController != null
                && interviewerFacesCandidate
                && televisionVisible
                && resultUiSeparated
                && questionLayoutValid;

            if (!valid)
            {
                throw new InvalidOperationException(
                    "纯语音、动画或面试 UI 仍有引用未绑定。"
                    + $" InterviewManager={interviewManager != null};"
                    + $" VoiceManager={voiceManager != null};"
                    + $" SpeechManager={speechManager != null};"
                    + $" AnimationController={animationController != null};"
                    + $" HandsFree={voiceManager != null && voiceManager.handsFreeMode};"
                    + $" AnimatorAssigned={animationController != null && animationController.GetComponent<Animator>()?.runtimeAnimatorController != null};"
                    + $" FacesCandidate={interviewerFacesCandidate};"
                    + $" TelevisionVisible={televisionVisible};"
                    + $" ResultUiSeparated={resultUiSeparated};"
                    + $" QuestionLayout={questionLayoutValid}."
                );
            }

            Debug.Log(
                "[VR Interview Validation] PASS：纯语音与动画引用完整；"
                + "面试官已朝向候选人；问题 UI 会在结果阶段整体隐藏；"
                + "问题图片已恢复原始比例；房间电视与渲染器保持可见；"
                + "问题文本已启用自动缩放、换行和溢出保护。"
            );
        }

        [MenuItem("Tools/VR Interview/打开主场景并运行")]
        public static void OpenAndPlay()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (scene.path != ScenePath)
            {
                if (scene.isDirty)
                {
                    throw new InvalidOperationException(
                        "当前场景有未保存修改，请先保存后再运行主场景。"
                    );
                }

                EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single
                );
            }

            EditorApplication.isPlaying = true;
        }

        private static void AlignCharacterToLegacy(
            GameObject interviewer,
            GameObject legacy
        )
        {
            if (legacy == null)
            {
                interviewer.transform.localPosition = Vector3.zero;
                interviewer.transform.localRotation =
                    Quaternion.Euler(0f, 180f, 0f);
                interviewer.transform.localScale = Vector3.one;
                return;
            }

            interviewer.transform.localPosition =
                legacy.transform.localPosition;
            interviewer.transform.localRotation =
                legacy.transform.localRotation
                * Quaternion.Euler(0f, 180f, 0f);
            interviewer.transform.localScale = Vector3.one;

            if (
                !TryGetRendererBounds(legacy, out Bounds legacyBounds)
                || !TryGetRendererBounds(
                    interviewer,
                    out Bounds interviewerBounds
                )
                || interviewerBounds.size.y <= 0.001f
            )
            {
                interviewer.transform.localScale =
                    legacy.transform.localScale;
                return;
            }

            float scale = Mathf.Clamp(
                legacyBounds.size.y / interviewerBounds.size.y,
                0.01f,
                100f
            );
            interviewer.transform.localScale = Vector3.one * scale;

            TryGetRendererBounds(
                interviewer,
                out interviewerBounds
            );

            Vector3 offset = new Vector3(
                legacyBounds.center.x - interviewerBounds.center.x,
                legacyBounds.min.y - interviewerBounds.min.y,
                legacyBounds.center.z - interviewerBounds.center.z
            );
            interviewer.transform.position += offset;
        }

        private static void ConfigureInterviewQuestionUi(
            GameObject interviewUIRoot,
            TMP_Text questionText,
            TMP_Text statusText
        )
        {
            if (questionText == null || statusText == null)
            {
                throw new InvalidOperationException(
                    "问题文字或状态文字引用为空。"
                );
            }

            Transform backgroundTransform =
                interviewUIRoot.transform.Find("BackGround");

            if (backgroundTransform == null)
            {
                throw new InvalidOperationException(
                    "InterviewTestCanvas 下找不到 BackGround。"
                );
            }

            RectTransform backgroundRect =
                backgroundTransform as RectTransform;

            if (backgroundRect == null)
            {
                throw new InvalidOperationException(
                    "问题界面 BackGround 不是 RectTransform。"
                );
            }

            backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            // 这张背景图在原场景中依靠 100 x 100 的 RectTransform
            // 与非均匀缩放保持既定视觉比例。不要直接拉伸图片尺寸。
            backgroundRect.anchoredPosition = new Vector2(0f, -27f);
            backgroundRect.sizeDelta = new Vector2(100f, 100f);
            backgroundRect.localScale = new Vector3(
                10.17f,
                5.1705174f,
                5.157361f
            );

            ConfigureTextBlock(
                questionText,
                new Vector2(840f, 210f),
                new Vector2(0f, 115f),
                20f,
                34f,
                new Vector4(36f, 22f, 36f, 22f),
                4f
            );

            ConfigureTextBlock(
                statusText,
                new Vector2(820f, 64f),
                new Vector2(0f, -72f),
                18f,
                30f,
                new Vector4(24f, 8f, 24f, 8f),
                0f
            );

            EditorUtility.SetDirty(backgroundRect);
            EditorUtility.SetDirty(questionText);
            EditorUtility.SetDirty(statusText);
        }

        private static void ConfigureTextBlock(
            TMP_Text text,
            Vector2 size,
            Vector2 position,
            float minimumFontSize,
            float maximumFontSize,
            Vector4 margin,
            float lineSpacing
        )
        {
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;

            text.enableAutoSizing = true;
            text.fontSizeMin = minimumFontSize;
            text.fontSizeMax = maximumFontSize;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = margin;
            text.lineSpacing = lineSpacing;

            EditorUtility.SetDirty(rect);
        }

        private static bool AreRenderersVisible(
            GameObject target
        )
        {
            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                return false;
            }

            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled)
                {
                    return false;
                }
            }

            return true;
        }

        private static GameObject FindSceneGameObject(
            string objectName
        )
        {
            foreach (
                GameObject candidate
                in Resources.FindObjectsOfTypeAll<GameObject>()
            )
            {
                if (
                    candidate.name == objectName
                    && candidate.scene.IsValid()
                    && candidate.scene.isLoaded
                    && !EditorUtility.IsPersistent(candidate)
                )
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool TryGetRendererBounds(
            GameObject target,
            out Bounds bounds
        )
        {
            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);

            bounds = default;
            bool found = false;

            foreach (Renderer renderer in renderers)
            {
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }
    }
}
#endif
