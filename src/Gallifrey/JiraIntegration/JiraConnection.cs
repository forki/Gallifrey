﻿using Gallifrey.AppTracking;
using Gallifrey.Comparers;
using Gallifrey.Exceptions.JiraIntegration;
using Gallifrey.Jira;
using Gallifrey.Jira.Enum;
using Gallifrey.Jira.Model;
using Gallifrey.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace Gallifrey.JiraIntegration
{
    public interface IJiraConnection
    {
        void ReConnect(IJiraConnectionSettings newJiraConnectionSettings, IExportSettings newExportSettings);
        bool DoesJiraExist(string jiraRef);
        Issue GetJiraIssue(string jiraRef);
        IEnumerable<string> GetJiraFilters();
        IEnumerable<Issue> GetJiraIssuesFromFilter(string filterName);
        IEnumerable<Issue> GetJiraIssuesFromSearchText(string searchText);
        IEnumerable<Issue> GetJiraIssuesFromSearchTextLimitedToKeys(string searchText, IEnumerable<string> jiraKeys);
        void LogTime(string jiraRef, DateTime exportTimeStamp, TimeSpan exportTime, WorkLogStrategy strategy, bool addStandardComment, string comment = "", TimeSpan? remainingTime = null);
        IEnumerable<Issue> GetJiraCurrentUserOpenIssues();
        IEnumerable<JiraProject> GetJiraProjects();
        IEnumerable<RecentJira> GetRecentJirasFound();
        IEnumerable<StandardWorkLog> GetWorkLoggedForDates(IEnumerable<DateTime> queryDates);
        IEnumerable<StandardWorkLog> GetWorkLoggedForDatesFilteredIssues(IEnumerable<DateTime> queryDates, IEnumerable<string> issueRefs);
        void UpdateCache();
        void AssignToCurrentUser(string jiraRef);
        User CurrentUser { get; }
        bool IsConnected { get; }
        bool HasTempo { get; }
        void TransitionIssue(string jiraRef, string transition);
        IEnumerable<Status> GetTransitions(string jiraRef);
        event EventHandler LoggedIn;
    }

    public class JiraConnection : IJiraConnection
    {
        private readonly ITrackUsage trackUsage;
        private readonly IRecentJiraCollection recentJiraCollection;
        private readonly List<JiraProject> jiraProjectCache;
        private IJiraConnectionSettings jiraConnectionSettings;
        private IExportSettings exportSettings;
        private IJiraClient jira;
        private DateTime lastCacheUpdate;

        public User CurrentUser { get; private set; }
        public bool IsConnected => jira != null;
        public bool HasTempo => jira != null && jira.HasTempo;
        public event EventHandler LoggedIn;

        public JiraConnection(ITrackUsage trackUsage)
        {
            this.trackUsage = trackUsage;
            recentJiraCollection = new RecentJiraCollection();
            jiraProjectCache = new List<JiraProject>();
            lastCacheUpdate = DateTime.MinValue;
        }

        public void ReConnect(IJiraConnectionSettings newJiraConnectionSettings, IExportSettings newExportSettings)
        {
            exportSettings = newExportSettings;
            jiraConnectionSettings = newJiraConnectionSettings;
            jira = null;
            CheckAndConnectJira();
            Task.Factory.StartNew(UpdateJiraProjectCache);
        }

        private void CheckAndConnectJira()
        {
            if (jira == null)
            {
                try
                {
                    jira = JiraClientFactory.BuildJiraClient(jiraConnectionSettings.JiraUrl, jiraConnectionSettings.JiraUsername, jiraConnectionSettings.JiraPassword, jiraConnectionSettings.UseTempo);

                    CurrentUser = jira.GetCurrentUser();
                    LoggedIn?.Invoke(this, null);

                    TrackingType trackingType;
                    if (jira.GetType() == typeof(JiraRestClient) && jira.HasTempo)
                    {
                        trackingType = jiraConnectionSettings.JiraUrl.Contains(".atlassian.net") ? TrackingType.JiraConnectCloudRestWithTempo : TrackingType.JiraConnectSelfhostRestWithTempo;
                    }
                    else if (jira.GetType() == typeof(JiraRestClient))
                    {
                        trackingType = jiraConnectionSettings.JiraUrl.Contains(".atlassian.net") ? TrackingType.JiraConnectCloudRest : TrackingType.JiraConnectSelfhostRest;
                    }
                    else
                    {
                        trackingType = jiraConnectionSettings.JiraUrl.Contains(".atlassian.net") ? TrackingType.JiraConnectCloudSoap : TrackingType.JiraConnectSelfhostSoap;
                    }

                    trackUsage.TrackAppUsage(trackingType);

                }
                catch (InvalidCredentialException)
                {
                    throw new MissingJiraConfigException("Required settings to create connection to jira are missing");
                }
                catch (Exception ex)
                {
                    throw new JiraConnectionException("Error creating instance of Jira", ex);
                }
            }
        }

        public bool DoesJiraExist(string jiraRef)
        {
            try
            {
                CheckAndConnectJira();
                var issue = GetJiraIssue(jiraRef);
                if (issue != null)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                recentJiraCollection.Remove(jiraRef);
                return false;
            }

            recentJiraCollection.Remove(jiraRef);
            return false;
        }

        public Issue GetJiraIssue(string jiraRef)
        {
            try
            {
                CheckAndConnectJira();
                var issue = jira.GetIssue(jiraRef);

                recentJiraCollection.AddRecentJira(issue);
                return issue;
            }
            catch (Exception ex)
            {
                recentJiraCollection.Remove(jiraRef);
                throw new NoResultsFoundException($"Unable to locate Jira {jiraRef}", ex);
            }
        }

        public IEnumerable<string> GetJiraFilters()
        {
            try
            {
                CheckAndConnectJira();
                trackUsage.TrackAppUsage(TrackingType.SearchLoad);
                var returnedFilters = jira.GetFilters();
                return returnedFilters.Select(returned => returned.name);
            }
            catch (Exception ex)
            {
                throw new NoResultsFoundException("Error loading filters", ex);
            }
        }

        public IEnumerable<Issue> GetJiraIssuesFromFilter(string filterName)
        {
            try
            {
                CheckAndConnectJira();
                trackUsage.TrackAppUsage(TrackingType.SearchFilter);
                var filterJql = jira.GetJqlForFilter(filterName);
                var issues = jira.GetIssuesFromFilter(filterName).ToList();
                recentJiraCollection.AddRecentJiras(issues);

                if (filterJql.ToLower().Contains("order by"))
                {
                    return issues;
                }

                return issues.OrderBy(x => x.key, new JiraReferenceComparer());
            }
            catch (Exception ex)
            {
                throw new NoResultsFoundException("Error loading jiras from filter", ex);
            }
        }

        public IEnumerable<Issue> GetJiraIssuesFromSearchText(string searchText)
        {
            try
            {
                CheckAndConnectJira();
                trackUsage.TrackAppUsage(TrackingType.SearchText);
                var issues = jira.GetIssuesFromJql(GetJql(searchText));
                recentJiraCollection.AddRecentJiras(issues);
                return issues.OrderBy(x => x.key, new JiraReferenceComparer());
            }
            catch (Exception ex)
            {
                throw new NoResultsFoundException("Error loading jiras from search text", ex);
            }
        }

        public IEnumerable<Issue> GetJiraIssuesFromSearchTextLimitedToKeys(string searchText, IEnumerable<string> jiraKeys)
        {
            try
            {
                CheckAndConnectJira();
                trackUsage.TrackAppUsage(TrackingType.SearchText);
                var searchTextJql = GetJql(searchText);
                var keysJql = $"key in (\"{String.Join("\",\"", jiraKeys)}\")";
                var issues = jira.GetIssuesFromJql($"({keysJql}) AND ({searchTextJql})");
                recentJiraCollection.AddRecentJiras(issues);
                return issues.OrderBy(x => x.key, new JiraReferenceComparer());
            }
            catch (Exception ex)
            {
                throw new NoResultsFoundException("Error loading jiras from search text", ex);
            }
        }

        public IEnumerable<Issue> GetJiraCurrentUserOpenIssues()
        {
            try
            {
                CheckAndConnectJira();
                var issues = jira.GetIssuesFromJql("assignee in (currentUser()) AND status not in (Closed,Resolved)");
                recentJiraCollection.AddRecentJiras(issues);
                return issues.OrderBy(x => x.key, new JiraReferenceComparer());
            }
            catch (Exception ex)
            {
                throw new NoResultsFoundException("Error loading jiras from search text", ex);
            }
        }

        private void UpdateJiraProjectCache()
        {
            if (lastCacheUpdate < DateTime.UtcNow.AddMinutes(-30))
            {
                try
                {
                    lastCacheUpdate = DateTime.UtcNow;
                    CheckAndConnectJira();
                    var projects = jira.GetProjects();
                    jiraProjectCache.Clear();
                    jiraProjectCache.AddRange(projects.Select(project => new JiraProject(project.key, project.name)));

                }
                catch (Exception) { lastCacheUpdate = DateTime.MinValue; }
            }
        }

        public IEnumerable<JiraProject> GetJiraProjects()
        {
            return jiraProjectCache;
        }

        public IEnumerable<RecentJira> GetRecentJirasFound()
        {
            return recentJiraCollection.GetRecentJiraCollection();
        }

        public IEnumerable<StandardWorkLog> GetWorkLoggedForDates(IEnumerable<DateTime> queryDates)
        {
            return jira.GetWorkLoggedForDatesFilteredIssues(queryDates, null);
        }

        public IEnumerable<StandardWorkLog> GetWorkLoggedForDatesFilteredIssues(IEnumerable<DateTime> queryDates, IEnumerable<string> issueRefs)
        {
            return jira.GetWorkLoggedForDatesFilteredIssues(queryDates, issueRefs);
        }

        public void UpdateCache()
        {
            recentJiraCollection.RemoveExpiredCache();
            UpdateJiraProjectCache();
        }

        public void AssignToCurrentUser(string jiraRef)
        {
            try
            {
                CheckAndConnectJira();
                jira.AssignIssue(jiraRef, CurrentUser.name);
            }
            catch (Exception ex)
            {
                throw new JiraConnectionException("Unable to assign issue.", ex);
            }

        }

        public void LogTime(string jiraRef, DateTime exportTimeStamp, TimeSpan exportTime, WorkLogStrategy strategy, bool addStandardComment, string comment = "", TimeSpan? remainingTime = null)
        {
            trackUsage.TrackAppUsage(TrackingType.ExportOccured);

            if (string.IsNullOrWhiteSpace(comment)) comment = exportSettings.EmptyExportComment;
            if (!string.IsNullOrWhiteSpace(exportSettings.ExportCommentPrefix))
            {
                comment = $"{exportSettings.ExportCommentPrefix}: {comment}";
            }

            try
            {
                jira.AddWorkLog(jiraRef, strategy, comment, exportTime, DateTime.SpecifyKind(exportTimeStamp, DateTimeKind.Local), remainingTime);
            }
            catch (Exception ex)
            {
                throw new WorkLogException("Error logging work", ex);
            }

            if (addStandardComment)
            {
                try
                {
                    jira.AddComment(jiraRef, comment);
                }
                catch (Exception ex)
                {
                    throw new CommentException("Comment was not added", ex);
                }
            }
        }

        public IEnumerable<Status> GetTransitions(string jiraRef)
        {
            try
            {
                return jira.GetIssueTransitions(jiraRef).transitions;
            }
            catch (Exception ex)
            {
                throw new JiraConnectionException("Unable to get transitions.", ex);
            }
        }

        public void TransitionIssue(string jiraRef, string transition)
        {
            try
            {
                jira.TransitionIssue(jiraRef, transition);
            }
            catch (Exception ex)
            {
                throw new JiraConnectionException("Unable to change issue state.", ex);
            }
        }

        private string GetJql(string searchText)
        {
            var projects = jira.GetProjects().ToList();
            var projectQuery = string.Empty;
            var nonProjectText = string.Empty;
            foreach (var keyword in searchText.Split(' '))
            {
                var firstProjectMatch = projects.FirstOrDefault(x => x.key == keyword);
                if (firstProjectMatch != null)
                {
                    if (!string.IsNullOrWhiteSpace(projectQuery))
                    {
                        projectQuery += " OR ";
                    }
                    projectQuery += $"project = \"{firstProjectMatch.name}\"";
                }
                else
                {
                    nonProjectText += $" {keyword}";
                }
            }
            nonProjectText = nonProjectText.Trim();

            var keyQuery = string.Empty;
            try
            {
                if (searchText.Contains("-") && jira.GetIssue(searchText) != null)
                {
                    keyQuery = $"(key = \"{searchText}\")";
                }
            }
            catch
            {
                //ignored
            }

            var jql = string.Empty;
            if (!string.IsNullOrWhiteSpace(nonProjectText))
            {
                if (string.IsNullOrWhiteSpace(jql))
                {
                    jql = $"(Summary ~ \"{nonProjectText}\" OR Description ~ \"{nonProjectText}\")";
                }
                else
                {
                    jql = $"({jql}) AND (Summary ~ \"{nonProjectText}\" OR Description ~ \"{nonProjectText}\")";
                }
            }
            if (!string.IsNullOrWhiteSpace(projectQuery))
            {
                if (string.IsNullOrWhiteSpace(jql))
                {
                    jql = $"({projectQuery})";
                }
                else
                {
                    jql = $"({jql}) AND ({projectQuery})";
                }
            }

            if (!string.IsNullOrWhiteSpace(keyQuery))
            {
                if (string.IsNullOrWhiteSpace(jql))
                {
                    jql = $"({keyQuery})";
                }
                else
                {
                    jql = $"({jql}) OR ({keyQuery})";
                }
            }

            return jql;
        }
    }
}
