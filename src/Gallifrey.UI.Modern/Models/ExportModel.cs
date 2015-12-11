﻿using System;
using System.ComponentModel;
using Gallifrey.Jira.Enum;
using Gallifrey.Jira.Model;
using Gallifrey.JiraTimers;
using Gallifrey.Settings;

namespace Gallifrey.UI.Modern.Models
{
    public class ExportModel : INotifyPropertyChanged
    {
        private TimeSpan? optionalExportLimit;
        private WorkLogStrategy workLogStrategy;
        public event PropertyChangedEventHandler PropertyChanged;

        public JiraTimer Timer { get; set; }
        public bool HasParent { get { return Timer.HasParent; } }
        public string JiraParentRef { get { return Timer.JiraParentReference; } }
        public string JiraParentDesc { get { return Timer.JiraParentName; } }
        public string JiraRef { get { return Timer.JiraReference; } }
        public string JiraDesc { get { return Timer.JiraName; } }
        public int ExportedHours { get { return Timer.ExportedTime.Hours; } }
        public int ExportedMinutes { get { return Timer.ExportedTime.Minutes; } }
        public int ToExportHours { get; set; }
        public int ToExportMaxHours { get; set; }
        public int ToExportMinutes { get; set; }
        public int RemainingHours { get; set; }
        public int RemainingMinutes { get; set; }
        public bool ShowRemaining { get { return workLogStrategy == WorkLogStrategy.SetValue; } }
        public DateTime ExportDate { get; set; }
        public string Comment { get; set; }
        public TimeSpan OriginalRemaining { get; set; }
        public TimeSpan ToExport { get { return new TimeSpan(ToExportHours, ToExportMinutes, 0); } }
        public TimeSpan? Remaining { get { return WorkLogStrategy == WorkLogStrategy.SetValue ? new TimeSpan(RemainingHours, RemainingMinutes, 0) : (TimeSpan?)null; } }

        public WorkLogStrategy WorkLogStrategy
        {
            get { return workLogStrategy; }
            set
            {
                workLogStrategy = value;
                SetRemaining();
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ShowRemaining"));
                }
            }
        }

        public ExportModel(JiraTimer timer, TimeSpan? exportTime, DefaultRemaining defaultWorkLogStrategy)
        {
            UpdateTimer(timer, exportTime);

            ExportDate = timer.DateStarted.Date != DateTime.Now.Date ? timer.DateStarted.Date.AddHours(12) : DateTime.Now;

            switch (defaultWorkLogStrategy)
            {
                case DefaultRemaining.Auto:
                    WorkLogStrategy = WorkLogStrategy.Automatic;
                    break;
                case DefaultRemaining.Leave:
                    WorkLogStrategy = WorkLogStrategy.LeaveRemaining;
                    break;
                case DefaultRemaining.Set:
                    WorkLogStrategy = WorkLogStrategy.SetValue;
                    break;
            }
        }

        public void UpdateTimer(JiraTimer timer, Issue jiraIssue)
        {
            UpdateTimer(timer, optionalExportLimit);

            OriginalRemaining = jiraIssue.fields.timetracking != null ? TimeSpan.FromSeconds(jiraIssue.fields.timetracking.remainingEstimateSeconds) : new TimeSpan();

            SetRemaining();
        }

        private void UpdateTimer(JiraTimer timer, TimeSpan? exportTime)
        {
            Timer = timer;

            if (exportTime.HasValue && exportTime.Value < timer.TimeToExport)
            {
                ToExportHours = exportTime.Value.Hours;
                ToExportMaxHours = exportTime.Value.Hours;
                ToExportMinutes = exportTime.Value.Minutes;
                optionalExportLimit = exportTime;
            }
            else
            {
                ToExportHours = timer.TimeToExport.Hours;
                ToExportMaxHours = timer.TimeToExport.Hours;
                ToExportMinutes = timer.TimeToExport.Minutes;

                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ToExportHours"));
                    PropertyChanged(this, new PropertyChangedEventArgs("ToExportMaxHours"));
                    PropertyChanged(this, new PropertyChangedEventArgs("ToExportMinutes"));
                }
            }

            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs("HasParent"));
                PropertyChanged(this, new PropertyChangedEventArgs("JiraParentRef"));
                PropertyChanged(this, new PropertyChangedEventArgs("JiraParentDesc"));
                PropertyChanged(this, new PropertyChangedEventArgs("JiraRef"));
                PropertyChanged(this, new PropertyChangedEventArgs("JiraDesc"));
                PropertyChanged(this, new PropertyChangedEventArgs("ExportedHours"));
                PropertyChanged(this, new PropertyChangedEventArgs("ExportedMinutes"));
            }
        }

        private void SetRemaining()
        {
            switch (workLogStrategy)
            {
                case WorkLogStrategy.Automatic:
                    var autoNewRemaining = OriginalRemaining.Subtract(ToExport);
                    if(autoNewRemaining.TotalSeconds < 0) autoNewRemaining = new TimeSpan();
                    RemainingHours = autoNewRemaining.Hours;
                    RemainingMinutes = autoNewRemaining.Minutes;
                    break;
                case WorkLogStrategy.LeaveRemaining:
                    RemainingHours = OriginalRemaining.Hours;
                    RemainingMinutes = OriginalRemaining.Minutes;
                    break;
                case WorkLogStrategy.SetValue:
                    RemainingHours = 0;
                    RemainingMinutes = 0;
                    break;
            }

            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs("RemainingHours"));
                PropertyChanged(this, new PropertyChangedEventArgs("RemainingMinutes"));
            }
        }
    }
}