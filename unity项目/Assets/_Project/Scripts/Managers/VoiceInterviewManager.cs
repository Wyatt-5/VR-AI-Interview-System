using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using VRInterview.Characters;

[RequireComponent(typeof(AudioSource))]
public class VoiceInterviewManager : MonoBehaviour
{
    public enum RecognitionTarget
    {
        TargetPosition,
        InterviewAnswer
    }

    [Header("面试界面")]
    [Tooltip("拖入 InterviewTestCanvas 下的 TargetPositionInput。")]
    public TMP_InputField targetPositionInput;

    [Tooltip("拖入 InterviewTestCanvas 下的 AnswerInput。")]
    public TMP_InputField answerInput;

    [Tooltip("可拖入当前场景中的 StatusText。")]
    public TMP_Text statusText;

    [Header("自动识别目标")]
    [Tooltip(
        "使用电脑快捷键时，脚本会优先识别当前可见且可输入的输入框。" +
        "两个输入框状态相同时，使用这里设置的默认目标。"
    )]
    public RecognitionTarget automaticFallbackTarget =
        RecognitionTarget.InterviewAnswer;

    [Header("后端地址")]
    [Tooltip(
        "电脑编辑器运行通常使用 http://127.0.0.1:8000；" +
        "VR一体机运行时改为电脑局域网IP。"
    )]
    public string backendBaseUrl =
        "http://127.0.0.1:8000";

    [Range(30, 600)]
    public int requestTimeoutSeconds = 300;

    [Header("录音设置")]
    [Range(5, 120)]
    public int maxRecordSeconds = 60;

    [Range(0.2f, 3f)]
    public float minimumRecordSeconds = 0.5f;

    public int preferredSampleRate = 16000;

    [Tooltip(
        "填写设备名称的一部分即可优先使用该麦克风；" +
        "留空时使用系统返回的第一个麦克风。"
    )]
    public string preferredMicrophoneKeyword = "";

    [Header("识别结果")]
    [Tooltip(
        "开启后，新的面试回答会追加到回答框；" +
        "关闭时会覆盖回答框。" +
        "岗位输入始终覆盖原岗位文字。"
    )]
    public bool appendToExistingAnswer = false;

    [Tooltip("识别前先回放本次录音，仅建议调试时开启。")]
    public bool playRecordingBeforeUpload = false;

    [Header("输入框内状态提示")]
    [Tooltip(
        "开启后，录音和识别状态会直接显示在当前输入框内部，" +
        "不会被当成真正的岗位或回答内容提交。"
    )]
    public bool showStateInsideInputField = true;

    public string targetRecordingHint =
        "正在录音，请说出目标岗位……";

    public string targetRecognizingHint =
        "录音完成，正在识别岗位……";

    public string answerRecordingHint =
        "正在录音，请开始回答……";

    public string answerRecognizingHint =
        "录音完成，正在识别回答……";

    [Header("电脑测试快捷键")]
    public bool enableKeyboardShortcuts = true;

    [Tooltip("同一按键可在岗位页面和回答页面开始录音。")]
    public KeyCode startRecordingKey = KeyCode.F6;

    [Tooltip("停止录音，并把文字填入当前页面的输入框。")]
    public KeyCode stopRecordingKey = KeyCode.F7;

    public KeyCode cancelRecordingKey = KeyCode.F8;

    [Header("纯语音自动面试")]
    [Tooltip(
        "开启后：面试官说完自动录音，检测到停顿后自动识别并提交。" +
        "原有输入框、按钮和快捷键仍可作为回退。"
    )]
    public bool handsFreeMode = true;

    public InterviewManager interviewManager;
    public QuestionSpeechManager questionSpeechManager;
    public InterviewerAnimationController interviewerAnimationController;

    [Range(0.1f, 2f)]
    public float automaticRecordingDelay = 0.35f;

    [Range(0.002f, 0.1f)]
    [Tooltip("麦克风声音高于此 RMS 值时视为正在说话。环境较吵时可适当调高。")]
    public float voiceActivityThreshold = 0.012f;

    [Range(0.5f, 3f)]
    [Tooltip("检测到说话后，连续静音达到该时长即自动结束回答。")]
    public float silenceToFinishSeconds = 1.2f;

    [Range(3f, 20f)]
    [Tooltip("自动录音开始后一直没有检测到说话时的等待上限。")]
    public float initialSilenceTimeoutSeconds = 8f;

