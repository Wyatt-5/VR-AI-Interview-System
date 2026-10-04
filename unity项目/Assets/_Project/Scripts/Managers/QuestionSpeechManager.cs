using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using VRInterview.Characters;

[RequireComponent(typeof(AudioSource))]
public class QuestionSpeechManager : MonoBehaviour
{
    [Header("语音功能")]
    [Tooltip("关闭后仍显示问题文字，但不请求或播放语音。")]
    public bool enableSpeech = true;

    [Header("后端地址")]
    [Tooltip(
        "电脑编辑器运行通常使用 http://127.0.0.1:8000/text-to-speech；" +
        "VR一体机运行时改为电脑局域网IP。"
    )]
    public string textToSpeechUrl =
        "http://127.0.0.1:8000/text-to-speech";

    [Range(5, 60)]
    [Tooltip(
        "语音生成超过该时间会直接回退到文字，" +
        "避免用户长时间等待。"
    )]
    public int requestTimeoutSeconds = 30;

    [Range(0, 120)]
    [Tooltip(
        "一次语音请求失败后，短时间内不再重复请求，" +
        "后续问题立即使用文字，防止连续卡顿。"
    )]
    public float failureCooldownSeconds = 20f;

    [Header("声音设置")]
    [Tooltip("面试官音色。Yunyang 是偏成熟、专业、可靠的中文男声。")]
    public string voice = "zh-CN-YunyangNeural";

    [Tooltip("语速格式示例：+0%、-10%、+15%。")]
    public string rate = "-8%";

    [Tooltip("音量格式示例：+0%、-10%、+10%。")]
    public string volume = "+0%";

    [Tooltip("音调格式示例：+0Hz、-10Hz、+10Hz。")]
    public string pitch = "-4Hz";

    [Tooltip(
        "开启后使用在线神经网络 MP3 音色；关闭后使用后端本机 WAV 语音。"
    )]
    public bool useNeuralVoice = true;

    [Header("调试")]
    [Tooltip("开启后会在 Console 中输出朗读状态。")]
    public bool logSpeechStatus = true;

    [Header("面试官动画")]
    [Tooltip("播放语音时切换为询问动画，播放结束后回到待机动画。")]
    public InterviewerAnimationController animationController;

    private AudioSource speechAudioSource;
    private Coroutine speechCoroutine;
    private int requestVersion;
    private float speechUnavailableUntil;
    private bool cooldownMessageLogged;

    private sealed class SpeechDownloadResult
    {
        public AudioClip clip;
        public string error = "";
        public long responseCode;
    }

    public bool IsSpeaking { get; private set; }

    public bool IsTemporarilyUnavailable =>
        Time.unscaledTime
        < speechUnavailableUntil;

    private void Awake()
    {
        speechAudioSource = GetComponent<AudioSource>();
        speechAudioSource.playOnAwake = false;
        speechAudioSource.loop = false;

        if (animationController == null)
        {
            animationController = FindObjectOfType<
                InterviewerAnimationController
            >();
        }
    }

    private void OnDisable()
    {
        StopSpeaking();
    }

    public void Speak(string text)
    {
        Speak(text, null);
    }

    public void Speak(
        string text,
        Action onCompleted
    )
    {
        if (
            !enableSpeech
            || !isActiveAndEnabled
            || string.IsNullOrWhiteSpace(text)
        )
        {
            onCompleted?.Invoke();
            return;
        }

        if (IsTemporarilyUnavailable)
        {
            if (
                logSpeechStatus
                && !cooldownMessageLogged
            )
            {
                Debug.LogWarning(
                    "[面试官语音] 语音服务处于短暂冷却期，" +
                    "本轮立即使用文字，不再等待。"
                );

                cooldownMessageLogged = true;
            }

            onCompleted?.Invoke();
            return;
        }

        cooldownMessageLogged = false;

        StopSpeaking();

        int currentVersion = requestVersion;

        speechCoroutine = StartCoroutine(
            RequestAndPlaySpeech(
                text.Trim(),
                currentVersion,
                onCompleted
            )
        );
    }

    public void StopSpeaking()
    {
        requestVersion++;

        if (speechCoroutine != null)
        {
            StopCoroutine(speechCoroutine);
            speechCoroutine = null;
        }

        if (speechAudioSource != null)
        {
            speechAudioSource.Stop();
            speechAudioSource.clip = null;
        }

        IsSpeaking = false;
    }

