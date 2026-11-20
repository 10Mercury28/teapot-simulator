using UnityEngine;

/// <summary>
/// PatModuleUI
/// - 根据 PatModule 状态驱动 UI（仅显示当前正在交互的模块）
/// - Inactive: SetInactive (alpha = 0，完全隐藏)
/// - Fail / Transition / Waiting: ShowStart (dim)
/// - Main Patting: BuildStack / Bright (bright, visual cue actively animated)
/// - Click Hit: OnHit (punch scale + flash)
/// - Complete: HideOnComplete (alpha = 0)
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PatModule))]
[RequireComponent(typeof(PatUIController))]
public class PatModuleUI : MonoBehaviour
{
    private PatModule module;
    private PatUIController ui;

    private bool stackBuilt = false;
    private bool completedHidden = false;
    private bool inactiveHidden = false;
    private bool popupHidden = false;
    private int lastHits = -1;

    private void Awake()
    {
        module = GetComponent<PatModule>();
        ui = GetComponent<PatUIController>();
    }

    private void Start()
    {
        // 初始待机状态：若模块尚未激活，完全隐藏 UI
        if (ui != null && module != null)
        {
            if (!module.active && !module.complete)
            {
                ui.SetInactive();
                inactiveHidden = true;
            }
        }
    }

    private void Update()
    {
        if (!module || !ui) return;

        bool inTransition = module.inTransition;
        int hits = module.CurrentHits;

        bool failShowing = module.failRaw && module.failRaw.enabled;
        bool transitionShowing = module.transitionRaw && module.transitionRaw.enabled;

        // ===================== 完成 =====================
        if (module.complete)
        {
            if (!completedHidden)
            {
                ui.HideOnComplete();   // alpha = 0
                completedHidden = true;
                inactiveHidden = false;
                popupHidden = false;
            }
            return;
        }

        if (!module.active)
        {
            if (!inactiveHidden)
            {
                ui.SetInactive();
                inactiveHidden = true;
                popupHidden = false;
                stackBuilt = false;
                lastHits = -1;
            }
            return;
        }

        inactiveHidden = false;

        // ===================== 弹窗期间隐藏交互 UI =====================
        bool popupShowing = module.IsInterrupted
                            || (PopupManager.ExistingInstance != null && PopupManager.ExistingInstance.HasActivePopup);
        if (popupShowing)
        {
            if (!popupHidden)
            {
                ui.SetInactive();
                popupHidden = true;
                stackBuilt = false;
                lastHits = -1;
            }
            return;
        }

        popupHidden = false;

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
}