    [Range(0, 3)]
    public int maxAutomaticRetries = 1;

    [Range(0.1f, 2f)]
    public float automaticSubmitDelay = 0.35f;

    private AudioSource playbackSource;
    private AudioClip recordingClip;
    private AudioClip lastTrimmedClip;
    private string microphoneDevice = "";
    private bool isRecording;
    private bool isRecognizing;
    private bool automaticRecordingActive;
    private bool voiceActivityDetected;
    private float automaticRecordingStartedAt;
    private float lastVoiceActivityAt;
    private int automaticRetryCount;
    private Coroutine automaticRecordingCoroutine;

    private RecognitionTarget recordingTarget =
        RecognitionTarget.InterviewAnswer;

    private TMP_InputField feedbackInputField;
    private TMP_Text feedbackPlaceholderText;
    private string originalPlaceholderText = "";
    private string textBeforeRecording = "";
    private bool originalInputInteractable = true;
    private bool inputFeedbackActive;

    [Serializable]
    private class SpeechToTextResponse
    {
        public string text;
        public string language;
        public float language_probability;
        public float duration;
    }

    [Serializable]
    private class ErrorResponse
    {
        public string detail;
    }

    public bool IsRecording => isRecording;
    public bool IsRecognizing => isRecognizing;
    public RecognitionTarget CurrentRecordingTarget =>
        recordingTarget;

    private void Awake()
    {
        playbackSource = GetComponent<AudioSource>();
        playbackSource.playOnAwake = false;
        playbackSource.loop = false;
        playbackSource.spatialBlend = 0f;

        if (interviewManager == null)
        {
            interviewManager = FindObjectOfType<InterviewManager>();
        }

        if (questionSpeechManager == null)
        {
            questionSpeechManager = FindObjectOfType<
                QuestionSpeechManager
            >();
        }

        if (interviewerAnimationController == null)
        {
            interviewerAnimationController = FindObjectOfType<
                InterviewerAnimationController
            >();
        }
    }

    private void OnEnable()
    {
        SubscribeToInterviewEvents();
    }

    private void Start()
    {
        DetectMicrophone();
    }

