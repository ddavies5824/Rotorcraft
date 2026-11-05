// Lets the game ask the headset for a refresh rate, through an OpenXR extension that Unity has no call for. Not every
// runtime has it, and then Rates is empty and the headset stays at whatever its own app is set to.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace Rotorcraft.VR
{
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "Rotorcraft headset refresh rate", BuildTargetGroups = new[] { BuildTargetGroup.Standalone }, Company = "Rotorcraft",
        Desc = "Lets the game ask the headset for a refresh rate it can hold.", OpenxrExtensionStrings = Extension, Version = "1.0.0", FeatureId = "com.rotorcraft.vr.headset-rate")]
#endif
    public sealed class HeadsetRate : OpenXRFeature
    {
        const string Extension = "XR_FB_display_refresh_rate";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int GetProcAddr(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr function);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int EnumerateRates(ulong session, uint capacity, out uint count, [In, Out] float[] rates);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int RequestRate(ulong session, float rate);

        static EnumerateRates enumerate;
        static RequestRate request;
        static ulong session;

        /// <summary>The rates this headset offers.</summary>
        public static IReadOnlyList<float> Rates { get; private set; } = new float[0];
        /// <summary>Counts each time the headset starts showing the game, when a rate can be asked for.</summary>
        public static int Begun { get; private set; }

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            enumerate = null;
            request = null;
            if (!OpenXRRuntime.IsExtensionEnabled(Extension))
            {
                Debug.Log("VR: this headset's software does not let a game choose the refresh rate");
                return true;
            }
            var get = Marshal.GetDelegateForFunctionPointer<GetProcAddr>(xrGetInstanceProcAddr);
            enumerate = Proc<EnumerateRates>(get, xrInstance, "xrEnumerateDisplayRefreshRatesFB");
            request = Proc<RequestRate>(get, xrInstance, "xrRequestDisplayRefreshRateFB");
            return true;
        }

        static T Proc<T>(GetProcAddr get, ulong instance, string name) where T : class =>
            get(instance, name, out var f) == 0 && f != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(f) : null;

        protected override void OnSessionCreate(ulong xrSession)
        {
            session = xrSession;
            ReadRates();
        }

        protected override void OnSessionBegin(ulong xrSession)
        {
            ReadRates();    // what is on offer before the headset is showing the game may not be the whole list
            Begun++;
        }

        static void ReadRates()
        {
            Rates = new float[0];
            if (enumerate == null || enumerate(session, 0, out var n, null) != 0 || n == 0) return;
            var rates = new float[n];
            if (enumerate(session, n, out n, rates) != 0) return;
            Rates = rates;
            Debug.Log($"VR: the headset offers {string.Join(", ", rates)} Hz");
        }

        protected override void OnSessionDestroy(ulong xrSession)
        {
            session = 0;
            Rates = new float[0];
        }

        /// <summary>Ask for a rate; false if the headset refused.</summary>
        public static bool Request(float hz)
        {
            if (request == null || session == 0) return false;
            var result = request(session, hz);
            Debug.Log($"VR: asked the headset for {hz} Hz: {(result == 0 ? "accepted" : $"refused, error {result}")}");
            return result == 0;
        }
    }
}
