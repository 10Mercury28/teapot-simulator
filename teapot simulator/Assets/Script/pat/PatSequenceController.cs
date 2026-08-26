using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using System.Collections;

public class PatSequenceController : MonoBehaviour
{
    [Header("🧩 模块引用")]
    public PatModule[] modules;

    [Header("🎯 状态监控")]
    public bool allComplete = false;

    public float waitAfterSuccess = 1f;

    [Header("🎬 最终成功视频")]
    public VideoPlayer successVideoPlayer;

    public RawImage successRawImage;

    private bool successVideoPlayed = false;

    IEnumerator Start()
    {
        // 等所有对象完成初始化
        yield return new WaitForEndOfFrame();

        allComplete = false;
        successVideoPlayed = false;

        // ===============================================
        // 初始化最终成功视频
        // ===============================================

        yield return
            StartCoroutine(
                SetupSuccessVideo()
            );

        // ===============================================
        // 顺序执行所有 Module
        // ===============================================

        for (int i = 0; i < modules.Length; i++)
        {
            PatModule current =
                modules[i];

            if (current == null)
            {
                Debug.LogWarning(
                    $"⚠️ Module [{i}] 是 null，跳过。"
                );

                continue;
            }

            // -------------------------------------------
            // 关闭所有非当前 module
            // -------------------------------------------

            for (int j = 0; j < modules.Length; j++)
            {
                if (
                    j != i &&
                    modules[j] != null
                )
                {
                    modules[j].Deactivate();
                }
            }

            Debug.Log(
                $"🎯 [Sequence] 开始 Module {i}: {current.name}"
            );

            // -------------------------------------------
            // 只在这里 Activate
            // -------------------------------------------

            current.Activate();

            // -------------------------------------------
            // 等当前 Module 完成
            // -------------------------------------------

            while (!current.complete)
            {
                yield return null;
            }

            Debug.Log(
                $"✅ [Sequence] Module {i}: {current.name} 完成"
            );
        }

        // ===============================================
        // 全部完成
        // ===============================================

        allComplete = true;

        Debug.Log(
            "🏁 [PatSequenceController] 所有模块完成。"
        );

        // ===============================================
        // 播放最终 Success Video
        // ===============================================

        yield return
            StartCoroutine(
                PlaySuccessVideoAndWait()
            );

        Debug.Log(
            "✅ [PatSequenceController] Success Video 播放完毕。"
        );

        yield return
            new WaitForSeconds(waitAfterSuccess);

        // ===============================================
        // Advance
        // ===============================================

        if (GlobalProgressManager.Instance != null)
        {
            Debug.Log(
                "🌍 [PatSequenceController] AdvanceOrder()"
            );

            GlobalProgressManager.Instance.AdvanceOrder();
        }
        else
        {
            Debug.LogWarning(
                "⚠️ [PatSequenceController] 未找到 GlobalProgressManager"
            );
        }
    }

    // ==========================================================
    // SETUP SUCCESS VIDEO
    // ==========================================================

    IEnumerator SetupSuccessVideo()
    {
        if (
            successVideoPlayer == null ||
            successRawImage == null
        )
        {
            Debug.LogWarning(
                "⚠️ [PatSequenceController] Success Video 未绑定。"
            );

            yield break;
        }

        Canvas canvas =
            successRawImage.GetComponentInParent<Canvas>();

        if (canvas != null)
        {
            Debug.Log(
                $"🎨 Success Canvas Mode = {canvas.renderMode}"
            );
        }

        // ===============================================
        // 不阻挡任何鼠标输入
        // ===============================================

        CanvasGroup cg =
            successRawImage.GetComponent<CanvasGroup>();

        if (cg == null)
        {
            cg =
                successRawImage.gameObject
                    .AddComponent<CanvasGroup>();
        }

        cg.alpha = 1f;
        cg.blocksRaycasts = false;
        cg.interactable = false;

        successRawImage.raycastTarget = false;

        // ===============================================
        // RenderTexture
        // ===============================================

        if (
            successVideoPlayer.targetTexture == null ||
            !successVideoPlayer.targetTexture.IsCreated()
        )
        {
            RenderTexture rt =
                new RenderTexture(
                    1920,
                    1080,
                    0,
                    RenderTextureFormat.ARGB32
                );

            rt.name =
                "RT_Success_Rebuilt";

            rt.Create();

            successVideoPlayer.targetTexture = rt;

            Debug.Log(
                "🧩 创建 RT_Success_Rebuilt"
            );
        }

        successRawImage.texture =
            successVideoPlayer.targetTexture;

        successRawImage.color =
            Color.white;

        // ===============================================
        // 最终视频开始之前先不要盖住游戏画面
        // ===============================================

        successRawImage.enabled = false;

        // ===============================================
        // VideoPlayer
        // ===============================================

        successVideoPlayer.playOnAwake = false;
        successVideoPlayer.isLooping = false;
        successVideoPlayer.skipOnDrop = true;
        successVideoPlayer.waitForFirstFrame = true;

        if (
            successVideoPlayer.clip != null &&
            !successVideoPlayer.isPrepared
        )
        {
            Debug.Log(
                "⏳ Preparing Success Video..."
            );

            successVideoPlayer.Prepare();

            while (
                !successVideoPlayer.isPrepared
            )
            {
                yield return null;
            }
        }

        successVideoPlayer.time = 0.0;

        Debug.Log(
            "🖼️ [PatSequenceController] Success Video 初始化完成。"
        );
    }

