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
    private float scrubInterval = 0.033f; // 限制 Scrub 每秒最多 30 次，防掉帧

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
        vp.Prepare();
        while (!vp.isPrepared)
        {
            yield return null;
        }
        onPrepared?.Invoke();
    }

    /// <summary>
    /// 从指定时间点开始，播放一小段（PatModule 点击反馈使用）
    /// </summary>
    public void PlayChunk(double startTime, double duration)
    {
        if (vp == null || vp.clip == null) return;
        
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
        
        // 等待播放结束
        while (vp.isPlaying)
        {
            yield return null;
        }
        
        onComplete?.Invoke();
    }

    /// <summary>
    /// 平滑地进行进度搓划 (CutModule 核心功能)
    /// </summary>
    public void ScrubTo(float progress)
    {
        if (vp == null || vp.clip == null) return;
        
        if (!vp.isPrepared)
        {
            vp.Prepare();
            return; 
        }

        // 限制 Scrub 的频率，否则强制 StepForward 会导致主线程卡顿
        if (Time.unscaledTime - lastScrubTime < scrubInterval) return;
        lastScrubTime = Time.unscaledTime;

        targetTime = -1.0; 
        double time = vp.length * Mathf.Clamp01(progress);
        
        if (vp.isPlaying) vp.Pause();
        
        vp.time = time;
        vp.StepForward();
    }

    public void Pause()
    {
        targetTime = -1.0;
        if (vp != null) vp.Pause();
    }
    
    public void StopAndReset()
    {
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
        if (vp != null)
        {
            vp.Stop();
            vp.enabled = false;
            vp.enabled = true;
            targetTime = -1.0;
        }
    }

    void Update()
    {
        // 如果我们设定了 targetTime，就监控进度，到了就暂停
        if (vp != null && targetTime >= 0.0 && vp.isPlaying)
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
