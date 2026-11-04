// The project's VR settings, made by code so they can be checked and repeated: OpenXR for Windows, started by the
// game when the player asks rather than at launch, both eyes drawn in one pass, and the Quest controllers' profiles.
// Only the Windows target is touched, which keeps VR out of the web build.
using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Rotorcraft.EditorTools
{
    public static class VrSetup
    {
        [MenuItem("Rotorcraft/Set up VR")]
        public static void Ensure()
        {
            const BuildTargetGroup Windows = BuildTargetGroup.Standalone;
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(Windows)) perTarget.CreateDefaultSettingsForBuildTarget(Windows);
            if (!perTarget.HasManagerSettingsForBuildTarget(Windows)) perTarget.CreateDefaultManagerSettingsForBuildTarget(Windows);
            var general = perTarget.SettingsForBuildTarget(Windows);
            general.InitManagerOnStart = false;
            EditorUtility.SetDirty(general);
            var manager = perTarget.ManagerSettingsForBuildTarget(Windows);
            var assigned = XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, Windows);
            EditorUtility.SetDirty(manager);

            FeatureHelpers.RefreshFeatures(Windows);
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(Windows);
            xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            xr.GetFeature<OculusTouchControllerProfile>().enabled = true;
            xr.GetFeature<MetaQuestTouchPlusControllerProfile>().enabled = true;   // the Quest 3 family's controllers
            EditorUtility.SetDirty(xr);
            AssetDatabase.SaveAssets();
            Debug.Log($"VR setup: OpenXR loader assigned {assigned}, loaders {manager.activeLoaders.Count}, starts at launch {general.InitManagerOnStart}, render mode {xr.renderMode}");
        }
    }
}
