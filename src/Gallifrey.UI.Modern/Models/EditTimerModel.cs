using System;
using System.ComponentModel;

namespace Gallifrey.UI.Modern.Models
{
    public class EditTimerModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string JiraReference { get; set; }
        public bool JiraReferenceEditable { get; set; }
        public DateTime MinDate { get; set; }
        public DateTime MaxDate { get; set; }
        public DateTime? RunDate { get; set; }
        public DateTime DisplayDate { get; set; }
        public bool DateEditable { get; set; }
        public int Hours { get; set; }
        public int Minutes { get; set; }
        public bool TimeEditable { get; set; }
        public string OriginalJiraReference { get; set; }
        public DateTime? OriginalRunDate { get; set; }
        public int OriginalHours { get; set; }
        public int OriginalMinutes { get; set; }
        public bool IsDefaultOnButton { get; set; }
        
        public EditTimerModel(IBackend gallifrey, Guid timerId)
        {
            var dateToday = DateTime.Now;
            var timer = gallifrey.JiraTimerCollection.GetTimer(timerId);

            JiraReference = timer.JiraReference;

            if (gallifrey.Settings.AppSettings.KeepTimersForDays > 0)
            {
                MinDate = dateToday.AddDays(gallifrey.Settings.AppSettings.KeepTimersForDays * -1);
                MaxDate = dateToday.AddDays(gallifrey.Settings.AppSettings.KeepTimersForDays);
            }
            else
            {
                MinDate = dateToday.AddDays(-300);
                MaxDate = dateToday.AddDays(300);
            }

            RunDate = timer.DateStarted;
            DisplayDate = timer.DateStarted;

            Hours = timer.ExactCurrentTime.Hours > 9 ? 9 : timer.ExactCurrentTime.Hours;
            Minutes = timer.ExactCurrentTime.Minutes;

            DateEditable = timer.HasExportedTime();
            JiraReferenceEditable = timer.HasExportedTime();
            TimeEditable = !timer.IsRunning;

            OriginalJiraReference = JiraReference;
            OriginalRunDate = RunDate;
            OriginalHours = Hours;
            OriginalMinutes = Minutes;

            IsDefaultOnButton = true;
        }

        public void SetNotDefaultButton()
        {
            IsDefaultOnButton = false;
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("IsDefaultOnButton"));
        }

        public bool HasModifiedJiraReference()
        {
            return OriginalJiraReference != JiraReference;
        }

        public bool HasModifiedRunDate()
        {
            return OriginalRunDate != RunDate;
        }

        public bool HasModifiedTime()
        {
            return OriginalHours != Hours || OriginalMinutes != Minutes;
        }

        public void AdjustTime(TimeSpan timeAdjustmentAmount, bool addTime)
        {
            var currentTime = new TimeSpan(Hours, Minutes, 0);

            if (addTime)
            {
                currentTime = currentTime.Add(timeAdjustmentAmount);
            }
            else
            {
                currentTime = currentTime.Subtract(timeAdjustmentAmount);
            }

            Hours = currentTime.Hours > 9 ? 9 : currentTime.Hours;
            Minutes = currentTime.Minutes;

            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Hours"));
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Minutes"));
        }
    }
}