﻿using Gallifrey.Jira.Model;
using Gallifrey.JiraIntegration;

namespace Gallifrey.UI.Modern.Models
{
    public class JiraIssueDisplayModel
    {
        public string Reference { get; set; }
        public string Description { get; set; }
        public string ParentReference { get; set; }
        public string ParentDescription { get; set; }
        public bool HasParent => !string.IsNullOrWhiteSpace(ParentReference);

        public JiraIssueDisplayModel(Issue issue)
        {
            Reference = issue.key;
            Description = issue.fields.summary;
            if (issue.fields.parent != null)
            {
                ParentReference = issue.fields.parent.key;
                ParentDescription = issue.fields.parent.fields.summary;
            }
        }

        public JiraIssueDisplayModel(RecentJira recentJira)
        {
            Reference = recentJira.JiraReference;
            Description = recentJira.JiraName;
            if (!string.IsNullOrWhiteSpace(recentJira.JiraParentReference))
            {
                ParentReference = recentJira.JiraParentReference;
                ParentDescription = recentJira.JiraParentName;
            }
        }

        public override bool Equals(object obj)
        {
            var other = obj as JiraIssueDisplayModel;
            return other != null && Reference.Equals(other.Reference);
        }

        public override int GetHashCode()
        {
            return Reference.GetHashCode();
        }
    }
}