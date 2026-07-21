using UnityEngine;
using Pansim.Hardware;

namespace Pansim.Controllers
{
    /// <summary>
    /// Controls objects using the left joystick input
    /// </summary>
    public class LeftJoystickController : MonoBehaviour
    {
        [Header("Control Target")]
        [SerializeField] private Transform targetObject;
        [SerializeField] private bool useThisTransform = true;

        [Header("Movement Settings")]
        [SerializeField] private MovementMode movementMode = MovementMode.Translate;
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private Vector3 moveAxis = Vector3.up;
        [SerializeField] private bool invertX = false;
        [SerializeField] private bool invertY = true;

        [Header("Movement Limits")]
        [SerializeField] private bool useLimits = true;
        [SerializeField] private float minLimit = 0f;
        [SerializeField] private float maxLimit = 20f;
        [SerializeField] private LimitAxis limitAxis = LimitAxis.Y;

        [Header("Input Settings")]
        [SerializeField] private float deadZone = 0.1f;
        [SerializeField] private float smoothing = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool showDebug = false;

        public enum MovementMode
        {
            Translate,
            Rotate,
            Scale
        }

        public enum LimitAxis
        {
            X, Y, Z
        }

        private PansimHardwareController hardwareController;
        private Vector2 smoothedInput;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private Vector3 startScale;

        private void Start()
        {
            // Get hardware controller
            hardwareController = PansimHardwareController.Instance;

            if (hardwareController == null)
            {
                Debug.LogError("[LeftJoystickController] PansimHardwareController not found!");
                enabled = false;
                return;
            }

            // Setup target
            if (useThisTransform || targetObject == null)
            {
                targetObject = transform;
            }

            // Store initial state
            startPosition = targetObject.localPosition;
            startRotation = targetObject.localRotation;
            startScale = targetObject.localScale;

            // Subscribe to events
            hardwareController.OnDataReceived += OnHardwareDataReceived;
        }

        private void OnHardwareDataReceived(HardwareData data)
        {
            // Process input with deadzone
            float inputX = Mathf.Abs(data.left_x) > deadZone ? data.left_x : 0f;
            float inputY = Mathf.Abs(data.left_y) > deadZone ? data.left_y : 0f;

            // Apply inversion
            if (invertX) inputX = -inputX;
            if (invertY) inputY = -inputY;

            // Smooth the input
            smoothedInput = Vector2.Lerp(smoothedInput, new Vector2(inputX, inputY), 1f - smoothing);
        }

        private void Update()
        {
            if (targetObject == null || hardwareController == null || !hardwareController.IsConnected())
                return;

            ApplyMovement();
        }

        private void ApplyMovement()
        {
            switch (movementMode)
            {
                case MovementMode.Translate:
                    ApplyTranslation();
                    break;
                case MovementMode.Rotate:
                    ApplyRotation();
                    break;
                case MovementMode.Scale:
                    ApplyScale();
                    break;
            }
        }

        private void ApplyTranslation()
        {
            // Calculate movement based on configured axis
            Vector3 movement = Vector3.zero;

            if (moveAxis == Vector3.up || moveAxis == Vector3.down)
            {
                // Vertical movement (Y-axis)
                movement = moveAxis.normalized * smoothedInput.y * moveSpeed * Time.deltaTime;
            }
            else if (moveAxis == Vector3.right || moveAxis == Vector3.left)
            {
                // Horizontal movement (X-axis)
                movement = moveAxis.normalized * smoothedInput.x * moveSpeed * Time.deltaTime;
            }
            else if (moveAxis == Vector3.forward || moveAxis == Vector3.back)
            {
                // Forward/back movement (Z-axis)
                movement = moveAxis.normalized * smoothedInput.y * moveSpeed * Time.deltaTime;
            }
            else
            {
                // Custom axis - use both inputs
                movement = moveAxis.normalized * (smoothedInput.y + smoothedInput.x) * moveSpeed * Time.deltaTime;
            }

            // Apply movement
            Vector3 newPosition = targetObject.position + movement;

            // Apply limits if enabled
            if (useLimits)
            {
                switch (limitAxis)
                {
                    case LimitAxis.X:
                        newPosition.x = Mathf.Clamp(newPosition.x, minLimit, maxLimit);
                        break;
                    case LimitAxis.Y:
                        newPosition.y = Mathf.Clamp(newPosition.y, minLimit, maxLimit);
                        break;
                    case LimitAxis.Z:
                        newPosition.z = Mathf.Clamp(newPosition.z, minLimit, maxLimit);
                        break;
                }
            }

            targetObject.position = newPosition;
        }

