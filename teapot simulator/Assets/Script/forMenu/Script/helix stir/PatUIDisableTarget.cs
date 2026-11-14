using UnityEngine;

public class PatUIDisableTarget : MonoBehaviour
{
    [Tooltip("拖入：这个 module 对应的 UI Canvas 根物体")]
    public GameObject uiCanvasRoot;

    public void DisableUI()
    {
        if (uiCanvasRoot != null)
        {
            uiCanvasRoot.SetActive(false);
            Debug.Log($"[PatUIDisableTarget] Disabled UI: {uiCanvasRoot.name}");
        }
        else
        {
            Debug.LogError("[PatUIDisableTarget] uiCanvasRoot is NULL");
        }
    }
}