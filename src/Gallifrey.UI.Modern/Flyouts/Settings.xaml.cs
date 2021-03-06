﻿using Gallifrey.Exceptions.JiraIntegration;
using Gallifrey.UI.Modern.Helpers;
using Gallifrey.UI.Modern.Models;
using MahApps.Metro.Controls.Dialogs;
using System.Windows;

namespace Gallifrey.UI.Modern.Flyouts
{
    public partial class Settings
    {
        private readonly ModelHelpers modelHelpers;
        private readonly ProgressDialogHelper progressDialogHelper;
        private SettingModel DataModel => (SettingModel)DataContext;

        public Settings(ModelHelpers modelHelpers)
        {
            this.modelHelpers = modelHelpers;
            InitializeComponent();

            progressDialogHelper = new ProgressDialogHelper(modelHelpers.DialogContext);
            DataContext = new SettingModel(modelHelpers.Gallifrey.Settings, modelHelpers.Gallifrey.VersionControl);
        }

        private async void SaveSettings(object sender, RoutedEventArgs e)
        {
            var successfulSave = true;
            DataModel.UpdateSettings(modelHelpers.Gallifrey.Settings, modelHelpers.Gallifrey.VersionControl);

            try
            {
                if (DataModel.JiraSettingsChanged)
                {
                    var trackingOptOut = DataModel.TrackingOptOut;
                    await progressDialogHelper.Do(() => modelHelpers.Gallifrey.SaveSettings(true, trackingOptOut), "Checking Jira Credentials", false, true);
                }
                else
                {
                    modelHelpers.Gallifrey.SaveSettings(false, DataModel.TrackingOptOut);
                }
            }
            catch (MissingJiraConfigException)
            {
                successfulSave = false;
            }
            catch (JiraConnectionException)
            {
                successfulSave = false;
            }

            if (successfulSave)
            {
                if (modelHelpers.Gallifrey.Settings.JiraConnectionSettings.UseTempo && !modelHelpers.Gallifrey.JiraConnection.HasTempo)
                {
                    await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Missing Tempo", "We Were Unable To Locate A Tempo Endpoint\nGallifrey Will Fall Back To Standard Jira Endpoints");
                    modelHelpers.Gallifrey.Settings.JiraConnectionSettings.UseTempo = false;
                    //Even though we have changed jira settings, we are changing because tempo is not in use, so don't reconnect
                    modelHelpers.Gallifrey.SaveSettings(false, false);
                }

                modelHelpers.CloseFlyout(this);
                var themeChanged = ThemeHelper.ChangeTheme(DataModel.Theme.Name, DataModel.Accent.Name);

                if (themeChanged == ThemeChangeDetail.Theme || themeChanged == ThemeChangeDetail.Both)
                {
                    //This is a really ugly solution!!
                    //The overides of system colours do not update automatically which is not good.
                    //This message will hopefully make people restart...
                    await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Restart Needed", "When Changing Theme, Some Colours Will Not Change Automatically\nIt Is Recommended To Restart The App");
                }
            }
            else
            {
                await DialogCoordinator.Instance.ShowMessageAsync(modelHelpers.DialogContext, "Invalid Jira Configuration", "You Cannot Save With Invalid Jira Configuration.\nTo Save You Have To Have A Valid Connection To Jira");
                Focus();
            }
        }

        private void AllowTrackingClick(object sender, RoutedEventArgs e)
        {
            if (!modelHelpers.Gallifrey.Settings.InternalSettings.IsPremium && !DataModel.AllowTracking)
            {
                modelHelpers.ShowGetPremiumMessage("To Opt-Out Of Tracking You Need To Have Gallifrey Premium");
                DataModel.EnableTracking();
            }
        }
    }
}
