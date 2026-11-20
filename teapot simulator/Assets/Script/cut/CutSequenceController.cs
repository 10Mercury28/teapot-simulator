using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;

public class CutSequenceController : MonoBehaviour
{
    [Header("模块列表")]
    public CutModule[] modules;

    [Header("=== Day 2 弹窗打断配置 ===")]
    [Tooltip("是否自动为 Cut 1 和 Cut 2 注入打断配置（Cut 1 在 Prepare 时按交心弹出 Day2_HA，Cut 2 在 Prepare 时弹出 Day2_ShaoA）")]
    public bool autoConfigureDay2Popups = true;

    [Tooltip("调试开关：强制模拟前一天已交心（LikeU == true），方便直接在此场景测试居士弹窗")]
    public bool debugSimulateLikeU = false;

    [Header("=== 全部完成后的 Complete 视频 ===")]
    [Tooltip("两段 Cut 及收尾转场全部播完后，播放的最终完成视频（如 cut complete.mp4）")]
    public VideoClip completeVideoClip;

    [Header("状态")]
    public bool isAllComplete = false;

    private bool hasSentGlobalSignal = false;
    private RenderTexture runtimeCompleteRT;
    private GameObject runtimeCompleteCanvasObj;



    private IEnumerator Start()

    {

        yield return new WaitForEndOfFrame();

        GlobalProgressManager.EnsureInstance();
        GlobalProgressManager.CurrentDay = 2;



        isAllComplete = false;

        hasSentGlobalSignal = false;



        // 调试模拟交心状态

        if (debugSimulateLikeU)

        {

            GlobalProgressManager.IsLikeU = true;

            Debug.Log("🧪 [CutSequenceController] 调试模式开启：强制模拟前一天已交心（LikeU = true）。");

        }



        // 自动装配 Day 2 弹窗打断逻辑
        if (autoConfigureDay2Popups && modules != null)
        {
            // Module 0 (Cut 1): 进入 Prepare 循环时，若前一天交心则弹出居士微信 (Day2_HA)
            if (modules.Length > 0 && modules[0] != null)
            {
                modules[0].enableEnterInterrupt = true;
                modules[0].requireLikeU = true;
                if (modules[0].interruptPopupSO == null && string.IsNullOrEmpty(modules[0].interruptPopupId))
                {
                    modules[0].interruptPopupId = "Day2_HA";
                }
                modules[0].enableTransitionInterrupt = false;
                Debug.Log("⚙️ [CutSequenceController] 已配置 Cut 1 Prepare 入场弹窗 (Day2_HA, requireLikeU=true)");
            }

            // Module 1 (Cut 2): 进入 Prepare 循环时无条件弹出老师微信 (Day2_ShaoA)
            if (modules.Length > 1 && modules[1] != null)
            {
                modules[1].enableEnterInterrupt = true;
                modules[1].requireLikeU = false;
                if (modules[1].interruptPopupSO == null && string.IsNullOrEmpty(modules[1].interruptPopupId))
                {
                    modules[1].interruptPopupId = "Day2_ShaoA";
                }
                modules[1].enableTransitionInterrupt = false;
                Debug.Log("⚙️ [CutSequenceController] 已配置 Cut 2 Prepare 入场弹窗 (Day2_ShaoA, 无条件弹出)");
            }
        }

        // 一开始关闭所有 Cut Module
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i] != null)
            {
                modules[i].gameObject.SetActive(false);
            }
        }

        // ⭐ 开场白 (BeforeStartController) 协同：等待玩家点击'开始'
        var beforeStart = FindObjectOfType<BeforeStartController>();
        if (beforeStart != null)
        {
            beforeStart.deferUnlockToSequenceController = true;
            Debug.Log("⏳ [CutSequenceController] 检测到开场白 (BeforeStartController)，等待玩家点击'开始'...");
            while (!beforeStart.HasUserClickedStart())
            {
                yield return null;
            }
            Debug.Log("👆 [CutSequenceController] 玩家已点击'开始'，启动 Cut 1 准备流程...");
        }

        // 启动 Cut 1
        if (modules.Length > 0 && modules[0] != null)
        {
            modules[0].gameObject.SetActive(true);
            modules[0].Initialize(this);

            Debug.Log($"✂️ 启动第 1 个 Cut 模块：{modules[0].name}");

            // 先确保 Cut 1 的 Prepare 循环（或主视频第 0 帧）已实际绘出，避免淡出开场白时黑屏闪帧
            float waitReady = 0f;
            while (!modules[0].IsMainFrameReady && waitReady < 4.5f)
            {
                waitReady += Time.unscaledDeltaTime;
                yield return null;
            }

            // 兼容旧流程：仅当 Cut 1 未配置独立 Prep 循环视频且触发弹窗时，才用开场白视频垫底循环等待
            if (modules[0].IsInterrupted() && !modules[0].HasPrepVideo)
            {
                Debug.Log("🔔 [CutSequenceController] Cut 1 未配置 Prep 视频，弹窗期间继续由开场白视频垫底循环，直至弹窗关闭...");
                float interruptTimeout = 25f;
                while (modules[0].IsInterrupted() && interruptTimeout > 0f)
                {
                    interruptTimeout -= Time.deltaTime;
                    yield return null;
                }
                if (modules[0].IsInterrupted())
                {
                    Debug.LogWarning("⚠️ [CutSequenceController] Cut 1 打断等待超时 (25s)，强制解除打断！");
                    modules[0].ForceClearInterrupt();
                }
            }
        }

        // 平滑淡出 BeforeStart Canvas 并停止开场白视频，切入 Cut 1 Prepare 循环（及按需弹出的居士微信）
        if (beforeStart != null)
        {
            beforeStart.FadeOutAndUnlock(0.5f);
            while (beforeStart.IsBeforeStartActive())
            {
                yield return null;
            }
            Debug.Log("🎬 [CutSequenceController] 开场白已完全淡出，正式进入 Cut 1 Prepare 阶段！");
        }

    }



    /// <summary>

    /// 当前 CutModule 完成完整流程后调用：

    /// Complete → Transition 都播放完之后才会来到这里

    /// </summary>

    public void OnModuleCompleted(CutModule module)

    {

        int index = System.Array.IndexOf(modules, module);



        if (index < 0)

        {

            Debug.LogWarning(

                $"⚠️ CutSequenceController 找不到模块：{module.name}"

            );

            return;

        }



        Debug.Log($"✅ Controller 收到完成信号：{module.name}");

        // ------------------------------------------------
        // 还有下一个 Cut：先启动并等待下一个 Cut 第 0 帧渲染完毕，再关闭并释放上一个 Cut
        // ------------------------------------------------

        int nextIndex = index + 1;

        if (nextIndex < modules.Length)
        {
            CutModule nextModule = modules[nextIndex];

            if (nextModule != null)
            {
                StartCoroutine(HandoffToNextModuleRoutine(module, nextModule));
            }
            else
            {
                module.ReleaseResources();
                module.gameObject.SetActive(false);
            }

            return;
        }

        // ------------------------------------------------
        // 所有 Cut 全部完成：播放 completeVideoClip 后切下一场景
        // ------------------------------------------------

        isAllComplete = true;

        Debug.Log("🏁 所有 CutModules 完成");

        StartCoroutine(PlayCompleteVideoThenAdvanceRoutine(module));

    }

    private IEnumerator PlayCompleteVideoThenAdvanceRoutine(CutModule lastModule)
    {
        if (completeVideoClip == null)
        {
            NotifyGlobalAdvance();
            yield break;
        }

        runtimeCompleteCanvasObj = new GameObject("Canvas_CutComplete");
        Canvas canvas = runtimeCompleteCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = runtimeCompleteCanvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject rawGo = new GameObject("RawImage_CutComplete", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        rawGo.transform.SetParent(runtimeCompleteCanvasObj.transform, false);
        RectTransform rt = rawGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        RawImage rawImg = rawGo.GetComponent<RawImage>();
        rawImg.color = Color.white;
        rawImg.raycastTarget = false;
        rawImg.enabled = false;

        runtimeCompleteRT = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
        {
            name = "RT_CutComplete",
            hideFlags = HideFlags.DontSave
        };
        runtimeCompleteRT.Create();
        rawImg.texture = runtimeCompleteRT;

        VideoPlayer vp = runtimeCompleteCanvasObj.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = false;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.aspectRatio = VideoAspectRatio.FitInside;
        vp.audioOutputMode = VideoAudioOutputMode.Direct;
        vp.targetTexture = runtimeCompleteRT;
        vp.clip = completeVideoClip;

        vp.Prepare();
        float prepTimeout = 2.5f;
        while (!vp.isPrepared && prepTimeout > 0f)
        {
            prepTimeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        bool finished = false;
        VideoPlayer.EventHandler endHandler = (player) => { finished = true; };
        vp.loopPointReached += endHandler;

        vp.time = 0;
        vp.Play();

        float firstFrameTimeout = 0.4f;
        while (vp.frame < 0 && firstFrameTimeout > 0f)
        {
            firstFrameTimeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        yield return new WaitForEndOfFrame();

        rawImg.enabled = true;
        yield return new WaitForEndOfFrame();

        // 确认 Complete 视频首帧已铺满画面后，再彻底释放最后一个 Cut 模块，避免切换闪黑
        if (lastModule != null)
        {
            lastModule.ReleaseResources();
            lastModule.gameObject.SetActive(false);
        }

        float clipLen = vp.length > 0.1 ? (float)vp.length : (float)completeVideoClip.length;
        float maxWait = clipLen > 0.1f ? clipLen + 2f : 12f;
        float elapsed = 0f;

        while (!finished && elapsed < maxWait)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        vp.loopPointReached -= endHandler;
        vp.Pause();

        NotifyGlobalAdvance();
    }

    private void OnDestroy()
    {
        if (runtimeCompleteCanvasObj != null)
        {
            Destroy(runtimeCompleteCanvasObj);
            runtimeCompleteCanvasObj = null;
        }

        if (runtimeCompleteRT != null)
        {
            runtimeCompleteRT.Release();
            Destroy(runtimeCompleteRT);
            runtimeCompleteRT = null;
        }
    }

    private IEnumerator HandoffToNextModuleRoutine(CutModule previousModule, CutModule nextModule)
    {
        // 1. 先释放上一个模块不再使用的 A/B 视频解码器，减轻 macOS 并发解码压力
        if (previousModule != null)
        {
            previousModule.ReleaseMainAndFailVideos();
        }

        // 2. 启动下一个模块并等待首帧
        nextModule.gameObject.SetActive(true);
        nextModule.Initialize(this);

        float waitReady = 0f;
        while (!nextModule.IsMainFrameReady && waitReady < 4.0f)
        {
            waitReady += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return new WaitForEndOfFrame();

        // 3. 彻底释放上一个模块的所有资源（包括转场）并关闭
        if (previousModule != null)
        {
            previousModule.ReleaseResources();
            previousModule.gameObject.SetActive(false);
        }

        Debug.Log($"➡️ {previousModule?.name} 完成并已无缝交接给下一个模块：{nextModule.name}");
    }



    /// <summary>

    /// 通知 GlobalProgressManager 进入下一个大阶段

    /// </summary>

    private void NotifyGlobalAdvance()

    {

        if (hasSentGlobalSignal)

            return;



        hasSentGlobalSignal = true;



        GlobalProgressManager global = GlobalProgressManager.EnsureInstance();



        if (global == null)

        {

            Debug.LogWarning(

                "⚠️ GlobalProgressManager.Instance 未找到，Cut 已经完成，但无法进入下一个大阶段！"

            );



            return;

        }



        if (global.sceneTransitioning)

        {

            Debug.Log(

                "⏳ GlobalProgressManager 正在转场，忽略重复调用。"

            );



            return;

        }



        GlobalProgressManager.CurrentDay = 2;
        if (global.currentOrder < 5)
        {
            global.currentOrder = 5;
        }

        global.AdvanceOrder();



        Debug.Log(

            "🌍 已调用 GlobalProgressManager.AdvanceOrder()"

        );

    }

}