using System;
using System.Collections;
using UnityEngine;
using Yarn.Unity;

/// <summary>
/// Connects the existing BarcodeScanner.OnBarcodeScanned event to Yarn Spinner.
/// Yarn: <<d2_scan "09-H2zhuangjj">> pauses until one scan is received.
/// Then Yarn reads $D2ScanMatched, and either completes or retries.
/// </summary>
public class JushiD2BarcodeBridge : MonoBehaviour
{
    [SerializeField] private DialogueRunner dialogueRunner;
    [SerializeField] private BarcodeScanner scanner;

    private void Awake()
    {
        // 自动查找引用
        if (dialogueRunner == null) dialogueRunner = FindObjectOfType<DialogueRunner>();
        if (scanner == null) scanner = FindObjectOfType<BarcodeScanner>();

        if (dialogueRunner == null || scanner == null)
        {
            return;
        }

        try
        {
            dialogueRunner.AddCommandHandler<string>("d2_scan", AwaitBarcode);
        }
        catch (Exception)
        {
            // 已在 Day1YarnCommands 中全局注册，忽略重复注册
        }
    }

    private IEnumerator AwaitBarcode(string expectedBarcode)
    {
        if (scanner == null || dialogueRunner == null)
        {
            Debug.LogError("d2_scan: Scanner or DialogueRunner missing.");
            yield break;
        }

        bool received = false;
        string scannedValue = null;
        void OnScan(string value)
        {
            if (!received)
            {
                scannedValue = value;
                received = true;
            }
        }

        dialogueRunner.VariableStorage.SetValue("$D2ScanMatched", false);
        scanner.OnBarcodeScanned.AddListener(OnScan);
        try
        {
            // The dialogue pauses here; a physical scanner's Enter key completes one scan.
            while (!received)
                yield return null;

            string cleanScanned = scannedValue != null ? scannedValue.Trim() : "";
            string cleanExpected = expectedBarcode != null ? expectedBarcode.Trim() : "";
            bool matches = string.Equals(cleanScanned, cleanExpected, StringComparison.OrdinalIgnoreCase);
            dialogueRunner.VariableStorage.SetValue("$D2ScanMatched", matches);
            Debug.Log($"[D2 barcode] got='{cleanScanned}', expected='{cleanExpected}', matched={matches}");
        }
        finally
        {
            scanner.OnBarcodeScanned.RemoveListener(OnScan);
        }
    }
}
