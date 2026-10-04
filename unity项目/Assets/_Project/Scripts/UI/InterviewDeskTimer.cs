using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class InterviewDeskTimer : MonoBehaviour
{
    [Header("面试流程")]
    [Tooltip("负责发出面试开始、重置和结束事件的 InterviewManager。")]
    public InterviewManager interviewManager;

    [Header("桌面计时器界面")]
    public TMP_Text timeText;
    public TMP_Text stateText;
    public CanvasGroup timerCanvasGroup;
    public Graphic stateIndicator;

    [Header("显示设置")]
    [Tooltip("结果页出现时隐藏数字界面，避免压住结算报告；外壳模型不会受影响。")]
    public bool hideWhenFinished = true;

    public Color readyColor = new Color32(126, 170, 181, 255);
    public Color runningColor = new Color32(88, 211, 195, 255);
    public Color finishedColor = new Color32(135, 181, 205, 255);

    private bool isRunning;
    private bool eventsBound;
    private float startedAt;
    private float elapsedSeconds;

    public bool IsRunning => isRunning;
    public float ElapsedSeconds => elapsedSeconds;

    private void Awake()
    {
        ResolveReferences();
        SetReadyVisual();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindEvents();
        SynchronizeWithInterview();
    }

    private void Start()
    {
        // 兼容脚本执行顺序：InterviewManager 可能在本组件之后完成 Start。
        SynchronizeWithInterview();
    }

    private void Update()
    {
        if (!isRunning)
        {
            return;
        }

        elapsedSeconds = Mathf.Max(
            0f,
            Time.realtimeSinceStartup - startedAt
        );

        RefreshTimeText();

        if (stateIndicator != null)
        {
            float pulse =
                0.72f
                + 0.28f
                * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f));
            Color color = runningColor;
            color.a *= pulse;
            stateIndicator.color = color;
        }
    }

    private void OnDisable()
    {
        UnbindEvents();
    }

    public void BeginTimer()
    {
        elapsedSeconds = 0f;
        startedAt = Time.realtimeSinceStartup;
        isRunning = true;

        SetCanvasVisible(true, 1f);
        SetState("面试进行中", runningColor);
        RefreshTimeText();
    }

    public void StopTimer()
    {
        if (isRunning)
        {
            elapsedSeconds = Mathf.Max(
                0f,
                Time.realtimeSinceStartup - startedAt
            );
        }

        isRunning = false;
        SetState("本次用时", finishedColor);
        RefreshTimeText();

        if (hideWhenFinished)
        {
            SetCanvasVisible(false, 0f);
        }
    }

    public void ResetTimer()
    {
        isRunning = false;
        elapsedSeconds = 0f;
        SetReadyVisual();
    }

    private void SetReadyVisual()
    {
        SetCanvasVisible(true, 0.92f);
        SetState("准备就绪", readyColor);
        RefreshTimeText();
    }

    private void ResolveReferences()
    {
        if (interviewManager == null)
        {
            interviewManager = FindObjectOfType<InterviewManager>();
        }
    }

    private void BindEvents()
    {
        if (eventsBound || interviewManager == null)
        {
            return;
        }

        interviewManager.InterviewStarted += BeginTimer;
        interviewManager.InterviewReset += ResetTimer;
        interviewManager.InterviewFinished += StopTimer;
        eventsBound = true;
    }

    private void UnbindEvents()
    {
        if (!eventsBound || interviewManager == null)
        {
            return;
        }

        interviewManager.InterviewStarted -= BeginTimer;
        interviewManager.InterviewReset -= ResetTimer;
        interviewManager.InterviewFinished -= StopTimer;
        eventsBound = false;
    }

    private void SynchronizeWithInterview()
    {
        if (interviewManager == null)
        {
            return;
        }

        switch (interviewManager.CurrentState)
        {
            case InterviewState.WaitingForPlayer:
                if (!isRunning && elapsedSeconds <= 0f)
                {
                    SetReadyVisual();
                }
                break;

            case InterviewState.OpeningSpeech:
            case InterviewState.AskingQuestion:
            case InterviewState.ListeningAnswer:
            case InterviewState.AnalyzingAnswer:
                if (!isRunning)
                {
                    BeginTimer();
                }
                break;

            case InterviewState.Finished:
                StopTimer();
                break;
        }
    }

    private void SetState(string label, Color indicatorColor)
    {
        if (stateText != null)
        {
            stateText.text = label;
        }

        if (stateIndicator != null)
        {
            stateIndicator.color = indicatorColor;
        }
    }

    private void SetCanvasVisible(bool visible, float alpha)
    {
        if (timerCanvasGroup == null)
        {
            return;
        }

        timerCanvasGroup.alpha = visible ? alpha : 0f;
        timerCanvasGroup.interactable = false;
        timerCanvasGroup.blocksRaycasts = false;
    }

    private void RefreshTimeText()
    {
        if (timeText == null)
        {
            return;
        }

        int totalSeconds = Mathf.FloorToInt(
            Mathf.Max(0f, elapsedSeconds)
        );

        if (totalSeconds >= 3600)
        {
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds / 60) % 60;
            int seconds = totalSeconds % 60;
            timeText.text = $"{hours:00}:{minutes:00}:{seconds:00}";
            return;
        }

        timeText.text =
            $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }
}
