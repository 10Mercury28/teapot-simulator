using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using System.Collections;

public class SceneBootstrapper : MonoBehaviour
{
    [Header("🎬 启动视频")]
    public VideoPlayer introVideo;              // 拖入启动视频
    public bool playOnStart = true;             // 是否自动播放
    public bool skipWithClick = true;           // 是否允许点击跳过
    public string nextSceneName = "chooseToolBasedOnCut"; // 下一场景名

    [Header("⚙️ 选项")]
    public bool useBlinkTransition = true;      // 使用 SceneBlinkTransition
    public float fadeDelay = 0.2f;              // 视频结束后延迟

    private bool hasStarted = false;
    private bool hasFinished = false;

    void Start()
    {
        if (introVideo != null)
        {
            introVideo.loopPointReached += OnVideoEnd;
            if (playOnStart)
            {
                PlayIntro();
            }
        }
        else
        {
            Debug.LogWarning("⚠️ 未分配启动视频，立即加载下一个场景。");
            LoadNextScene();
        }
    }

    void Update()
    {
        // 允许用户点击跳过
        if (skipWithClick && !hasFinished && Input.GetMouseButtonDown(0))
        {
            Debug.Log("⏭ 用户点击跳过视频");
            OnVideoEnd(introVideo);
        }
    }

    // ----------------------------------------------------
    // 播放启动视频
    // ----------------------------------------------------
    public void PlayIntro()
    {
        if (hasStarted || introVideo == null) return;
        hasStarted = true;

        introVideo.Stop();
        introVideo.Play();
        Debug.Log("▶️ 启动视频开始播放");
    }

    // ----------------------------------------------------
    // 视频播放结束事件
    // ----------------------------------------------------
    private void OnVideoEnd(VideoPlayer vp)
    {
        if (hasFinished) return;
        hasFinished = true;

        Debug.Log("✅ 视频播放结束，准备跳转场景...");
        StartCoroutine(LoadNextSceneAfterDelay(fadeDelay));
    }

    private IEnumerator LoadNextSceneAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        LoadNextScene();
    }

    // ----------------------------------------------------
    // 加载下一个场景
    // ----------------------------------------------------
    private void LoadNextScene()
    {
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogError("❌ 未设置下一场景名称，无法加载！");
            return;
        }

        // 通知 GlobalProgressManager 重置进度
        if (GlobalProgressManager.Instance != null)
        {
            GlobalProgressManager.Instance.ResetProgress();
        }

        // 如果使用场景过渡
        if (useBlinkTransition && SceneBlinkTransition.Instance != null)
        {
            SceneBlinkTransition.Instance.LoadSceneConcealed(nextSceneName);
        }
        else
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }
}