    private IEnumerator RequestAndPlaySpeech(
        string text,
        int currentVersion,
        Action onCompleted
    )
    {
        IsSpeaking = true;

        if (logSpeechStatus)
        {
            Debug.Log(
                "[面试官语音] 正在生成并下载语音："
                + text
            );
        }

        SpeechDownloadResult download =
            new SpeechDownloadResult();

        yield return DownloadSpeechClip(
            text,
            useNeuralVoice,
            currentVersion,
            download
        );

        if (currentVersion != requestVersion)
        {
            yield break;
        }

        if (download.clip == null && useNeuralVoice)
        {
            Debug.LogWarning(
                "[面试官语音] 神经网络男声暂时不可用，"
                + "正在切换到本机成熟男声。"
                + "\n错误："
                + download.error
                + "\nHTTP状态码："
                + download.responseCode
            );

            SpeechDownloadResult localFallback =
                new SpeechDownloadResult();

            yield return DownloadSpeechClip(
                text,
                false,
                currentVersion,
                localFallback
            );

            if (currentVersion != requestVersion)
            {
                yield break;
            }

            if (localFallback.clip != null)
            {
                download = localFallback;
                Debug.Log(
                    "[面试官语音] 已自动切换为本机成熟男声。"
                );
            }
            else
            {
                download.error =
                    download.error
                    + "；本机男声备用失败："
                    + localFallback.error;
                download.responseCode =
                    localFallback.responseCode;
            }
        }

        if (download.clip == null)
        {
            Debug.LogWarning(
                "[面试官语音] 在线与本机男声均不可用，"
                + "面试将继续使用文字。"
                + "\n错误："
                + download.error
                + "\nHTTP状态码："
                + download.responseCode
            );

            speechUnavailableUntil =
                Time.unscaledTime
                + failureCooldownSeconds;

            CompleteCurrentRequest(
                currentVersion,
                onCompleted,
                false
            );

            yield break;
        }

        AudioClip speechClip = download.clip;
        speechAudioSource.clip = speechClip;
        speechAudioSource.Play();
        animationController?.OnSpeechOutputStarted();

        if (logSpeechStatus)
        {
            Debug.Log(
                "[面试官语音] 开始播放，时长："
                + speechClip.length.ToString("F1")
                + "秒"
            );
        }

        while (
            currentVersion == requestVersion
            && speechAudioSource.isPlaying
        )
        {
            yield return null;
        }

        if (currentVersion != requestVersion)
        {
            yield break;
        }

        speechAudioSource.clip = null;

        CompleteCurrentRequest(
            currentVersion,
            onCompleted,
            true
        );
    }

    private IEnumerator DownloadSpeechClip(
        string text,
        bool neuralVoice,
        int currentVersion,
        SpeechDownloadResult result
    )
    {
        string requestUrl = BuildRequestUrl(
            text,
            neuralVoice
        );
        AudioType audioType =
            neuralVoice
                ? AudioType.MPEG
                : AudioType.WAV;

        using UnityWebRequest request =
            UnityWebRequestMultimedia.GetAudioClip(
                requestUrl,
                audioType
            );

        request.timeout = requestTimeoutSeconds;

        DownloadHandlerAudioClip audioHandler =
            request.downloadHandler
            as DownloadHandlerAudioClip;

        if (audioHandler != null)
        {
            audioHandler.streamAudio = false;
        }

        yield return request.SendWebRequest();

        if (currentVersion != requestVersion)
        {
            yield break;
        }

        result.responseCode = request.responseCode;

        if (
            request.result
            != UnityWebRequest.Result.Success
        )
        {
            result.error = request.error;
            yield break;
        }

        try
        {
            result.clip =
                DownloadHandlerAudioClip.GetContent(
                    request
                );
        }
        catch (Exception error)
        {
            result.error = error.Message;
        }

        if (result.clip == null && string.IsNullOrEmpty(result.error))
        {
            result.error = "后端未返回可解析的音频。";
        }
    }

    private void CompleteCurrentRequest(
        int currentVersion,
        Action onCompleted,
        bool audioPlayed
    )
    {
        if (currentVersion != requestVersion)
        {
            return;
        }

        IsSpeaking = false;
        speechCoroutine = null;
        animationController?.OnSpeechOutputCompleted();

        if (logSpeechStatus)
        {
            if (audioPlayed)
            {
                Debug.Log("[面试官语音] 播放结束。");
            }
            else
            {
                Debug.Log(
                    "[面试官语音] 已完成文字回退，" +
                    "面试流程继续。"
                );
            }
        }

        onCompleted?.Invoke();
    }

    private string BuildRequestUrl(
        string text,
        bool neuralVoice
    )
    {
        string separator =
            textToSpeechUrl.Contains("?")
                ? "&"
                : "?";

        StringBuilder builder = new StringBuilder();

        builder.Append(textToSpeechUrl);
        builder.Append(separator);

        builder.Append("text=");
        builder.Append(Escape(text));

        builder.Append("&voice=");
        builder.Append(Escape(voice));

        builder.Append("&rate=");
        builder.Append(Escape(rate));

        builder.Append("&volume=");
        builder.Append(Escape(volume));

        builder.Append("&pitch=");
        builder.Append(Escape(pitch));

        builder.Append(
            neuralVoice
                ? "&format=mp3"
                : "&format=wav"
        );

        return builder.ToString();
    }

    private string Escape(string value)
    {
        return UnityWebRequest.EscapeURL(
            value ?? "",
            Encoding.UTF8
        );
    }
}
