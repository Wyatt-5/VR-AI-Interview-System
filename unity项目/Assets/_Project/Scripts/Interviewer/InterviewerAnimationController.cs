using System;
using System.Threading;
using UnityEngine;

namespace VRInterview.Characters
{
    public enum InterviewerAnimationState
    {
        Standby = 0,
        Talking = 1,
        Thinking = 2,
        Opening = 3
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class InterviewerAnimationController : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private InterviewerAnimationState initialState = InterviewerAnimationState.Standby;

        private const string MotionParameter = "Motion";
        private int motionHash;
        private int pendingState = -1;
        private volatile bool acceptRequests;
        private bool bindingValid;

        public InterviewerAnimationState RequestedState { get; private set; }

        private void Reset() => animator = GetComponent<Animator>();

        private void Awake()
        {
            if (!animator) animator = GetComponent<Animator>();
            motionHash = Animator.StringToHash(MotionParameter);
        }

        private void OnEnable()
        {
            bindingValid = ValidateAnimator();
            acceptRequests = bindingValid;
            Interlocked.Exchange(ref pendingState, bindingValid ? (int)initialState : -1);
        }

        private bool ValidateAnimator()
        {
            if (animator && animator.runtimeAnimatorController)
            {
                foreach (var parameter in animator.parameters)
                    if (parameter.name == MotionParameter && parameter.type == AnimatorControllerParameterType.Int)
                        return true;
            }
            Debug.LogError("面试官动画配置缺失：请绑定含有 Int 参数 Motion 的 Interviewer.controller。", this);
            return false;
        }

        private void Update()
        {
            int next = Interlocked.Exchange(ref pendingState, -1);
            if (next < 0 || !bindingValid) return;
            RequestedState = (InterviewerAnimationState)next;
            // 所有 Unity Animator 操作都在主线程 Update 中执行。
            // 相同状态不会重新开始；一帧内多次请求以最后一次为准，避免语音回调堆积。
            if (animator.GetInteger(motionHash) != next) animator.SetInteger(motionHash, next);
        }

        private void OnDisable()
        {
            acceptRequests = false;
            Interlocked.Exchange(ref pendingState, -1);
        }

        public void SetState(InterviewerAnimationState state)
        {
            if ((int)state < 0 || (int)state > 3) throw new ArgumentOutOfRangeException(nameof(state));
            // 这里只记录请求，不访问 Unity 对象，因此已缓存引用的语音 SDK 后台回调也能调用。
            // 组件未启用时忽略请求；重新启用后回到 Inspector 设置的初始状态。
            if (acceptRequests) Interlocked.Exchange(ref pendingState, (int)state);
        }

        // Inspector 的 UnityEvent 可以绑定 PlayState(int)：0=待机，1=说话，2=思考，3=第四段动作。
        public void PlayState(int state) => SetState((InterviewerAnimationState)state);
        public void PlayStandby() => SetState(InterviewerAnimationState.Standby);
        public void PlayTalking() => SetState(InterviewerAnimationState.Talking);
        public void PlayThinking() => SetState(InterviewerAnimationState.Thinking);
        public void PlayOpening() => SetState(InterviewerAnimationState.Opening);

        // 保留旧名称，避免已经绑定到 UnityEvent 的场景失效。
        [Obsolete("请改用 PlayOpening。")]
        public void PlayViking() => PlayOpening();

        // ==================== 后续语音输入 / ASR 接入 ====================
        // 1. 在你的语音管理脚本中增加并在 Inspector 拖入本组件：
        //    [SerializeField] private VRInterview.Characters.InterviewerAnimationController character;
        // 2. 开始录音 / VAD 检测到用户开始说话：character.OnVoiceInputStarted();
        //    用户说话时人物保持待机倾听。当前没有独立的 listening 动画，暂时使用 standby。
        // 3. ASR 返回最终文本并向后端提交问题：character.OnVoiceInputCompleted();
        //    不要在每次音量变化或每条临时识别结果上调用 Completed。
        // 4. 用户取消录音：character.OnVoiceInputCancelled();
        // 5. 识别失败、请求超时或取消当前轮次：character.OnVoiceError();
        // SDK 若在后台线程回调，可调用以上方法；对象查找和获取组件须提前在 Unity 主线程完成。
        public void OnVoiceInputStarted() => PlayStandby();
        public void OnVoiceInputCompleted() => PlayThinking();
        public void OnVoiceInputCancelled() => PlayStandby();
        public void OnVoiceError() => PlayStandby();

        // ==================== 后续语音输出 / TTS 接入 ====================
        // 真正开始播放面试官语音时：character.OnSpeechOutputStarted();
        // 音频播放结束、被停止或中断时：character.OnSpeechOutputCompleted();
        // 只收到回复文本但音频还未开始时，继续保留 think，避免提前进入 talking。
        // 现有 QuestionSpeechManager 可在 AudioSource.Play() 后调用 Started，
        // 在播放结束 / StopSpeaking() / 错误回退路径调用 Completed 或 OnVoiceError。
        // 本脚本仅控制身体动画，不开始录音、不发送网络请求，也不生成口型。
        // 打断旧播报时，语音管理器须停止旧音频，并过滤过期轮次回调；再发送当前轮次事件。
        public void OnSpeechOutputStarted() => PlayTalking();
        public void OnSpeechOutputCompleted() => PlayStandby();
    }
}
