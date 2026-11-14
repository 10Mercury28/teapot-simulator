using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using Yarn.Unity;

/// <summary>
/// 修复 Yarn Spinner OptionsPresenter 复用池化 OptionItem 时的默认选中失效问题：
/// 1. 清除上一次对话选项残留在 EventSystem 中的 currentSelectedGameObject；
/// 2. 当选项面板淡入完成、CanvasGroup.interactable 变为 true 时，强制重新选中第一个可用选项；
/// 3. 先清空再 Select()，避免 Unity EventSystem 因选中同一物体而跳过 OnSelect 视觉高亮。
/// </summary>
public class YarnOptionDefaultSelector : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;

    private readonly List<OptionItem> activeOptionsBuffer = new List<OptionItem>();
    private bool wasShowingOptions = false;
    private bool pendingSelectFirst = false;
    private string lastOptionSignature = "";

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }
    }

    private void LateUpdate()
    {
        CollectActiveInteractableOptions(activeOptionsBuffer);
        bool hasActiveOptions = activeOptionsBuffer.Count > 0;

        if (!hasActiveOptions)
        {
            if (wasShowingOptions)
            {
                ClearOwnEventSystemSelection();
            }

            wasShowingOptions = false;
            pendingSelectFirst = false;
            lastOptionSignature = "";
            return;
        }

        string signature = BuildOptionSignature(activeOptionsBuffer);
        bool groupInteractable = canvasGroup == null || (canvasGroup.interactable && canvasGroup.alpha > 0.01f);

        if (!wasShowingOptions || signature != lastOptionSignature)
        {
            wasShowingOptions = true;
            lastOptionSignature = signature;
            pendingSelectFirst = true;

            // 在选项刚开始呈现或淡入期间，清理残留选中状态以免高亮错位
            ClearOwnEventSystemSelection();
        }

        if (pendingSelectFirst && groupInteractable)
        {
            SelectFirstOption(activeOptionsBuffer[0]);
            pendingSelectFirst = false;
        }
    }

    private void CollectActiveInteractableOptions(List<OptionItem> results)
    {
        results.Clear();
        OptionItem[] candidates = GetComponentsInChildren<OptionItem>(false);
        for (int i = 0; i < candidates.Length; i++)
        {
            OptionItem item = candidates[i];
            if (item != null && item.gameObject.activeInHierarchy && item.IsInteractable())
            {
                results.Add(item);
            }
        }
    }

    private static string BuildOptionSignature(List<OptionItem> options)
    {
        if (options.Count == 0) return "";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(options.Count).Append('|');
        for (int i = 0; i < options.Count; i++)
        {
            OptionItem item = options[i];
            TMP_Text tmp = item.GetComponentInChildren<TMP_Text>(true);
            sb.Append(item.GetInstanceID()).Append(':').Append(tmp != null ? tmp.text : "").Append(';');
        }
        return sb.ToString();
    }

    private void ClearOwnEventSystemSelection()
    {
        EventSystem es = EventSystem.current;
        if (es == null) return;

        GameObject current = es.currentSelectedGameObject;
        if (current != null && current.transform.IsChildOf(transform))
        {
            es.SetSelectedGameObject(null);
        }
    }

    private static void SelectFirstOption(OptionItem firstOption)
    {
        if (firstOption == null) return;

        EventSystem es = EventSystem.current;
        if (es != null)
        {
            // 先置空再选中，强制触发 OptionItem.OnSelect 更新高亮样式
            es.SetSelectedGameObject(null);
        }

        firstOption.Select();

        if (es != null && es.currentSelectedGameObject != firstOption.gameObject)
        {
            es.SetSelectedGameObject(firstOption.gameObject);
        }
    }
}
