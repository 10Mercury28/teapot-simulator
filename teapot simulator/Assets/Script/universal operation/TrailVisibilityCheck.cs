using UnityEngine;

public class TrailVisibilityCheck : MonoBehaviour
{
    public Camera trailCam;
    public TrailRenderer trail;

    void Start()
    {
        if (trailCam == null || trail == null)
        {
            Debug.LogError("❌ 请在Inspector中绑定 TrailCamera 和 TrailRenderer");
            return;
        }

        Debug.Log($"🔎 [Trail Check Init]");
        Debug.Log($"TrailCamera: name={trailCam.name}, depth={trailCam.depth}, mask={trailCam.cullingMask}, clearFlags={trailCam.clearFlags}");
        Debug.Log($"TrailRenderer: layer={trail.gameObject.layer}, material={trail.sharedMaterial?.shader.name ?? "NULL"}");

        // 检查 Layer 是否匹配
        bool maskIncludes = (trailCam.cullingMask & (1 << trail.gameObject.layer)) != 0;
        Debug.Log($"🎯 层级匹配状态：{maskIncludes}");

        // 检查渲染边界
        var bounds = trail.bounds;
        bool inView = trailCam.WorldToViewportPoint(bounds.center).z > 0;
        Debug.Log($"📦 位置Z={bounds.center.z:F2}, 相机可见：{inView}");

        // 检查材质颜色和透明度
        var grad = trail.colorGradient;
        float maxAlpha = 0;
        foreach (var key in grad.alphaKeys) if (key.alpha > maxAlpha) maxAlpha = key.alpha;
        Debug.Log($"🎨 Trail最大透明度Alpha={maxAlpha}");

        // 检查是否被别的摄像机覆盖
        Camera[] cams = Camera.allCameras;
        foreach (var cam in cams)
        {
            Debug.Log($"📸 相机: {cam.name}, depth={cam.depth}, clear={cam.clearFlags}, mask={cam.cullingMask}");
        }

        Debug.Log($"✅ 检查完毕。如果所有为True但依旧不可见，则可能是Render Queue或Material问题。");
    }
}