    private void Update()
    {
        if (enableKeyboardShortcuts)
        {
            if (Input.GetKeyDown(startRecordingKey))
            {
                StartRecording();
            }

            if (Input.GetKeyDown(stopRecordingKey))
            {
                StopRecordingAndRecognize();
            }

            if (Input.GetKeyDown(cancelRecordingKey))
            {
                CancelRecording();
            }
        }

        if (
            handsFreeMode
            && automaticRecordingActive
            && isRecording
        )
        {
            UpdateAutomaticVoiceDetection();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromInterviewEvents();

        if (automaticRecordingCoroutine != null)
        {
            StopCoroutine(automaticRecordingCoroutine);
            automaticRecordingCoroutine = null;
        }

        if (isRecording)
        {
            Microphone.End(microphoneDevice);
            isRecording = false;
        }

        automaticRecordingActive = false;
        RestoreInputFeedback(true);
    }

    public void DetectMicrophone()
    {
        string[] devices = Microphone.devices;

        if (devices == null || devices.Length == 0)
        {
            microphoneDevice = "";
            SetStatus("没有检测到可用麦克风。");
            Debug.LogError("没有检测到可用麦克风。");
            return;
        }

        microphoneDevice = SelectMicrophoneDevice(
            devices
        );

        Debug.Log(
            "当前使用的麦克风：" + microphoneDevice
        );

        for (int index = 0; index < devices.Length; index++)
        {
            Debug.Log(
                $"检测到麦克风 {index + 1}：{devices[index]}"
            );
        }
    }

    /// <summary>
    /// 电脑快捷键使用的方法。
    /// 根据当前可见、可输入的输入框自动判断写入岗位还是回答。
    /// </summary>
    public void StartRecording()
    {
        RecognitionTarget target =
            ResolveAutomaticTarget();

        StartRecordingForTarget(target, false);
    }

    /// <summary>
    /// 岗位输入页面的“开始录音”按钮可绑定此方法。
    /// </summary>
    public void StartTargetPositionRecording()
    {
        StartRecordingForTarget(
            RecognitionTarget.TargetPosition,
            false
        );
    }

    /// <summary>
    /// 面试回答页面的“开始录音”按钮可绑定此方法。
    /// </summary>
    public void StartAnswerRecording()
    {
        StartRecordingForTarget(
            RecognitionTarget.InterviewAnswer,
            false
        );
    }

    private void StartRecordingForTarget(
        RecognitionTarget target,
        bool automatic
    )
    {
        if (isRecognizing)
        {
            SetStatus("正在识别上一段语音，请稍候。");
            return;
        }

        if (isRecording)
        {
            SetStatus("当前已经在录音。");
            return;
        }

        if (!HasBoundInput(target))
        {
            SetStatus(
                target == RecognitionTarget.TargetPosition
                    ? "岗位输入框尚未绑定。"
                    : "回答输入框尚未绑定。"
            );
            return;
        }

        TMP_InputField targetInput =
            GetInputField(target);

        if (!IsInputAvailable(targetInput))
        {
            SetStatus(
                target == RecognitionTarget.TargetPosition
                    ? "请等待岗位输入框出现后再录音。"
                    : "请听完问题，等待回答框出现后再录音。"
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(microphoneDevice))
        {
            DetectMicrophone();

            if (string.IsNullOrWhiteSpace(microphoneDevice))
            {
                return;
            }
        }

        int sampleRate = ResolveSampleRate();

        playbackSource.Stop();

        recordingTarget = target;

        recordingClip = Microphone.Start(
            microphoneDevice,
            false,
            maxRecordSeconds,
            sampleRate
        );

        if (recordingClip == null)
        {
            SetStatus("录音启动失败，请检查麦克风权限。");
            Debug.LogError(
                "Microphone.Start 返回了空的 AudioClip。"
            );
            return;
        }

        isRecording = true;
        automaticRecordingActive = automatic;
        voiceActivityDetected = false;
        automaticRecordingStartedAt = Time.unscaledTime;
        lastVoiceActivityAt = automaticRecordingStartedAt;
        interviewerAnimationController?.OnVoiceInputStarted();

        BeginInputFeedback(
            target,
            GetInputRecordingHint(target)
        );

        SetStatus(
            automatic
                ? GetAutomaticRecordingMessage(target)
                : GetRecordingMessage(target)
        );

        Debug.Log(
            $"开始录音。用途：{target}，" +
            $"设备：{microphoneDevice}，" +
            $"采样率：{sampleRate}"
        );
    }

    public void StopRecordingAndRecognize()
    {
        if (isRecognizing)
        {
            SetStatus("正在识别，请稍候。");
            return;
        }

        if (!isRecording)
        {
            SetStatus("当前没有正在进行的录音。");
            return;
        }

        int recordedSampleFrames = Microphone.GetPosition(
            microphoneDevice
        );

        Microphone.End(microphoneDevice);
        isRecording = false;
        interviewerAnimationController?.OnVoiceInputCompleted();

        if (
            recordingClip == null
            || recordedSampleFrames <= 0
        )
        {
            RestoreInputFeedback(true);
            SetStatus("没有录到有效声音，请重新尝试。");
            HandleAutomaticRecognitionFailure(recordingTarget);
            Debug.LogWarning("录音样本长度为 0。");
            return;
        }

        float recordedDuration =
            (float)recordedSampleFrames
            / recordingClip.frequency;

        if (recordedDuration < minimumRecordSeconds)
        {
            RestoreInputFeedback(true);
            SetStatus("录音时间太短，请重新说一遍。");
            HandleAutomaticRecognitionFailure(recordingTarget);
            Debug.LogWarning(
                $"录音时间过短：{recordedDuration:F2}秒"
            );
            return;
        }

        lastTrimmedClip = TrimRecording(
            recordingClip,
            recordedSampleFrames
        );

        if (lastTrimmedClip == null)
        {
            RestoreInputFeedback(true);
            SetStatus("处理录音失败，请重新尝试。");
            HandleAutomaticRecognitionFailure(recordingTarget);
            return;
        }

        UpdateInputFeedback(
            GetInputRecognizingHint(recordingTarget)
        );

        if (playRecordingBeforeUpload)
        {
            playbackSource.clip = lastTrimmedClip;
            playbackSource.Play();
        }

        StartCoroutine(
            UploadRecordingAndFillInput(
                lastTrimmedClip,
                recordingTarget
            )
        );
    }

    public void ToggleRecording()
    {
        if (isRecording)
        {
            StopRecordingAndRecognize();
        }
        else
        {
            StartRecording();
        }
    }

    /// <summary>
    /// 岗位页面只做一个麦克风按钮时，绑定此方法。
    /// 第一次点击开始，第二次点击停止并识别。
    /// </summary>
    public void ToggleTargetPositionRecording()
    {
        ToggleRecordingForTarget(
            RecognitionTarget.TargetPosition
        );
    }

    /// <summary>
    /// 回答页面只做一个麦克风按钮时，绑定此方法。
    /// 第一次点击开始，第二次点击停止并识别。
    /// </summary>
    public void ToggleAnswerRecording()
    {
        ToggleRecordingForTarget(
            RecognitionTarget.InterviewAnswer
        );
    }

    private void ToggleRecordingForTarget(
        RecognitionTarget target
    )
    {
        if (isRecording)
        {
            if (recordingTarget != target)
            {
                SetStatus("另一项语音输入正在录音，请先停止。");
                return;
            }

            StopRecordingAndRecognize();
            return;
        }

        StartRecordingForTarget(target, false);
    }

    public void CancelRecording()
    {
        if (!isRecording)
        {
            return;
        }

        Microphone.End(microphoneDevice);
        isRecording = false;
        recordingClip = null;
        automaticRecordingActive = false;
        interviewerAnimationController?.OnVoiceInputCancelled();

        RestoreInputFeedback(true);
        SetStatus("录音已取消。");
    }

    public void PlayLastRecording()
    {
        if (lastTrimmedClip == null)
        {
            SetStatus("当前没有可回放的录音。");
            return;
        }

        playbackSource.clip = lastTrimmedClip;
        playbackSource.Play();
    }

    private IEnumerator UploadRecordingAndFillInput(
        AudioClip audioClip,
        RecognitionTarget target
    )
    {
        isRecognizing = true;
        SetStatus(GetRecognizingMessage(target));

        byte[] wavBytes;

        try
        {
            wavBytes = WavEncoder.Encode(audioClip);
        }
        catch (Exception error)
        {
            isRecognizing = false;
            RestoreInputFeedback(true);
            SetStatus("录音编码失败，请重新尝试。");
            HandleAutomaticRecognitionFailure(target);
            Debug.LogException(error);
            yield break;
        }

        string endpoint =
            backendBaseUrl.TrimEnd('/')
            + "/speech-to-text";

        string inputType =
            target == RecognitionTarget.TargetPosition
                ? "target_position"
                : "interview_answer";

        string fileName =
            target == RecognitionTarget.TargetPosition
                ? "target_position.wav"
                : "interview_answer.wav";

        List<IMultipartFormSection> formSections =
            new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(
                    "input_type",
                    inputType
                ),
                new MultipartFormFileSection(
                    "file",
                    wavBytes,
                    fileName,
                    "audio/wav"
                )
            };

        using UnityWebRequest request =
            UnityWebRequest.Post(
                endpoint,
                formSections
            );

        request.timeout = requestTimeoutSeconds;

        yield return request.SendWebRequest();

        isRecognizing = false;

        if (
            request.result
            != UnityWebRequest.Result.Success
        )
        {
            RestoreInputFeedback(true);

            string serverMessage = ExtractServerError(
                request.downloadHandler?.text
            );

            SetStatus(
                string.IsNullOrWhiteSpace(serverMessage)
                    ? "语音识别失败，请检查后端连接。"
                    : serverMessage
            );

            Debug.LogError(
                "语音识别请求失败："
                + request.error
                + "\n服务器返回："
                + request.downloadHandler?.text
            );

            HandleAutomaticRecognitionFailure(target);

            yield break;
        }

        SpeechToTextResponse response;

        try
        {
            response = JsonUtility.FromJson<
                SpeechToTextResponse
            >(
                request.downloadHandler.text
            );
        }
        catch (Exception error)
        {
            RestoreInputFeedback(true);
            SetStatus("无法解析语音识别结果。");
            HandleAutomaticRecognitionFailure(target);
            Debug.LogException(error);
            yield break;
        }

        string recognizedText =
            response?.text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            RestoreInputFeedback(true);
            SetStatus("没有识别到有效内容，请重新说一遍。");
            HandleAutomaticRecognitionFailure(target);
            yield break;
        }

        if (target == RecognitionTarget.TargetPosition)
        {
            recognizedText = CleanTargetPositionText(
                recognizedText
            );
        }

        string previousText = textBeforeRecording;

        RestoreInputFeedback(false);

        if (
            !FillTargetInput(
                target,
                recognizedText,
                previousText
            )
        )
        {
            RestoreInputFeedback(true);
            SetStatus("输入框引用丢失，无法写入识别文字。");
            HandleAutomaticRecognitionFailure(target);
            yield break;
        }

        SetStatus(GetCompletedMessage(target));
        interviewerAnimationController?.OnVoiceInputCompleted();

        bool continueAutomatically =
            handsFreeMode && automaticRecordingActive;

        automaticRecordingActive = false;
        automaticRetryCount = 0;

        Debug.Log(
            $"语音识别完成。用途：{target}，" +
            $"文本：{recognizedText}"
        );

        if (continueAutomatically)
        {
            yield return new WaitForSecondsRealtime(
                automaticSubmitDelay
            );

            if (interviewManager != null)
            {
                if (target == RecognitionTarget.TargetPosition)
                {
                    interviewManager.StartInterview();
                }
                else
                {
                    interviewManager.SubmitAnswer();
                }
            }
        }
    }

