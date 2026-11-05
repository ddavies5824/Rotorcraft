// The web download has a hard size limit. URP keeps every combination of post-processing effects, and the game uses
// only bloom without dirt, ACES tonemapping, colour adjustment and vignette (Settings/TestFlight Volume, SkyEnvironment).
// Web builds leave out the variants for the rest; add a keyword's effect to the game and it must come off this list.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rotorcraft.EditorTools
{
    sealed class WebShaderStripping : IPreprocessShaders
    {
        const string UberPost = "Hidden/Universal Render Pipeline/UberPost";
        static readonly string[] Unused = { "_DISTORTION", "_CHROMATIC_ABERRATION", "_FILM_GRAIN", "_BLOOM_LQ_DIRT", "_BLOOM_HQ_DIRT", "_TONEMAP_NEUTRAL", "_HDR_GRADING" };

        public int callbackOrder => 100;   // after URP's own stripping

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL || shader.name != UberPost) return;
            var keywords = Unused.Select(k => new ShaderKeyword(shader, k)).ToArray();
            for (int i = data.Count - 1; i >= 0; i--)
                if (keywords.Any(k => data[i].shaderKeywordSet.IsEnabled(k))) data.RemoveAt(i);
        }
    }
}
