using System;
using System.Collections.Generic;

namespace BackupUI.Models
{
    public class BackupSchedule
    {
        public Guid JobId { get; set; }
        public bool Enabled { get; set; }
        public ScheduleFrequency Frequency { get; set; }
        public TimeSpan Time { get; set; }
        public List<DayOfWeek> DaysOfWeek { get; set; } = new();
        public int DayOfMonth { get; set; }
        public DateTime? NextRunTime { get; set; }
    }

    public enum ScheduleFrequency
    {
        Daily,
        Weekly,
        Monthly,
        Once
    }
}
