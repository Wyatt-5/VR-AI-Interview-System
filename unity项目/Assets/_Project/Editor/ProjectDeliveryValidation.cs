#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRInterview.EditorTools
{
    public static class ProjectDeliveryValidation
    {
        private const string MainScenePath =
            "Assets/Scenes/MainScene_AIInterview_VoiceTest_v1.unity";

        private const string ControllerPath =
            "Assets/_Project/Models/Character/MetaHumanInterviewer/"
            + "Interviewer.controller";

        private static readonly string[] RequiredAnimationPaths =
        {
            "Assets/_Project/Models/Character/MetaHumanInterviewer/"
                + "Animations/standby_Seated.anim",
            "Assets/_Project/Models/Character/MetaHumanInterviewer/"
                + "Animations/talking_Seated.anim",
            "Assets/_Project/Models/Character/MetaHumanInterviewer/"
                + "Animations/think_Seated.anim",
            "Assets/_Project/Models/Character/MetaHumanInterviewer/"
                + "Animations/vikinganition_Seated.anim",
        };

        [MenuItem("Tools/VR Interview/交付前完整检查")]
        public static void Validate()
        {
            Scene scene = EditorSceneManager.OpenScene(
                MainScenePath,
                OpenSceneMode.Single
            );

            ValidateBuildSettings();
            ValidateMissingScripts(scene);
            ValidateRequiredAnimations();

            VoiceAnimationIntegrationSetup.ValidateCurrentScene();
            VoiceAndDeskTimerSetup.Validate();
            ResultUiPackageIntegrationSetup.Validate();

            Debug.Log(
                "[Project Delivery Validation] PASS：主场景、Build Settings、"
                + "脚本引用、四个面试官动画、纯语音、桌面计时器和结果页均通过检查。"
            );
        }

        public static void ValidateFromCommandLine()
        {
            try
            {
                Validate();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void ValidateBuildSettings()
        {
            EditorBuildSettingsScene[] scenes =
                EditorBuildSettings.scenes;

            bool mainSceneEnabled = false;

            foreach (EditorBuildSettingsScene scene in scenes)
            {
                if (scene.enabled && scene.path == MainScenePath)
                {
                    mainSceneEnabled = true;
                    break;
                }
            }

            if (!mainSceneEnabled)
            {
                throw new InvalidOperationException(
                    "Build Settings 中未启用主场景：" + MainScenePath
                );
            }
        }

        private static void ValidateMissingScripts(Scene scene)
        {
            int missingScriptCount = 0;
            List<string> affectedObjects = new List<string>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (
                    Transform item in root.GetComponentsInChildren<Transform>(
                        true
                    )
                )
                {
                    int currentCount =
                        GameObjectUtility
                            .GetMonoBehavioursWithMissingScriptCount(
                                item.gameObject
                            );

                    if (currentCount > 0)
                    {
                        missingScriptCount += currentCount;
                        affectedObjects.Add(
                            GetHierarchyPath(item)
                                + $" ({currentCount})"
                        );
                    }
                }
            }

            if (missingScriptCount > 0)
            {
                throw new InvalidOperationException(
                    $"主场景中检测到 {missingScriptCount} 个 Missing Script："
                    + string.Join(", ", affectedObjects)
                );
            }
        }

        private static string GetHierarchyPath(Transform item)
        {
            string path = item.name;

            while (item.parent != null)
            {
                item = item.parent;
                path = item.name + "/" + path;
            }

            return path;
        }

        private static void ValidateRequiredAnimations()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    ControllerPath
                );

            if (controller == null)
            {
                throw new InvalidOperationException(
                    "找不到面试官 Animator Controller。"
                );
            }

            HashSet<Motion> controllerMotions = new HashSet<Motion>();

            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                CollectMotions(layer.stateMachine, controllerMotions);
            }

            foreach (string clipPath in RequiredAnimationPaths)
            {
                AnimationClip clip =
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

                if (clip == null)
                {
                    throw new InvalidOperationException(
                        "缺少面试官动画：" + clipPath
                    );
                }

                if (!controllerMotions.Contains(clip))
                {
                    throw new InvalidOperationException(
                        "动画未接入 Animator Controller：" + clipPath
                    );
                }
            }
        }

        private static void CollectMotions(
            AnimatorStateMachine stateMachine,
            HashSet<Motion> motions
        )
        {
            foreach (ChildAnimatorState childState in stateMachine.states)
            {
                CollectMotion(childState.state.motion, motions);
            }

            foreach (
                ChildAnimatorStateMachine childMachine
                in stateMachine.stateMachines
            )
            {
                CollectMotions(childMachine.stateMachine, motions);
            }
        }

        private static void CollectMotion(
            Motion motion,
            HashSet<Motion> motions
        )
        {
            if (motion == null || !motions.Add(motion))
            {
                return;
            }

            BlendTree blendTree = motion as BlendTree;

            if (blendTree == null)
            {
                return;
            }

            foreach (ChildMotion child in blendTree.children)
            {
                CollectMotion(child.motion, motions);
            }
        }
    }
}
#endif
