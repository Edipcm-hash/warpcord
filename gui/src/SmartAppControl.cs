using System;
using Microsoft.Win32;

namespace WarpCord
{
    internal enum SacState
    {
        Off,
        On,
        Evaluation,
        Unknown
    }

    /// <summary>
    /// Smart App Control blocks unsigned binaries that have no reputation and has
    /// no per-app allowlist, so an unsigned WarpCord.exe cannot start there and
    /// sing-box.exe cannot run either. Read its state so the UI can say so.
    /// </summary>
    internal static class SmartAppControl
    {
        private const string PolicyKey = @"SYSTEM\CurrentControlSet\Control\CI\Policy";
        private const string ValueName = "VerifiedAndReputablePolicyState";

        public static SacState Read()
        {
            // WARPCORD_SAC_FORCE lets the UI be exercised on a machine without SAC
            string forced = Environment.GetEnvironmentVariable("WARPCORD_SAC_FORCE");
            if (!string.IsNullOrEmpty(forced))
            {
                switch (forced.Trim())
                {
                    case "0": return SacState.Off;
                    case "1": return SacState.On;
                    case "2": return SacState.Evaluation;
                    default: return SacState.Unknown;
                }
            }

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(PolicyKey))
                {
                    if (key == null) return SacState.Unknown;
                    object raw = key.GetValue(ValueName);
                    if (raw == null) return SacState.Unknown;
                    int state = Convert.ToInt32(raw);
                    if (state == 0) return SacState.Off;
                    if (state == 1) return SacState.On;
                    if (state == 2) return SacState.Evaluation;
                }
            }
            catch
            {
                // policy key is absent on Windows 10 and on older 11 builds
            }
            return SacState.Unknown;
        }

        public static string Name(SacState state)
        {
            switch (state)
            {
                case SacState.Off: return "kapalı";
                case SacState.On: return "açık";
                case SacState.Evaluation: return "değerlendirme modunda";
                default: return "bilinmiyor";
            }
        }

        public static bool Blocks(SacState state)
        {
            return state == SacState.On || state == SacState.Evaluation;
        }
    }
}
