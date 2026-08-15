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

        for (int i = 0; i < chooseModules.Count; i++)
        {
            if (chooseModules[i] != null)
            {
                chooseModules[i].Init(this);  // ✅ 必须初始化！
                bool active = (i == global.currentOrder);
                chooseModules[i].SetModuleActive(active);
            }
        }

        Debug.Log($"✅ 模块初始化完成，当前索引 {global.currentOrder}");
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

    // ✅ 当模块完成时由 ChooseModule 调用
    public void OnModuleCompleted(ChooseModule module)
    {
        global.AdvanceOrder();

        // 若未完成所有模块则激活下一个
        if (global.currentOrder < chooseModules.Count)
        {
            for (int i = 0; i < chooseModules.Count; i++)
            {
                bool active = (i == global.currentOrder);
                if (chooseModules[i] != null)
                    chooseModules[i].SetModuleActive(active);
            }
        }
        else
        {
            Debug.Log("✅ 所有模块完成。可以触发下一场景或全局事件。");
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