    private bool FillTargetInput(
        RecognitionTarget target,
        string recognizedText,
        string previousText
    )
    {
        if (target == RecognitionTarget.TargetPosition)
        {
            return FillInputField(
                targetPositionInput,
                recognizedText,
                false,
                "",
                "Target Position Input"
            );
        }

        return FillInputField(
            answerInput,
            recognizedText,
            appendToExistingAnswer,
            previousText,
            "Answer Input"
        );
    }

    private bool FillInputField(
        TMP_InputField inputField,
        string recognizedText,
        bool append,
        string previousText,
        string fieldName
    )
    {
        if (inputField == null)
        {
            Debug.LogError(
                $"VoiceInterviewManager 的 {fieldName} 未绑定。"
            );
            return false;
        }

        if (
            append
            && !string.IsNullOrWhiteSpace(previousText)
        )
        {
            string separator =
                previousText.EndsWith(" ")
                || previousText.EndsWith("\n")
                    ? ""
                    : "\n";

            inputField.text =
                previousText
                + separator
                + recognizedText;
        }
        else
        {
            inputField.text = recognizedText;
        }

        inputField.interactable = true;
        inputField.caretPosition =
            inputField.text.Length;

        inputField.ForceLabelUpdate();
        inputField.ActivateInputField();

        return true;
    }

