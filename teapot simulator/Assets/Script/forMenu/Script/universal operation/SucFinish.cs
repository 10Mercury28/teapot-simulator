using UnityEngine;
using UnityEngine.SceneManagement;

public class SucFinish : MonoBehaviour
{
    private bool hasTriggered = false;

    public void OnVideoComplete()
    {
        if (hasTriggered) return;
        hasTriggered = true;

        Debug.Log("🌟 [SucFinish] 收到播放完成信号 → 通知 GlobalProgressManager");

        if (GlobalProgressManager.Instance != null)
        {
            GlobalProgressManager.Instance.AdvanceOrder();
        }
        else
        {
            Debug.LogWarning("⚠️ [SucFinish] 没有 GlobalProgressManager，回退到直接加载 chooseToolBasedOnCut");
            SceneManager.LoadScene("chooseToolBasedOnCut");
        }

        // 真正成功后再销毁自己
        Debug.Log("🧹 [SucFinish] 成功后销毁成功层");
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        Debug.LogWarning($"💀 [SucFinish] 被销毁！对象名：{gameObject.name}，时间：{Time.time:F2}s");
    }
}