using UnityEngine;
using System.Collections;

public class CutSequenceController : MonoBehaviour
{
    [Header("模块列表")]
    public CutModule[] modules;

    [Header("状态")]
    public bool isAllComplete = false;
    private bool hasSentGlobalSignal = false;

    IEnumerator Start()
    {
        yield return new WaitForEndOfFrame();

        isAllComplete = false;
        hasSentGlobalSignal = false;

        // 禁用所有模块
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i])
                modules[i].gameObject.SetActive(false);
        }

        // 启动第一个模块
        if (modules.Length > 0 && modules[0])
        {
            modules[0].gameObject.SetActive(true);
            modules[0].Initialize(this);
            Debug.Log($"✂️ 启动第 1 个 Cut 模块：{modules[0].name}");
        }
    }

    public void OnModuleCompleted(CutModule m)
    {
        int idx = System.Array.IndexOf(modules, m);
        if (idx == -1) return;

        // 禁用当前模块
        m.gameObject.SetActive(false);

        if (idx + 1 < modules.Length)
        {
            modules[idx + 1].gameObject.SetActive(true);
            modules[idx + 1].Initialize(this);
            Debug.Log($"➡️ 启动下一个模块：{modules[idx + 1].name}");
        }
        else
        {
            isAllComplete = true;
            Debug.Log("🏁 所有 CutModules 完成 → 通知 Global 切换场景");
            NotifyGlobalAdvance();
        }
    }

    /// <summary>
    /// 向 GlobalProgressManager 发送换场信号
    /// </summary>
    private void NotifyGlobalAdvance()
    {
        if (hasSentGlobalSignal) return;
        hasSentGlobalSignal = true;

        var global = GlobalProgressManager.Instance;
        if (global == null)
        {
            Debug.LogWarning("⚠️ GlobalProgressManager.Instance 未找到，无法通知换场！");
            return;
        }

        if (global.sceneTransitioning)
        {
            Debug.Log("⏳ Global 正在转场，忽略重复调用。");
            return;
        }

        global.AdvanceOrder();
        Debug.Log("🌍 已调用 GlobalProgressManager.AdvanceOrder()");
    }
}