    private TMP_InputField GetInputField(
        RecognitionTarget target
    )
    {
        return target == RecognitionTarget.TargetPosition
            ? targetPositionInput
            : answerInput;
    }

    private void BeginInputFeedback(
        RecognitionTarget target,
        string message
    )
    {
        if (!showStateInsideInputField)
        {
            return;
        }

        RestoreInputFeedback(true);

        feedbackInputField = GetInputField(target);

        if (feedbackInputField == null)
        {
            return;
        }

        feedbackPlaceholderText =
            feedbackInputField.placeholder as TMP_Text;

        originalPlaceholderText =
            feedbackPlaceholderText != null
                ? feedbackPlaceholderText.text
                : "";

        textBeforeRecording =
            feedbackInputField.text ?? "";

        originalInputInteractable =
            feedbackInputField.interactable;

        feedbackInputField.text = "";
        feedbackInputField.interactable = false;

        inputFeedbackActive = true;

        UpdateInputFeedback(message);
    }

    private void UpdateInputFeedback(
        string message
    )
    {
        if (
            !showStateInsideInputField
            || !inputFeedbackActive
            || feedbackInputField == null
        )
        {
            return;
        }

        if (feedbackPlaceholderText != null)
        {
            feedbackPlaceholderText.text = message;
        }

        feedbackInputField.text = "";
        feedbackInputField.ForceLabelUpdate();
    }

