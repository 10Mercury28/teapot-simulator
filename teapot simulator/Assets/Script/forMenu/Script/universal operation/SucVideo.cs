using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[RequireComponent(typeof(VideoPlayer))]
public class SucVideo : MonoBehaviour
{
    [Header("🎬 成功视频组件")]
    public RawImage rawImage;
    public VideoPlayer videoPlayer;

    [Header("📺 播放状态")]
    public bool isPlaying = false;
    public bool isFinished = false;

    void Awake()
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();

        if (rawImage == null)
            rawImage = GetComponentInChildren<RawImage>();

        // 注册结束回调
        videoPlayer.loopPointReached += OnVideoComplete;
        Debug.Log("🟢 [SucVideo] VideoPlayer 已注册回调。");
    }

    void Start()
    {
        if (videoPlayer == null)
        {
            Debug.LogError("❌ [SucVideo] VideoPlayer 未绑定！");
            return;
        }

        // 如果 RawImage 存在，将视频输出目标设置为它
        if (rawImage != null)
            videoPlayer.targetTexture = null;

        PlaySuccessVideo();
    }

    public void PlaySuccessVideo()
    {
        if (videoPlayer == null) return;

        videoPlayer.Play();
        isPlaying = true;
        isFinished = false;
        Debug.Log("▶️ [SucVideo] 成功视频播放开始。");
    }

    private void OnVideoComplete(VideoPlayer vp)
    {
        isPlaying = false;
        isFinished = true;
        Debug.Log("✅ [SucVideo] 播放完毕！");
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
            videoPlayer.loopPointReached -= OnVideoComplete;

        Debug.LogWarning($"💀 [SucVideo] 被销毁！对象名：{gameObject.name}");
    }
}