    // ==========================================================
    // PLAY SUCCESS
    // ==========================================================

    IEnumerator PlaySuccessVideoAndWait()
    {
        if (successVideoPlayed)
            yield break;

        if (
            successVideoPlayer == null ||
            successRawImage == null
        )
            yield break;

        if (successVideoPlayer.clip == null)
        {
            Debug.LogWarning(
                "⚠️ Success Video 没有 Clip。"
            );

            yield break;
        }

        successVideoPlayed = true;

        // ===============================================
        // 再确认一次 RT 连接
        // ===============================================

        if (
            successVideoPlayer.targetTexture != null
        )
        {
            successRawImage.texture =
                successVideoPlayer.targetTexture;
        }

        successRawImage.color =
            Color.white;

        successRawImage.enabled =
            true;

        // ===============================================
        // 再确保 Prepare
        // ===============================================

        if (!successVideoPlayer.isPrepared)
        {
            successVideoPlayer.Prepare();

            while (
                !successVideoPlayer.isPrepared
            )
            {
                yield return null;
            }
        }

        successVideoPlayer.time = 0.0;

        successVideoPlayer.Play();

        Debug.Log(
            $"▶️ Success Video Play | " +
            $"length={successVideoPlayer.clip.length:F2}s | " +
            $"RT={successVideoPlayer.targetTexture?.name}"
        );

        // ===============================================
        // 等真正开始
        // ===============================================

        float startTimeout = 2f;

        while (
            !successVideoPlayer.isPlaying &&
            startTimeout > 0f
        )
        {
            startTimeout -=
                Time.unscaledDeltaTime;

            yield return null;
        }

        if (!successVideoPlayer.isPlaying)
        {
            Debug.LogWarning(
                "⚠️ Success Video 没有进入 Playing 状态。"
            );

            yield break;
        }
        
        // ⚠️ 修复最终蓝屏闪烁：
        // 只有在这里（确认 successVideo 真的已经开始播放，有画面了），我们再去关闭所有的模块！
        // 这样就实现了 100% 的无缝衔接。
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i] != null)
            {
                // 强制立刻隐藏
                modules[i].ForceHideAndRelease();
            }
        }

        // ===============================================
        // 等真正播放结束
        // ===============================================

        while (successVideoPlayer.isPlaying)
        {
            yield return null;
        }

        successVideoPlayer.Pause();

        Debug.Log(
            $"🟢 Success Video End | frame={successVideoPlayer.frame}"
        );
    }

    // ==========================================================
    // MODULE CALLBACK
    // ==========================================================

    public void OnModuleCompleted(PatModule m)
    {
        int idx =
            System.Array.IndexOf(
                modules,
                m
            );

        // ===============================================
        // 非常重要：
        //
        // 这里绝对不要再调用：
        //
        // modules[idx + 1].Activate();
        //
        // Start() coroutine 已经负责顺序切换。
        // ===============================================

        Debug.Log(
            $"✅ [PatSequenceController] 收到 Module {idx} 完成通知。"
        );
    }
}