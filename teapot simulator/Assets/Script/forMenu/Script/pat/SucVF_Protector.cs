using UnityEngine;
using UnityEngine.SceneManagement;

public class SucVF_Protector : MonoBehaviour
{
    private string originScene;

    void Awake()
    {
        originScene = SceneManager.GetActiveScene().name;
        DontDestroyOnLoad(gameObject);
        Debug.Log($"🛡 [SucVF_Protector] {gameObject.name} 在场景 '{originScene}' 中激活并设为 DontDestroyOnLoad。");

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != originScene)
        {
            Debug.Log($"💥 [SucVF_Protector] 检测到场景切换至 '{scene.name}' → 自动销毁 {gameObject.name}");
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}