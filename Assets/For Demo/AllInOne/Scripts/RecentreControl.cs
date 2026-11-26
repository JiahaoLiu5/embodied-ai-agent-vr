using System.Collections;
using System.Collections.Generic;
using DemoKit;
using UnityEngine;
using UnityEngine.XR;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// Puts the user on the start place, looking at the agent, with the eyes at the height of the study. Runs again when
    /// the headset is recentred by the system.
    /// </summary>
    public sealed class RecentreControl : MonoBehaviour
    {
        [SerializeField] private UserRig rig;
        [SerializeField] private ConditionSwitcher switcher;
        [SerializeField] private AvatarDresser body;

        private readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        private bool isPending;

        private void OnDestroy()
        {
            foreach (XRInputSubsystem subsystem in subsystems) subsystem.trackingOriginUpdated -= HandleTrackingOrigin;
        }

        /// <summary>Follows the recentring of the system. Call it once, when it is known whether there is a headset.</summary>
        public void Listen()
        {
            SubsystemManager.GetSubsystems(subsystems);
            foreach (XRInputSubsystem subsystem in subsystems) subsystem.trackingOriginUpdated += HandleTrackingOrigin;
        }

        /// <summary>Covers the view for a moment and recentres.</summary>
        public void RecentreSoftly() => DemoFade.For(rig.ViewCamera).Blink(Recentre, 0.15f, 0.3f, 0.3f);

        public void Recentre()
        {
            if (rig.Desktop != null)
            {
                rig.Desktop.Pitch = 0f;
                rig.Desktop.Teleport(UserRig.Home, UserRig.HomeYaw);
            }
            else
            {
                rig.Origin.MatchOriginUpCameraForward(Vector3.up, Vector3.back);
                rig.Origin.MoveCameraToWorldLocation(UserRig.Home + Vector3.up * UserRig.EyeHeight);
            }
            switcher.Refresh();
            StartCoroutine(body.SnapFeet());
        }

        private void HandleTrackingOrigin(XRInputSubsystem subsystem)
        {
            if (isPending || !rig.IsHeadset || !isActiveAndEnabled) return;
            StartCoroutine(RecentreNextFrame());
        }

        private IEnumerator RecentreNextFrame()
        {
            isPending = true;
            yield return null;
            isPending = false;
            Recentre();
        }
    }
}