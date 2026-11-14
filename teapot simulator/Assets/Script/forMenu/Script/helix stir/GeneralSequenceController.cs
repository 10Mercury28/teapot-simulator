using UnityEngine;
using System.Collections;

public class GeneralSequenceController : MonoBehaviour
{
    [Header("模块列表（可混合各种类型）")]
    public SequenceModuleBase[] modules;

    public bool isAllComplete = false;
    private bool hasSentGlobalSignal = false;

    IEnumerator Start()
    {
        yield return null;

        // 关闭所有
        foreach (var m in modules)
            if (m) m.gameObject.SetActive(false);

        // 开始第一个
        if (modules.Length > 0 && modules[0] != null)
        {
            modules[0].gameObject.SetActive(true);
            modules[0].Initialize(this);
        }
    }

    public void OnModuleCompleted(SequenceModuleBase module)
    {
        int idx = System.Array.IndexOf(modules, module);
        if (idx < 0) return;

        module.gameObject.SetActive(false);

        // 下一个
        if (idx + 1 < modules.Length)
        {
            var next = modules[idx + 1];
            next.gameObject.SetActive(true);
            next.Initialize(this);
        }
        else
        {
            isAllComplete = true;
            NotifyGlobalAdvance();
        }
    }

    private void NotifyGlobalAdvance()
    {
        if (hasSentGlobalSignal) return;
        hasSentGlobalSignal = true;

        var global = GlobalProgressManager.Instance;
        if (global != null)
            global.AdvanceOrder();
    }
}