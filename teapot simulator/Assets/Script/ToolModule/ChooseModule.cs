using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.SceneManagement;
using System.Collections;

public class ChooseModule : MonoBehaviour
{
    [Header("Module Info")]
    public string moduleName;
    public int orderIndex;
    public string nextSceneName;

    [Header("区域碰撞体")]
    public Collider2D areaA;
    public Collider2D areaB;
    public Collider2D areaC;

    [Header("视频组件")]
    public VideoPlayer videoMain;   // 主视频（拖动）
    public VideoPlayer videoFail;   // 失败视频
    public VideoPlayer videoWrong;  // 顺序错误视频

    [Header("Raw 显示层")]
    public RawImage rawMain;
    public RawImage rawFail;
    public RawImage rawWrong;

    [Header("Canvas 控制")]
    public GameObject canvasRoot;
    public CanvasGroup canvasGroup;

    [Header("Progress Mapping")]
    public Transform startPoint;
    public Transform endPoint;

    [Header("区域透明度")]
    [Range(0f, 1f)] public float normalAlpha = 0.6f;
    [Range(0f, 1f)] public float failAlpha = 0.2f;
    [Range(0f, 1f)] public float completeAlpha = 0f;

    [Header("可视层")]
    public SpriteRenderer areaASprite;
    public SpriteRenderer areaBSprite;
    public SpriteRenderer areaCSprite;

    [Header("虚线控制（Dotted Line）")]
    public SpriteRenderer dottedLine;                     // 拖入虚线Sprite
    [Range(0f, 1f)] public float dottedAlphaActive = 0.4f; // 当前模块时亮度
    [Range(0f, 1f)] public float dottedAlphaInactive = 0.2f; // 非当前模块时亮度

    private ChooseController controller;
    private GlobalProgressManager global;
    private Camera mainCam;

    private bool dragging = false;
    private bool prepared = false;
    private float currentPercent = 0f;

    // ----------------------------------------------------
    // 初始化
    // ----------------------------------------------------
    void Start()
    {
        mainCam = Camera.main;
        global = GlobalProgressManager.Instance;
        HideAll();
        SetAreaAlpha(normalAlpha);
        UpdateDottedLineAlpha(); // 初始化时同步一次
    }

    public void Init(ChooseController ctrl)
    {
        controller = ctrl;
        global = GlobalProgressManager.Instance;
        HideAll();
        SetAreaAlpha(normalAlpha);
        UpdateDottedLineAlpha(); // 初始化时同步一次
    }

    // ----------------------------------------------------
    // 主循环
    // ----------------------------------------------------
    void Update()
    {
        if (controller == null || (global != null && global.sceneTransitioning)) return;
        if (!mainCam) mainCam = Camera.main;

        // 🔸 每帧更新 dotted line 透明度（平滑）
        UpdateDottedLineAlpha();

        Vector2 mouseWorld = mainCam.ScreenToWorldPoint(Input.mousePosition);

        // 点击 A 区 → 开始交互
        if (Input.GetMouseButtonDown(0))
        {
            Debug.Log($"[ChooseModule {moduleName}] Mouse down! dragging={dragging}");
            if (!dragging)
            {
                if (areaA != null)
                {
                    bool overlap = areaA.OverlapPoint(mouseWorld);
                    Debug.Log($"[ChooseModule {moduleName}] areaA overlap with {mouseWorld}? {overlap}");
                    
                    if (overlap)
                    {
                        if (global != null)
                        {
                            Debug.Log($"[ChooseModule {moduleName}] global.currentOrder={global.currentOrder}, this.orderIndex={orderIndex}");
                            if (global.currentOrder != orderIndex)
                            {
                                Debug.Log($"❌ Wrong order clicked on {moduleName}");
                                StartCoroutine(PlayWrongOrderSelf());
                                return;
                            }
                        }

                        controller.NotifyModuleStarted(this);
                        dragging = true;
                        currentPercent = 0f;
                        StartCoroutine(RestartAndPrepareMain());
                        Debug.Log($"[{moduleName}] ✅ 开始拖动播放");
                    }
                }
                else
                {
                    Debug.LogWarning($"[ChooseModule {moduleName}] areaA is NULL!");
                }
            }
        }

        // 拖动控制视频帧
        if (dragging && Input.GetMouseButton(0) && prepared && videoMain != null && videoMain.isPrepared)
        {
            UpdateVideoProgress(mouseWorld);
        }

        // 松手判断成功 / 失败
        if (Input.GetMouseButtonUp(0) && dragging)
        {
            dragging = false;

            if (areaC && areaC.OverlapPoint(mouseWorld))
            {
                Debug.Log($"[{moduleName}] 🏁 到达C区 → 播放完结");
                StartCoroutine(PlayToEnd(videoMain, rawMain, true));
            }
            else
            {
                Debug.Log($"[{moduleName}] ❌ 未到达C区 → 播放失败");
                StartCoroutine(PlayFailVideo());
            }
        }
    }

    // ----------------------------------------------------
    // 视频播放逻辑
    // ----------------------------------------------------
    private IEnumerator RestartAndPrepareMain()
    {
        HideAll();
        if (canvasRoot) canvasRoot.SetActive(true);
        if (canvasGroup) canvasGroup.alpha = 1f;

        if (videoMain == null || rawMain == null) yield break;

        videoMain.Stop();
        videoMain.time = 0;
        videoMain.Prepare();
        while (!videoMain.isPrepared)
            yield return null;

        rawMain.enabled = true;
        videoMain.Play();
        videoMain.Pause();
        prepared = true;

        Debug.Log($"[{moduleName}] 🎞️ 主视频准备完毕 (length {videoMain.clip.length:F2}s)");
    }

