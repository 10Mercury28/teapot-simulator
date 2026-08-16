using UnityEngine;
using System.Collections;

public class CutSequenceController : MonoBehaviour
{
    [Header("模块列表")]
    public CutModule[] modules;

    [Header("状态")]
    public bool isAllComplete = false;

    private bool hasSentGlobalSignal = false;

    private IEnumerator Start()
    {
        yield return new WaitForEndOfFrame();

        isAllComplete = false;
        hasSentGlobalSignal = false;

        // 一开始关闭所有 Cut Module
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i] != null)
            {
                modules[i].gameObject.SetActive(false);
            }
        }

        // 启动 Cut 1
        if (modules.Length > 0 && modules[0] != null)
        {
            modules[0].gameObject.SetActive(true);
            modules[0].Initialize(this);

            Debug.Log($"✂️ 启动第 1 个 Cut 模块：{modules[0].name}");
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

        // 先关闭当前模块
        module.gameObject.SetActive(false);

        // ------------------------------------------------
        // 还有下一个 Cut
        // ------------------------------------------------
        int nextIndex = index + 1;

        if (nextIndex < modules.Length)
        {
            CutModule nextModule = modules[nextIndex];

            if (nextModule != null)
            {
                nextModule.gameObject.SetActive(true);
                nextModule.Initialize(this);

                Debug.Log(
                    $"➡️ {module.name} 完成，正式启动下一个模块：{nextModule.name}"
                );
            }

            return;
        }

        // ------------------------------------------------
        // 所有 Cut 全部完成
        // ------------------------------------------------
        isAllComplete = true;

        Debug.Log("🏁 所有 CutModules 完成");

        NotifyGlobalAdvance();
    }

    /// <summary>
    /// 通知 GlobalProgressManager 进入下一个大阶段
    /// </summary>
    private void NotifyGlobalAdvance()
    {
        if (hasSentGlobalSignal)
            return;

        hasSentGlobalSignal = true;

        GlobalProgressManager global = GlobalProgressManager.Instance;

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

        global.AdvanceOrder();

        Debug.Log(
            "🌍 已调用 GlobalProgressManager.AdvanceOrder()"
        );
    }
}