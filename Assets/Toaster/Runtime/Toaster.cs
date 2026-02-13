using UnityEngine;

namespace Toaster
{
    public static class Appliance
    {
        public const string Version = "0.6 (Crumb)";

        public enum BrowningLevel
        {
            Raw = 0,    // 32^3
            Light = 1,  // 64^3
            Burnt = 2   // 128^3
        }

        public static int GetResolution(BrowningLevel level) => level switch
        {
            BrowningLevel.Raw => 32,
            BrowningLevel.Light => 64,
            BrowningLevel.Burnt => 128,
            _ => 64
        };

        public static void Log(string message)
        {
            Debug.Log($"[TOASTER] {message}");
        }

        public static void LogWarning(string message)
        {
            Debug.LogWarning($"[TOASTER] {message}");
        }
    }
}
