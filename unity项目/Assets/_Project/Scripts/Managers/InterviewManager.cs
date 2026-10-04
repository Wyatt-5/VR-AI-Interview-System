using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using VRInterview.Characters;

[System.Serializable]
public class NextQuestionRequest
{
    public int question_number;
    public string target_position;
    public string[] previous_questions;
    public string[] previous_answers;
}

[System.Serializable]
public class NextQuestionResponse
{
    public string reaction;
    public string question;
    public string speech_text;
    public bool is_follow_up;
}

[System.Serializable]
public class InterviewScoreRequest
{
    public string target_position;
    public string[] questions;
    public string[] answers;
}

[System.Serializable]
public class InterviewScoreResponse
{
    public int overall_score;
    public int language_score;
    public int reaction_score;
    public int professional_score;
    public int demeanor_score;
    public string highlights;
    public string suggestions;
}

public enum InterviewState
{
    WaitingForPlayer,
    OpeningSpeech,
    AskingQuestion,
    ListeningAnswer,
    AnalyzingAnswer,
    Finished
}

public enum InterviewRunMode
{
    OnlineAI,
    OfflinePreset,
    AutoFallback
}

public class InterviewManager : MonoBehaviour
{
    public event System.Action StartPromptCompleted;
    public event System.Action AnswerListeningStarted;
    public event System.Action AnswerAnalysisStarted;
    public event System.Action InterviewStarted;
    public event System.Action InterviewReset;
    public event System.Action InterviewFinished;

    public InterviewState CurrentState => state;

    [Header("通用文字界面")]
    [Tooltip("问题阶段的整套界面根节点。面试结束时会整体隐藏，避免遮挡结果页面。")]
    public GameObject interviewUIRoot;
    public TMP_Text questionText;
    public TMP_Text statusText;

    [Header("岗位选择界面")]
    public TMP_InputField targetPositionInput;
    public GameObject startInterviewButton;

    [Header("回答界面")]
    public TMP_InputField answerInput;
    public GameObject submitButton;

    [Header("结果页面")]
    public GameObject resultUI;
    public TMP_Text overallScoreText;
    public TMP_Text languageScoreText;
    public TMP_Text reactionScoreText;
    public TMP_Text professionalScoreText;
    public TMP_Text demeanorScoreText;
    public TMP_Text highlightsText;
    public TMP_Text suggestionsText;

    [Header("运行模式")]
    [Tooltip("OnlineAI：必须连接后端；OfflinePreset：完全离线；AutoFallback：优先AI，失败后自动切换离线模式。")]
    public InterviewRunMode runMode = InterviewRunMode.AutoFallback;

    [Header("面试设置")]
    public int maxQuestionCount = 4;

    [Header("Python后端地址")]
    public string nextQuestionUrl = "http://127.0.0.1:8000/next-question";
    public string scoreUrl = "http://127.0.0.1:8000/score";

    [Header("后端请求设置")]
    [Range(5, 60)]
    [Tooltip("生成下一道问题的最长等待时间。")]
    public int nextQuestionTimeoutSeconds = 25;

    [Range(15, 180)]
    [Tooltip(
        "生成详细面试评分的最长等待时间。" +
        "详细亮点和建议比普通出题耗时更长。"
    )]
    public int scoreTimeoutSeconds = 90;

    [Range(0, 2)]
    [Tooltip("评分请求失败后自动重试的次数。")]
    public int scoreRetryCount = 1;

    [Range(0.2f, 5f)]
    [Tooltip("两次评分请求之间的等待时间。")]
    public float scoreRetryDelaySeconds = 1f;

    [Tooltip(
        "AI评分最终失败时，是否自动生成本地备用评分，" +
        "避免结果页面只显示错误提示。"
    )]
    public bool useLocalScoreWhenOnlineScoreFails = true;

    [Header("面试官语音")]
    [Tooltip("拖入挂载了 QuestionSpeechManager 的对象。为空时只显示文字，不播放语音。")]
    public QuestionSpeechManager questionSpeechManager;

    [Tooltip("控制面试官的开场、思考、询问和待机动画。")]
    public InterviewerAnimationController interviewerAnimationController;

    [Tooltip("是否自动朗读AI面试官每一轮的完整回应。")]
    public bool speakInterviewQuestions = true;

    [Header("岗位页面语音")]
    [Tooltip("进入岗位输入页面时，是否主动询问用户想面试的岗位。")]
    public bool speakStartPrompt = true;

    [TextArea(2, 4)]
    public string startPromptText =
        "你好，请问你想面试什么岗位？你可以输入岗位名称，也可以使用语音输入。";

    [Header("AI自然对话")]
    [Tooltip(
        "在线模式下，面试官的回应和问题由AI一起生成，" +
        "只进行一次语音合成，避免固定过渡词和问题分开等待。"
    )]
    public bool showReactionInQuestionPanel = true;

    [Tooltip(
        "AI和语音生成期间显示的提示。"
    )]
    public string interviewerThinkingText =
        "面试官正在思考你的回答……";

    [Header("离线模式自然过渡")]
    [Tooltip(
        "开启后，离线模式会根据上一段回答的长度和关键词，" +
        "选择不同的中性过渡语。"
    )]
    public bool useContextAwareOfflineTransitions = true;

