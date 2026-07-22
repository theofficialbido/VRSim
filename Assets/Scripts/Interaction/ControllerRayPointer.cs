using UnityEngine;
using UnityEngine.InputSystem;
using VRSIM.Binding;

namespace VRSIM.Interaction
{
    /// <summary>
    /// Aim-and-click with a VR controller: casts a ray, highlights whatever
    /// <see cref="RigCommandButton"/> it lands on, and presses it when the
    /// trigger is pulled.
    ///
    /// It binds several inputs (trigger, grip, A/X) rather than one, because a
    /// diagnostic that fails silently because the wrong button was bound is
    /// worse than useless -- it would look like a network fault. For the same
    /// reason it draws a visible ray: if nothing highlights, the problem is
    /// aim or tracking, not the connection.
    ///
    /// The real control library will use XRI interactors. This is deliberately
    /// self-contained so that a failure here has exactly one meaning.
    /// </summary>
    public class ControllerRayPointer : MonoBehaviour
    {
        [Header("Ray")]
        public float maxDistance = 5f;
        public float rayWidth = 0.006f;
        public Color idleColour = new Color(0.45f, 0.65f, 0.95f, 0.85f);
        public Color hitColour = new Color(0.35f, 0.85f, 0.45f, 0.95f);

        [Header("Input")]
        [Tooltip("Which hand this pointer follows. Used to build the input bindings.")]
        public string hand = "RightHand";

        private LineRenderer _line;
        private InputAction _press;
        private IRigPressable _hovered;

        private void Awake()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.widthMultiplier = rayWidth;
            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.textureMode = LineTextureMode.Stretch;
            SetRayColour(idleColour);

            // Bind everything a Quest controller might reasonably call "click",
            // so the test does not hinge on guessing the right one.
            _press = new InputAction($"{hand} Press", InputActionType.Button);
            _press.AddBinding($"<XRController>{{{hand}}}/triggerPressed");
            _press.AddBinding($"<XRController>{{{hand}}}/gripPressed");
            _press.AddBinding($"<XRController>{{{hand}}}/primaryButton");
            _press.AddBinding($"<XRController>{{{hand}}}/trigger").WithProcessor("stickDeadzone(min=0.5)");
        }

        private void OnEnable() => _press?.Enable();
        private void OnDisable() => _press?.Disable();
        private void OnDestroy() => _press?.Dispose();

        private void Update()
        {
            var origin = transform.position;
            var direction = transform.forward;
            var endPoint = origin + direction * maxDistance;

            IRigPressable target = null;
            if (Physics.Raycast(origin, direction, out var hit, maxDistance))
            {
                endPoint = hit.point;
                // Any IRigPressable: a diagnostic button or a real bound control.
                target = hit.collider.GetComponentInParent<MonoBehaviour>() as IRigPressable
                         ?? FindPressable(hit.collider);
            }

            if (!ReferenceEquals(target, _hovered))
            {
                _hovered = target;
                SetRayColour(target != null ? hitColour : idleColour);
            }

            _line.SetPosition(0, origin);
            _line.SetPosition(1, endPoint);

            if (_hovered != null && _press.WasPressedThisFrame())
                _hovered.Press();
        }

        /// <summary>Searches the hit object's parents for anything pressable.</summary>
        private static IRigPressable FindPressable(Collider collider)
        {
            foreach (var behaviour in collider.GetComponentsInParent<MonoBehaviour>())
                if (behaviour is IRigPressable pressable) return pressable;
            return null;
        }

        private void SetRayColour(Color colour)
        {
            _line.startColor = colour;
            _line.endColor = new Color(colour.r, colour.g, colour.b, colour.a * 0.25f);
        }
    }
}
