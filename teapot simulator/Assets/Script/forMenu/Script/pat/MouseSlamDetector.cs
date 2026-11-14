using UnityEngine;
using System;

public class MouseSlamDetector : MonoBehaviour
{
    [Header("Thresholds (tune in Play Mode)")]
    [SerializeField] private float speedThreshold = 4000f;   // 像素/秒
    [SerializeField] private float accelThreshold = 25000f;  // (像素/秒)/秒
    [SerializeField] private float cooldown = 0.2f;          // 触发后冷却时间 s

    public static event Action OnSlam; // ✅ 全局事件（任何脚本都可订阅）

    private Vector3 lastPos;
    private float lastSpeed;
    private float cdTimer;

    void Start()
    {
        lastPos = Input.mousePosition;
        lastSpeed = 0f;
    }

    void Update()
    {
        cdTimer -= Time.deltaTime;

        Vector3 pos = Input.mousePosition;
        float dt = Mathf.Max(Time.deltaTime, 1e-6f);
        float speed = (pos - lastPos).magnitude / dt;  // 像素/秒
        float accel = (speed - lastSpeed) / dt;        // 加速度

        if (cdTimer <= 0f && speed > speedThreshold && accel > accelThreshold)
        {
            Debug.Log("Mouse SLAM detected!");
            OnSlam?.Invoke(); // ✅ 触发全局事件
            cdTimer = cooldown;
        }

        lastPos = pos;
        lastSpeed = speed;
    }
}