    private void RestoreInputFeedback(
        bool restorePreviousText
    )
    {
        if (!inputFeedbackActive)
        {
            return;
        }

        if (feedbackPlaceholderText != null)
        {
            feedbackPlaceholderText.text =
                originalPlaceholderText;
        }

        if (feedbackInputField != null)
        {
            feedbackInputField.interactable =
                originalInputInteractable;

            if (restorePreviousText)
            {
                feedbackInputField.text =
                    textBeforeRecording;
            }

            feedbackInputField.ForceLabelUpdate();
        }

        feedbackInputField = null;
        feedbackPlaceholderText = null;
        originalPlaceholderText = "";
        originalInputInteractable = true;
        inputFeedbackActive = false;
    }

    private string GetInputRecordingHint(
        RecognitionTarget target
    )
    {
        return target == RecognitionTarget.TargetPosition
            ? targetRecordingHint
            : answerRecordingHint;
    }

    private string GetInputRecognizingHint(
        RecognitionTarget target
    )
    {
        return target == RecognitionTarget.TargetPosition
            ? targetRecognizingHint
            : answerRecognizingHint;
    }

    private RecognitionTarget ResolveAutomaticTarget()
    {
        if (
            targetPositionInput != null
            && targetPositionInput.isFocused
        )
        {
            return RecognitionTarget.TargetPosition;
        }

        if (
            answerInput != null
            && answerInput.isFocused
        )
        {
            return RecognitionTarget.InterviewAnswer;
        }

        bool targetPositionAvailable =
            IsInputAvailable(targetPositionInput);

        bool answerAvailable =
            IsInputAvailable(answerInput);

        if (
            targetPositionAvailable
            && !answerAvailable
        )
        {
            return RecognitionTarget.TargetPosition;
        }

        if (
            answerAvailable
            && !targetPositionAvailable
        )
        {
            return RecognitionTarget.InterviewAnswer;
        }

        return automaticFallbackTarget;
    }

    private bool IsInputAvailable(
        TMP_InputField inputField
    )
    {
        return inputField != null
            && inputField.gameObject.activeInHierarchy
            && inputField.enabled
            && inputField.interactable
            && !inputField.readOnly;
    }

    private bool HasBoundInput(
        RecognitionTarget target
    )
    {
        return target == RecognitionTarget.TargetPosition
            ? targetPositionInput != null
            : answerInput != null;
    }

    private string CleanTargetPositionText(
        string originalText
    )
    {
        string text = originalText.Trim(
            ' ',
            '。',
            '，',
            ',',
            '.',
            '！',
            '!',
            '？',
            '?',
            '“',
            '”',
            '"'
        );

        string[] prefixes =
        {
            "我的目标岗位是",
            "目标岗位是",
            "应聘岗位是",
            "面试岗位是",
            "我想应聘",
            "我要应聘",
            "我想面试",
            "我要面试",
            "我应聘的是",
            "我面试的是"
        };

        foreach (string prefix in prefixes)
        {
            if (
                text.StartsWith(
                    prefix,
                    StringComparison.Ordinal
                )
            )
            {
                text = text.Substring(
                    prefix.Length
                ).Trim();
                break;
            }
        }

        string[] suffixes =
        {
            "这个岗位",
            "这一岗位"
        };

        foreach (string suffix in suffixes)
        {
            if (
                text.Length > suffix.Length
                && text.EndsWith(
                    suffix,
                    StringComparison.Ordinal
                )
            )
            {
                text = text.Substring(
                    0,
                    text.Length - suffix.Length
                ).Trim();
                break;
            }
        }

        text = text.Trim(
            ' ',
            '。',
            '，',
            ',',
            '.',
            '！',
            '!',
            '？',
            '?',
            '“',
            '”',
            '"'
        );

        return string.IsNullOrWhiteSpace(text)
            ? originalText.Trim()
            : text;
    }

    private void SubscribeToInterviewEvents()
    {
        if (interviewManager == null)
        {
            return;
        }

        interviewManager.StartPromptCompleted -=
            HandleStartPromptCompleted;
        interviewManager.AnswerListeningStarted -=
            HandleAnswerListeningStarted;
        interviewManager.AnswerAnalysisStarted -=
            HandleAnswerAnalysisStarted;
        interviewManager.InterviewFinished -=
            HandleInterviewFinished;

        interviewManager.StartPromptCompleted +=
            HandleStartPromptCompleted;
        interviewManager.AnswerListeningStarted +=
            HandleAnswerListeningStarted;
        interviewManager.AnswerAnalysisStarted +=
            HandleAnswerAnalysisStarted;
        interviewManager.InterviewFinished +=
            HandleInterviewFinished;
    }

