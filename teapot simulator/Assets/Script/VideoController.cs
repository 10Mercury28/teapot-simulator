using UnityEngine;
using UnityEngine.Video;
using System.Collections;

/// <summary>
/// 干净的视频控制器封装，彻底干掉原来杂乱的协程和 isPrepared 检查。
/// </summary>
public class VideoController : MonoBehaviour
{
    public VideoPlayer vp { get; private set; }
    private double targetTime = -1.0;
    private float lastScrubTime = 0f;
    private float scrubInterval = 0.033f; // fallback 节流（用于回退/大跳跃 Seek）

    private bool scrubMode = false;
    private double scrubTargetTime = -1.0;
    private bool isSeeking = false;
    private float seekStartRealtime = 0f;
    private bool hasPendingSeek = false;
    private double pendingSeekTime = 0.0;
    private bool waitingSeekFrame = false;
    private float seekFrameWaitStartRealtime = 0f;
    private float scrubPlayRequestedRealtime = -1f;
    private bool scrubFrameReadySubscribed = false;
    private bool scrubSeekSubscribed = false;

    /// <summary>
    /// 获取或创建 VideoController
    /// </summary>
    public static VideoController GetOrCreate(VideoPlayer videoPlayer)
    {
        if (videoPlayer == null) return null;
        
        VideoController ctrl = videoPlayer.GetComponent<VideoController>();
        if (ctrl == null)
        {
            ctrl = videoPlayer.gameObject.AddComponent<VideoController>();
        }
        ctrl.Initialize(videoPlayer);
        return ctrl;
    }

    private void Initialize(VideoPlayer videoPlayer)
    {
        vp = videoPlayer;
        if (vp != null)
        {
            vp.playOnAwake = false;
            vp.isLooping = false;
            vp.skipOnDrop = true;
            vp.waitForFirstFrame = true;
            
            // ⚠️ 修复：不要在这里自动 Prepare()！
            // 如果场景里有几十个视频同时 Prepare，会导致底层解码器资源耗尽，出现蓝屏/黑屏 Bug。
            // 把 Prepare 的控制权交还给业务层（比如 Activate 时才 Prepare）。
        }
    }

    /// <summary>
    /// 确保视频准备好，并执行回调
    /// </summary>
    public void PrepareNow(System.Action onPrepared = null)
    {
        if (vp == null) return;
        if (vp.isPrepared)
        {
            onPrepared?.Invoke();
            return;
        }
        StartCoroutine(PrepareRoutine(onPrepared));
    }

    private IEnumerator PrepareRoutine(System.Action onPrepared)
    {
        if (vp == null)
        {
            onPrepared?.Invoke();
            yield break;
        }

        if (!vp.enabled) vp.enabled = true;

        vp.Prepare();
        float timeout = 2.5f;
        while (!vp.isPrepared && timeout > 0f)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (!vp.isPrepared)
        {
            Debug.LogWarning($"⚠️ [VideoController] Prepare 超时: {vp.name} (已强制放行，避免主流程卡死)");
        }

        onPrepared?.Invoke();
    }

    /// <summary>
    /// 从指定时间点开始，播放一小段（PatModule 点击反馈使用）
    /// </summary>
    public void PlayChunk(double startTime, double duration)
    {
        if (vp == null || vp.clip == null) return;

        CancelScrub();
        targetTime = startTime + duration;
        
        if (!vp.isPrepared)
        {
            PrepareNow(() => {
                if (targetTime >= 0)
                {
                    vp.time = startTime;
                    vp.Play();
                }
            });
            return;
        }

        vp.time = startTime;
        if (!vp.isPlaying)
        {
            vp.Play();
        }
    }

    /// <summary>
    /// 完整播放视频直到结束 (Fail / Transition)
    /// </summary>
    public void PlayFull(System.Action onComplete = null)
    {
        if (vp == null) return;

        CancelScrub();
        targetTime = -1.0; // 清除可能残留的片段播放限制

        if (!vp.isPrepared)
        {
            PrepareNow(() => {
                vp.time = 0;
                vp.Play();
                if (onComplete != null) StartCoroutine(WaitCompletion(onComplete));
            });
            return;
        }
        
        vp.time = 0;
        vp.Play();
        if (onComplete != null) StartCoroutine(WaitCompletion(onComplete));
    }

