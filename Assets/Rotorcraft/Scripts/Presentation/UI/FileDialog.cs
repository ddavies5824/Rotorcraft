// The Windows "open file" dialog for importing a save (comdlg32). Elsewhere (the web build) there is none:
// importing is done by pasting the save text instead.
using System;
using System.Runtime.InteropServices;

namespace Rotorcraft.Presentation.UI
{
    public static class FileDialog
    {
        public static bool Available =>
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            true;
#else
            false;
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OpenFileName
        {
            public int lStructSize; public IntPtr hwndOwner, hInstance;
            public string lpstrFilter, lpstrCustomFilter; public int nMaxCustFilter, nFilterIndex;
            public IntPtr lpstrFile; public int nMaxFile;
            public string lpstrFileTitle; public int nMaxFileTitle;
            public string lpstrInitialDir, lpstrTitle; public int Flags; public short nFileOffset, nFileExtension;
            public string lpstrDefExt; public IntPtr lCustData, lpfnHook; public string lpTemplateName;
            public IntPtr pvReserved; public int dwReserved, flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileNameW(ref OpenFileName ofn);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
#endif

        /// <summary>Ask for a .json file; null if cancelled or not available.</summary>
        public static string OpenJson(string title)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            const int MAX = 1024;
            var buf = Marshal.AllocHGlobal(MAX * 2);
            try
            {
                Marshal.WriteInt16(buf, 0);
                var ofn = new OpenFileName
                {
                    lStructSize = Marshal.SizeOf<OpenFileName>(), hwndOwner = GetActiveWindow(),
                    lpstrFilter = "Rotorcraft save (*.json)\0*.json\0All files (*.*)\0*.*\0\0", nFilterIndex = 1,
                    lpstrFile = buf, nMaxFile = MAX, lpstrTitle = title,
                    Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008,   // explorer, file must exist, path must exist, keep the folder
                };
                return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(buf) : null;
            }
            catch { return null; }
            finally { Marshal.FreeHGlobal(buf); }
#else
            return null;
#endif
        }
    }
}