    private void UpdateVideoProgress(Vector2 mousePos)
    {
        if (!videoMain.isPrepared) return;

        Vector2 A = startPoint.position;
        Vector2 C = endPoint.position;
        Vector2 AC = C - A;
        Vector2 AM = mousePos - A;

        float projection = Vector2.Dot(AM, AC.normalized);
        currentPercent = Mathf.Clamp01(projection / AC.magnitude);

        double targetTime = currentPercent * videoMain.clip.length;
        videoMain.time = targetTime;
        videoMain.Play();
        StartCoroutine(ForcePauseAfterFrame(videoMain));

        Debug.Log($"{moduleName} 拖动进度: {currentPercent * 100f:F1}% → time={videoMain.time:F2}s/{videoMain.clip.length:F2}s");
    }

    private IEnumerator ForcePauseAfterFrame(VideoPlayer vp)
    {
        yield return new WaitForEndOfFrame();
        vp.Pause();
    }

    private IEnumerator PlayToEnd(VideoPlayer vp, RawImage raw, bool success)
    {
        if (vp == null || raw == null) yield break;
        vp.time = currentPercent * vp.clip.length;
        vp.Play();
        yield return new WaitUntil(() => !vp.isPlaying);
        vp.Stop();
        raw.enabled = false;
        if (canvasRoot) canvasRoot.SetActive(false);

        if (success)
        {
            controller.OnModuleCompleted(this);
            if (!string.IsNullOrEmpty(nextSceneName))
            {
                Debug.Log($"[{moduleName}] ✅ 切换场景 → {nextSceneName}");
                SceneManager.LoadScene(nextSceneName);
            }
        }
    }

    private IEnumerator PlayFailVideo()
    {
        HideAll();
        if (canvasRoot) canvasRoot.SetActive(true);
        if (canvasGroup) canvasGroup.alpha = 1f;

        if (videoFail == null || rawFail == null) yield break;

        videoFail.Stop();
        videoFail.time = 0;
        videoFail.Prepare();
        while (!videoFail.isPrepared) yield return null;

        rawFail.enabled = true;
        videoFail.Play();
        Debug.Log($"[{moduleName}] ▶ 播放失败视频");
        yield return new WaitForSeconds((float)videoFail.clip.length);
        videoFail.Stop();
        rawFail.enabled = false;
        if (canvasRoot) canvasRoot.SetActive(false);
    }

    private IEnumerator PlayWrongOrderSelf()
    {
        HideAll();
        if (canvasRoot) canvasRoot.SetActive(true);
        if (canvasGroup) canvasGroup.alpha = 1f;

        if (videoWrong == null || rawWrong == null)
        {
            Debug.LogWarning($"[{moduleName}] ⚠ 无 Wrong 视频");
            yield break;
        }

        videoWrong.Stop();
        videoWrong.time = 0;
        videoWrong.Prepare();
        while (!videoWrong.isPrepared) yield return null;

        rawWrong.enabled = true;
        videoWrong.Play();
        Debug.Log($"[{moduleName}] ▶ 播放 Wrong 视频 ✅");
        yield return new WaitForSeconds((float)videoWrong.clip.length);
        videoWrong.Stop();
        rawWrong.enabled = false;
        if (canvasRoot) canvasRoot.SetActive(false);
        Debug.Log($"[{moduleName}] Wrong 视频播放结束。");
    }

    // ----------------------------------------------------
    // 可视化与辅助函数
    // ----------------------------------------------------
    private void HideAll()
    {
        if (rawMain) rawMain.enabled = false;
        if (rawFail) rawFail.enabled = false;
        if (rawWrong) rawWrong.enabled = false;
        if (canvasRoot) canvasRoot.SetActive(false);
        if (canvasGroup) canvasGroup.alpha = 0f;
    }

    private void SetAreaAlpha(float alpha)
    {
        if (areaASprite) SetAlpha(areaASprite, alpha);
        if (areaBSprite) SetAlpha(areaBSprite, alpha);
        if (areaCSprite) SetAlpha(areaCSprite, alpha);
    }

    private void SetAlpha(SpriteRenderer sr, float alpha)
    {
        Color c = sr.color;
        c.a = alpha;
        sr.color = c;
    }

    // 🔸 新增：更新 dotted line 透明度
    private void UpdateDottedLineAlpha()
    {
        if (dottedLine == null || global == null) return;

        float targetAlpha = (global.currentOrder == orderIndex)
            ? dottedAlphaActive
            : dottedAlphaInactive;

        Color c = dottedLine.color;
        c.a = Mathf.Lerp(c.a, targetAlpha, Time.deltaTime * 6f);
        dottedLine.color = c;
    }

    public void HideAllForExternal() => HideAll();
    public void SetModuleActive(bool active)
    {
        if (canvasRoot)
            canvasRoot.SetActive(active);
        if (canvasGroup)
            canvasGroup.alpha = active ? 1f : 0f;

        if (!active)
        {
            if (videoMain) videoMain.Pause();
            if (videoFail) videoFail.Pause();
            if (videoWrong) videoWrong.Pause();
        }
    }
}
