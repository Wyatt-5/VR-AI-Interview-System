using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class BackendHealthCheck : MonoBehaviour
{
    [Header("面试管理器")]
    public InterviewManager interviewManager;

    [Header("后端健康检查地址")]
    public string healthUrl = "http://127.0.0.1:8000/health";

    void Start()
    {
        if (interviewManager == null)
        {
            interviewManager = FindObjectOfType<InterviewManager>();
        }

        if (interviewManager != null && !interviewManager.ShouldCheckBackend())
        {
            // 当前模式不需要后端，直接跳过健康检查。
            return;
        }

        StartCoroutine(CheckBackend());
    }

    IEnumerator CheckBackend()
    {
        Debug.Log("正在连接Python后端……");

        using (UnityWebRequest request = UnityWebRequest.Get(healthUrl))
        {
            request.timeout = 5;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("后端连接成功：" + request.downloadHandler.text);
            }
            else if (interviewManager != null && interviewManager.runMode == InterviewRunMode.AutoFallback)
            {
                Debug.LogWarning("后端连接失败，将启用本地备用流程：" + request.error);
            }
            else
            {
                Debug.LogError("后端连接失败：" + request.error);
            }
        }
    }
}
