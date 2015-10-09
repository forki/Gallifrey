﻿using System;
using System.Windows;
using System.Windows.Controls;
using Gallifrey.Exceptions.JiraIntegration;
using Gallifrey.Jira.Model;
using Gallifrey.UI.Modern.Models;
using MahApps.Metro.Controls.Dialogs;

namespace Gallifrey.UI.Modern.Flyouts
{
    /// <summary>
    /// Interaction logic for EditTimer.xaml
    /// </summary>
    public partial class EditTimer
    {
        private readonly MainViewModel viewModel;
        private EditTimerModel DataModel { get { return (EditTimerModel)DataContext; } }

        public EditTimer(MainViewModel viewModel, Guid selected)
        {
            this.viewModel = viewModel;
            InitializeComponent();

            DataContext = new EditTimerModel(viewModel.Gallifrey, selected);
        }

        private async void SaveButton(object sender, RoutedEventArgs e)
        {
            var uniqueId = DataModel.UniqueId;

            if (DataModel.HasModifiedRunDate())
            {
                if (!DataModel.RunDate.HasValue)
                {
                    DialogCoordinator.Instance.ShowMessageAsync(viewModel,"Missing Date", "You Must Enter A Start Date");
                    return;
                }

                if (DataModel.RunDate.Value < DataModel.MinDate || DataModel.RunDate.Value > DataModel.MaxDate)
                {
                    DialogCoordinator.Instance.ShowMessageAsync(viewModel,"Invalid Date", string.Format("You Must Enter A Start Date Between {0} And {1}", DataModel.MinDate.ToShortDateString(), DataModel.MaxDate.ToShortDateString()));
                    return;
                }

                try
                {
                    uniqueId = viewModel.Gallifrey.JiraTimerCollection.ChangeTimerDate(uniqueId, DataModel.RunDate.Value);
                }
                catch (Exception)
                {
                    DialogCoordinator.Instance.ShowMessageAsync(viewModel,"Duplicate Timer", "This Timer Already Exists On That Date!");
                    return;
                }
            }

            if (DataModel.HasModifiedJiraReference())
            {
                Issue jiraIssue;
                try
                {
                    jiraIssue = viewModel.Gallifrey.JiraConnection.GetJiraIssue(DataModel.JiraReference);
                }
                catch (NoResultsFoundException)
                {
                    DialogCoordinator.Instance.ShowMessageAsync(viewModel,"Invalid Jira", "Unable To Locate The Jira");
                    return;
                }

                var result = await DialogCoordinator.Instance.ShowMessageAsync(viewModel,"Correct Jira?", string.Format("Jira found!\n\nRef: {0}\nName: {1}\n\nIs that correct?", jiraIssue.key, jiraIssue.fields.summary), MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings { AffirmativeButtonText = "Yes", NegativeButtonText = "No" });

                if (result == MessageDialogResult.Negative)
                {
                    return;
                }

                try
                {
                    uniqueId = viewModel.Gallifrey.JiraTimerCollection.RenameTimer(uniqueId, jiraIssue);
                }
                catch (Exception)
                {
                    DialogCoordinator.Instance.ShowMessageAsync(viewModel,"Duplicate Timer", "This Timer Already Exists On That Date!");
                    return;
                }
            }

            if (DataModel.HasModifiedTime())
            {
                var orignalTime = new TimeSpan(DataModel.OriginalHours, DataModel.OriginalMinutes, 0);
                var newTime = new TimeSpan(DataModel.Hours, DataModel.Minutes, 0);
                var difference = newTime.Subtract(orignalTime);
                var addTime = difference.TotalSeconds > 0;

                viewModel.Gallifrey.JiraTimerCollection.AdjustTime(uniqueId, Math.Abs(difference.Hours), Math.Abs(difference.Minutes), addTime);
            }

            viewModel.RefreshModel();
            viewModel.SetSelectedTimer(uniqueId);
            IsOpen = false;
        }
    }
}
