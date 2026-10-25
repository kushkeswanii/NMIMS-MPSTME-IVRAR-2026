using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

namespace TrustBreach.Analytics.Dashboard
{
    /// <summary>
    /// The research dashboard is a desktop (Editor / standalone) screen. If XR was initialised on startup, stop it so the
    /// dashboard renders to the Game view / monitor instead of a headset. Uses the XR Plug-in Management API only.
    /// </summary>
    public class DesktopModeGuard : MonoBehaviour
    {
        public static bool XRStoppedByGuard { get; private set; }

        protected virtual IEnumerator Start()
        {
            // Wait one frame so a loader that is still initialising has finished.
            yield return null;
            XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null || !manager.isInitializationComplete) yield break;
            manager.StopSubsystems();
            manager.DeinitializeLoader();
            XRStoppedByGuard = true;
            Debug.Log("[TrustBreach.Dashboard] XR stopped: the research dashboard runs on the desktop display.");
        }
    }
}
