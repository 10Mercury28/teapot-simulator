using UnityEngine;
using UnityEngine.SceneManagement;

public class SelectionZoneLoader: MonoBehaviour
{
    public static SelectionZoneLoader instance;

    private void Awake()
    {
        instance = this;
    }

    // Called by DraggableTool
    public static void LoadScene(string sceneName)
    {
        if (instance)
            instance.StartCoroutine(instance.LoadSceneRoutine(sceneName));
    }

    private System.Collections.IEnumerator LoadSceneRoutine(string sceneName)
    {
        // Optional fade-out or delay here
        Debug.Log($"🧭 Loading scene: {sceneName}");
        yield return new WaitForSeconds(0.2f);
        SceneManager.LoadScene(sceneName);
    }
}