using Gallifrey.Comparers;
using Gallifrey.ExtensionMethods;
using Gallifrey.Jira.Model;
using Gallifrey.UI.Modern.Helpers;
using Gallifrey.Versions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Timers;
using System.Windows;

namespace Gallifrey.UI.Modern.Models
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly TargetBarValues targetBarValues;

        public ModelHelpers ModelHelpers { get; }
        public event PropertyChangedEventHandler PropertyChanged;
        public ObservableCollection<TimerDateModel> TimerDates { get; private set; }
        public string InactiveMinutes { get; private set; }
        public TimeSpan TimeTimeActivity { get; private set; }

        public MainViewModel(ModelHelpers modelHelpers)
        {
            ModelHelpers = modelHelpers;
            TimerDates = new ObservableCollection<TimerDateModel>();

            var backgroundRefresh = new Timer(TimeSpan.FromHours(1).TotalMilliseconds);
            backgroundRefresh.Elapsed += (sender, args) => RefreshModel();
            backgroundRefresh.Start();

            modelHelpers.Gallifrey.VersionControl.NewVersionPresent += (sender, args) => NewVersionPresent();
            modelHelpers.Gallifrey.IsPremiumChanged += (sender, args) => PremiumChanged();
            modelHelpers.Gallifrey.BackendModifiedTimers += (sender, args) => BackendModification();
            modelHelpers.Gallifrey.SettingsChanged += (sender, args) => SettingsChanged();
            modelHelpers.Gallifrey.JiraConnection.LoggedIn += (sender, args) => UserLoggedIn();
            modelHelpers.Gallifrey.JiraTimerCollection.GeneralTimerModification += (sender, args) => GeneralTimerModification();
            modelHelpers.Gallifrey.DailyTrackingEvent += (sender, args) => DailyEvent();
            modelHelpers.RefreshModelEvent += (sender, args) => RefreshModel();
            modelHelpers.SelectRunningTimerEvent += (sender, args) => SelectRunningTimer();
            modelHelpers.SelectTimerEvent += (sender, timerId) => SetSelectedTimer(timerId);

            targetBarValues = new TargetBarValues(modelHelpers.Gallifrey);
        }

        public string ExportedNumber => ModelHelpers.Gallifrey.JiraTimerCollection.GetNumberExported().Item1.ToString();
        public string TotalTimerCount => ModelHelpers.Gallifrey.JiraTimerCollection.GetNumberExported().Item2.ToString();
        public string LocalTime => ModelHelpers.Gallifrey.JiraTimerCollection.GetTotalLocalTime().FormatAsString(false);
        public string ExportableTime => ModelHelpers.Gallifrey.JiraTimerCollection.GetTotalExportableTime().FormatAsString(false);
        public string Exported => ModelHelpers.Gallifrey.JiraTimerCollection.GetTotalExportedTimeThisWeek(ModelHelpers.Gallifrey.Settings.AppSettings.StartOfWeek).FormatAsString(false);
        public string ExportTarget => ModelHelpers.Gallifrey.Settings.AppSettings.GetTargetThisWeek().FormatAsString(false);

        public string VersionName => ModelHelpers.Gallifrey.VersionControl.UpdateInstalled || ModelHelpers.Gallifrey.VersionControl.UpdateReinstallNeeded ? "NEW VERSION AVAILABLE" : ModelHelpers.Gallifrey.VersionControl.VersionName.ToUpper();
        public bool HasUpdate => ModelHelpers.Gallifrey.VersionControl.UpdateInstalled;
        public bool ReinstallNeeded => ModelHelpers.Gallifrey.VersionControl.UpdateReinstallNeeded;

        public bool HasInactiveTime => !string.IsNullOrWhiteSpace(InactiveMinutes);
        public bool TimerRunning => !string.IsNullOrWhiteSpace(CurrentRunningTimerDescription);
        public bool HaveTimeToExport => !string.IsNullOrWhiteSpace(TimeToExportMessage);
        public bool HaveLocalTime => !string.IsNullOrWhiteSpace(LocalTimeMessage);
        public bool IsPremium => ModelHelpers.Gallifrey.Settings.InternalSettings.IsPremium;
        public bool IsStable => ModelHelpers.Gallifrey.VersionControl.InstanceType == InstanceType.Stable;
        public bool TrackingOnly => ModelHelpers.Gallifrey.Settings.ExportSettings.TrackingOnly;

        public string TargetBarExportedPercentage => $"{targetBarValues.ExportedWidth}*";
        public string TargetBarUnexportedPercentage => $"{targetBarValues.UnexportedWidth}*";
        public string TargetBarRemainingPercentage => $"{targetBarValues.RemainingWidth}*";

        public string TargetBarExportedLabel => $"Exported: {targetBarValues.ExportedTime.FormatAsString(false)}";
        public string TargetBarUnexportedLabel => $"Un-Exported (inc Local): {targetBarValues.UnexportedTime.FormatAsString(false)}";
        public string TargetBarRemainingLabel => $"Remaining: {targetBarValues.RemainingTime.FormatAsString(false)}";

        public string AppTitle
        {
            get
            {
                var instanceType = ModelHelpers.Gallifrey.VersionControl.InstanceType;
                var appName = IsPremium ? "Gallifrey Premium" : "Gallifrey";
                return instanceType == InstanceType.Stable ? $"{appName}" : $"{appName} ({instanceType})";
            }
        }

        public string TimeToExportMessage
        {
            get
            {
                if (TrackingOnly)
                {
                    return string.Empty;
                }

                var unexportedTime = ModelHelpers.Gallifrey.JiraTimerCollection.GetStoppedTotalExportableTime();
                var unexportedCount = ModelHelpers.Gallifrey.JiraTimerCollection.GetStoppedUnexportedTimers().Count(x => !x.LocalTimer);

                var excludingRunning = string.Empty;
                var runningTimerId = ModelHelpers.Gallifrey.JiraTimerCollection.GetRunningTimerId();
                if (runningTimerId.HasValue)
                {
                    var runningTimer = ModelHelpers.Gallifrey.JiraTimerCollection.GetTimer(runningTimerId.Value);
                    if (!runningTimer.FullyExported && !runningTimer.LocalTimer)
                    {
                        excludingRunning = "(Excluding 1 Running Timer)";
                    }
                }

                return unexportedTime.TotalMinutes > 0 ? $"You Have {unexportedCount} Timer{(unexportedCount > 1 ? "s" : "")} Worth {unexportedTime.FormatAsString(false)} To Export {excludingRunning}" : string.Empty;
            }
        }

        public string LocalTimeMessage
        {
            get
            {
                if (TrackingOnly)
                {
                    return string.Empty;
                }

                var localTime = ModelHelpers.Gallifrey.JiraTimerCollection.GetTotalLocalTime();
                var unexportedCount = ModelHelpers.Gallifrey.JiraTimerCollection.GetAllLocalTimers().Count(x => x.LocalTimer);

                return localTime.TotalMinutes > 0 ? $"You Have {unexportedCount} Local Timer{(unexportedCount > 1 ? "s" : "")} Worth {localTime.FormatAsString(false)}" : string.Empty;
            }
        }

        public string CurrentRunningTimerDescription
        {
            get
            {
                var runningTimerId = ModelHelpers.Gallifrey.JiraTimerCollection.GetRunningTimerId();
                if (!runningTimerId.HasValue) return string.Empty;
                var runningTimer = ModelHelpers.Gallifrey.JiraTimerCollection.GetTimer(runningTimerId.Value);

                return $"Currently Running {runningTimer.JiraReference} ({runningTimer.JiraName})";
            }
        }

        public void SetNoActivityMilliseconds(int millisecondsSinceActivity)
        {
            TimeTimeActivity = TimeSpan.FromMilliseconds(millisecondsSinceActivity);
            TimeTimeActivity = TimeTimeActivity.Subtract(TimeSpan.FromMilliseconds(TimeTimeActivity.Milliseconds));
            TimeTimeActivity = TimeTimeActivity.Subtract(TimeSpan.FromSeconds(TimeTimeActivity.Seconds));

            if (TimeTimeActivity.TotalMinutes > 0)
            {
                var minutesPlural = TimeTimeActivity.TotalMinutes > 1 ? "s" : "";
                InactiveMinutes = $"No Timer Running For {TimeTimeActivity.TotalMinutes} Minute{minutesPlural}";
            }
            else
            {
                InactiveMinutes = string.Empty;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("InactiveMinutes"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HasInactiveTime"));
        }

        public IEnumerable<Guid> GetSelectedTimerIds()
        {
            foreach (var timerDateModel in TimerDates.Where(x => x.DateIsSelected))
            {
                foreach (var timerModel in timerDateModel.Timers.Where(timerModel => timerModel.TimerIsSelected))
                {
                    yield return timerModel.JiraTimer.UniqueId;
                }
            }
        }

        #region Private Helpers

        private class TargetBarValues
        {
            private readonly IBackend gallifrey;
            public double UnexportedWidth { get; private set; }
            public double ExportedWidth { get; private set; }
            public double RemainingWidth { get; private set; }
            public TimeSpan ExportedTime { get; private set; }
            public TimeSpan UnexportedTime { get; private set; }
            public TimeSpan RemainingTime { get; private set; }

            public TargetBarValues(IBackend gallifrey)
            {
                this.gallifrey = gallifrey;
            }

            public void Update()
            {
                var targetTime = gallifrey.Settings.AppSettings.GetTargetThisWeek();

                ExportedTime = gallifrey.JiraTimerCollection.GetTotalExportedTimeThisWeek(gallifrey.Settings.AppSettings.StartOfWeek);
                UnexportedTime = gallifrey.JiraTimerCollection.GetTotalTimeThisWeekNoSeconds(gallifrey.Settings.AppSettings.StartOfWeek).Subtract(ExportedTime);
                RemainingTime = targetTime.Subtract(ExportedTime).Subtract(UnexportedTime);

                var target = targetTime.TotalMinutes;
                var exported = ExportedTime.TotalMinutes;
                var unexported = UnexportedTime.TotalMinutes;
                var remainign = RemainingTime.TotalMinutes;

                RemainingWidth = remainign / target * 100;
                ExportedWidth = exported / target * 100;
                UnexportedWidth = unexported / target * 100;

                if (ExportedWidth >= 100)
                {
                    ExportedWidth = 100;
                    UnexportedWidth = 0;
                    RemainingWidth = 0;
                }
                else if (ExportedWidth + UnexportedWidth >= 100)
                {
                    UnexportedWidth = 100 - ExportedWidth;
                    RemainingWidth = 0;
                }
            }
        }

        private void RefreshModel()
        {
            var workingDays = ModelHelpers.Gallifrey.Settings.AppSettings.ExportDays.ToList();
            var workingDate = DateTime.Now.AddDays((ModelHelpers.Gallifrey.Settings.AppSettings.KeepTimersForDays - 1) * -1).Date;
            var validTimerDates = new List<DateTime>();
            var jiraCache = new List<Issue>();

            while (workingDate.Date <= DateTime.Now.Date)
            {
                if (workingDays.Contains(workingDate.DayOfWeek))
                {
                    validTimerDates.Add(workingDate.Date);
                }
                workingDate = workingDate.AddDays(1);
            }

            foreach (var timerDate in ModelHelpers.Gallifrey.JiraTimerCollection.GetValidTimerDates())
            {
                if (!validTimerDates.Contains(timerDate))
                {
                    validTimerDates.Add(timerDate.Date);
                }
            }

            foreach (var timerDate in validTimerDates.OrderBy(x => x.Date))
            {
                var dateModel = TimerDates.FirstOrDefault(x => x.TimerDate.Date == timerDate.Date);

                if (dateModel == null)
                {
                    dateModel = new TimerDateModel(timerDate, ModelHelpers.Gallifrey.JiraTimerCollection);
                    TimerDates.Add(dateModel);
                }

                var dateTimers = ModelHelpers.Gallifrey.JiraTimerCollection.GetTimersForADate(timerDate).ToList();

                foreach (var timer in dateTimers)
                {
                    if (!dateModel.Timers.Any(x => x.JiraTimer.UniqueId == timer.UniqueId))
                    {
                        timer.PropertyChanged += (sender, args) => TimerChanged();
                        dateModel.AddTimerModel(new TimerModel(timer));
                    }
                }

                foreach (var removeTimer in dateModel.Timers.Where(timerModel => !dateTimers.Any(x => x.UniqueId == timerModel.JiraTimer.UniqueId)).ToList())
                {
                    dateModel.RemoveTimerModel(removeTimer);
                }

                foreach (var defaultJira in ModelHelpers.Gallifrey.Settings.AppSettings.DefaultTimers ?? new List<string>())
                {
                    if (!dateModel.Timers.Any(x => x.JiraTimer.JiraReference == defaultJira) && dateModel.TimerDate.Date <= DateTime.Now.Date)
                    {
                        try
                        {
                            var jira = jiraCache.FirstOrDefault(x => x.key == defaultJira);
                            if (jira == null && ModelHelpers.Gallifrey.JiraConnection.IsConnected)
                            {
                                jira = ModelHelpers.Gallifrey.JiraConnection.GetJiraIssue(defaultJira);
                                jiraCache.Add(jira);
                            }

                            if (jira != null)
                            {
                                var timerId = ModelHelpers.Gallifrey.JiraTimerCollection.AddTimer(jira, timerDate.Date, TimeSpan.Zero, false);
                                var timer = ModelHelpers.Gallifrey.JiraTimerCollection.GetTimer(timerId);
                                timer.PropertyChanged += (sender, args) => TimerChanged();
                                dateModel.AddTimerModel(new TimerModel(timer));
                            }
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }
                }
            }

            //see if the order would be different now, and if so, recreate the TimerDates
            var orderedCollection = TimerDates.Where(x => validTimerDates.Contains(x.TimerDate)).OrderByDescending(x => x.TimerDate).ToList();
            if (orderedCollection.Count != TimerDates.Count)
            {
                TimerDates = new ObservableCollection<TimerDateModel>(orderedCollection);
            }
            else
            {
                for (var i = 0; i < TimerDates.Count; i++)
                {
                    var main = TimerDates[i];
                    var ordered = orderedCollection[i];

                    if (main.TimerDate != ordered.TimerDate)
                    {
                        TimerDates = new ObservableCollection<TimerDateModel>(orderedCollection);
                        break;
                    }
                }
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TimerDates"));

            SetTrackingOnlyInModel();
            targetBarValues.Update();
        }

        private void SetSelectedTimer(Guid value)
        {
            foreach (var timerDateModel in TimerDates)
            {
                var selectedTimerFound = false;
                foreach (var timerModel in timerDateModel.Timers)
                {
                    if (timerModel.JiraTimer.UniqueId == value)
                    {
                        timerModel.SetSelected(true);
                        selectedTimerFound = true;
                    }
                    else
                    {
                        timerModel.SetSelected(false);
                    }
                }

                timerDateModel.SetSelected(selectedTimerFound);
            }
        }

        private void SelectRunningTimer()
        {
            var runningTimer = ModelHelpers.Gallifrey.JiraTimerCollection.GetRunningTimerId();
            if (runningTimer.HasValue)
            {
                ModelHelpers.SetSelectedTimer(runningTimer.Value);
            }
            else
            {
                var dates = ModelHelpers.Gallifrey.JiraTimerCollection.GetValidTimerDates();
                if (dates.Any())
                {
                    var date = dates.Max();
                    var topTimer = ModelHelpers.Gallifrey.JiraTimerCollection.GetTimersForADate(date).OrderBy(x => x.JiraReference, new JiraReferenceComparer()).FirstOrDefault();
                    if (topTimer != null)
                    {
                        SetSelectedTimer(topTimer.UniqueId);
                    }
                }
            }
        }

        private void SetTrackingOnlyInModel()
        {
            foreach (var timerDateModel in TimerDates)
            {
                timerDateModel.SetTrackingOnly(TrackingOnly);

                foreach (var timerModel in timerDateModel.Timers)
                {
                    timerModel.SetTrackingOnly(TrackingOnly);
                }
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TimerDates"));
        }

        #endregion

        #region Events

        private void NewVersionPresent()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("VersionName"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HasUpdate"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ReinstallNeeded"));
        }

        private void BackendModification()
        {
            Application.Current.Dispatcher.Invoke(RefreshModel);
        }

        private void PremiumChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("AppTitle"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsPremium"));
        }

        private void UserLoggedIn()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("LoggedInAs"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("LoggedInDisplayName"));
        }

        private void GeneralTimerModification()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ExportedNumber"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TotalTimerCount"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("UnexportedTime"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Exported"));

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TimeToExportMessage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HaveTimeToExport"));

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TimerRunning"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CurrentRunningTimerDescription"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HaveLocalTime"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("LocalTimeMessage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("LocalTime"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ExportableTime"));

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ExportTarget"));

            targetBarValues.Update();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarExportedPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarUnexportedPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarRemainingPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarExportedLabel"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarUnexportedLabel"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarRemainingLabel"));
        }

        private void SettingsChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ExportTarget"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TrackingOnly"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HaveTimeToExport"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ExportableTime"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TimeToExportMessage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HaveLocalTime"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("LocalTimeMessage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("LocalTime"));

            targetBarValues.Update();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarExportedPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarUnexportedPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarRemainingPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarExportedLabel"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarUnexportedLabel"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarRemainingLabel"));

            SetTrackingOnlyInModel();
            RefreshModel();
        }

        private void TimerChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TimeToExportMessage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HaveTimeToExport"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("UnexportedTime"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ExportableTime"));

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TimerRunning"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CurrentRunningTimerDescription"));

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ExportTarget"));

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HaveLocalTime"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("LocalTimeMessage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("LocalTime"));

            targetBarValues.Update();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarExportedPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarUnexportedPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarRemainingPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarExportedLabel"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarUnexportedLabel"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarRemainingLabel"));
        }

        private void DailyEvent()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ExportTarget"));

            targetBarValues.Update();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarExportedPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarUnexportedPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarRemainingPercentage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarExportedLabel"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarUnexportedLabel"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TargetBarRemainingLabel"));
        }

        #endregion
    }
}