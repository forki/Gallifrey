﻿using System;
using Newtonsoft.Json;

namespace Gallifrey.JiraIntegration
{
    public class RecentJira
    {
        public string JiraReference { get; }
        public string JiraProjectName { get; private set; }
        public string JiraName { get; private set; }
        public string JiraParentReference { get; private set; }
        public string JiraParentName { get; private set; }
        public DateTime DateSeen { get; private set; }

        [JsonConstructor]
        public RecentJira(string jiraReference, string jiraProjectName, string jiraName, string jiraParentReference, string jiraParentName, DateTime dateSeen)
        {
            JiraReference = jiraReference;
            JiraProjectName = jiraProjectName;
            JiraName = jiraName;
            JiraParentReference = jiraParentReference;
            JiraParentName = jiraParentName;
            DateSeen = dateSeen;
        }

        public RecentJira(string jiraReference, string jiraProjectName, string jiraName, string jiraParentReference, string jiraParentName)
        {
            JiraReference = jiraReference;
            JiraProjectName = jiraProjectName;
            JiraName = jiraName;
            JiraParentReference = jiraParentReference;
            JiraParentName = jiraParentName;
        }

        public void UpdateDetail(string jiraProjectName, string jiraName, string jiraParentReference, string jiraParentName, DateTime dateSeen)
        {
            JiraProjectName = jiraProjectName;
            JiraName = jiraName;
            JiraParentReference = jiraParentReference;
            JiraParentName = jiraParentName;
            DateSeen = dateSeen;
        }

        public override bool Equals(object obj)
        {
            // Try to cast the object to compare to to be a Person
            var otherRecentJira = obj as RecentJira;

            return otherRecentJira != null && JiraReference == otherRecentJira.JiraReference;
        }

        public override int GetHashCode()
        {
            return JiraReference.GetHashCode();
        }
    }
}
