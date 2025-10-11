using System;

namespace BattleshipsVR.Bootstrap
{
    /// <summary>Simple -key=value CLI reader used at boot</summary>
    public static class StartupArgs
    {
        /// <summary>Returns value for provided key or default when missing</summary>
        public static string Get(string keyConst, string defaultValue)
        {
            string[] args = Environment.GetCommandLineArgs();
            foreach (string a in args)
                if (a.StartsWith(keyConst, StringComparison.OrdinalIgnoreCase))
                    return a.Substring(keyConst.Length);
            return defaultValue;
        }
    }
}