    [Tooltip("上一段回答过短时使用的过渡语。")]
    [TextArea(2, 3)]
    public string offlineShortAnswerReaction =
        "我明白了，不过这段回答比较简短。接下来我想从另一个角度继续了解。";

    [Tooltip("上一段回答包含数字或量化结果时使用的过渡语。")]
    [TextArea(2, 3)]
    public string offlineNumericReaction =
        "谢谢，你提供了一些较具体的信息。接下来我想了解另一个方面。";

    [Tooltip("上一段回答提到问题解决过程时使用的过渡语。")]
    [TextArea(2, 3)]
    public string offlineProblemSolvingReaction =
        "明白了，你提到了分析和解决问题的过程。我们继续下面的问题。";

    [Tooltip("上一段回答提到团队协作时使用的过渡语。")]
    [TextArea(2, 3)]
    public string offlineTeamworkReaction =
        "好的，你提到了团队协作和沟通。接下来换一个角度继续了解。";

    [Tooltip("没有匹配到特殊情况时，按顺序循环使用这些过渡语。")]
    public string[] offlineTransitionPhrases =
    {
        "明白了，谢谢你的回答。下面我们继续。",
        "好的，我了解了。接下来我想了解另一个方面。",
        "谢谢你的分享。下面请继续回答这个问题。"
    };

    [Tooltip("显示结果页面时，是否播报结束提示。")]
    public bool speakResultAnnouncement = true;

    [Tooltip("结果播报中是否读出综合评分。")]
    public bool includeOverallScoreInResultSpeech = true;

    [TextArea(2, 4)]
    public string resultAnnouncementText =
        "本次模拟面试已经结束，这是你的面试结果。请查看各项评分、表现亮点和改进建议。";

    private InterviewState state;
    private string targetPosition;

    private readonly List<string> questions = new List<string>();
    private readonly List<string> answers = new List<string>();
    private readonly List<float> answerDurations = new List<float>();

    private InterviewScoreResponse scoreResponse;
    private bool offlineFallbackActive;
    private float questionShownAt;

    private string pendingReaction = "";
    private string pendingSpeechText = "";
    private bool pendingIsFollowUp;

    private readonly string[] structureKeywords =
    {
        "首先", "其次", "最后", "第一", "第二", "第三",
        "因为", "所以", "例如", "具体", "结果", "总结",
        "目标", "过程", "问题", "解决", "反思"
    };

    private readonly string[] generalProfessionalKeywords =
    {
        "项目", "需求", "用户", "数据", "分析", "方案", "计划",
        "团队", "沟通", "协作", "负责", "优化", "实现", "测试",
        "复盘", "效率", "结果", "目标", "问题", "解决", "经验",
        "学习", "风险", "质量", "流程", "反馈"
    };

    void Awake()
    {
        if (interviewUIRoot == null && questionText != null)
        {
            Canvas questionCanvas =
                questionText.GetComponentInParent<Canvas>(true);

            if (questionCanvas != null)
            {
                interviewUIRoot = questionCanvas.gameObject;
            }
        }

        if (interviewerAnimationController == null)
        {
            interviewerAnimationController = FindObjectOfType<
                InterviewerAnimationController
            >();
        }
    }

    void Start()
    {
        ResetToStartScreen();
    }

    public bool IsOfflineModeActive()
    {
        return runMode == InterviewRunMode.OfflinePreset || offlineFallbackActive;
    }

    public bool ShouldCheckBackend()
    {
        return runMode != InterviewRunMode.OfflinePreset;
    }

    public void UseOnlineMode()
    {
        runMode = InterviewRunMode.OnlineAI;
        ResetToStartScreen();
    }

    public void UseOfflineMode()
    {
        runMode = InterviewRunMode.OfflinePreset;
        ResetToStartScreen();
    }

    public void UseAutoFallbackMode()
    {
        runMode = InterviewRunMode.AutoFallback;
        ResetToStartScreen();
    }

    void ResetToStartScreen()
    {
        CancelInvoke();
        StopAllCoroutines();

        if (questionSpeechManager != null)
        {
            questionSpeechManager.StopSpeaking();
        }

        targetPosition = "";
        questions.Clear();
        answers.Clear();
        answerDurations.Clear();
        scoreResponse = null;
        offlineFallbackActive = false;
        questionShownAt = 0f;
        pendingReaction = "";
        pendingSpeechText = "";
        pendingIsFollowUp = false;

        if (interviewUIRoot != null)
        {
            interviewUIRoot.SetActive(true);
        }

        resultUI.SetActive(false);

        questionText.gameObject.SetActive(true);
        statusText.gameObject.SetActive(true);

        targetPositionInput.gameObject.SetActive(true);
        startInterviewButton.SetActive(true);

        answerInput.gameObject.SetActive(false);
        submitButton.SetActive(false);

        targetPositionInput.text = "";
        answerInput.text = "";

        questionText.text = "欢迎使用模拟面试系统";
        statusText.text = GetStartScreenStatus();

        overallScoreText.text = "0";
        languageScoreText.text = "0";
        reactionScoreText.text = "0";
        professionalScoreText.text = "0";
        demeanorScoreText.text = "0";

        highlightsText.text = "";
        suggestionsText.text = "";

        state = InterviewState.WaitingForPlayer;
        InterviewReset?.Invoke();
        targetPositionInput.ActivateInputField();

        interviewerAnimationController?.PlayOpening();
        SpeakStartScreenPrompt();

        Debug.Log("已返回岗位选择页面。当前模式：" + GetModeName());
    }

