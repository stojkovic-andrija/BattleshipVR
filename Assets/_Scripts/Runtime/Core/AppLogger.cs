using System;
using UnityEngine;

namespace BattleshipsVR.Core
{
    /// <summary>Central log with a hook for in-VR overlay if needed</summary>
    public static class AppLogger
    {
        public static event Action<string> OnLog;

        public static void Info(string msg)
        {
            Debug.Log($"[BSVR] {msg}");
            OnLog?.Invoke(msg);
        }

        public static void Warn(string msg)
        {
            Debug.LogWarning($"[BSVR] {msg}");
            OnLog?.Invoke(msg);
        }

        public static void Error(string msg)
        {
            Debug.LogError($"[BSVR] {msg}");
            OnLog?.Invoke(msg);
        }
    }
}
