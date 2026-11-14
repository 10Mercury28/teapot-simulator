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
        global = GlobalProgressManager.EnsureInstance();
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

        global = GlobalProgressManager.EnsureInstance();
        int expectedOrder = global != null ? global.GetExpectedToolOrderIndex() : 0;
        Debug.Log($"[ChooseController] InitializeModules started. Day={GlobalProgressManager.CurrentDay}, global.currentOrder={(global != null ? global.currentOrder : 0)}, expectedToolOrder={expectedOrder}");

        for (int i = 0; i < chooseModules.Count; i++)
        {
            if (chooseModules[i] != null)
            {
                chooseModules[i].Init(this);  // ✅ 必须初始化！
                
                // ⚠️ 用 GlobalProgressManager.GetExpectedToolOrderIndex() 匹配 Day 1 (orderIndex=0) 与 Day 2 (orderIndex=2)
                bool active = (global != null) ? (chooseModules[i].orderIndex == expectedOrder) : (i == 0);
                Debug.Log($"[ChooseController] module '{chooseModules[i].moduleName}' (orderIndex={chooseModules[i].orderIndex}) -> SetModuleActive({active})");
                
                chooseModules[i].SetModuleActive(active);
            }
        }

        Debug.Log($"✅ 模块初始化完成，当前 Day={GlobalProgressManager.CurrentDay}, 激活工具索引 {expectedOrder}");
    }

    private void PlayIntroSequence()
    {
        if (v1 != null)
        {
            v1.loopPointReached -= OnV1Finished;
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
            v1.loopPointReached -= OnV1Finished;
            v1.Stop();
            v1.gameObject.SetActive(false);
        }

        if (v2 != null)
        {
            v2.isLooping = true;
            v2.Play();
        }
    }

    void OnDestroy()
    {
        if (v1 != null)
        {
            v1.loopPointReached -= OnV1Finished;
        }
    }

    public void OnModuleCompleted(ChooseModule module)
    {
        if (global != null)
        {
            global.AdvanceOrder();
        }

        // 跨场景架构下，AdvanceOrder 会立刻切场景，这部分其实不会在视觉上停留太久
        // 但为了严谨，我们同样修正这里的激活判断：
        if (chooseModules != null && global != null)
        {
            int expectedOrder = global.GetExpectedToolOrderIndex();
            for (int i = 0; i < chooseModules.Count; i++)
            {
                if (chooseModules[i] != null)
                {
                    bool active = (chooseModules[i].orderIndex == expectedOrder);
                    chooseModules[i].SetModuleActive(active);
                }
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
