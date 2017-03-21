﻿using System;
using System.Deployment.Application;
using System.Threading.Tasks;
using System.Windows.Forms;
using Gallifrey.Settings;
using Gallifrey.Versions;

namespace Gallifrey.AppTracking
{
    public interface ITrackUsage
    {
        void TrackAppUsage(TrackingType trackingType);
    }

    public class TrackUsage : ITrackUsage
    {
        private readonly ISettingsCollection settingsCollection;
        private readonly InstanceType instanceType;
        private WebBrowser webBrowser;

        public TrackUsage(ISettingsCollection settingsCollection, InstanceType instanceType)
        {
            this.settingsCollection = settingsCollection;
            this.instanceType = instanceType;
            SetupBrowser();
            TrackAppUsage(TrackingType.AppLoad);
        }

        private bool IsTrackingEnabled(TrackingType trackingType)
        {
            return settingsCollection.AppSettings.UsageTracking || trackingType == TrackingType.DailyHearbeat;
        }

        private void SetupBrowser()
        {
            try
            {
                webBrowser = new WebBrowser
                {
                    ScrollBarsEnabled = false,
                    ScriptErrorsSuppressed = true
                };

            }
            catch (Exception)
            {
                webBrowser = null;
            }

        }

        public async void TrackAppUsage(TrackingType trackingType)
        {
            if (IsTrackingEnabled(trackingType) && ApplicationDeployment.IsNetworkDeployed)
            {
                if (webBrowser == null)
                {
                    SetupBrowser();
                }

                try
                {
                    webBrowser.Navigate(GetNavigateUrl(trackingType));
                    while (webBrowser.ReadyState != WebBrowserReadyState.Complete)
                    {
                        await Task.Delay(1000);
                    }
                }
                catch (Exception)
                {
                    SetupBrowser();
                }
            }
        }

        private string GetNavigateUrl(TrackingType trackingType)
        {
            var prem = "Gallifrey";
            if (settingsCollection.InternalSettings.IsPremium)
            {
                prem = "Gallifrey_Premium";
            }

            return $"http://releases.gallifreyapp.co.uk/tracking/{trackingType}.html?utm_source={prem}&utm_medium={instanceType}&utm_campaign={settingsCollection.InternalSettings.LastChangeLogVersion}&uid={settingsCollection.InstallationHash}";
        }
    }
}