    private void UnsubscribeFromInterviewEvents()
    {
        if (interviewManager == null)
        {
            return;
        }

        interviewManager.StartPromptCompleted -=
            HandleStartPromptCompleted;
        interviewManager.AnswerListeningStarted -=
            HandleAnswerListeningStarted;
        interviewManager.AnswerAnalysisStarted -=
            HandleAnswerAnalysisStarted;
        interviewManager.InterviewFinished -=
            HandleInterviewFinished;
    }

    private void HandleStartPromptCompleted()
    {
        automaticRetryCount = 0;
        ScheduleAutomaticRecording(
            RecognitionTarget.TargetPosition
        );
    }

    private void HandleAnswerListeningStarted()
    {
        automaticRetryCount = 0;
        ScheduleAutomaticRecording(
            RecognitionTarget.InterviewAnswer
        );
    }

    private void HandleAnswerAnalysisStarted()
    {
        CancelPendingAutomaticRecording();
        interviewerAnimationController?.PlayThinking();
    }

    private void HandleInterviewFinished()
    {
        CancelPendingAutomaticRecording();
        interviewerAnimationController?.PlayStandby();
    }

    private void ScheduleAutomaticRecording(
        RecognitionTarget target
    )
    {
        if (!handsFreeMode || !isActiveAndEnabled)
        {
            return;
        }

        CancelPendingAutomaticRecording();
        automaticRecordingCoroutine = StartCoroutine(
            StartAutomaticRecordingAfterDelay(target)
        );
    }

    private IEnumerator StartAutomaticRecordingAfterDelay(
        RecognitionTarget target
    )
    {
        yield return new WaitForSecondsRealtime(
            automaticRecordingDelay
        );

        automaticRecordingCoroutine = null;

        if (
            !handsFreeMode
            || isRecording
            || isRecognizing
            || interviewManager == null
        )
        {
            yield break;
        }

        bool stateMatches =
            target == RecognitionTarget.TargetPosition
                ? interviewManager.CurrentState
                    == InterviewState.WaitingForPlayer
                : interviewManager.CurrentState
                    == InterviewState.ListeningAnswer;

        if (!stateMatches)
        {
            yield break;
        }

        automaticRecordingActive = true;
        StartRecordingForTarget(target, true);

        if (!isRecording)
        {
            HandleAutomaticRecognitionFailure(target);
        }
    }

    private void CancelPendingAutomaticRecording()
    {
        if (automaticRecordingCoroutine != null)
        {
            StopCoroutine(automaticRecordingCoroutine);
            automaticRecordingCoroutine = null;
        }
    }

    private void UpdateAutomaticVoiceDetection()
    {
        if (recordingClip == null)
        {
            return;
        }

        int currentPosition = Microphone.GetPosition(
            microphoneDevice
        );

        if (currentPosition <= 0)
        {
            return;
        }

        const int sampleFramesToInspect = 512;
        int sampleFrames = Mathf.Min(
            sampleFramesToInspect,
            currentPosition
        );
        int channels = Mathf.Max(1, recordingClip.channels);
        float[] samples = new float[
            sampleFrames * channels
        ];
        int offsetFrames = Mathf.Max(
            0,
            currentPosition - sampleFrames
        );

        if (recordingClip.GetData(samples, offsetFrames))
        {
            double squaredTotal = 0d;

            for (int index = 0; index < samples.Length; index++)
            {
                squaredTotal += samples[index] * samples[index];
            }

            float rms = Mathf.Sqrt(
                (float)(squaredTotal / samples.Length)
            );

            if (rms >= voiceActivityThreshold)
            {
                voiceActivityDetected = true;
                lastVoiceActivityAt = Time.unscaledTime;
            }
        }

        float elapsed =
            Time.unscaledTime - automaticRecordingStartedAt;

        if (
            voiceActivityDetected
            && elapsed >= minimumRecordSeconds
            && Time.unscaledTime - lastVoiceActivityAt
                >= silenceToFinishSeconds
        )
        {
            StopRecordingAndRecognize();
            return;
        }

        if (
            !voiceActivityDetected
            && elapsed >= initialSilenceTimeoutSeconds
        )
        {
            RecognitionTarget retryTarget = recordingTarget;
            Microphone.End(microphoneDevice);
            isRecording = false;
            recordingClip = null;
            RestoreInputFeedback(true);
            interviewerAnimationController?.OnVoiceError();
            SetStatus("没有检测到说话声音，准备重新监听。");
            HandleAutomaticRecognitionFailure(retryTarget);
            return;
        }

        if (elapsed >= maxRecordSeconds - 0.1f)
        {
            StopRecordingAndRecognize();
        }
    }