        private void ApplyRotation()
        {
            float rotationAmount = smoothedInput.y * moveSpeed * Time.deltaTime;
            targetObject.Rotate(moveAxis * rotationAmount, Space.Self);
        }

        private void ApplyScale()
        {
            float scaleAmount = 1f + (smoothedInput.y * moveSpeed * Time.deltaTime * 0.1f);
            Vector3 newScale = targetObject.localScale * scaleAmount;

            if (useLimits)
            {
                float maxScaleComponent = Mathf.Max(newScale.x, newScale.y, newScale.z);
                float minScaleComponent = Mathf.Min(newScale.x, newScale.y, newScale.z);

                if (maxScaleComponent > maxLimit)
                {
                    float scaleFactor = maxLimit / maxScaleComponent;
                    newScale *= scaleFactor;
                }
                if (minScaleComponent < minLimit)
                {
                    float scaleFactor = minLimit / minScaleComponent;
                    newScale *= scaleFactor;
                }
            }

            targetObject.localScale = newScale;
        }

        public void ResetToStart()
        {
            if (targetObject == null) return;

            switch (movementMode)
            {
                case MovementMode.Translate:
                    targetObject.localPosition = startPosition;
                    break;
                case MovementMode.Rotate:
                    targetObject.localRotation = startRotation;
                    break;
                case MovementMode.Scale:
                    targetObject.localScale = startScale;
                    break;
            }
        }

        private void OnGUI()
        {
            if (!showDebug || !enabled) return;

            GUILayout.BeginArea(new Rect(10, 320, 250, 150));
            GUI.Box(new Rect(0, 0, 250, 150), "");

            GUILayout.Label("LEFT JOYSTICK CONTROLLER");
            GUILayout.Label($"Mode: {movementMode}");
            GUILayout.Label($"Target: {(targetObject != null ? targetObject.name : "None")}");
            GUILayout.Label($"Input: X={smoothedInput.x:F2} Y={smoothedInput.y:F2}");

            if (movementMode == MovementMode.Translate && targetObject != null)
            {
                float currentValue = 0f;
                switch (limitAxis)
                {
                    case LimitAxis.X: currentValue = targetObject.position.x; break;
                    case LimitAxis.Y: currentValue = targetObject.position.y; break;
                    case LimitAxis.Z: currentValue = targetObject.position.z; break;
                }
                GUILayout.Label($"Position ({limitAxis}): {currentValue:F2}");
                
                if (useLimits)
                {
                    GUILayout.Label($"Limits: [{minLimit:F1}, {maxLimit:F1}]");
                }
            }

            if (GUILayout.Button("Reset Position"))
            {
                ResetToStart();
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
            if (targetObject == null) return;

            // Draw movement axis
            Gizmos.color = Color.green;
            Gizmos.DrawRay(targetObject.position, moveAxis.normalized * 2f);

            // Draw limits
            if (useLimits && movementMode == MovementMode.Translate)
            {
                Gizmos.color = Color.red;
                Vector3 minPos = targetObject.position;
                Vector3 maxPos = targetObject.position;

                switch (limitAxis)
                {
                    case LimitAxis.X:
                        minPos.x = minLimit;
                        maxPos.x = maxLimit;
                        break;
                    case LimitAxis.Y:
                        minPos.y = minLimit;
                        maxPos.y = maxLimit;
                        break;
                    case LimitAxis.Z:
                        minPos.z = minLimit;
                        maxPos.z = maxLimit;
                        break;
                }

                Gizmos.DrawWireCube(minPos, Vector3.one * 0.5f);
                Gizmos.DrawWireCube(maxPos, Vector3.one * 0.5f);
                Gizmos.DrawLine(minPos, maxPos);
            }
        }
    }
}