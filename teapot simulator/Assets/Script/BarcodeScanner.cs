using System.Text;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 监听键盘输入（特别是扫码枪模拟键盘的输入）
/// 并在检测到回车符时，将收集到的字符串通过 UnityEvent 广播出去。
/// </summary>
public class BarcodeScanner : MonoBehaviour
{
    [Header("扫码事件")]
    [Tooltip("当扫码枪扫描完成（遇到回车符）时触发此事件，并传递扫描到的条码内容")]
    public UnityEvent<string> OnBarcodeScanned;

    [Header("调试")]
    public bool debugLog = true;

    // 用于缓存拼接近几帧输入的字符
    private StringBuilder buffer = new StringBuilder();

    void Update()
    {
        // 遍历当前帧发生的所有字符输入
        foreach (char c in Input.inputString)
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
                    string barcode = buffer.ToString();
                    if (debugLog)
                    {
                        Debug.Log($"[BarcodeScanner] 扫描成功！条码内容: {barcode}");
                    }

                    // 触发事件
                    OnBarcodeScanned?.Invoke(barcode);

                    // 清空缓存，准备下一次扫码
                    buffer.Clear();
                }
            }
            // 正常字符，加入缓存
            else
            {
                buffer.Append(c);
            }
        }
    }
}
