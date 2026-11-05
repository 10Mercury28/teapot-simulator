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
        if (dialogueRunner == null || scanner == null)
        {
            Debug.LogError("JushiD2BarcodeBridge: Assign DialogueRunner and BarcodeScanner in Inspector.");
            return;
        }

        dialogueRunner.AddCommandHandler<string>("d2_scan", AwaitBarcode);
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

            bool matches = string.Equals(scannedValue, expectedBarcode, StringComparison.Ordinal);
            dialogueRunner.VariableStorage.SetValue("$D2ScanMatched", matches);
            Debug.Log($"[D2 barcode] got='{scannedValue}', expected='{expectedBarcode}', matched={matches}");
        }
        finally
        {
            scanner.OnBarcodeScanned.RemoveListener(OnScan);
        }
    }
}