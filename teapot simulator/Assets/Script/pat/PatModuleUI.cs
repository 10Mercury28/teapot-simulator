using UnityEngine;

/// <summary>
/// PatModuleUI (Final)
/// - 根据 PatModule 状态驱动 UI
/// - fail / transition / waiting → dim
/// - main → bright
/// - complete → alpha = 0
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PatModule))]
[RequireComponent(typeof(PatUIController))]
public class PatModuleUI : MonoBehaviour
{
    PatModule module;
    PatUIController ui;

    bool stackBuilt = false;
    bool completedHidden = false;
    int lastHits = -1;

    void Awake()
    {
        module = GetComponent<PatModule>();
        ui = GetComponent<PatUIController>();
    }

    void Update()
    {
        if (!module || !ui) return;

        bool inTransition = GetInTransition();
        int hits = GetHits();

        bool failShowing = module.failRaw && module.failRaw.enabled;
        bool transitionShowing = module.transitionRaw && module.transitionRaw.enabled;

        // ===================== 完成 =====================
        if (module.complete)
        {
            if (!completedHidden)
            {
                ui.HideOnComplete();   // alpha = 0
                completedHidden = true;
            }
            return;
        }

        if (!module.active) return;

        // ===================== Dim 状态 =====================
        if (failShowing || transitionShowing || inTransition)
        {
            ui.Dim();

            // 进入 transition / fail，重置 stack 状态
            stackBuilt = false;
            lastHits = -1;

            ui.ShowStart();
            return;
        }

        // ===================== Main 状态 =====================
        if (!stackBuilt)
        {
            ui.BuildStack(module.requiredHits);
            stackBuilt = true;
            completedHidden = false;
            lastHits = hits;
            return;
        }

        ui.Bright();

        if (hits != lastHits && hits > lastHits)
        {
            ui.OnHit();
            lastHits = hits;
        }
    }

    // ===================== 反射 =====================

    bool GetInTransition()
    {
        var f = typeof(PatModule).GetField(
            "inTransition",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        return (bool)f.GetValue(module);
    }

    int GetHits()
    {
        var f = typeof(PatModule).GetField(
            "currentHits",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        return (int)f.GetValue(module);
    }
    
}
