﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using Gallifrey.AppTracking;
using Gallifrey.Exceptions;
using Gallifrey.Exceptions.JiraIntegration;
using Gallifrey.ExtensionMethods;
using Gallifrey.JiraTimers;
using Gallifrey.UI.Modern.Extensions;
using Gallifrey.UI.Modern.Flyouts;
using Gallifrey.UI.Modern.Helpers;
using Gallifrey.UI.Modern.Models;
using Gallifrey.Versions;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;

namespace Gallifrey.UI.Modern.MainViews
{
    public partial class MainWindow
    {
        private readonly ModelHelpers modelHelpers;
        private readonly ExceptionlessHelper exceptionlessHelper;
        private MainViewModel ViewModel => (MainViewModel)DataContext;
        private bool machineLocked;
        private bool machineIdle;

        public MainWindow(InstanceType instance)
        {
            InitializeComponent();

            var gallifrey = new Backend(instance);
            modelHelpers = new ModelHelpers(gallifrey, FlyoutsControl);
            exceptionlessHelper = new ExceptionlessHelper(modelHelpers);
            exceptionlessHelper.RegisterExceptionless();
            var viewModel = new MainViewModel(modelHelpers);
            modelHelpers.RefreshModel();
            modelHelpers.SelectRunningTimer();

            DataContext = viewModel;

            gallifrey.NoActivityEvent += GallifreyOnNoActivityEvent;
            gallifrey.ExportPromptEvent += GallifreyOnExportPromptEvent;
            SystemEvents.SessionSwitch += SessionSwitchHandler;

            Height = gallifrey.Settings.UiSettings.Height;
            Width = gallifrey.Settings.UiSettings.Width;
            ThemeHelper.ChangeTheme(gallifrey.Settings.UiSettings.Theme, gallifrey.Settings.UiSettings.Accent);

            if (gallifrey.VersionControl.IsAutomatedDeploy)
            {
                PerformUpdate(false, true);
                var updateHeartbeat = new Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
                updateHeartbeat.Elapsed += AutoUpdateCheck;
                updateHeartbeat.Enabled = true;
            }

            var idleDetectionHeartbeat = new Timer(TimeSpan.FromSeconds(30).TotalMilliseconds);
            idleDetectionHeartbeat.Elapsed += IdleDetectionCheck;
            idleDetectionHeartbeat.Enabled = true;

            var flyoutOpenCheck = new Timer(100);
            flyoutOpenCheck.Elapsed += FlyoutOpenCheck;
            flyoutOpenCheck.Enabled = true;
        }

        private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            var debuggerMissing = false;
            var multipleInstances = false;
            var showSettings = false;
            try
            {
                var progressDialogHelper = new ProgressDialogHelper(modelHelpers.DialogContext);
                var result = await progressDialogHelper.Do(modelHelpers.Gallifrey.Initialise, "Initialising Gallifrey", true, true);
                if (result.Status == ProgressResult.JiraHelperStatus.Cancelled)
                {
                    await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Gallifrey Not Initialised", "Gallifrey Initialisation Was Cancelled, The App Will Now Close");
                    modelHelpers.CloseApp();
                }
            }
            catch (MissingJiraConfigException)
            {
                showSettings = true;
            }
            catch (JiraConnectionException)
            {
                showSettings = true;
            }
            catch (MultipleGallifreyRunningException)
            {
                multipleInstances = true;
            }
            catch (DebuggerException)
            {
                debuggerMissing = true;
            }

            if (debuggerMissing)
            {
                await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Debugger Not Running", "It Looks Like Your Running Without Auto-Update\nPlease Use The Installed Shortcut To Start Gallifrey Or Download Again From GallifreyApp.co.uk");
                modelHelpers.CloseApp();
            }
            else if (multipleInstances)
            {
                modelHelpers.Gallifrey.TrackEvent(TrackingType.MultipleInstancesRunning);
                await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Multiple Instances", "You Can Only Have One Instance Of Gallifrey Running At A Time\nPlease Close The Other Instance");
                modelHelpers.CloseApp();
            }
            else if (showSettings)
            {
                modelHelpers.Gallifrey.TrackEvent(TrackingType.SettingsMissing);
                await modelHelpers.OpenFlyout(new Flyouts.Settings(modelHelpers));
                if (!modelHelpers.Gallifrey.JiraConnection.IsConnected)
                {
                    await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Connection Required", "You Must Have A Working Jira Connection To Use Gallifrey");
                    modelHelpers.CloseApp();
                }
                modelHelpers.RefreshModel();
            }

