using UnityEngine;

using System.Collections;



public class CutSequenceController : MonoBehaviour

{

    [Header("模块列表")]

    public CutModule[] modules;



    [Header("=== Day 2 弹窗打断配置 ===")]

    [Tooltip("是否自动为 Cut 1 和 Cut 2 注入打断配置（Cut 1 绑定 Day2_HA 并检测交心，Cut 2 绑定 Day2_ShaoA 无条件弹出）")]

    public bool autoConfigureDay2Popups = true;



    [Tooltip("调试开关：强制模拟前一天已交心（LikeU == true），方便直接在此场景测试居士弹窗")]

    public bool debugSimulateLikeU = false;



    [Header("状态")]

    public bool isAllComplete = false;



    private bool hasSentGlobalSignal = false;



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
            // Module 0 (Cut 1):
            if (modules.Length > 0 && modules[0] != null)
            {
                // 1. 入场准备打断：居士微信 (Day2_HA), 仅在交心时触发
                modules[0].enableEnterInterrupt = true;
                modules[0].requireLikeU = true;
                if (modules[0].interruptPopupSO == null && string.IsNullOrEmpty(modules[0].interruptPopupId))
                {
                    modules[0].interruptPopupId = "Day2_HA";
                }
                Debug.Log("⚙️ [CutSequenceController] 已配置 Cut 1 居士入场弹窗打断 (Day2_HA, requireLikeU=true)");

                // 2. 转场准备打断：老师微信 (Day2_ShaoA), Cut 1 切完进入转场准备视频时播放，并保持播放直至弹窗结束
                modules[0].enableTransitionInterrupt = true;
                modules[0].transitionHoldUntilPopupClosed = true;
                if (modules[0].transitionPopupSO == null && string.IsNullOrEmpty(modules[0].transitionPopupId))
                {
                    modules[0].transitionPopupId = "Day2_ShaoA";
                }
                Debug.Log("⚙️ [CutSequenceController] 已配置 Cut 1 转场打断 (Day2_ShaoA, 保持转场视频直至弹窗结束)");
            }

            // Module 1 (Cut 2): 老师通知已在转场准备期间触发完毕，Cut 2 入场直接准备切割，无需重复打断
            if (modules.Length > 1 && modules[1] != null)
            {
                modules[1].enableEnterInterrupt = false;
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

            // ⭐ 若 Cut 1 触发了入场打断弹窗（如 Day 2 居士微信），保持开场白视频在背景继续动态循环播放！
            if (modules[0].IsInterrupted())
            {
                Debug.Log("🔔 [CutSequenceController] Cut 1 处于弹窗打断挂起状态，保持待机/开场白视频动态循环播放，直至弹窗关闭...");
                float interruptTimeout = 25f;
                while (modules[0].IsInterrupted() && interruptTimeout > 0f)
                {
                    interruptTimeout -= Time.unscaledDeltaTime;
                    yield return null;
                }
                if (modules[0].IsInterrupted())
                {
                    Debug.LogWarning("⚠️ [CutSequenceController] Cut 1 打断等待超时 (25s)，强制解除打断！");
                    modules[0].ForceClearInterrupt();
                }
                Debug.Log("🔓 [CutSequenceController] 弹窗已关闭！开始平滑淡出待机视频，切入第 0 帧供玩家切割。");
            }

            // 确保 Cut 1 主视频第 0 帧已实际绘出后再淡出开场白遮罩
            float waitReady = 0f;
            while (!modules[0].IsMainFrameReady && waitReady < 1.5f)
            {
                waitReady += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // 弹窗关闭后（或未触发弹窗时），平滑淡出 BeforeStart Canvas 并停止其视频
        if (beforeStart != null)
        {
            beforeStart.FadeOutAndUnlock(0.5f);
            while (beforeStart.IsBeforeStartActive())
            {
                yield return null;
            }
            Debug.Log("🎬 [CutSequenceController] 开场白已完全淡出，正式进入 Cut 1 切割阶段！");
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
        // 所有 Cut 全部完成（保留最后一帧直至黑屏/闭眼转场完全盖住）
        // ------------------------------------------------

        isAllComplete = true;



        Debug.Log("🏁 所有 CutModules 完成");



        NotifyGlobalAdvance();

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
        while (!nextModule.IsMainFrameReady && waitReady < 2f)
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