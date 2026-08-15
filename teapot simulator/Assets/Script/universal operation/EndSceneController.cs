using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.SceneManagement;
using System.Collections;

public class EndSceneController : MonoBehaviour
{
    [Header("🎬 End Video")]
    public VideoPlayer endVideo;        // 视频播放器
    public RawImage endRaw;             // 视频显示RawImage

    [Header("📝 UI Elements")]
    public CanvasGroup endUI;           // “The End” + 按钮的CanvasGroup
    public Text endText;
    public Button returnButton;

    [Header("⚙️ Settings")]
    [Tooltip("返回的场景名，在 Inspector 中填写")]
    public string returnSceneName = "IntroScene";  // 👈 回到第一幕（IntroScene）
    public float fadeInDuration = 1.5f;            // UI 淡入时间

    private void Start()
    {
        // 初始化UI隐藏
        if (endUI != null)
        {
            endUI.alpha = 0f;
            endUI.interactable = false;
            endUI.blocksRaycasts = false;
        }

        // 注册按钮事件
        if (returnButton != null)
            returnButton.onClick.AddListener(ReturnToStart);

        // 自动播放视频
        if (endVideo != null)
        {
            endVideo.loopPointReached += OnVideoEnd;
            endVideo.Play();
            Debug.Log("▶️ [EndSceneController] 视频开始播放。");
        }
    }

    private void OnVideoEnd(VideoPlayer vp)
    {
        Debug.Log("🎬 [EndSceneController] 视频播放结束，停在最后一帧。");
        vp.Pause(); // 停在最后一帧
        StartCoroutine(ShowEndUI());
    }

    private IEnumerator ShowEndUI()
    {
        float t = 0f;

        endUI.interactable = true;
        endUI.blocksRaycasts = true;

        while (t < fadeInDuration)
        {
            t += Time.deltaTime;
            endUI.alpha = Mathf.Lerp(0f, 1f, t / fadeInDuration);
            yield return null;
        }

        endUI.alpha = 1f;
        Debug.Log("📝 [EndSceneController] The End UI 显示完毕。");
    }

    private void ReturnToStart()
    {
        Debug.Log($"🏁 [EndSceneController] 返回场景：{returnSceneName}");

        // ✅ 1. 重置全局进度（GlobalProgressManager）
        if (GlobalProgressManager.Instance != null)
        {
            GlobalProgressManager.Instance.ResetProgress();
        }

        // ✅ 2. 清除全局对象，防止残留（如有）
        foreach (var obj in FindObjectsOfType<GlobalProgressManager>())
        {
            Destroy(obj.gameObject);
        }

        // ✅ 3. 跳转回起始场景
        SceneManager.LoadScene(returnSceneName);
    }
}
