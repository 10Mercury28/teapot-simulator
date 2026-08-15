using UnityEngine;
using UnityEngine.Video;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class IntroSceneController : MonoBehaviour
{
    [Header("Video Settings")]
    public VideoPlayer videoPlayer;          // 视频组件
    public RawImage rawImage;                // 显示层（RawImage）
    public string nextSceneName = "NextScene"; // 播放完跳转的场景名

    [Header("UI")]
    public Button playButton;                // 播放按钮

    private bool hasStarted = false;

    void Start()
    {
        // 确保视频组件存在
        if (videoPlayer == null || rawImage == null)
        {
            Debug.LogError("⚠️ VideoPlayer 或 RawImage 未绑定！");
            return;
        }

        // 准备视频（不自动播放）
        videoPlayer.playOnAwake = false;
        videoPlayer.Pause();

        // 设置 RawImage 的纹理
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.Prepare();

        // 绑定按钮事件
        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayClicked);
        }

        // 视频结束回调
        videoPlayer.loopPointReached += OnVideoEnd;
    }

    void OnVideoPrepared(VideoPlayer vp)
    {
        rawImage.texture = vp.texture;
        vp.frame = 0;
        vp.Pause(); // 卡在第一帧
        Debug.Log("🎞 视频准备完成，停在第一帧。");
    }

    void OnPlayClicked()
    {
        if (hasStarted) return;

        hasStarted = true;
        playButton.gameObject.SetActive(false);  // 隐藏播放按钮
        videoPlayer.Play();
        Debug.Log("▶️ 视频开始播放。");
    }

    void OnVideoEnd(VideoPlayer vp)
    {
        Debug.Log("✅ 视频播放结束，准备跳转场景...");
        StartCoroutine(LoadNextScene());
    }

    IEnumerator LoadNextScene()
    {
        yield return new WaitForSeconds(1f); // 给一点缓冲
        SceneManager.LoadScene(nextSceneName);
    }
}