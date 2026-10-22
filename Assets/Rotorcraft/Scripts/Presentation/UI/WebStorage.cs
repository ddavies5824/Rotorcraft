// The browser side of the web build (Plugins/WebGL/Rotorcraft.jslib): saves live in the browser's IndexedDB,
// flushed after each write; Google sign-in uses Google Identity Services in a popup. Elsewhere these do nothing.
using System.Runtime.InteropServices;

namespace Rotorcraft.Presentation.UI
{
    public static class WebStorage
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void RcSyncFs();
        [DllImport("__Internal")] static extern void RcGoogleToken(string clientId, string scopes, string gameObject);
        public static void Flush() => RcSyncFs();
        /// <summary>Ask Google for an access token; the answer arrives as OnWebToken(json) on the named object.</summary>
        public static void GoogleToken(string clientId, string scopes, string gameObject) => RcGoogleToken(clientId, scopes, gameObject);
#else
        public static void Flush() { }
        public static void GoogleToken(string clientId, string scopes, string gameObject) { }
#endif
    }
}