    private void HandleAutomaticRecognitionFailure(
        RecognitionTarget target
    )
    {
        bool shouldRetry =
            handsFreeMode && automaticRecordingActive;

        automaticRecordingActive = false;
        interviewerAnimationController?.OnVoiceError();

        if (!shouldRetry)
        {
            return;
        }

        if (automaticRetryCount >= maxAutomaticRetries)
        {
            SetStatus(
                "自动语音未成功，请点击麦克风重试或使用文字输入。"
            );
            return;
        }

        automaticRetryCount++;
        ScheduleAutomaticRecording(target);
    }

    private string GetAutomaticRecordingMessage(
        RecognitionTarget target
    )
    {
        return target == RecognitionTarget.TargetPosition
            ? "请直接说出目标岗位，停顿后将自动开始面试。"
            : "请直接回答，停顿后将自动识别并提交。";
    }

    private string GetRecordingMessage(
        RecognitionTarget target
    )
    {
        return target == RecognitionTarget.TargetPosition
            ? "正在录音，请说出目标岗位。"
            : "正在录音，请开始回答。";
    }

    private string GetRecognizingMessage(
        RecognitionTarget target
    )
    {
        return target == RecognitionTarget.TargetPosition
            ? "正在识别目标岗位，请稍候。"
            : "正在识别回答，请稍候。";
    }

    private string GetCompletedMessage(
        RecognitionTarget target
    )
    {
        return target == RecognitionTarget.TargetPosition
            ? "岗位识别完成，可修改后开始面试。"
            : "识别完成，可修改后提交回答。";
    }

    private string SelectMicrophoneDevice(
        string[] devices
    )
    {
        if (
            !string.IsNullOrWhiteSpace(
                preferredMicrophoneKeyword
            )
        )
        {
            foreach (string device in devices)
            {
                if (
                    device.IndexOf(
                        preferredMicrophoneKeyword,
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0
                )
                {
                    return device;
                }
            }

            Debug.LogWarning(
                "没有找到名称包含“"
                + preferredMicrophoneKeyword
                + "”的麦克风，"
                + "已改用第一个设备。"
            );
        }

        return devices[0];
    }

    private int ResolveSampleRate()
    {
        Microphone.GetDeviceCaps(
            microphoneDevice,
            out int minimumFrequency,
            out int maximumFrequency
        );

        if (
            minimumFrequency == 0
            && maximumFrequency == 0
        )
        {
            return preferredSampleRate;
        }

        if (
            preferredSampleRate >= minimumFrequency
            && preferredSampleRate <= maximumFrequency
        )
        {
            return preferredSampleRate;
        }

        if (maximumFrequency > 0)
        {
            return maximumFrequency;
        }

        return 44100;
    }

    private AudioClip TrimRecording(
        AudioClip sourceClip,
        int recordedSampleFrames
    )
    {
        if (
            sourceClip == null
            || recordedSampleFrames <= 0
        )
        {
            return null;
        }

        int channels = sourceClip.channels;
        int totalSampleCount =
            recordedSampleFrames * channels;

        float[] samples = new float[
            totalSampleCount
        ];

        if (!sourceClip.GetData(samples, 0))
        {
            Debug.LogError(
                "无法从录音 AudioClip 中读取声音数据。"
            );
            return null;
        }

        AudioClip trimmedClip = AudioClip.Create(
            "RecordedInterviewVoice",
            recordedSampleFrames,
            channels,
            sourceClip.frequency,
            false
        );

        if (!trimmedClip.SetData(samples, 0))
        {
            Debug.LogError(
                "无法写入裁剪后的录音数据。"
            );
            return null;
        }

        return trimmedClip;
    }

    private string ExtractServerError(
        string responseBody
    )
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "";
        }

        try
        {
            ErrorResponse errorResponse =
                JsonUtility.FromJson<ErrorResponse>(
                    responseBody
                );

            return errorResponse?.detail?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        Debug.Log("[语音输入] " + message);
    }
}
