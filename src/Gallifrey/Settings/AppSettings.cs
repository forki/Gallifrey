﻿using Gallifrey.Serialization;

namespace Gallifrey.Settings
{
    public class AppSettings
    {
        public bool AlertWhenNotRunning { get; set; }
        public int AlertTimeMilliseconds { get; set; }
        public int KeepTimersForDays { get; set; }
        public int TargetLogHoursPerDay { get; set; }
        public bool UiAlwaysOnTop { get; set; }
        public UiAnimationLevel UiAnimationLevel { get; set; }

        internal void SaveSettings()
        {
            AppSettingsSerializer.Serialize(this);    
        }
    }
}