    private IEnumerator WaitCompletion(System.Action onComplete)
    {
        // 留一点缓冲时间等它真正开始播
        float timeout = 1f;
        while (!vp.isPlaying && timeout > 0)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        
        // 等待播放结束，增加最大安全超时保护
        float maxWait = (vp.length > 0 ? (float)vp.length : 5f) + 1.5f;
        float elapsed = 0f;
        while (vp.isPlaying && elapsed < maxWait)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        
        onComplete?.Invoke();
    }

    /// <summary>
    /// 平滑地进行进度搓划 (CutModule / ChooseModule 核心功能)
    /// Windows Media Foundation 对 H.264 High Profile B 帧视频执行 Pause + time + StepForward 会黑屏；
    /// 改用「前向高倍速追帧 + 串行化 Seek 等首帧」驱动真实渲染输出。
    /// </summary>
    public void ScrubTo(float progress, bool allowBackwardSeek = true)
    {
        if (vp == null || (vp.clip == null && string.IsNullOrEmpty(vp.url))) return;

        if (!vp.isPrepared)
        {
            vp.Prepare();
            return;
        }

        if (vp.targetTexture != null && !vp.targetTexture.IsCreated())
        {
            vp.targetTexture.Create();
        }

        double len = vp.length > 0.01 ? vp.length : (vp.clip != null ? vp.clip.length : 1.0);
        double maxTime = len > 0.08 ? (len - 0.05) : len;
        double desiredTime = len * Mathf.Clamp01(progress);
        if (desiredTime < 0.0) desiredTime = 0.0;
        else if (desiredTime > maxTime) desiredTime = maxTime;

        targetTime = -1.0;
        vp.isLooping = false;
        scrubMode = true;
        scrubTargetTime = desiredTime;

        if (isSeeking)
        {
            hasPendingSeek = true;
            pendingSeekTime = desiredTime;
            return;
        }

        double delta = desiredTime - vp.time;

        if (delta > 0.015 && delta <= 2.2)
        {
            waitingSeekFrame = false;
            UnsubscribeScrubFrameReady();

            if (vp.canSetPlaybackSpeed && (!vp.isPlaying || Time.unscaledTime - lastScrubTime >= scrubInterval))
            {
                vp.playbackSpeed = Mathf.Clamp((float)(delta / 0.12), 1.0f, 4.0f);
                lastScrubTime = Time.unscaledTime;
            }

            if (!vp.isPlaying)
            {
                vp.Play();
                scrubPlayRequestedRealtime = Time.unscaledTime;
            }
        }
        else if ((allowBackwardSeek && delta < -0.05) || delta > 2.2)
        {
            BeginSerializedSeek(desiredTime);
        }
        else
        {
            if (vp.canSetPlaybackSpeed) vp.playbackSpeed = 1f;
            if (!waitingSeekFrame)
            {
                if (vp.isPlaying) vp.Pause();
                scrubMode = false;
            }
        }
    }

    private void BeginSerializedSeek(double seekTime)
    {
        if (vp == null) return;

        isSeeking = true;
        seekStartRealtime = Time.unscaledTime;
        lastScrubTime = Time.unscaledTime;
        hasPendingSeek = false;
        waitingSeekFrame = false;

        if (vp.canSetPlaybackSpeed) vp.playbackSpeed = 1f;

        if (!scrubSeekSubscribed)
        {
            vp.seekCompleted += HandleScrubSeekCompleted;
            scrubSeekSubscribed = true;
        }
        if (!scrubFrameReadySubscribed)
        {
            vp.sendFrameReadyEvents = true;
            vp.frameReady += HandleScrubFrameReady;
            scrubFrameReadySubscribed = true;
        }

        vp.time = seekTime;
        if (!vp.isPlaying)
        {
            vp.Play();
            scrubPlayRequestedRealtime = Time.unscaledTime;
        }
    }

    private void HandleScrubSeekCompleted(VideoPlayer source)
    {
        isSeeking = false;
        UnsubscribeScrubSeek();

        if (vp == null) return;

        if (hasPendingSeek)
        {
            double next = pendingSeekTime;
            hasPendingSeek = false;
            scrubTargetTime = next;
            double delta = next - vp.time;
            if (delta < -0.05 || delta > 2.2)
            {
                BeginSerializedSeek(next);
                return;
            }
        }

        waitingSeekFrame = true;
        seekFrameWaitStartRealtime = Time.unscaledTime;
    }

