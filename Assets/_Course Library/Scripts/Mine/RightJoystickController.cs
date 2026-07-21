using UnityEngine;
using Pansim.Hardware;

namespace Pansim.Controllers
{
    /// <summary>
    /// Controls camera and view using the right joystick input
    /// </summary>
    public class RightJoystickController : MonoBehaviour
    {
        [Header("Control Target")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool findMainCamera = true;

        [Header("Rotation Settings (X-Axis)")]
        [SerializeField] private bool enableRotation = true;
        [SerializeField] private float rotationSpeed = 50f;
        [SerializeField] private bool invertRotation = false;
        [SerializeField] private RotationAxis rotationAxis = RotationAxis.Y;

        [Header("Zoom Settings (Y-Axis)")]
        [SerializeField] private bool enableZoom = true;
        [SerializeField] private ZoomMode zoomMode = ZoomMode.FieldOfView;
        [SerializeField] private float zoomSpeed = 20f;
        [SerializeField] private bool invertZoom = false;
        [SerializeField] private float minZoom = 20f;
        [SerializeField] private float maxZoom = 90f;

        [Header("Look Settings")]
        [SerializeField] private bool enableFreeLook = false;
        [SerializeField] private float lookSensitivity = 2f;
        [SerializeField] private float maxLookAngle = 80f;

        [Header("Input Settings")]
        [SerializeField] private float deadZone = 0.1f;
        [SerializeField] private float smoothing = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool showDebug = false;

        public enum RotationAxis
        {
            X, Y, Z, XY, All
        }

        public enum ZoomMode
        {
            FieldOfView,
            Distance,
            OrthoSize
        }

        private PansimHardwareController hardwareController;
        private Vector2 smoothedInput;
        private float currentRotation;
        private float currentPitch;
        private float currentZoom;
        private Vector3 initialCameraPosition;
        private Quaternion initialCameraRotation;

        private void Start()
        {
            // Get hardware controller
            hardwareController = PansimHardwareController.Instance;

            if (hardwareController == null)
            {
                Debug.LogError("[RightJoystickController] PansimHardwareController not found!");
                enabled = false;
                return;
            }

            // Setup camera
            if (findMainCamera && targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (cameraTransform == null && targetCamera != null)
            {
                cameraTransform = targetCamera.transform;
            }

            if (cameraTransform == null)
            {
                Debug.LogError("[RightJoystickController] No camera transform assigned!");
                enabled = false;
                return;
            }

            // Store initial state
            initialCameraPosition = cameraTransform.position;
            initialCameraRotation = cameraTransform.rotation;

            // Initialize zoom
            if (targetCamera != null)
            {
                switch (zoomMode)
                {
                    case ZoomMode.FieldOfView:
                        currentZoom = targetCamera.fieldOfView;
                        break;
                    case ZoomMode.OrthoSize:
                        currentZoom = targetCamera.orthographicSize;
                        break;
                    case ZoomMode.Distance:
                        currentZoom = Vector3.Distance(cameraTransform.position, Vector3.zero);
                        break;
                }
            }

            // Subscribe to events
            hardwareController.OnDataReceived += OnHardwareDataReceived;
        }

        private void OnHardwareDataReceived(HardwareData data)
        {
            // Process input with deadzone
            float inputX = Mathf.Abs(data.right_x) > deadZone ? data.right_x : 0f;
            float inputY = Mathf.Abs(data.right_y) > deadZone ? data.right_y : 0f;

            // Apply inversion
            if (invertRotation) inputX = -inputX;
            if (invertZoom) inputY = -inputY;

            // Smooth the input
            smoothedInput = Vector2.Lerp(smoothedInput, new Vector2(inputX, inputY), 1f - smoothing);
        }

        private void Update()
        {
            if (cameraTransform == null || hardwareController == null || !hardwareController.IsConnected())
                return;

            if (enableFreeLook)
            {
                ApplyFreeLook();
            }
            else
            {
                if (enableRotation)
                    ApplyRotation();

                if (enableZoom && targetCamera != null)
                    ApplyZoom();
            }
        }

        private void ApplyRotation()
        {
            float rotationAmount = smoothedInput.x * rotationSpeed * Time.deltaTime;

            switch (rotationAxis)
            {
                case RotationAxis.X:
                    cameraTransform.Rotate(rotationAmount, 0, 0, Space.World);
                    break;
                case RotationAxis.Y:
                    cameraTransform.Rotate(0, rotationAmount, 0, Space.World);
                    break;
                case RotationAxis.Z:
                    cameraTransform.Rotate(0, 0, rotationAmount, Space.World);
                    break;
                case RotationAxis.XY:
                    cameraTransform.Rotate(smoothedInput.y * rotationSpeed * Time.deltaTime, 
                                          rotationAmount, 0, Space.Self);
                    break;
                case RotationAxis.All:
                    cameraTransform.Rotate(smoothedInput.y * rotationSpeed * Time.deltaTime,
                                          rotationAmount, 0, Space.Self);
                    break;
            }

            currentRotation += rotationAmount;
        }

        private void ApplyZoom()
        {
            float zoomDelta = smoothedInput.y * zoomSpeed * Time.deltaTime;

            switch (zoomMode)
            {
                case ZoomMode.FieldOfView:
                    if (targetCamera != null && !targetCamera.orthographic)
                    {
                        targetCamera.fieldOfView = Mathf.Clamp(
                            targetCamera.fieldOfView + zoomDelta,
                            minZoom, maxZoom
                        );
                        currentZoom = targetCamera.fieldOfView;
                    }
                    break;

                case ZoomMode.OrthoSize:
                    if (targetCamera != null && targetCamera.orthographic)
                    {
                        targetCamera.orthographicSize = Mathf.Clamp(
                            targetCamera.orthographicSize + zoomDelta,
                            minZoom, maxZoom
                        );
                        currentZoom = targetCamera.orthographicSize;
                    }
                    break;

                case ZoomMode.Distance:
                    Vector3 direction = cameraTransform.forward;
                    Vector3 newPosition = cameraTransform.position + direction * zoomDelta;
                    
                    float distance = Vector3.Distance(newPosition, Vector3.zero);
                    if (distance >= minZoom && distance <= maxZoom)
                    {
                        cameraTransform.position = newPosition;
                        currentZoom = distance;
                    }
                    break;
            }
        }

        private void ApplyFreeLook()
        {
            // Horizontal rotation (yaw)
            currentRotation += smoothedInput.x * lookSensitivity;
            
            // Vertical rotation (pitch)
            currentPitch -= smoothedInput.y * lookSensitivity;
            currentPitch = Mathf.Clamp(currentPitch, -maxLookAngle, maxLookAngle);

            // Apply rotation
            cameraTransform.rotation = Quaternion.Euler(currentPitch, currentRotation, 0);
        }

        public void ResetCamera()
        {
            if (cameraTransform != null)
            {
                cameraTransform.position = initialCameraPosition;
                cameraTransform.rotation = initialCameraRotation;
            }

            currentRotation = 0;
            currentPitch = 0;

            if (targetCamera != null)
            {
                switch (zoomMode)
                {
                    case ZoomMode.FieldOfView:
                        targetCamera.fieldOfView = (minZoom + maxZoom) / 2f;
                        break;
                    case ZoomMode.OrthoSize:
                        targetCamera.orthographicSize = (minZoom + maxZoom) / 2f;
                        break;
                }
            }
        }

        public void SetZoomLimits(float min, float max)
        {
            minZoom = min;
            maxZoom = max;
        }

        public void SetRotationSpeed(float speed)
        {
            rotationSpeed = speed;
        }

        public void SetZoomSpeed(float speed)
        {
            zoomSpeed = speed;
        }

        private void OnGUI()
        {
            if (!showDebug || !enabled) return;

            GUILayout.BeginArea(new Rect(270, 320, 250, 180));
            GUI.Box(new Rect(0, 0, 250, 180), "");

            GUILayout.Label("RIGHT JOYSTICK CONTROLLER");
            
            if (enableFreeLook)
            {
                GUILayout.Label("Mode: Free Look");
                GUILayout.Label($"Rotation: {currentRotation:F1}°");
                GUILayout.Label($"Pitch: {currentPitch:F1}°");
            }
            else
            {
                GUILayout.Label($"Rotation: {(enableRotation ? "ON" : "OFF")} ({rotationAxis})");
                GUILayout.Label($"Zoom: {(enableZoom ? "ON" : "OFF")} ({zoomMode})");
            }

            GUILayout.Label($"Input: X={smoothedInput.x:F2} Y={smoothedInput.y:F2}");

            if (targetCamera != null && enableZoom)
            {
                GUILayout.Label($"Current Zoom: {currentZoom:F1}");
                GUILayout.Label($"Zoom Range: [{minZoom:F1}, {maxZoom:F1}]");
            }

            if (cameraTransform != null)
            {
                GUILayout.Label($"Cam Pos: {cameraTransform.position}");
            }

            if (GUILayout.Button("Reset Camera"))
            {
                ResetCamera();
            }

            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (hardwareController != null)
            {
                hardwareController.OnDataReceived -= OnHardwareDataReceived;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (cameraTransform == null) return;

            // Draw camera view cone
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(cameraTransform.position, 0.5f);

            if (targetCamera != null && !targetCamera.orthographic)
            {
                float angle = targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float distance = 10f;
                
                Vector3[] corners = new Vector3[4];
                float halfHeight = distance * Mathf.Tan(angle);
                float halfWidth = halfHeight * targetCamera.aspect;

                corners[0] = cameraTransform.position + cameraTransform.forward * distance + cameraTransform.up * halfHeight - cameraTransform.right * halfWidth;
                corners[1] = cameraTransform.position + cameraTransform.forward * distance + cameraTransform.up * halfHeight + cameraTransform.right * halfWidth;
                corners[2] = cameraTransform.position + cameraTransform.forward * distance - cameraTransform.up * halfHeight + cameraTransform.right * halfWidth;
                corners[3] = cameraTransform.position + cameraTransform.forward * distance - cameraTransform.up * halfHeight - cameraTransform.right * halfWidth;

                Gizmos.color = Color.cyan;
                for (int i = 0; i < 4; i++)
                {
                    Gizmos.DrawLine(cameraTransform.position, corners[i]);
                    Gizmos.DrawLine(corners[i], corners[(i + 1) % 4]);
                }
            }
        }
    }
}