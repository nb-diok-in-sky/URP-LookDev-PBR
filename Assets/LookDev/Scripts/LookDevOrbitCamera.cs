using UnityEngine;

// 运行模式下的看图相机（仿虚幻材质编辑器）：
// 左键拖 = 绕物体转；中键拖 = 平移；滚轮 = 推拉；
// Shift + 左键拖 = 转 HDRI（太阳跟着转）；F = 回到初始机位。
public class LookDevOrbitCamera : MonoBehaviour
{
    public Vector3 pivot = new Vector3(0f, 0.6f, 0f);
    public LookDevController controller;
    public float orbitSpeed = 0.25f;
    public float panSpeed = 0.002f;
    public float zoomSpeed = 0.1f;

    float _yaw, _pitch, _distance;
    Vector3 _pivot;
    float _startYaw, _startPitch, _startDistance;
    Vector3 _startPivot;
    Vector3 _lastMouse;

    void Start()
    {
        _pivot = pivot;
        Vector3 offset = transform.position - _pivot;
        _distance = offset.magnitude;
        Quaternion q = Quaternion.LookRotation(-offset);
        _yaw = q.eulerAngles.y;
        _pitch = q.eulerAngles.x > 180f ? q.eulerAngles.x - 360f : q.eulerAngles.x;
        _startYaw = _yaw; _startPitch = _pitch; _startDistance = _distance; _startPivot = _pivot;
        _lastMouse = Input.mousePosition;
    }

    void LateUpdate()
    {
        Vector3 delta = Input.mousePosition - _lastMouse;
        _lastMouse = Input.mousePosition;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetMouseButton(0))
        {
            if (shift && controller != null)
                controller.hdriRotation = Mathf.Repeat(controller.hdriRotation + delta.x * orbitSpeed, 360f);
            else
            {
                _yaw += delta.x * orbitSpeed;
                _pitch = Mathf.Clamp(_pitch - delta.y * orbitSpeed, -85f, 85f);
            }
        }
        if (Input.GetMouseButton(2))
            _pivot += (-transform.right * delta.x - transform.up * delta.y) * panSpeed * _distance;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f) _distance = Mathf.Max(0.05f, _distance * (1f - scroll * zoomSpeed));

        if (Input.GetKeyDown(KeyCode.F))
        {
            _yaw = _startYaw; _pitch = _startPitch; _distance = _startDistance; _pivot = _startPivot;
        }

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.SetPositionAndRotation(_pivot - rot * Vector3.forward * _distance, rot);
    }
}