    string GetStartScreenStatus()
    {
        // 运行模式只决定题目与评分来源，不在面试界面中额外展示。
        return "请输入目标岗位，然后点击“开始面试”。";
    }

    string GetModeName()
    {
        if (IsOfflineModeActive())
        {
            return offlineFallbackActive ? "离线备用模式" : "离线预设模式";
        }

        if (runMode == InterviewRunMode.AutoFallback)
        {
            return "自动模式（当前使用AI）";
        }

        return "在线AI模式";
    }

    public void RestartInterview()
    {
        ResetToStartScreen();
    }

    public void StartInterview()
    {
        if (state != InterviewState.WaitingForPlayer)
        {
            return;
        }

        string position = targetPositionInput.text.Trim();

        if (string.IsNullOrEmpty(position))
        {
            statusText.text = "目标岗位不能为空，请先输入岗位名称。";
            targetPositionInput.ActivateInputField();
            return;
        }

        if (questionSpeechManager != null)
        {
            questionSpeechManager.StopSpeaking();
        }

        targetPosition = position;
        questions.Clear();
        answers.Clear();
        answerDurations.Clear();
        scoreResponse = null;
        offlineFallbackActive = false;

        targetPositionInput.gameObject.SetActive(false);
        startInterviewButton.SetActive(false);

        interviewerAnimationController?.PlayThinking();
        SetState(InterviewState.OpeningSpeech);
        InterviewStarted?.Invoke();
    }

    void SetState(InterviewState newState)
    {
        state = newState;

        switch (state)
        {
            case InterviewState.WaitingForPlayer:
                questionText.text = "欢迎使用模拟面试系统";
                statusText.text = GetStartScreenStatus();
                break;

            case InterviewState.OpeningSpeech:
                questionText.text = "面试官正在准备本次面试……";
                statusText.text = "请稍候。";

                answerInput.gameObject.SetActive(false);
                submitButton.SetActive(false);

                Invoke(nameof(BeginFirstQuestion), 0.2f);
                break;

            case InterviewState.AskingQuestion:
                ShowCurrentQuestion();
                break;

            case InterviewState.ListeningAnswer:
                break;

            case InterviewState.AnalyzingAnswer:
                statusText.text = "正在记录并分析你的回答……";
                break;

            case InterviewState.Finished:
                ShowInterviewResult();
                break;
        }
    }

    void BeginFirstQuestion()
    {
        if (IsOfflineModeActive())
        {
            UseNextOfflineQuestion();
        }
        else
        {
            StartCoroutine(RequestNextQuestionFromBackend());
        }
    }

    void UseNextOfflineQuestion()
    {
        interviewerAnimationController?.PlayThinking();
        answerInput.gameObject.SetActive(false);
        submitButton.SetActive(false);

        questionText.text = "面试官正在准备下一道问题……";
        statusText.text = "请稍候。";

        int nextQuestionNumber = questions.Count + 1;
        string offlineQuestion = GetOfflineQuestion(nextQuestionNumber);

        pendingIsFollowUp = false;

        if (nextQuestionNumber == 1)
        {
            pendingReaction =
                "你好，欢迎参加“"
                + targetPosition
                + "”岗位的模拟面试。"
                + "我们先从简单的问题开始。";
        }
        else
        {
            pendingReaction =
                GetOfflineTransitionReaction(
                    nextQuestionNumber
                );
        }

        pendingSpeechText = BuildSpeechText(
            pendingReaction,
            offlineQuestion
        );

        questions.Add(offlineQuestion);
        SetState(InterviewState.AskingQuestion);
    }

    string GetOfflineTransitionReaction(
        int nextQuestionNumber
    )
    {
        if (
            !useContextAwareOfflineTransitions
            || answers.Count == 0
        )
        {
            return GetRotatingOfflineTransition(
                nextQuestionNumber
            );
        }

        string lastAnswer =
            answers[answers.Count - 1].Trim();

        string compactAnswer =
            lastAnswer.Replace(" ", "")
                .Replace("\n", "")
                .Replace("\r", "");

        if (compactAnswer.Length < 18)
        {
            return string.IsNullOrWhiteSpace(
                offlineShortAnswerReaction
            )
                ? "我明白了，不过这段回答比较简短。接下来我想从另一个角度继续了解。"
                : offlineShortAnswerReaction.Trim();
        }

        if (ContainsNumericCharacter(lastAnswer))
        {
            return string.IsNullOrWhiteSpace(
                offlineNumericReaction
            )
                ? "谢谢，你提供了一些较具体的信息。接下来我想了解另一个方面。"
                : offlineNumericReaction.Trim();
        }

        if (
            ContainsAny(
                lastAnswer,
                new[]
                {
                    "困难", "问题", "分析", "解决",
                    "调试", "优化", "改进", "处理"
                }
            )
        )
        {
            return string.IsNullOrWhiteSpace(
                offlineProblemSolvingReaction
            )
                ? "明白了，你提到了分析和解决问题的过程。我们继续下面的问题。"
                : offlineProblemSolvingReaction.Trim();
        }

        if (
            ContainsAny(
                lastAnswer,
                new[]
                {
                    "团队", "沟通", "协作", "合作",
                    "分工", "成员", "同学", "同事"
                }
            )
        )
        {
            return string.IsNullOrWhiteSpace(
                offlineTeamworkReaction
            )
                ? "好的，你提到了团队协作和沟通。接下来换一个角度继续了解。"
                : offlineTeamworkReaction.Trim();
        }

        return GetRotatingOfflineTransition(
            nextQuestionNumber
        );
    }

