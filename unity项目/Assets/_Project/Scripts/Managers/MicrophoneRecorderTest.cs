using TMPro;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class MicrophoneRecorderTest : MonoBehaviour
{
    [Header("界面提示")]
    [Tooltip("可以拖入当前场景中的状态文字。不拖也能运行。")]
    public TMP_Text statusText;

    [Header("录音设置")]
    [Range(5, 120)]
    public int maxRecordSeconds = 30;

    public int preferredSampleRate = 16000;

    [Header("临时测试快捷键")]
    public KeyCode startRecordingKey = KeyCode.R;
    public KeyCode stopRecordingKey = KeyCode.T;

    private AudioSource playbackSource;
    private AudioClip recordingClip;
    private string microphoneDevice;
    private bool isRecording;

    private void Awake()
    {
        playbackSource = GetComponent<AudioSource>();

        playbackSource.playOnAwake = false;
        playbackSource.loop = false;
        playbackSource.spatialBlend = 0f;
    }

    private void Start()
    {
        DetectMicrophone();
    }

    private void Update()
    {
        if (Input.GetKeyDown(startRecordingKey))
        {
            StartRecording();
        }

        if (Input.GetKeyDown(stopRecordingKey))
        {
            StopRecordingAndPlay();
        }
    }

    private void OnDisable()
    {
        if (isRecording)
        {
            Microphone.End(microphoneDevice);
            isRecording = false;
        }
    }

    private void DetectMicrophone()
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            SetStatus("没有检测到可用麦克风。");
            Debug.LogError("没有检测到可用麦克风。");
            return;
        }

        microphoneDevice = Microphone.devices[0];

        SetStatus("麦克风已准备，可以开始录音。");

        Debug.Log("当前使用的麦克风：" + microphoneDevice);

        for (int i = 0; i < Microphone.devices.Length; i++)
        {
            Debug.Log($"检测到麦克风 {i + 1}：{Microphone.devices[i]}");
        }
    }

    public void StartRecording()
    {
        if (isRecording)
        {
            SetStatus("当前已经在录音。");
            return;
        }

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            DetectMicrophone();

            if (string.IsNullOrEmpty(microphoneDevice))
            {
                return;
            }
        }

        int sampleRate = ResolveSampleRate();

        playbackSource.Stop();

        recordingClip = Microphone.Start(
            microphoneDevice,
            false,
            maxRecordSeconds,
            sampleRate
        );

        if (recordingClip == null)
        {
            SetStatus("录音启动失败。");
            Debug.LogError("Microphone.Start 返回了空的 AudioClip。");
            return;
        }

        isRecording = true;

        SetStatus("正在录音，请开始说话。");
        Debug.Log(
            $"开始录音。设备：{microphoneDevice}，采样率：{sampleRate}"
        );
    }

    public void StopRecordingAndPlay()
    {
        if (!isRecording)
        {
            SetStatus("当前没有正在进行的录音。");
            return;
        }

        int recordedSampleFrames = Microphone.GetPosition(microphoneDevice);

        Microphone.End(microphoneDevice);
        isRecording = false;

        if (recordingClip == null || recordedSampleFrames <= 0)
        {
            SetStatus("没有录到有效声音，请重新尝试。");
            Debug.LogWarning("录音样本长度为0。");
            return;
        }

        AudioClip trimmedClip = TrimRecording(
            recordingClip,
            recordedSampleFrames
        );

        if (trimmedClip == null)
        {
            SetStatus("处理录音失败。");
            return;
        }

        playbackSource.clip = trimmedClip;
        playbackSource.Play();

        float duration = trimmedClip.length;

        SetStatus($"录音完成，正在回放。时长：{duration:F1}秒");
        Debug.Log($"录音完成，实际时长：{duration:F2}秒");
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

        SetStatus("录音已取消。");
    }

    private int ResolveSampleRate()
    {
        Microphone.GetDeviceCaps(
            microphoneDevice,
            out int minimumFrequency,
            out int maximumFrequency
        );

        // 某些设备返回0和0，表示可使用任意常见采样率。
        if (minimumFrequency == 0 && maximumFrequency == 0)
        {
            return preferredSampleRate;
        }

        if (
            preferredSampleRate >= minimumFrequency &&
            preferredSampleRate <= maximumFrequency
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
        if (sourceClip == null || recordedSampleFrames <= 0)
        {
            return null;
        }

        int channels = sourceClip.channels;
        int sampleCount = recordedSampleFrames * channels;

        float[] recordedSamples = new float[sampleCount];

        bool readSucceeded = sourceClip.GetData(recordedSamples, 0);

        if (!readSucceeded)
        {
            Debug.LogError("无法从录音 AudioClip 中读取声音数据。");
            return null;
        }

        AudioClip trimmedClip = AudioClip.Create(
            "RecordedVoice",
            recordedSampleFrames,
            channels,
            sourceClip.frequency,
            false
        );

        bool writeSucceeded = trimmedClip.SetData(recordedSamples, 0);

        if (!writeSucceeded)
        {
            Debug.LogError("无法写入裁剪后的录音数据。");
            return null;
        }

        return trimmedClip;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        Debug.Log("[语音测试] " + message);
    }
}