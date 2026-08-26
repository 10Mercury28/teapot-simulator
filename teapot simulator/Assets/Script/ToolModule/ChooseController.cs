using UnityEngine;
using UnityEngine.Video;
using System.Collections.Generic;

public class ChooseController : MonoBehaviour
{
    [Header("模块管理")]
    public List<ChooseModule> chooseModules;

    [Header("视频组件")]
    public VideoPlayer v1; // 开场视频
    public VideoPlayer v2; // 循环视频

    private GlobalProgressManager global;

    void Start()
    {
        global = GlobalProgressManager.Instance;
        InitializeModules();
        PlayIntroSequence();
    }

    // 初始化模块，仅激活当前索引对应的模块
    private void InitializeModules()
    {
        if (chooseModules == null || chooseModules.Count == 0)
        {
            Debug.LogWarning("⚠️ ChooseVideoController: 未分配 chooseModules。");
            return;
        }

        var global = GlobalProgressManager.Instance;
        Debug.Log($"[ChooseController] InitializeModules started. global.currentOrder = {global.currentOrder}");

        for (int i = 0; i < chooseModules.Count; i++)
        {
            if (chooseModules[i] != null)
            {
                chooseModules[i].Init(this);  // ✅ 必须初始化！
                
                // ⚠️ 正确逻辑：必须用 Inspector 里填写的 orderIndex 来和全局进度对比！
                bool active = (chooseModules[i].orderIndex == global.currentOrder);
                Debug.Log($"[ChooseController] module '{chooseModules[i].moduleName}' (orderIndex={chooseModules[i].orderIndex}) -> SetModuleActive({active})");
                
                chooseModules[i].SetModuleActive(active);
            }
        }

        Debug.Log($"✅ 模块初始化完成，当前全局进度索引 {global.currentOrder}");
    }

    private void PlayIntroSequence()
    {
        if (v1 != null)
        {
            v1.loopPointReached += OnV1Finished;
            v1.Play();
        }
        else if (v2 != null)
        {
            v2.isLooping = true;
            v2.Play();
        }
    }

    private void OnV1Finished(VideoPlayer vp)
    {
        if (v1 != null)
        {
            v1.Stop();
            v1.gameObject.SetActive(false);
        }

        if (v2 != null)
        {
            v2.isLooping = true;
            v2.Play();
        }
    }

    public void OnModuleCompleted(ChooseModule module)
    {
        global.AdvanceOrder();

        // 跨场景架构下，AdvanceOrder 会立刻切场景，这部分其实不会在视觉上停留太久
        // 但为了严谨，我们同样修正这里的激活判断：
        for (int i = 0; i < chooseModules.Count; i++)
        {
            if (chooseModules[i] != null)
            {
                bool active = (chooseModules[i].orderIndex == global.currentOrder);
                chooseModules[i].SetModuleActive(active);
            }
        }
    }

    public void NotifyModuleStarted(ChooseModule active)
    {
        foreach (var m in chooseModules)
        {
            if (m != null && m != active)
                m.HideAllForExternal();
        }
    }
}