            if (modelHelpers.Gallifrey.VersionControl.IsAutomatedDeploy && modelHelpers.Gallifrey.VersionControl.IsFirstRun)
            {
                var changeLog = modelHelpers.Gallifrey.GetChangeLog(XDocument.Parse(Properties.Resources.ChangeLog)).Where(x => x.NewVersion).ToList();

                if (changeLog.Any())
                {
                    await modelHelpers.OpenFlyout(new Flyouts.ChangeLog(changeLog));
                }
            }

            exceptionlessHelper.RegisterExceptionless();
        }

        private async void GallifreyOnExportPromptEvent(object sender, ExportPromptDetail e)
        {
            var timer = modelHelpers.Gallifrey.JiraTimerCollection.GetTimer(e.TimerId);
            if (timer != null)
            {
                var exportTime = e.ExportTime;
                var message = $"Do You Want To Export '{timer.JiraReference}'?\n";
                if (modelHelpers.Gallifrey.Settings.ExportSettings.ExportPromptAll || (new TimeSpan(exportTime.Ticks - exportTime.Ticks % 600000000) == new TimeSpan(timer.TimeToExport.Ticks - (timer.TimeToExport.Ticks % 600000000))))
                {
                    exportTime = timer.TimeToExport;
                    message += $"You Have '{exportTime.FormatAsString(false)}' To Export";
                }
                else
                {
                    message += $"You Have '{exportTime.FormatAsString(false)}' To Export For This Change";
                }

                var messageResult = await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Do You Want To Export?", message, MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings { AffirmativeButtonText = "Yes", NegativeButtonText = "No", DefaultButtonFocus = MessageDialogResult.Affirmative });

                if (messageResult == MessageDialogResult.Affirmative)
                {
                    if (modelHelpers.Gallifrey.Settings.ExportSettings.ExportPromptAll)
                    {
                        await modelHelpers.OpenFlyout(new Export(modelHelpers, e.TimerId, null));
                    }
                    else
                    {
                        await modelHelpers.OpenFlyout(new Export(modelHelpers, e.TimerId, e.ExportTime));
                    }
                }
            }
        }


        private void GallifreyOnNoActivityEvent(object sender, int millisecondsSinceActivity)
        {
            Dispatcher.Invoke(() =>
            {
                ViewModel.SetNoActivityMilliseconds(millisecondsSinceActivity);

                if (millisecondsSinceActivity == 0 || ViewModel.TimerRunning)
                {
                    this.StopFlashingWindow();
                }
                else
                {
                    this.FlashWindow();
                }
            });
        }