    private void HandleScrubFrameReady(VideoPlayer source, long frameIdx)
    {
        if (!waitingSeekFrame) return;

        waitingSeekFrame = false;
        UnsubscribeScrubFrameReady();

        if (vp != null && (!scrubMode || vp.time >= scrubTargetTime - 0.03))
        {
            if (vp.canSetPlaybackSpeed) vp.playbackSpeed = 1f;
            if (vp.isPlaying) vp.Pause();
            scrubMode = false;
        }
    }

    private void UnsubscribeScrubSeek()
    {
        if (vp != null && scrubSeekSubscribed)
        {
            vp.seekCompleted -= HandleScrubSeekCompleted;
        }
        scrubSeekSubscribed = false;
    }

    private void UnsubscribeScrubFrameReady()
    {
        if (vp != null && scrubFrameReadySubscribed)
        {
            vp.frameReady -= HandleScrubFrameReady;
            vp.sendFrameReadyEvents = false;
        }
        scrubFrameReadySubscribed = false;
    }

    public void CancelScrub()
    {
        scrubMode = false;
        scrubTargetTime = -1.0;
        isSeeking = false;
        hasPendingSeek = false;
        waitingSeekFrame = false;
        scrubPlayRequestedRealtime = -1f;

        UnsubscribeScrubSeek();
        UnsubscribeScrubFrameReady();

        if (vp != null && vp.canSetPlaybackSpeed)
        {
            vp.playbackSpeed = 1f;
        }
    }

    public void Pause()
    {
        CancelScrub();
        targetTime = -1.0;
        if (vp != null) vp.Pause();
    }
    
    public void StopAndReset()
    {
        CancelScrub();
        if (vp != null)
        {
            vp.Stop();
            targetTime = -1.0;
        }
    }

    /// <summary>
    /// 彻底释放硬件解码器资源，防止多模块累积导致解码器卡死
    /// </summary>
    public void Release()
    {
        CancelScrub();
        if (vp != null)
        {
            vp.Stop();
            vp.enabled = false;
            vp.enabled = true;
            targetTime = -1.0;
        }
    }

    private void OnDisable()
    {
        CancelScrub();
    }

    private void OnDestroy()
    {
        CancelScrub();
    }

    void Update()
    {
        if (vp == null) return;

        if (scrubMode)
        {
            if (isSeeking && (Time.unscaledTime - seekStartRealtime > 0.8f))
            {
                isSeeking = false;
                UnsubscribeScrubSeek();
                waitingSeekFrame = true;
                seekFrameWaitStartRealtime = Time.unscaledTime;
            }

            if (waitingSeekFrame && (Time.unscaledTime - seekFrameWaitStartRealtime > 0.4f))
            {
                waitingSeekFrame = false;
                UnsubscribeScrubFrameReady();
            }

            if (!isSeeking && !waitingSeekFrame)
            {
                double remaining = scrubTargetTime - vp.time;
                bool playGraceExpired = (scrubPlayRequestedRealtime < 0f) || (Time.unscaledTime - scrubPlayRequestedRealtime > 0.25f);

                if (remaining <= 0.025 || (!vp.isPlaying && playGraceExpired))
                {
                    if (vp.canSetPlaybackSpeed) vp.playbackSpeed = 1f;
                    if (vp.isPlaying) vp.Pause();
                    scrubMode = false;
                }
                else
                {
                    if (!vp.isPlaying)
                    {
                        vp.Play();
                        scrubPlayRequestedRealtime = Time.unscaledTime;
                    }
                    if (vp.canSetPlaybackSpeed)
                    {
                        vp.playbackSpeed = Mathf.Clamp((float)(remaining / 0.12), 0.75f, 4.0f);
                    }
                }
            }
            return;
        }

        // 如果我们设定了 targetTime，就监控进度，到了就暂停
        if (targetTime >= 0.0 && vp.isPlaying)
        {
            if (vp.time >= targetTime || vp.time >= vp.length - 0.05)
            {
                vp.Pause();
                vp.time = targetTime; // 贴合目标时间
                targetTime = -1.0;
            }
        }
    }
}
