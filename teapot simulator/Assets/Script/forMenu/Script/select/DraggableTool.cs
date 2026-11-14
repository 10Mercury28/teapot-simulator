using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class DraggableTool : MonoBehaviour
{
    [Header("拖拽工具目标场景名称")]
    public string targetSceneName = "Scene1";

    private Vector3 offset;
    private Vector3 startPos;
    private bool overZone = false;
    private Camera mainCam;

    [Header("可选：音效 / 动画反馈")]
    public AudioSource pickUpSound;
    public AudioSource dropSound;

    void Start()
    {
        mainCam = Camera.main;
        startPos = transform.position;
    }

    void OnMouseDown()
    {
        // 记录偏移
        Vector3 mouse = mainCam.ScreenToWorldPoint(Input.mousePosition);
        mouse.z = 0f;
        offset = transform.position - mouse;

        if (pickUpSound) pickUpSound.Play();
    }

    void OnMouseDrag()
    {
        // 实时更新位置
        Vector3 mouse = mainCam.ScreenToWorldPoint(Input.mousePosition);
        mouse.z = 0f;
        transform.position = mouse + offset;
    }

    void OnMouseUp()
    {
        if (overZone)
        {
            Debug.Log($"🧭 工具进入选区，准备切换场景：{targetSceneName}");
            
            if (SceneBlinkTransition.Instance != null)
            {
                // 🎬 使用同步眨眼场景切换
                SceneBlinkTransition.Instance.LoadSceneConcealed(targetSceneName);
            }
            else
            {
                Debug.LogWarning("⚠️ SceneBlinkTransition.Instance 未找到，直接加载场景！");
                UnityEngine.SceneManagement.SceneManager.LoadScene(targetSceneName);
            }

            if (dropSound) dropSound.Play();
        }
        else
        {
            // 放回原位
            transform.position = startPos;
            Debug.Log("❌ 未放入选区，已重置位置");
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("SelectionZone"))
        {
            overZone = true;
            Debug.Log("✅ 进入选区区域");
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("SelectionZone"))
        {
            overZone = false;
            Debug.Log("🚫 离开选区区域");
        }
    }
}
