using System;
using System.Collections;
using System.Collections.Generic;
using DemoKit;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using CommonUsages = UnityEngine.XR.CommonUsages;
using InputDevice = UnityEngine.XR.InputDevice;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// The user's body input: a headset with two controllers, or a simulated body with mouse and keyboard. Also sets up
    /// the pointers for the panels and the keys of the desktop version.
    /// </summary>
    [DefaultExecutionOrder(-95)]
    public sealed class UserRig : MonoBehaviour
    {
        public static readonly Vector3 Home = new Vector3(0f, 0f, 0.5f);
        public const float HomeYaw = 180f;
        public const float EyeHeight = 1.71f;

        private const float LongestHeadsetWait = 5f;
        private const float DesktopFieldOfView = 75f;
        private const float WalkRadius = 0.35f;
        private const float TurnSeconds = 0.25f;
        private const float ObserverFieldOfView = 45f;

        private const string DesktopHelp =
            "Right mouse: look    Q E: turn    WASD: step    1 idle  2 wave  3 4 5 raise  6 point  7 talk    " +
            "Space: push to talk    Tab: side panel    V: view from the side    Esc: menu    H: hide";

        private static readonly Vector3 ObserverPosition = new Vector3(2.3f, 1.55f, 0f);
        private static readonly Vector3 ObserverTarget = new Vector3(0f, 1.25f, 0f);

        [SerializeField] private XROrigin origin;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform leftController;
        [SerializeField] private Transform rightController;
        [Tooltip("Layers that the observer camera of the desktop version does not show.")]
        [SerializeField] private LayerMask hiddenFromObserver;

        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private InputAction talk;
        private DemoPointerMouse mouse;
        private Camera observer;
        private float turnTarget, turnVelocity;
        private bool isTurning, isTalkPressed;

        /// <summary>For automated tests: behave as with a headset, although none drives the head and the hands.</summary>
        internal static bool TestHeadset;

        public bool IsHeadset { get; private set; }

        public Camera ViewCamera => viewCamera;

        /// <summary>The simulated body, or null with a headset.</summary>
        public DemoDesktopRig Desktop { get; private set; }

        public Transform Head => Desktop != null ? Desktop.Head : viewCamera.transform;

        public XROrigin Origin => origin;

        /// <summary>True once the headset knows where it is. Always true without a headset.</summary>
        public bool IsTracked
        {
            get
            {
                if (!IsHeadset || TestHeadset) return true;
                InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                return head.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked;
            }
        }

        /// <summary>The push-to-talk button was pressed or released.</summary>
        public event Action<bool> TalkPressed;

        /// <summary>The user wants to go back to the menu (desktop).</summary>
        public event Action MenuPressed;

        /// <summary>The user wants to look at the side panel or back (desktop).</summary>
        public event Action PanelPressed;

        /// <summary>
        /// Waits for a headset that is about to render, which happens only some frames after the scene was loaded. Ends at
        /// once on a computer without a headset.
        /// </summary>
        public IEnumerator WaitForHeadset()
        {
            for (float waited = 0f; waited < LongestHeadsetWait; waited += Time.deltaTime)
            {
                bool isStarting = !TestHeadset && !IsDesktopForced() && HasHeadset() && !DemoXr.IsHeadsetActive();
                if (!isStarting) yield break;
                yield return null;
            }
        }

        /// <summary>Chooses headset or desktop. Call it once, after WaitForHeadset and before panels are built.</summary>
        public void SetUp()
        {
            // A stand-alone headset that nobody wears does not render yet, and it has no desktop to fall back to.
            bool hasHeadset = DemoXr.IsHeadsetActive() || (Application.isMobilePlatform && HasHeadset());
            IsHeadset = TestHeadset || (hasHeadset && !IsDesktopForced());
            if (!IsHeadset || TestHeadset) DisableTracking();

            if (IsHeadset) SetUpHeadset();
            else SetUpDesktop();
            DemoUi.Head = Head;

            talk = new InputAction("Push to talk", InputActionType.Button);
            talk.AddBinding("<XRController>{RightHand}/primaryButton");
            talk.AddBinding("<Keyboard>/space");
            talk.Enable();
        }

        /// <summary>Turns the view of the desktop version toward a direction. The user can look elsewhere at any time.</summary>
        public void TurnTo(float yaw)
        {
            if (Desktop == null) return;
            turnTarget = yaw;
            turnVelocity = 0f;
            isTurning = true;
        }

        public bool IsTurnedTo(float yaw) => Desktop != null && Mathf.Abs(Mathf.DeltaAngle(Desktop.Yaw, yaw)) < 12f;

        /// <summary>Shows or hides the list of keys. The user can change it with H.</summary>
        public void ShowHelp(bool on)
        {
            if (Desktop != null) Desktop.ShowHelp = on;
        }

        public void ToggleObserver()
        {
            if (IsHeadset) return;
            if (observer == null) observer = CreateObserver();
            bool observes = !observer.enabled;
            observer.enabled = observes;
            viewCamera.enabled = !observes;
            mouse.Camera = observes ? observer : viewCamera;
        }

        private void Update()
        {
            if (talk == null) return;

            bool isPressed = talk.IsPressed();
            if (isPressed != isTalkPressed)
            {
                isTalkPressed = isPressed;
                TalkPressed?.Invoke(isPressed);
            }
            if (Desktop != null) UpdateDesktop();
        }

        private void UpdateDesktop()
        {
            leftController.SetPositionAndRotation(Desktop.LeftHand.position, Desktop.LeftHand.rotation);
            rightController.SetPositionAndRotation(Desktop.RightHand.position, Desktop.RightHand.rotation);

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && Desktop.UserControl)
            {
                if (keyboard.vKey.wasPressedThisFrame) ToggleObserver();
                if (keyboard.tabKey.wasPressedThisFrame) PanelPressed?.Invoke();
                if (keyboard.escapeKey.wasPressedThisFrame) MenuPressed?.Invoke();
            }

            if (!isTurning) return;
            bool userLooks = Mouse.current != null && Mouse.current.rightButton.isPressed;
            Desktop.Yaw = Mathf.SmoothDampAngle(Desktop.Yaw, turnTarget, ref turnVelocity, TurnSeconds, Mathf.Infinity, DemoClock.DeltaTime);
            if (userLooks || Mathf.Abs(Mathf.DeltaAngle(Desktop.Yaw, turnTarget)) < 0.2f) isTurning = false;
        }

        private void SetUpHeadset()
        {
            Transform space = origin.CameraFloorOffsetObject.transform;
            DemoInput.AddHands(DemoPoseDriver.Create(space, DemoHand.Left).transform, DemoPoseDriver.Create(space, DemoHand.Right).transform);
        }

        private void SetUpDesktop()
        {
            origin.enabled = false;
            Desktop = DemoDesktopRig.Create(Home, HomeYaw, viewCamera);
            Desktop.EyeHeight = EyeHeight;
            Desktop.WalkRadius = WalkRadius;
            Desktop.HelpText = DesktopHelp;
            Desktop.Teleport(Home, HomeYaw);
            viewCamera.fieldOfView = DesktopFieldOfView;
            mouse = DemoInput.AddMouse(viewCamera);
        }

        /// <summary>Without devices the tracking components would hide the controllers or hold them at the origin.</summary>
        private void DisableTracking()
        {
            foreach (TrackedPoseDriver driver in origin.GetComponentsInChildren<TrackedPoseDriver>(true)) driver.enabled = false;
            if (origin.TryGetComponent(out XRInputModalityManager manager)) manager.enabled = false;
            leftController.gameObject.SetActive(true);
            rightController.gameObject.SetActive(true);
        }

        private Camera CreateObserver()
        {
            var created = new GameObject("Observer Camera").AddComponent<Camera>();
            created.transform.position = ObserverPosition;
            created.transform.LookAt(ObserverTarget);
            created.fieldOfView = ObserverFieldOfView;
            created.nearClipPlane = 0.05f;
            created.cullingMask = ~hiddenFromObserver.value;
            created.enabled = false;
            return created;
        }

        private bool HasHeadset()
        {
            SubsystemManager.GetSubsystems(displays);
            return displays.Count > 0;
        }

        private static bool IsDesktopForced() => Array.IndexOf(Environment.GetCommandLineArgs(), "-desktop") >= 0;

        private void OnDestroy() => talk?.Dispose();
    }
}