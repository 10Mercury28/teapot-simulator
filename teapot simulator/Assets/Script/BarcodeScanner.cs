using System.Text;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 监听键盘输入（特别是扫码枪模拟键盘的输入）
/// 并在检测到回车符时，将收集到的字符串通过 UnityEvent 广播出去。
/// 包含防脏字符滞留超时清空机制，以及按键模拟扫码的调试功能。
/// </summary>
public class BarcodeScanner : MonoBehaviour
{
    [Header("扫码事件")]
    [Tooltip("当扫码枪扫描完成（遇到回车符）时触发此事件，并传递扫描到的条码内容")]
    public UnityEvent<string> OnBarcodeScanned;

    [Header("输入超时防误触")]
    [Tooltip("字符之间超过此间隔（秒）则视为误触键盘，自动清空缓存，防止脏字符滞留破坏下次扫码")]
    public float inputTimeout = 0.5f;

    [Header("键盘模拟调试（无扫描枪时测试使用）")]
    [Tooltip("开启后，按键盘数字键可直接模拟扫描特定条码")]
    public bool enableDebugKeySimulation = true;
    public KeyCode simulateKey1 = KeyCode.Alpha1;
    public string simulateBarcode1 = "TOOL_01";
    public KeyCode simulateKey2 = KeyCode.Alpha2;
    public string simulateBarcode2 = "TOOL_02";

    [Header("调试日志")]
    public bool debugLog = true;

    // 用于缓存拼接近几帧输入的字符
    private readonly StringBuilder buffer = new StringBuilder();
    private float lastInputTime = 0f;

    void Update()
    {
        // 1. 无扫码枪时的快捷键模拟测试
        if (enableDebugKeySimulation)
        {
            if (Input.GetKeyDown(simulateKey1))
            {
                TriggerBarcode(simulateBarcode1, "快捷键 " + simulateKey1);
                return;
            }
            if (Input.GetKeyDown(simulateKey2))
            {
                TriggerBarcode(simulateBarcode2, "快捷键 " + simulateKey2);
                return;
            }
        }

        // 2. 超时清空防脏字符滞留（扫码枪字符流通常在 50ms 内瞬间完成，手打慢输入或误触会在超时后清空）
        if (buffer.Length > 0 && Time.unscaledTime - lastInputTime > inputTimeout)
        {
            if (debugLog)
            {
                Debug.Log($"⚠️ [BarcodeScanner] 输入超时（>{inputTimeout}s），已自动清空残余字符: '{buffer}'");
            }
            buffer.Clear();
        }

        // 3. 遍历当前帧发生的所有字符输入
        string frameInput = Input.inputString;
        if (string.IsNullOrEmpty(frameInput)) return;

        lastInputTime = Time.unscaledTime;

        foreach (char c in frameInput)
        {
            // 如果是退格键 (Backspace)
            if (c == '\b')
            {
                if (buffer.Length != 0)
                {
                    buffer.Remove(buffer.Length - 1, 1);
                }
            }
            // 如果是回车或换行符 (Enter)
            else if (c == '\n' || c == '\r')
            {
                if (buffer.Length > 0)
                {
                    string barcode = buffer.ToString().Trim();
                    buffer.Clear();

                    if (!string.IsNullOrEmpty(barcode))
                    {
                        TriggerBarcode(barcode, "扫码枪输入");
                    }
                }
            }
            // 忽略不可见控制字符
            else if (!char.IsControl(c))
            {
                buffer.Append(c);
            }
        }
    }

    private void TriggerBarcode(string barcode, string source)
    {
        if (debugLog)
        {
            Debug.Log($"🎯 [BarcodeScanner] 扫描成功 ({source})！条码内容: '{barcode}'");
        }

        // 触发外部绑定事件
        OnBarcodeScanned?.Invoke(barcode);
    }
}
