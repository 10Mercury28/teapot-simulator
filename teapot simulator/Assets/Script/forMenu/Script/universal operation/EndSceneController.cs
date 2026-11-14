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

    [Header("📜 Rolling Credits (可选滚动字幕)")]
    [Tooltip("包含滚动文字的 RectTransform（挂 TMP_Text + ContentSizeFitter 垂直 Preferred Size）")]
    public RectTransform creditsScrollContent;
    [Tooltip("遮罩容器 RectTransform（挂 RectMask2D）；留空则默认按父物体或 1080 高度计算起止位置")]
    public RectTransform creditsViewport;
    [Tooltip("可选：整个滚动字幕面板的 CanvasGroup，用于柔和淡入淡出")]
    public CanvasGroup creditsGroup;
    [Tooltip("滚动速度（像素/秒）")]
    public float creditsScrollSpeed = 85f;
    [Tooltip("按住鼠标左键或空格键时的快进倍率")]
    public float creditsFastForwardMultiplier = 3.5f;
    [Tooltip("视频播完后停顿多久开始滚动")]
    public float creditsStartDelay = 0.4f;
    [Tooltip("字幕滚完后的淡入/淡出时长")]
    public float creditsFadeDuration = 0.6f;
    [Tooltip("滚动结束后是否淡出字幕面板再显示返回按钮")]
    public bool fadeOutCreditsAfterRoll = true;
    [Tooltip("滚动字幕期间先隐藏 returnButton，播完再显示")]
    public bool hideReturnButtonDuringCredits = true;

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

        // 初始化滚动字幕状态（若已配置）
        if (creditsGroup != null)
        {
            creditsGroup.alpha = 0f;
            creditsGroup.interactable = false;
            creditsGroup.blocksRaycasts = false;
        }
        if (creditsScrollContent != null)
        {
            creditsScrollContent.gameObject.SetActive(false);
            if (hideReturnButtonDuringCredits && returnButton != null)
            {
                returnButton.gameObject.SetActive(false);
            }
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
        StartCoroutine(EndSequenceRoutine());
    }

    private IEnumerator EndSequenceRoutine()
    {
        if (creditsScrollContent != null)
        {
            yield return StartCoroutine(RollCreditsRoutine());
        }

        yield return StartCoroutine(ShowEndUI());
    }

    private IEnumerator RollCreditsRoutine()
    {
        creditsScrollContent.gameObject.SetActive(true);
        if (creditsGroup != null)
        {
            creditsGroup.gameObject.SetActive(true);
            creditsGroup.alpha = 0f;
        }

        // 强制刷新一次布局，确保 ContentSizeFitter 已经算出真实文本总高度
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(creditsScrollContent);

        RectTransform viewport = creditsViewport != null
            ? creditsViewport
            : creditsScrollContent.parent as RectTransform;
        float viewportHeight = viewport != null && viewport.rect.height > 1f
            ? viewport.rect.height
            : 1080f;
        float contentHeight = Mathf.Max(creditsScrollContent.rect.height, 100f);

        // 对任意 Anchor/Pivot 自动对齐：从视口底边外侧滚到视口顶边外侧
        float anchorCenterY = 0.5f * (creditsScrollContent.anchorMin.y + creditsScrollContent.anchorMax.y);
        float startY = -anchorCenterY * viewportHeight - (1f - creditsScrollContent.pivot.y) * contentHeight;
        float endY = (1f - anchorCenterY) * viewportHeight + creditsScrollContent.pivot.y * contentHeight;

        Vector2 pos = creditsScrollContent.anchoredPosition;
        pos.y = startY;
        creditsScrollContent.anchoredPosition = pos;

        if (creditsStartDelay > 0f)
        {
            yield return new WaitForSeconds(creditsStartDelay);
        }

        if (creditsGroup != null && creditsFadeDuration > 0f)
        {
            float fadeT = 0f;
            while (fadeT < creditsFadeDuration)
            {
                fadeT += Time.deltaTime;
                creditsGroup.alpha = Mathf.Clamp01(fadeT / creditsFadeDuration);
                yield return null;
            }
            creditsGroup.alpha = 1f;
        }
        else if (creditsGroup != null)
        {
            creditsGroup.alpha = 1f;
        }

        float speed = Mathf.Max(10f, creditsScrollSpeed);
        while (creditsScrollContent.anchoredPosition.y < endY)
        {
            bool fastForward = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.DownArrow);
            float mult = fastForward ? Mathf.Max(1f, creditsFastForwardMultiplier) : 1f;
            pos.y = Mathf.MoveTowards(creditsScrollContent.anchoredPosition.y, endY, speed * mult * Time.deltaTime);
            creditsScrollContent.anchoredPosition = pos;
            yield return null;
        }

        if (fadeOutCreditsAfterRoll && creditsGroup != null)
        {
            float fadeT = 0f;
            float dur = Mathf.Max(0.01f, creditsFadeDuration);
            while (fadeT < dur)
            {
                fadeT += Time.deltaTime;
                creditsGroup.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(fadeT / dur));
                yield return null;
            }
            creditsGroup.alpha = 0f;
        }
    }

    private IEnumerator ShowEndUI()
    {
        if (returnButton != null && !returnButton.gameObject.activeSelf)
        {
            returnButton.gameObject.SetActive(true);
        }

        if (endUI == null)
        {
            Debug.Log("📝 [EndSceneController] End UI 未绑定，直接展示场景按钮。");
            yield break;
        }

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
