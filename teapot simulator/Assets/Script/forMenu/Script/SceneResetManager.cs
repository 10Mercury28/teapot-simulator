using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneResetManager : MonoBehaviour
{
    [Header("Key Settings")]
    [Tooltip("按下这个键会重置整个场景")]
    public KeyCode resetKey = KeyCode.R;

    [Tooltip("是否在Console显示提示信息")]
    public bool debug = true;

    void Update()
    {
        // 检测按键
        if (Input.GetKeyDown(resetKey))
        {
            if (debug)
            {
                Debug.Log($"🔁 Reset key [{resetKey}] pressed → Reloading scene: {SceneManager.GetActiveScene().name}");
            }

            // 获取当前激活场景
            Scene current = SceneManager.GetActiveScene();

            // 重新加载场景
            SceneManager.LoadScene(current.name);
        }
    }
}