    string GetRotatingOfflineTransition(
        int nextQuestionNumber
    )
    {
        if (
            offlineTransitionPhrases == null
            || offlineTransitionPhrases.Length == 0
        )
        {
            return "好的，我了解了。我们继续。";
        }

        int startIndex = Mathf.Max(
            0,
            nextQuestionNumber - 2
        );

        for (
            int offset = 0;
            offset < offlineTransitionPhrases.Length;
            offset++
        )
        {
            int index =
                (
                    startIndex + offset
                )
                % offlineTransitionPhrases.Length;

            string phrase =
                offlineTransitionPhrases[index];

            if (!string.IsNullOrWhiteSpace(phrase))
            {
                return phrase.Trim();
            }
        }

        return "好的，我了解了。我们继续。";
    }

    bool ContainsNumericCharacter(
        string text
    )
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (
            int index = 0;
            index < text.Length;
            index++
        )
        {
            if (char.IsDigit(text[index]))
            {
                return true;
            }
        }

        return false;
    }

    string GetOfflineQuestion(int questionNumber)
    {
        switch (questionNumber)
        {
            case 1:
                return "请用1至2分钟做一个自我介绍，并说明你为什么适合“" + targetPosition + "”岗位。";

            case 2:
                return GetSecondOfflineQuestion();

            case 3:
                return "请描述一次你在学习、项目或工作中遇到困难的经历。你是如何分析问题、采取行动并得到结果的？";

            case 4:
                return "你认为“" + targetPosition + "”岗位最重要的三项能力是什么？入职后你准备如何提升这些能力？";

            default:
                return "请结合一个具体经历，说明你能为“" + targetPosition + "”岗位带来什么价值。";
        }
    }

    string GetSecondOfflineQuestion()
    {
        string position = targetPosition.ToLower();

        if (ContainsAny(position, new[] { "数据", "算法", "开发", "程序", "软件", "计算机", "测试", "产品" }))
        {
            return "请介绍一个与“" + targetPosition + "”相关的项目。请说明你的职责、使用的技术、遇到的问题以及最终结果。";
        }

        if (ContainsAny(position, new[] { "运营", "市场", "销售", "新媒体", "策划" }))
        {
            return "请介绍一次你参与活动、运营或推广的经历。你负责什么，如何判断效果，最后取得了什么结果？";
        }

        if (ContainsAny(position, new[] { "教师", "教育", "辅导", "培训" }))
        {
            return "请介绍一次教学、辅导或组织学习活动的经历。你如何了解学习者需求并调整教学方法？";
        }

        if (ContainsAny(position, new[] { "设计", "视觉", "交互", "ui", "ux" }))
        {
            return "请介绍一个你参与的设计项目。你如何理解需求、形成方案，并根据反馈完成优化？";
        }

        return "请介绍一段与你申请岗位最相关的经历。请说明你的任务、具体行动和最终成果。";
    }

    IEnumerator RequestNextQuestionFromBackend()
    {
        interviewerAnimationController?.PlayThinking();
        answerInput.gameObject.SetActive(false);
        submitButton.SetActive(false);

        questionText.text = interviewerThinkingText;
        statusText.text = "正在生成自然回应和面试问题，请稍候。";

        int nextQuestionNumber = questions.Count + 1;

        NextQuestionRequest requestData = new NextQuestionRequest
        {
            question_number = nextQuestionNumber,
            target_position = targetPosition,
            previous_questions = questions.ToArray(),
            previous_answers = answers.ToArray()
        };

        string json = JsonUtility.ToJson(requestData);
        Debug.Log("发送动态出题请求：" + json);

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(nextQuestionUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = nextQuestionTimeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseJson = request.downloadHandler.text;
                Debug.Log("AI问题返回成功：" + responseJson);

                NextQuestionResponse response = JsonUtility.FromJson<NextQuestionResponse>(responseJson);

                if (
                    response != null
                    && !string.IsNullOrWhiteSpace(
                        response.question
                    )
                )
                {
                    string question =
                        response.question.Trim();

                    pendingReaction =
                        response.reaction == null
                            ? ""
                            : response.reaction.Trim();

                    pendingSpeechText =
                        string.IsNullOrWhiteSpace(
                            response.speech_text
                        )
                            ? BuildSpeechText(
                                pendingReaction,
                                question
                            )
                            : response.speech_text.Trim();

                    pendingIsFollowUp =
                        response.is_follow_up;

                    questions.Add(question);
                    SetState(
                        InterviewState.AskingQuestion
                    );
                    yield break;
                }

                Debug.LogWarning("后端没有返回有效问题。\n" + responseJson);
            }
            else
            {
                Debug.LogWarning("生成问题失败：" + request.error);
            }
        }

        if (runMode == InterviewRunMode.AutoFallback)
        {
            EnableOfflineFallback("后端暂时不可用，已启用本地题目作为备用。", true);
        }
        else
        {
            questionText.text = "暂时无法生成下一道问题。";
            statusText.text = "请稍后重试。";
        }
    }

    void EnableOfflineFallback(string message, bool continueWithQuestion)
    {
        offlineFallbackActive = true;
        Debug.LogWarning(message);

        statusText.text = continueWithQuestion
            ? "正在准备下一道问题，请稍候。"
            : "正在生成面试评分，请稍候。";

        if (continueWithQuestion)
        {
            Invoke(nameof(UseNextOfflineQuestion), 0.8f);
        }
        else
        {
            Invoke(nameof(GenerateOfflineScoreAndFinish), 0.8f);
        }
    }

    void ShowCurrentQuestion()
    {
        if (questions.Count == 0)
        {
            Debug.LogError(
                "当前没有可以显示的问题。"
            );
            return;
        }

        string currentQuestion =
            questions[questions.Count - 1];

        string questionLabel =
            pendingIsFollowUp
                ? "追问："
                : "第"
                    + questions.Count
                    + "题：";

        if (
            showReactionInQuestionPanel
            && !string.IsNullOrWhiteSpace(
                pendingReaction
            )
        )
        {
            questionText.text =
                pendingReaction
                + "\n\n"
                + questionLabel
                + currentQuestion;
        }
        else
        {
            questionText.text =
                questionLabel
                + currentQuestion;
        }

        answerInput.gameObject.SetActive(false);
        submitButton.SetActive(false);
        answerInput.text = "";

        state = InterviewState.AskingQuestion;

        if (
            speakInterviewQuestions
            && questionSpeechManager != null
        )
        {
            statusText.text =
                "请听完面试官的问题后作答。";

            string speechText =
                string.IsNullOrWhiteSpace(
                    pendingSpeechText
                )
                    ? BuildSpeechText(
                        pendingReaction,
                        currentQuestion
                    )
                    : pendingSpeechText;

            questionSpeechManager.Speak(
                speechText,
                BeginListeningForCurrentQuestion
            );
        }
        else
        {
            BeginListeningForCurrentQuestion();
        }
    }

    string BuildSpeechText(
        string reaction,
        string question
    )
    {
        string reactionText =
            string.IsNullOrWhiteSpace(reaction)
                ? ""
                : reaction.Trim();

        string questionTextValue =
            string.IsNullOrWhiteSpace(question)
                ? ""
                : question.Trim();

        if (
            !string.IsNullOrEmpty(reactionText)
            && !"。！？!?".Contains(
                reactionText[
                    reactionText.Length - 1
                ].ToString()
            )
        )
        {
            reactionText += "。";
        }

        return reactionText
            + questionTextValue;
    }

    void SpeakStartScreenPrompt()
    {
        if (
            !speakStartPrompt
            || questionSpeechManager == null
            || string.IsNullOrWhiteSpace(
                startPromptText
            )
        )
        {
            StartPromptCompleted?.Invoke();
            return;
        }

        questionSpeechManager.Speak(
            startPromptText.Trim(),
            () => StartPromptCompleted?.Invoke()
        );
    }

    void SpeakResultAnnouncement()
    {
        if (
            !speakResultAnnouncement
            || questionSpeechManager == null
            || scoreResponse == null
        )
        {
            return;
        }

        string speechText =
            string.IsNullOrWhiteSpace(
                resultAnnouncementText
            )
                ? "本次模拟面试已经结束，这是你的面试结果。"
                : resultAnnouncementText.Trim();

        if (includeOverallScoreInResultSpeech)
        {
            speechText =
                "本次模拟面试已经结束。"
                + "你的综合评分是"
                + scoreResponse.overall_score
                + "分。"
                + "这是你的面试结果，"
                + "请查看各项评分、表现亮点和改进建议。";
        }

        questionSpeechManager.Speak(
            speechText
        );
    }

    void BeginListeningForCurrentQuestion()
    {
        if (state != InterviewState.AskingQuestion)
        {
            return;
        }

        statusText.text = "请输入你的回答。";

        answerInput.gameObject.SetActive(true);
        submitButton.SetActive(true);

        answerInput.text = "";
        answerInput.interactable = true;
        answerInput.ActivateInputField();

        questionShownAt = Time.realtimeSinceStartup;
        state = InterviewState.ListeningAnswer;

        pendingReaction = "";
        pendingSpeechText = "";
        pendingIsFollowUp = false;

        interviewerAnimationController?.PlayStandby();
        AnswerListeningStarted?.Invoke();
    }

    public void SubmitAnswer()
    {
        if (state != InterviewState.ListeningAnswer)
        {
            return;
        }

        string answer = answerInput.text.Trim();

        if (string.IsNullOrEmpty(answer))
        {
            statusText.text = "回答不能为空，请先输入内容。";
            answerInput.ActivateInputField();
            return;
        }

        answers.Add(answer);
        answerDurations.Add(Mathf.Max(0f, Time.realtimeSinceStartup - questionShownAt));

        if (questionSpeechManager != null)
        {
            questionSpeechManager.StopSpeaking();
        }

        SetState(InterviewState.AnalyzingAnswer);
        answerInput.gameObject.SetActive(false);
        submitButton.SetActive(false);
        interviewerAnimationController?.PlayThinking();
        AnswerAnalysisStarted?.Invoke();

        if (answers.Count < maxQuestionCount)
        {
            if (IsOfflineModeActive())
            {
                Invoke(nameof(UseNextOfflineQuestion), 0.5f);
            }
            else
            {
                StartCoroutine(RequestNextQuestionFromBackend());
            }
        }
        else
        {
            if (IsOfflineModeActive())
            {
                GenerateOfflineScoreAndFinish();
            }
            else
            {
                StartCoroutine(RequestScoreFromBackend());
            }
        }
    }

    IEnumerator RequestScoreFromBackend()
    {
        questionText.text = "本次面试已经完成。";
        statusText.text = "DeepSeek正在生成详细面试评分，请稍候……";

        InterviewScoreRequest requestData = new InterviewScoreRequest
        {
            target_position = targetPosition,
            questions = questions.ToArray(),
            answers = answers.ToArray()
        };

        string json = JsonUtility.ToJson(requestData);
        Debug.Log("发送评分请求：" + json);

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        int totalAttempts =
            Mathf.Max(1, scoreRetryCount + 1);

        string lastErrorMessage = "";

        for (
            int attempt = 0;
            attempt < totalAttempts;
            attempt++
        )
        {
            if (attempt > 0)
            {
                statusText.text =
                    "第一次评分请求未完成，"
                    + "正在重新尝试生成详细评分……";

                yield return new WaitForSecondsRealtime(
                    scoreRetryDelaySeconds
                );
            }

            using (
                UnityWebRequest request =
                    new UnityWebRequest(
                        scoreUrl,
                        "POST"
                    )
            )
            {
                request.uploadHandler =
                    new UploadHandlerRaw(bodyRaw);

                request.downloadHandler =
                    new DownloadHandlerBuffer();

                request.SetRequestHeader(
                    "Content-Type",
                    "application/json"
                );

                request.timeout =
                    scoreTimeoutSeconds;

                Debug.Log(
                    "正在发送评分请求，"
                    + "尝试次数："
                    + (attempt + 1)
                    + "/"
                    + totalAttempts
                    + "，超时时间："
                    + scoreTimeoutSeconds
                    + "秒"
                );

                yield return request.SendWebRequest();

                if (
                    request.result
                    == UnityWebRequest.Result.Success
                )
                {
                    string responseJson =
                        request.downloadHandler.text;

                    Debug.Log(
                        "DeepSeek评分返回成功："
                        + responseJson
                    );

                    scoreResponse =
                        JsonUtility.FromJson<
                            InterviewScoreResponse
                        >(
                            responseJson
                        );

                    if (scoreResponse != null)
                    {
                        SetState(
                            InterviewState.Finished
                        );

                        yield break;
                    }

                    lastErrorMessage =
                        "后端返回的评分数据无法解析。";

                    Debug.LogWarning(
                        lastErrorMessage
                        + "\n"
                        + responseJson
                    );
                }
                else
                {
                    string responseBody =
                        request.downloadHandler != null
                            ? request.downloadHandler.text
                            : "";

                    lastErrorMessage =
                        request.error;

                    Debug.LogWarning(
                        "评分请求失败。"
                        + "\n尝试次数："
                        + (attempt + 1)
                        + "/"
                        + totalAttempts
                        + "\n错误："
                        + request.error
                        + "\nHTTP状态码："
                        + request.responseCode
                        + "\n后端返回："
                        + responseBody
                    );
                }
            }
        }

        if (
            runMode
                == InterviewRunMode.AutoFallback
            || useLocalScoreWhenOnlineScoreFails
        )
        {
            Debug.LogWarning(
                "AI评分最终失败，"
                + "已切换为本地备用评分。"
                + "\n最后错误："
                + lastErrorMessage
            );

            statusText.text =
                "AI评分暂时不可用，"
                + "正在生成本地备用评分……";

            GenerateOfflineScoreAndFinish();
        }
        else
        {
            statusText.text =
                "暂时无法生成面试评分，"
                + "请检查后端后重新进行面试。";
        }
    }

    void GenerateOfflineScoreAndFinish()
    {
        questionText.text = "本次面试已经完成。";
        statusText.text = "正在生成面试评分……";

        scoreResponse = BuildOfflineScore();
        Invoke(nameof(FinishOfflineScore), 0.7f);
    }

    void FinishOfflineScore()
    {
        SetState(InterviewState.Finished);
    }

    InterviewScoreResponse BuildOfflineScore()
    {
        string allAnswers = CombineAnswers();
        float averageLength = GetAverageAnswerLength();
        int substantialAnswers = CountSubstantialAnswers(35);
        int structureHits = CountKeywordHits(allAnswers, structureKeywords);
        int generalProfessionalHits = CountKeywordHits(allAnswers, generalProfessionalKeywords);
        int roleKeywordHits = CountKeywordHits(allAnswers, GetRoleKeywords());
        int numericEvidenceCount = CountNumericCharacters(allAnswers);
        int punctuationCount = CountPunctuation(allAnswers);

        int languageScore = 55;
        languageScore += Mathf.Min(18, Mathf.RoundToInt(averageLength / 6f));
        languageScore += Mathf.Min(12, structureHits * 2);
        languageScore += punctuationCount >= 8 ? 5 : 0;
        languageScore -= substantialAnswers < answers.Count ? 5 : 0;
        languageScore = Mathf.Clamp(languageScore, 55, 95);

        int reactionScore = 60;
        reactionScore += Mathf.RoundToInt(15f * answers.Count / Mathf.Max(1, maxQuestionCount));
        reactionScore += Mathf.Min(12, substantialAnswers * 3);
        reactionScore += GetResponseTimeBonus();
        reactionScore += GetAnswerVarietyBonus();
        reactionScore = Mathf.Clamp(reactionScore, 55, 93);

        int professionalScore = 50;
        professionalScore += Mathf.Min(24, generalProfessionalHits * 2);
        professionalScore += Mathf.Min(15, roleKeywordHits * 3);
        professionalScore += Mathf.Min(6, numericEvidenceCount * 2);
        professionalScore += Mathf.Min(8, substantialAnswers * 2);
        professionalScore = Mathf.Clamp(professionalScore, 50, 95);

        int demeanorScore = 68;
        demeanorScore += Mathf.RoundToInt(10f * answers.Count / Mathf.Max(1, maxQuestionCount));
        demeanorScore += Mathf.Min(8, substantialAnswers * 2);
        demeanorScore += structureHits >= 4 ? 4 : 0;
        demeanorScore = Mathf.Clamp(demeanorScore, 60, 90);

        int overallScore = Mathf.RoundToInt(
            languageScore * 0.30f +
            reactionScore * 0.20f +
            professionalScore * 0.35f +
            demeanorScore * 0.15f
        );

        return new InterviewScoreResponse
        {
            overall_score = overallScore,
            language_score = languageScore,
            reaction_score = reactionScore,
            professional_score = professionalScore,
            demeanor_score = demeanorScore,
            highlights = BuildOfflineHighlights(
                languageScore,
                reactionScore,
                professionalScore,
                substantialAnswers,
                structureHits,
                numericEvidenceCount
            ),
            suggestions = BuildOfflineSuggestions(
                languageScore,
                reactionScore,
                professionalScore,
                averageLength,
                structureHits,
                numericEvidenceCount
            )
        };
    }

    string BuildOfflineHighlights(
        int languageScore,
        int reactionScore,
        int professionalScore,
        int substantialAnswers,
        int structureHits,
        int numericEvidenceCount)
    {
        List<string> items = new List<string>();

        if (substantialAnswers >= Mathf.Max(1, maxQuestionCount - 1))
        {
            items.Add("回答较完整，能够持续完成整场面试。") ;
        }

        if (languageScore >= 80 || structureHits >= 5)
        {
            items.Add("表达具有一定层次，能够使用因果、步骤或总结性语句组织内容。") ;
        }

        if (professionalScore >= 80)
        {
            items.Add("回答中包含项目、任务、方法或结果等岗位相关信息。") ;
        }

        if (numericEvidenceCount > 0)
        {
            items.Add("能够使用数字或具体结果增强回答的可信度。") ;
        }

        if (reactionScore >= 80)
        {
            items.Add("各题均能给出有效回答，整体应答较稳定。") ;
        }

        if (items.Count == 0)
        {
            items.Add("已完成全部面试问题，并能够围绕题目给出基本回答。") ;
        }

        return JoinItems(items);
    }

    string BuildOfflineSuggestions(
        int languageScore,
        int reactionScore,
        int professionalScore,
        float averageLength,
        int structureHits,
        int numericEvidenceCount)
    {
        List<string> items = new List<string>();

        if (averageLength < 60f)
        {
            items.Add("回答偏简短，可补充背景、任务、行动和结果，避免只给结论。") ;
        }

        if (languageScore < 80 || structureHits < 4)
        {
            items.Add("建议使用“首先、其次、最后”或STAR结构，让表达更清楚。") ;
        }

        if (professionalScore < 80)
        {
            items.Add("可增加与目标岗位直接相关的技能、工具、项目职责和解决方法。") ;
        }

        if (numericEvidenceCount == 0)
        {
            items.Add("尽量加入可量化结果，例如效率提升、完成数量、准确率或用户反馈。") ;
        }

        if (reactionScore < 75)
        {
            items.Add("回答前可先快速确定核心观点，再用一个具体经历支撑观点。") ;
        }

        items.Add("本次仪态评分主要参考回答完整度与表达组织性，建议结合语音、表情和动作数据综合判断。") ;

        return JoinItems(items);
    }

    string JoinItems(List<string> items)
    {
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < items.Count; i++)
        {
            builder.Append(i + 1);
            builder.Append(". ");
            builder.Append(items[i]);

            if (i < items.Count - 1)
            {
                builder.Append("\n");
            }
        }

        return builder.ToString();
    }

    string CombineAnswers()
    {
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < answers.Count; i++)
        {
            builder.Append(answers[i]);
            builder.Append("\n");
        }

        return builder.ToString();
    }

    float GetAverageAnswerLength()
    {
        if (answers.Count == 0)
        {
            return 0f;
        }

        int totalLength = 0;

        for (int i = 0; i < answers.Count; i++)
        {
            totalLength += answers[i].Length;
        }

        return (float)totalLength / answers.Count;
    }

    int CountSubstantialAnswers(int minimumLength)
    {
        int count = 0;

        for (int i = 0; i < answers.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(answers[i]) && answers[i].Length >= minimumLength)
            {
                count++;
            }
        }

        return count;
    }

    int CountKeywordHits(string text, string[] keywords)
    {
        if (string.IsNullOrEmpty(text) || keywords == null)
        {
            return 0;
        }

        int count = 0;

        for (int i = 0; i < keywords.Length; i++)
        {
            if (!string.IsNullOrEmpty(keywords[i]) && text.Contains(keywords[i]))
            {
                count++;
            }
        }

        return count;
    }

    string[] GetRoleKeywords()
    {
        string position = targetPosition.ToLower();

        if (ContainsAny(position, new[] { "数据", "大数据", "算法", "人工智能", "ai", "分析" }))
        {
            return new[] { "python", "sql", "数据库", "模型", "算法", "清洗", "可视化", "特征", "准确率", "数据" };
        }

        if (ContainsAny(position, new[] { "开发", "程序", "软件", "测试", "前端", "后端", "unity", "计算机" }))
        {
            return new[] { "代码", "接口", "api", "调试", "测试", "性能", "版本", "git", "数据库", "框架", "unity" };
        }

        if (ContainsAny(position, new[] { "运营", "市场", "销售", "新媒体", "策划" }))
        {
            return new[] { "用户", "转化", "增长", "渠道", "内容", "活动", "传播", "复盘", "数据", "反馈" };
        }

        if (ContainsAny(position, new[] { "教师", "教育", "辅导", "培训" }))
        {
            return new[] { "学生", "课堂", "课程", "教学", "备课", "反馈", "目标", "评价", "互动", "学习" };
        }

        if (ContainsAny(position, new[] { "设计", "视觉", "交互", "ui", "ux" }))
        {
            return new[] { "用户", "需求", "交互", "视觉", "原型", "figma", "迭代", "反馈", "体验", "设计" };
        }

        if (ContainsAny(position, new[] { "管理", "行政", "人力", "hr" }))
        {
            return new[] { "沟通", "协调", "计划", "组织", "流程", "风险", "资源", "团队", "目标", "执行" };
        }

        return new[] { "岗位", "能力", "经验", "目标", "职责", "成果", "团队", "学习" };
    }

    bool ContainsAny(string text, string[] keywords)
    {
        if (string.IsNullOrEmpty(text) || keywords == null)
        {
            return false;
        }

        for (int i = 0; i < keywords.Length; i++)
        {
            if (!string.IsNullOrEmpty(keywords[i]) && text.Contains(keywords[i]))
            {
                return true;
            }
        }

        return false;
    }

    int CountNumericCharacters(string text)
    {
        int count = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                count++;
            }
        }

        return count;
    }

    int CountPunctuation(string text)
    {
        int count = 0;
        string punctuation = "，。；：、,.!?！？";

        for (int i = 0; i < text.Length; i++)
        {
            if (punctuation.Contains(text[i].ToString()))
            {
                count++;
            }
        }

        return count;
    }

    int GetResponseTimeBonus()
    {
        if (answerDurations.Count == 0)
        {
            return 0;
        }

        float total = 0f;

        for (int i = 0; i < answerDurations.Count; i++)
        {
            total += answerDurations[i];
        }

        float averageSeconds = total / answerDurations.Count;

        if (averageSeconds >= 8f && averageSeconds <= 180f)
        {
            return 6;
        }

        if (averageSeconds >= 3f && averageSeconds <= 300f)
        {
            return 3;
        }

        return 0;
    }

    int GetAnswerVarietyBonus()
    {
        HashSet<string> uniqueAnswers = new HashSet<string>();

        for (int i = 0; i < answers.Count; i++)
        {
            uniqueAnswers.Add(answers[i].Trim());
        }

        return uniqueAnswers.Count == answers.Count ? 4 : 0;
    }

    string CleanResultSectionText(string content, params string[] headings)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "暂无内容";
        }

        string cleaned = content.Trim();

        foreach (string heading in headings)
        {
            if (string.IsNullOrWhiteSpace(heading))
            {
                continue;
            }

            if (cleaned.StartsWith(heading))
            {
                cleaned = cleaned.Substring(heading.Length).TrimStart();
                cleaned = cleaned.TrimStart('：', ':', '-', '—', '\n', '\r', ' ');
                break;
            }
        }

        return string.IsNullOrWhiteSpace(cleaned) ? "暂无内容" : cleaned;
    }

    void ShowInterviewResult()
    {
        if (questionSpeechManager != null)
        {
            questionSpeechManager.StopSpeaking();
        }

        if (scoreResponse == null)
        {
            Debug.LogError("没有收到有效的评分数据。");
            statusText.text = "没有收到有效的面试评分。";
            return;
        }

        resultUI.SetActive(true);

        if (
            interviewUIRoot != null
            && !resultUI.transform.IsChildOf(interviewUIRoot.transform)
        )
        {
            interviewUIRoot.SetActive(false);
        }

        questionText.gameObject.SetActive(false);
        statusText.gameObject.SetActive(false);

        targetPositionInput.gameObject.SetActive(false);
        startInterviewButton.SetActive(false);

        answerInput.gameObject.SetActive(false);
        submitButton.SetActive(false);

        overallScoreText.text = scoreResponse.overall_score.ToString();
        languageScoreText.text = scoreResponse.language_score.ToString();
        reactionScoreText.text = scoreResponse.reaction_score.ToString();
        professionalScoreText.text = scoreResponse.professional_score.ToString();
        demeanorScoreText.text = scoreResponse.demeanor_score.ToString();

        highlightsText.text = CleanResultSectionText(scoreResponse.highlights, "表现亮点", "亮点");
        suggestionsText.text = CleanResultSectionText(scoreResponse.suggestions, "改进建议", "建议");

        interviewerAnimationController?.PlayStandby();
        InterviewFinished?.Invoke();
        SpeakResultAnnouncement();
    }
}
