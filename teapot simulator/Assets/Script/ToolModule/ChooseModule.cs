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

    private VideoController ctrlMain;
    private VideoController ctrlFail;
    private VideoController ctrlWrong;

    private bool dragging = false;
    private bool prepared = false;
    private bool hasReachedC = false;
    private float currentPercent = 0f;
    private Coroutine activeRoutine = null;

    // ----------------------------------------------------
    // 初始化
    // ----------------------------------------------------
    void Start()
    {
        mainCam = Camera.main;
        global = GlobalProgressManager.Instance;
        BindControllers();
        HideAll();
        SetAreaAlpha(normalAlpha);
        UpdateDottedLineAlpha();
    }

    public void Init(ChooseController ctrl)
    {
        controller = ctrl;
        global = GlobalProgressManager.Instance;
        BindControllers();
        HideAll();
        SetAreaAlpha(normalAlpha);
        UpdateDottedLineAlpha();
    }

    private void BindControllers()
    {
        if (videoMain != null && ctrlMain == null) ctrlMain = VideoController.GetOrCreate(videoMain);
        if (videoFail != null && ctrlFail == null) ctrlFail = VideoController.GetOrCreate(videoFail);
        if (videoWrong != null && ctrlWrong == null) ctrlWrong = VideoController.GetOrCreate(videoWrong);
    }

    // ----------------------------------------------------
    // 主循环
    // ----------------------------------------------------
    void Update()
    {
        if (controller == null || (global != null && global.sceneTransitioning)) return;
        if (!mainCam) mainCam = Camera.main;

        // 平滑更新 dotted line 透明度
        UpdateDottedLineAlpha();

        if (mainCam == null) return;
        Vector2 mouseWorld = mainCam.ScreenToWorldPoint(Input.mousePosition);

        // 点击 A 区 → 开始交互
        if (Input.GetMouseButtonDown(0))
        {
            if (!dragging)
            {
                if (areaA != null && areaA.OverlapPoint(mouseWorld))
                {
                    if (global != null && global.currentOrder != orderIndex)
                    {
                        Debug.Log($"❌ Wrong order clicked on {moduleName}");
                        StartSequence(PlayWrongOrderSelf());
                        return;
                    }

                    controller.NotifyModuleStarted(this);
                    dragging = true;
                    hasReachedC = false;
                    currentPercent = 0f;
                    StartSequence(RestartAndPrepareMain());
                    Debug.Log($"[{moduleName}] ✅ 开始拖动播放");
                }
            }
        }

        // 拖动控制视频帧 (通过 VideoController 30fps 节流，完全杜绝每帧生成协程和 AVPlayer 解码塞车)
        if (dragging && Input.GetMouseButton(0) && prepared)
        {
            UpdateVideoProgress(mouseWorld);
        }

        // 松手判断成功 / 失败 (带手感容差：划满 85% 或曾触达 C 区均判成功)
        if (Input.GetMouseButtonUp(0) && dragging)
        {
            dragging = false;

            bool isSuccess = hasReachedC ||
                             (areaC != null && areaC.OverlapPoint(mouseWorld)) ||
                             currentPercent >= 0.85f;

            if (isSuccess)
            {
                Debug.Log($"[{moduleName}] 🏁 成功到达终点 (progress={currentPercent:P0}) → 播放完结");
                StartSequence(PlayToEnd(videoMain, rawMain, true));
            }
            else
            {
                Debug.Log($"[{moduleName}] ❌ 未到达终点 (progress={currentPercent:P0}) → 播放失败");
                StartSequence(PlayFailVideo());
            }
        }
    }

    private void StartSequence(IEnumerator routine)
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
        }
        activeRoutine = StartCoroutine(routine);
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

        BindControllers();

        bool isPrepared = false;
        ctrlMain.PrepareNow(() => isPrepared = true);
        while (!isPrepared) yield return null;

        rawMain.enabled = true;

        // 预渲染第 0 帧到 RenderTexture，并给底层渲染管线 0.04s 缓冲防黑屏闪烁
        videoMain.time = 0;
        videoMain.Play();
        yield return new WaitForEndOfFrame();
        yield return new WaitForSecondsRealtime(0.04f);
        videoMain.Pause();
        videoMain.time = 0;

        prepared = true;
        Debug.Log($"[{moduleName}] 🎞️ 主视频准备完毕 (length {videoMain.length:F2}s)");
    }

    private void UpdateVideoProgress(Vector2 mousePos)
    {
        if (videoMain == null || !videoMain.isPrepared) return;

        if (startPoint != null && endPoint != null)
        {
            Vector2 A = startPoint.position;
            Vector2 C = endPoint.position;
            Vector2 AC = C - A;
            Vector2 AM = mousePos - A;

            float mag = AC.magnitude;
            if (mag > 0.001f)
            {
                float projection = Vector2.Dot(AM, AC.normalized);
                currentPercent = Mathf.Clamp01(projection / mag);
            }
        }

        // 终点触达标记
        if (areaC != null && areaC.OverlapPoint(mousePos))
        {
            hasReachedC = true;
        }

        // 接入 VideoController 进行 30fps 节流 Scrubbing，零 GC 分配，彻底根除 Mac 滑动掉帧
        if (ctrlMain != null)
        {
            ctrlMain.ScrubTo(currentPercent);
        }
        else
        {
            videoMain.time = currentPercent * videoMain.length;
            videoMain.StepForward();
        }
    }

    private IEnumerator PlayToEnd(VideoPlayer vp, RawImage raw, bool success)
    {
        if (vp == null || raw == null) yield break;

        double len = vp.length > 0 ? vp.length : (vp.clip != null ? vp.clip.length : 1.0);
        vp.time = currentPercent * len;
        vp.Play();

        // 留一点缓冲时间等视频起播
        float timeout = 1f;
        while (!vp.isPlaying && timeout > 0)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        while (vp.isPlaying)
        {
            yield return null;
        }

        vp.Stop();
        raw.enabled = false;
        if (canvasRoot) canvasRoot.SetActive(false);

        if (success)
        {
            if (controller != null)
            {
                controller.OnModuleCompleted(this);
            }
            else if (!string.IsNullOrEmpty(nextSceneName))
            {
                Debug.Log($"[{moduleName}] ✅ 无控制器，直接切换场景 → {nextSceneName}");
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

        BindControllers();

        bool isPrepared = false;
        ctrlFail.PrepareNow(() => isPrepared = true);
        while (!isPrepared) yield return null;

        rawFail.enabled = true;
        videoFail.time = 0;
        videoFail.Play();
        Debug.Log($"[{moduleName}] ▶ 播放失败视频");

        float timeout = 1f;
        while (!videoFail.isPlaying && timeout > 0)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        while (videoFail.isPlaying)
        {
            yield return null;
        }

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

        BindControllers();

        bool isPrepared = false;
        ctrlWrong.PrepareNow(() => isPrepared = true);
        while (!isPrepared) yield return null;

        rawWrong.enabled = true;
        videoWrong.time = 0;
        videoWrong.Play();
        Debug.Log($"[{moduleName}] ▶ 播放 Wrong 视频 ✅");

        float timeout = 1f;
        while (!videoWrong.isPlaying && timeout > 0)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        while (videoWrong.isPlaying)
        {
            yield return null;
        }

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

    // 更新 dotted line 透明度
    private void UpdateDottedLineAlpha()
    {
        if (dottedLine == null || global == null) return;

        float targetAlpha = (global.currentOrder == orderIndex)
            ? dottedAlphaActive
            : dottedAlphaInactive;

        Color c = dottedLine.color;
        if (Mathf.Abs(c.a - targetAlpha) > 0.005f)
        {
            c.a = Mathf.Lerp(c.a, targetAlpha, Time.deltaTime * 6f);
            dottedLine.color = c;
        }
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
            if (ctrlMain != null) ctrlMain.StopAndReset();
            else if (videoMain != null) videoMain.Stop();

            if (ctrlFail != null) ctrlFail.StopAndReset();
            else if (videoFail != null) videoFail.Stop();

            if (ctrlWrong != null) ctrlWrong.StopAndReset();
            else if (videoWrong != null) videoWrong.Stop();
        }
    }
}
