using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PillarsAbove
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class V2OrbitCameraController : MonoBehaviour
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        private struct CGPoint
        {
            public double x;
            public double y;
        }

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern IntPtr CGEventCreate(IntPtr source);

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern CGPoint CGEventGetLocation(IntPtr eventRef);

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern void CFRelease(IntPtr cf);

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern int CGWarpMouseCursorPosition(CGPoint newCursorPosition);
#endif

        [Header("SampleScene orbit behavior")]
        [SerializeField] private Vector2 targetHorizontal = Vector2.zero;
        [SerializeField] private float targetHeight;
        [SerializeField] private float orbitYaw = 42f;
        [SerializeField] private float orbitPitch = 18f;
        [SerializeField, Min(0.1f)] private float orbitDistance = 22f;

        [Header("Input")]
        [SerializeField, Min(0f)] private float orbitSpeed = 13.5f;
        [SerializeField, Min(0f)] private float pitchSpeed = 10f;
        [SerializeField, Min(0f)] private float panSpeed = 0.032f;
        [SerializeField, Min(0f)] private float keyboardVerticalSpeed = 8f;
        [SerializeField, Min(0f)] private float zoomSpeed = 1.5f;
        [SerializeField, Min(0f)] private float dragActivationThresholdPixels = 8f;
        [SerializeField] private Vector2 pitchRange = new Vector2(4f, 74f);
        [SerializeField, Min(0.1f)] private float minimumDistance = 7f;
        [SerializeField, Min(0.1f)] private float maximumDistance = 50f;
        [SerializeField] private Vector2 verticalRange = new Vector2(-8f, 18f);

        private bool gestureActive;
        private bool leftPressCandidate;
        private bool rightPressCandidate;
        private bool rotateDragActive;
        private bool panDragActive;
        private Vector2 leftPressPosition;
        private Vector2 rightPressPosition;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        private CGPoint savedCursorPosition;
        private bool hasSavedCursorPosition;
#endif

        private void Awake()
        {
            var controlledCamera = GetComponent<Camera>();
            controlledCamera.orthographic = false;
            controlledCamera.fieldOfView = 36f;
            controlledCamera.nearClipPlane = 0.1f;
            controlledCamera.farClipPlane = 2000f;
        }

        private void LateUpdate()
        {
            HandleInput();
            UpdateCameraTransform();
        }

        private void HandleInput()
        {
            UpdateMouseDragState();
            var rotateGesture = rotateDragActive;
            var panGesture = panDragActive || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            var active = rotateGesture || panGesture;
            SetGestureCursor(active);
            if (rotateGesture)
            {
                orbitYaw += Input.GetAxis("Mouse X") * orbitSpeed;
                orbitPitch = Mathf.Clamp(
                    orbitPitch - Input.GetAxis("Mouse Y") * pitchSpeed,
                    pitchRange.x,
                    pitchRange.y);
            }
            else if (panGesture)
            {
                var rotation = Quaternion.Euler(0f, orbitYaw, 0f);
                var right = rotation * Vector3.right;
                var forward = rotation * Vector3.forward;
                var scale = Mathf.Max(0.1f, orbitDistance) * panSpeed;
                var delta = (-right * Input.GetAxis("Mouse X") - forward * Input.GetAxis("Mouse Y")) * scale;
                targetHorizontal += new Vector2(delta.x, delta.z);
            }
            if (active)
            {
                RestoreCursorPosition(false);
            }

            orbitDistance = Mathf.Clamp(
                orbitDistance - Input.mouseScrollDelta.y * zoomSpeed,
                minimumDistance,
                maximumDistance);

            if (Input.GetKey(KeyCode.Q)) targetHeight -= Time.deltaTime * keyboardVerticalSpeed;
            if (Input.GetKey(KeyCode.E)) targetHeight += Time.deltaTime * keyboardVerticalSpeed;
            targetHeight = Mathf.Clamp(targetHeight, verticalRange.x, verticalRange.y);
        }

        private void UpdateMouseDragState()
        {
            if (Input.GetMouseButtonDown(0))
            {
                leftPressCandidate = true;
                rotateDragActive = false;
                leftPressPosition = Input.mousePosition;
                SaveCursorPosition();
            }
            if (Input.GetMouseButtonDown(1))
            {
                rightPressCandidate = true;
                panDragActive = false;
                rightPressPosition = Input.mousePosition;
                SaveCursorPosition();
            }

            if (leftPressCandidate && Input.GetMouseButton(0) &&
                Vector2.Distance(leftPressPosition, Input.mousePosition) > dragActivationThresholdPixels)
            {
                leftPressCandidate = false;
                rotateDragActive = true;
            }
            if (rightPressCandidate && Input.GetMouseButton(1) &&
                Vector2.Distance(rightPressPosition, Input.mousePosition) > dragActivationThresholdPixels)
            {
                rightPressCandidate = false;
                panDragActive = true;
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (!rotateDragActive && !gestureActive)
                {
                    ClearSavedCursorPosition();
                }
                leftPressCandidate = false;
                rotateDragActive = false;
            }
            if (Input.GetMouseButtonUp(1))
            {
                if (!panDragActive && !gestureActive)
                {
                    ClearSavedCursorPosition();
                }
                rightPressCandidate = false;
                panDragActive = false;
            }
        }

        private void UpdateCameraTransform()
        {
            var target = new Vector3(targetHorizontal.x, targetHeight, targetHorizontal.y);
            var rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            transform.position = target + rotation * new Vector3(0f, 0f, -orbitDistance);
            transform.LookAt(target);
        }

        private void SetGestureCursor(bool active)
        {
            if (gestureActive == active) return;
            gestureActive = active;
            if (active)
            {
                EnsureCursorPositionSaved();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                RestoreCursorPosition(true);
            }
        }

        private void SaveCursorPosition()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            var eventRef = CGEventCreate(IntPtr.Zero);
            if (eventRef == IntPtr.Zero)
            {
                hasSavedCursorPosition = false;
                return;
            }
            savedCursorPosition = CGEventGetLocation(eventRef);
            hasSavedCursorPosition = true;
            CFRelease(eventRef);
#endif
        }

        private void EnsureCursorPositionSaved()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            if (!hasSavedCursorPosition)
            {
                SaveCursorPosition();
            }
#else
            SaveCursorPosition();
#endif
        }

        private void ClearSavedCursorPosition()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            hasSavedCursorPosition = false;
#endif
        }

        private void RestoreCursorPosition(bool clearAfterRestore)
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            if (!hasSavedCursorPosition) return;
            CGWarpMouseCursorPosition(savedCursorPosition);
            if (clearAfterRestore)
            {
                hasSavedCursorPosition = false;
            }
#endif
        }

        private void OnDisable()
        {
            SetGestureCursor(false);
        }
    }
}
