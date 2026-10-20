using UnityEngine;

// 转台：运行模式下让物体绕 Y 轴自转，按 T 开关。
public class LookDevTurntable : MonoBehaviour
{
    public bool spinning;
    [Range(-90, 90)] public float degreesPerSecond = 20f;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T)) spinning = !spinning;
        if (spinning) transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.World);
    }
}
