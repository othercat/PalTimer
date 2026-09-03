using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Pal98Timer
{
    internal sealed class ObsWindowStyleSettings
    {
        public bool Enabled;
        public int ChromeOpacity;
        public Keys ToggleHotkey;

        public static ObsWindowStyleSettings CreateDefault()
        {
            return new ObsWindowStyleSettings
            {
                Enabled = false,
                ChromeOpacity = 0,
                ToggleHotkey = Keys.None,
            };
        }

        public ObsWindowStyleSettings Clone()
        {
            return new ObsWindowStyleSettings
            {
                Enabled = Enabled,
                ChromeOpacity = ChromeOpacity,
                ToggleHotkey = ToggleHotkey,
            };
        }

        public void Normalize()
        {
            ChromeOpacity = Math.Max(0, Math.Min(100, ChromeOpacity));
        }
    }

    internal static class ObsWindowStyleStore
    {
        internal const string ConfigFileName = "obs_window_style";

        public static ObsWindowStyleSettings Load()
        {
            ObsWindowStyleSettings result = ObsWindowStyleSettings.CreateDefault();
            try
            {
                if (!File.Exists(ConfigFileName))
                {
                    return result;
                }

                foreach (string rawLine in File.ReadAllLines(ConfigFileName, Encoding.UTF8))
                {
                    int separator = rawLine.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = rawLine.Substring(0, separator).Trim();
                    string value = rawLine.Substring(separator + 1).Trim();
                    int integer;
                    switch (key)
                    {
                        case "enabled":
                            result.Enabled = value == "1";
                            break;
                        case "chrome_opacity":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                            {
                                result.ChromeOpacity = integer;
                            }
                            break;
                        case "toggle_hotkey":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                            {
                                result.ToggleHotkey = (Keys)integer;
                            }
                            break;
                    }
                }
            }
            catch
            {
                return ObsWindowStyleSettings.CreateDefault();
            }

            result.Normalize();
            return result;
        }

        public static void Save(ObsWindowStyleSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            ObsWindowStyleSettings value = settings.Clone();
            value.Normalize();
            StringBuilder text = new StringBuilder();
            text.AppendLine("version=2");
            text.AppendLine("enabled=" + (value.Enabled ? "1" : "0"));
            text.AppendLine("chrome_opacity=" + value.ChromeOpacity.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("toggle_hotkey=" + ((int)value.ToggleHotkey).ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(ConfigFileName, text.ToString(), new UTF8Encoding(false));
        }
    }
}
