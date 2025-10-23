using UnityEngine;

public sealed class XR_Parenter : MonoBehaviour
{
    [Tooltip("X-axis rotation for desktop/editor camera.")]
    [SerializeField] private float _desktopCameraPitch = -15f;

    private void Start()
    {
        var xrDdol = FindFirstObjectByType<XRMultiSceneObjectDDOL>();
        if (xrDdol == null)
        {
            Debug.LogWarning("[XR_Parenter] XRMultiSceneObjectDDOL not found.");
            return;
        }

        // Parent the persistent XR object under this transform
        xrDdol.transform.parent = transform;

        // If running in editor or desktop build (no XR device), rotate main camera
#if UNITY_EDITOR || !UNITY_ANDROID && !UNITY_IOS
        Camera childCamera = xrDdol.GetComponentInChildren<Camera>(true);
        if (childCamera != null)
        {
            Vector3 euler = childCamera.transform.localEulerAngles;
            euler.x = _desktopCameraPitch;
            childCamera.transform.localEulerAngles = euler;
        }
#endif
    }
}