        private void SessionSwitchHandler(object sender, SessionSwitchEventArgs e)
        {
            if (!modelHelpers.Gallifrey.Settings.AppSettings.TrackLockTime)
            {
                return;
            }

            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                case SessionSwitchReason.SessionLogoff:
                case SessionSwitchReason.RemoteDisconnect:
                case SessionSwitchReason.ConsoleDisconnect:

                    machineLocked = true;
                    if (machineIdle)
                    {
                        machineIdle = false;
                    }
                    else
                    {
                        modelHelpers.Gallifrey.StartLockTimer();
                    }
                    break;

                case SessionSwitchReason.SessionUnlock:
                case SessionSwitchReason.SessionLogon:
                case SessionSwitchReason.RemoteConnect:
                case SessionSwitchReason.ConsoleConnect:

                    machineLocked = false;
                    machineIdle = false;
                    StopLockTimer(modelHelpers.Gallifrey.Settings.AppSettings.LockTimeThresholdMilliseconds);
                    break;
            }
        }

        private void IdleDetectionCheck(object sender, ElapsedEventArgs e)
        {
            if (!modelHelpers.Gallifrey.Settings.AppSettings.TrackIdleTime)
            {
                return;
            }

            if (machineLocked)
            {
                machineIdle = false;
                return;
            }

            var idleTimeInfo = IdleTimeDetector.GetIdleTimeInfo();
            if (idleTimeInfo.IdleTime.TotalMilliseconds >= modelHelpers.Gallifrey.Settings.AppSettings.IdleTimeThresholdMilliseconds && !machineIdle)
            {
                machineIdle = true;
                modelHelpers.Gallifrey.StartLockTimer(idleTimeInfo.IdleTime);
            }
            else if (idleTimeInfo.IdleTime.TotalMilliseconds < modelHelpers.Gallifrey.Settings.AppSettings.IdleTimeThresholdMilliseconds && machineIdle)
            {
                machineIdle = false;
                Dispatcher.Invoke(() => StopLockTimer(modelHelpers.Gallifrey.Settings.AppSettings.IdleTimeThresholdMilliseconds));
            }
        }

        private void FlyoutOpenCheck(object sender, ElapsedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (modelHelpers.Gallifrey.Settings.UiSettings.TopMostOnFlyoutOpen)
                {
                    if (modelHelpers.FlyoutOpen)
                    {
                        this.FlashWindow();
                        WindowState = WindowState.Normal;
                        Activate();
                    }
                    else
                    {
                        this.StopFlashingWindow();
                    }

                    Topmost = modelHelpers.FlyoutOpen;
                }
            });
        }

        private async void StopLockTimer(int threshold)
        {
            var idleTimerId = modelHelpers.Gallifrey.StopLockTimer();
            if (!idleTimerId.HasValue) return;

            var idleTimer = modelHelpers.Gallifrey.IdleTimerCollection.GetTimer(idleTimerId.Value);
            if (idleTimer == null) return;

            if (modelHelpers.Gallifrey.Settings.AppSettings.TargetLogPerDay == TimeSpan.Zero && idleTimer.IdleTimeValue > TimeSpan.FromHours(10))
            {
                modelHelpers.Gallifrey.IdleTimerCollection.RemoveTimer(idleTimerId.Value);
            }
            else if (idleTimer.IdleTimeValue > modelHelpers.Gallifrey.Settings.AppSettings.TargetLogPerDay)
            {
                modelHelpers.Gallifrey.IdleTimerCollection.RemoveTimer(idleTimerId.Value);
            }
            else if (idleTimer.IdleTimeValue.TotalMilliseconds < threshold)
            {
                modelHelpers.Gallifrey.IdleTimerCollection.RemoveTimer(idleTimerId.Value);
                var runningId = modelHelpers.Gallifrey.JiraTimerCollection.GetRunningTimerId();
                if (runningId.HasValue)
                {
                    modelHelpers.Gallifrey.JiraTimerCollection.GetTimer(runningId.Value).AddIdleTimer(idleTimer);
                }
            }
            else
            {
                if (modelHelpers.FlyoutOpen)
                {
                    modelHelpers.HideAllFlyouts();
                }

                await modelHelpers.OpenFlyout(new LockedTimer(modelHelpers));

                foreach (var hiddenFlyout in modelHelpers.GetHiddenFlyouts())
                {
                    await modelHelpers.OpenFlyout(hiddenFlyout);
                }
            }
        }

        private void ManualUpdateCheck(object sender, RoutedEventArgs e)
        {
            PerformUpdate(true, true);
        }

        private void GetPremium(object sender, RoutedEventArgs e)
        {
            modelHelpers.ShowGetPremiumMessage();
        }

        private void AutoUpdateCheck(object sender, ElapsedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (!machineLocked && !modelHelpers.FlyoutOpen)
                {
                    PerformUpdate(false, false);
                }
            });
        }

        private async void PerformUpdate(bool manualUpdateCheck, bool promptReinstall)
        {
            try
            {
                UpdateResult updateResult;
                if (manualUpdateCheck)
                {
                    modelHelpers.Gallifrey.TrackEvent(TrackingType.ManualUpdateCheck);
                    var controller = await DialogCoordinator.Instance.ShowProgressAsync(modelHelpers.DialogContext, "Please Wait", "Checking For Updates");
                    controller.SetIndeterminate();
                    updateResult = await modelHelpers.Gallifrey.VersionControl.CheckForUpdates(true);
                    await controller.CloseAsync();
                }
                else
                {
                    updateResult = await modelHelpers.Gallifrey.VersionControl.CheckForUpdates();
                }

                if (updateResult == UpdateResult.Updated)
                {
                    if (manualUpdateCheck)
                    {
                        var messageResult = await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Update Found", "Restart Now To Install Update?", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings { AffirmativeButtonText = "Yes", NegativeButtonText = "No", DefaultButtonFocus = MessageDialogResult.Affirmative });

                        if (messageResult == MessageDialogResult.Affirmative)
                        {
                            modelHelpers.CloseApp(true);
                            modelHelpers.Gallifrey.TrackEvent(TrackingType.ManualUpdateRestart);
                        }
                    }
                    else
                    {
                        if (modelHelpers.Gallifrey.Settings.AppSettings.AutoUpdate)
                        {
                            modelHelpers.CloseApp(true);
                            modelHelpers.Gallifrey.TrackEvent(TrackingType.AutoUpdateInstalled);
                        }
                    }
                }
                else if (manualUpdateCheck && updateResult == UpdateResult.NoUpdate)
                {
                    await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "No Update Found", "There Are No Updates At This Time, Check Back Soon!");
                }
                else if (manualUpdateCheck && updateResult == UpdateResult.NotDeployable)
                {
                    await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Unable To Update", "You Cannot Auto Update This Version Of Gallifrey");
                }
                else if (manualUpdateCheck && updateResult == UpdateResult.Error)
                {
                    throw new Exception();//Trigger error condition
                }
                else if (manualUpdateCheck && promptReinstall && updateResult == UpdateResult.ReinstallNeeded)
                {
                    var messageResult = await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Update Error", "To Update An Uninstall/Reinstall Is Required.\nThis Can Happen Automatically\nNo Timers Will Be Lost\nDo You Want To Update Now?", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings { AffirmativeButtonText = "Yes", NegativeButtonText = "No", DefaultButtonFocus = MessageDialogResult.Affirmative });

                    if (messageResult == MessageDialogResult.Affirmative)
                    {
                        modelHelpers.Gallifrey.VersionControl.ManualReinstall();
                    }
                }
            }
            catch (Exception)
            {
                await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Update Error", "There Was An Error Trying To Update Gallifrey, If This Problem Persists Please Contact Support");
            }
        }
        
        private void GetBeta(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("http://releases.gallifreyapp.co.uk/download/modern/beta/setup.exe"));
        }

        private void MainWindow_OnClosed(object sender, EventArgs e)
        {
            modelHelpers.Gallifrey.Settings.UiSettings.Height = (int)Height;
            modelHelpers.Gallifrey.Settings.UiSettings.Width = (int)Width;
            modelHelpers.Gallifrey.Close();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (modelHelpers.FlyoutOpen) return;

            var key = e.Key;
            RemoteButtonTrigger trigger;

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                switch (key)
                {
                    case Key.A: trigger = RemoteButtonTrigger.Add; break;
                    case Key.D: trigger = RemoteButtonTrigger.Delete; break;
                    case Key.F: trigger = RemoteButtonTrigger.Search; break;
                    case Key.E: trigger = RemoteButtonTrigger.Edit; break;
                    case Key.U: trigger = RemoteButtonTrigger.Export; break;
                    case Key.L: trigger = RemoteButtonTrigger.LockTimer; break;
                    case Key.S: trigger = RemoteButtonTrigger.Settings; break;
                    case Key.C: trigger = RemoteButtonTrigger.Copy; break;
                    case Key.V: trigger = RemoteButtonTrigger.Paste; break;
                    default: return;
                }
            }
            else if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            {
                switch (key)
                {
                    case Key.F1: trigger = RemoteButtonTrigger.Info; break;
                    case Key.F2: trigger = RemoteButtonTrigger.Twitter; break;
                    case Key.F3: trigger = RemoteButtonTrigger.Email; break;
                    case Key.F4: trigger = RemoteButtonTrigger.Gitter; break;
                    case Key.F5: trigger = RemoteButtonTrigger.GitHub; break;
                    case Key.F6: trigger = RemoteButtonTrigger.PayPal; break;
                    default: return;
                }
            }
            else
            {
                switch (key)
                {
                    case Key.F1: trigger = RemoteButtonTrigger.Add; break;
                    case Key.F2: trigger = RemoteButtonTrigger.Delete; break;
                    case Key.F3: trigger = RemoteButtonTrigger.Search; break;
                    case Key.F4: trigger = RemoteButtonTrigger.Edit; break;
                    case Key.F5: trigger = RemoteButtonTrigger.Export; break;
                    case Key.F6: trigger = RemoteButtonTrigger.LockTimer; break;
                    case Key.F7: trigger = RemoteButtonTrigger.Settings; break;
                    default: return;
                }
            }

            modelHelpers.TriggerRemoteButtonPress(trigger);
        }

        private void MainWindow_Activated(object sender, EventArgs e)
        {
            this.StopFlashingWindow();
        }

        private void MainWindow_StateChange(object sender, EventArgs e)
        {
            if (Topmost && WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
        }
    }
}
