﻿using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using GrinPlusPlus.Droid.Classes;
using System;
using System.IO;

using AndroidApp = Android.App.Application;

namespace GrinPlusPlus.Droid
{
    [Service(Exported = false, Name = "com.grinplusplus.mobile.GrinNodeService")]
    public class GrinNodeService : Android.App.Service
    {
        static readonly string TAG = typeof(GrinNodeService).FullName;

        System.Timers.Timer timer;

        const string channelId = "default";
        const string channelName = "Default";
        const string channelDescription = "The default channel for notifications.";

        static string nativeLibraryDir;

        NotificationManager manager;
        bool channelInitialized = false;
        
        public override IBinder OnBind(Intent intent)
        {
            return null;
        }

        public override void OnCreate()
        {
            base.OnCreate();

            Xamarin.Essentials.Preferences.Set("IsLoggedIn", false);
            Xamarin.Essentials.Preferences.Set("Status", Service.SyncHelpers.GetStatusLabel(string.Empty));
            Xamarin.Essentials.Preferences.Set("ProgressPercentage", (double)0);
            Xamarin.Essentials.Preferences.Set("DataFolder", new Java.IO.File(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), ".GrinPP/MAINNET/NODE")).AbsolutePath);
            Xamarin.Essentials.Preferences.Set("LogsFolder", new Java.IO.File(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), ".GrinPP/MAINNET/LOGS")).AbsolutePath);
            Xamarin.Essentials.Preferences.Set("BackendFolder", new Java.IO.File(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), ".GrinPP/MAINNET/")).AbsolutePath);

            nativeLibraryDir = PackageManager.GetApplicationInfo(ApplicationInfo.PackageName, PackageInfoFlags.SharedLibraryFiles).NativeLibraryDir;

            SetNodeTimer();
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            if (intent.Action == null)
            {
                NodeControl.StartTor(nativeLibraryDir);
                NodeControl.StartNode(nativeLibraryDir);

                RegisterForegroundService(Service.SyncHelpers.GetStatusLabel(string.Empty));
            }
            else
            {
                NodeControl.StopNode();
                NodeControl.StopTor();

                if (intent.Action.Equals(Constants.ACTION_STOP_SERVICE))
                {
                    Log.Info(TAG, "Stop Service called.");

                    try
                    {
                        Xamarin.Essentials.Platform.CurrentActivity.Finish();
                    }
                    catch (Exception e)
                    {
                        Log.Verbose(TAG, e.Message);
                    }

                    if (timer != null)
                    {
                        timer.Stop();
                        timer.Enabled = false;
                    }

                    StopForeground(true);
                    StopSelf();
                }
                else if (intent.Action.Equals(Constants.ACTION_RESTART_NODE))
                {
                    Log.Info(TAG, "Restart Grin Node called.");

                    NodeControl.StartTor(nativeLibraryDir);
                    NodeControl.StartNode(nativeLibraryDir);
                }
                else if (intent.Action.Equals(Constants.ACTION_RESYNC_NODE))
                {
                    Log.Info(TAG, "Resync Grin Node called.");
                    
                    NodeControl.DeleteNodeDataFolder(Xamarin.Essentials.Preferences.Get("DataFolder", ""));

                    NodeControl.StartTor(nativeLibraryDir);
                    NodeControl.StartNode(nativeLibraryDir);
                }
            }

            // This tells Android not to restart the service if it is killed to reclaim resources.
            return StartCommandResult.Sticky;
        }

        private void SetNodeTimer()
        {
            timer = new System.Timers.Timer(1475);
            timer.Elapsed += OnNodeTimedEvent;
            timer.Start();
        }

        private async void OnNodeTimedEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
            var label = Service.SyncHelpers.GetStatusLabel(string.Empty);

            if (!NodeControl.IsTorRunning())
            {
                Log.Error(TAG, $"Tor is not running, starting Tor...");
                NodeControl.StartTor(nativeLibraryDir);
            }

            if (!NodeControl.IsNodeRunning())
            {
                Log.Error(TAG, $"Node is not running. Starting Node...");
                NodeControl.StartNode(nativeLibraryDir);
                label = Service.SyncHelpers.GetStatusLabel(string.Empty);
            }
            else
            {
                try
                {
                    var nodeStatus = await Service.Node.Instance.Status().ConfigureAwait(false);

                    Xamarin.Essentials.Preferences.Set("ProgressPercentage", Service.SyncHelpers.GetProgressPercentage(nodeStatus));

                    label = Service.SyncHelpers.GetStatusLabel(nodeStatus.SyncStatus);

                    Xamarin.Essentials.Preferences.Set("HeaderHeight", nodeStatus.HeaderHeight);
                    Xamarin.Essentials.Preferences.Set("Blocks", nodeStatus.Chain.Height);
                    Xamarin.Essentials.Preferences.Set("NetworkHeight", nodeStatus.Network.Height);
                }
                catch (System.Net.WebException ex)
                {
                    Log.Error(TAG, $"Node is not running: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Communication Error: {ex.Message}");
                }
            }

            Xamarin.Essentials.Preferences.Set("Status", label);

            if (!label.Equals("Not Connected") && !label.Equals("Waiting for Peers"))
            {
                var percentage = $"{string.Format($"{ Double.Parse(Xamarin.Essentials.Preferences.Get("ProgressPercentage", "0").ToString()) * 100:F}")} %";
                RegisterForegroundService($"{label} {percentage}");
            }
            else
            {
                RegisterForegroundService(label);
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            Xamarin.Essentials.Preferences.Set("Status", Service.SyncHelpers.GetStatusLabel(string.Empty));

            NodeControl.StopNode();
            NodeControl.StopTor();

            if (timer != null)
            {
                timer.Stop();
            }
        }

        private void RegisterForegroundService(string status)
        {
            if (!channelInitialized)
            {
                CreateNotificationChannel();
            }

            // Work has finished, now dispatch a notification to let the user know.
            var notification = new Notification.Builder(AndroidApp.Context, channelId)
                .SetContentTitle("Grin Node")
                .SetContentText(status)
                .SetSmallIcon(Resource.Drawable.logo)
                .SetContentIntent(BuildIntentToShowMainActivity())
                .SetOngoing(true)
                .AddAction(BuildRestartNodeAction())
                .AddAction(BuildResyncNodeAction())
                .AddAction(BuildStopServiceAction())
                .Build();

            // Enlist this instance of the service as a foreground service
            StartForeground(Constants.SERVICE_RUNNING_NOTIFICATION_ID, notification);
        }

        void CreateNotificationChannel()
        {
            manager = (NotificationManager)AndroidApp.Context.GetSystemService(AndroidApp.NotificationService);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channelNameJava = new Java.Lang.String(channelName);
                var channel = new NotificationChannel(channelId, channelNameJava, NotificationImportance.None)
                {
                    Description = channelDescription,
                };
                channel.EnableVibration(false);
                channel.EnableLights(false);
                manager.CreateNotificationChannel(channel);
            }

            channelInitialized = true;
        }

        /// <summary>
        /// Builds a PendingIntent that will display the main activity of the app. This is used when the 
        /// user taps on the notification; it will take them to the main activity of the app.
        /// </summary>
        /// <returns>The content intent.</returns>
        PendingIntent BuildIntentToShowMainActivity()
        {
            var notificationIntent = new Intent(this, typeof(MainActivity));
            notificationIntent.SetAction(Constants.ACTION_MAIN_ACTIVITY);
            notificationIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTask);

            var pendingIntent = PendingIntent.GetActivity(this, 0, notificationIntent, PendingIntentFlags.UpdateCurrent);
            return pendingIntent;
        }

        /// <summary>
		/// Builds a Notification.Action that will instruct the service to restart the node.
		/// </summary>
		/// <returns>The restart node action.</returns>
		Notification.Action BuildRestartNodeAction()
        {
            var action = "Run";
            var status = Xamarin.Essentials.Preferences.Get("Status", string.Empty);
            if (!status.Equals("Not Running"))
            {
                action = "Restart";
            }
            var restartIntent = new Intent(this, GetType());
            restartIntent.SetAction(Constants.ACTION_RESTART_NODE);
            var restartTimerPendingIntent = PendingIntent.GetService(this, 0, restartIntent, 0);

            var builder = new Notification.Action.Builder(null,
                                              action,
                                              restartTimerPendingIntent);

            return builder.Build();
        }

        /// <summary>
		/// Builds a Notification.Action that will instruct the service to resync the node.
		/// </summary>
		/// <returns>The resync node action.</returns>
		Notification.Action BuildResyncNodeAction()
        {
            var status = Xamarin.Essentials.Preferences.Get("Status", string.Empty);
            if (status.Equals("Not Running"))
            {
                return null;
            }

            var resyncNodeIntent = new Intent(this, GetType());
            resyncNodeIntent.SetAction(Constants.ACTION_RESYNC_NODE);
            var restartTimerPendingIntent = PendingIntent.GetService(this, 0, resyncNodeIntent, 0);

            var builder = new Notification.Action.Builder(null,
                                              "(RE)Synchronize",
                                              restartTimerPendingIntent);

            return builder.Build();
        }

        /// <summary>
		/// Builds the Notification.Action that will allow the user to stop the service via the
		/// notification in the status bar
		/// </summary>
		/// <returns>The stop service action.</returns>
		Notification.Action BuildStopServiceAction()
        {
            var stopServiceIntent = new Intent(this, GetType());
            stopServiceIntent.SetAction(Constants.ACTION_STOP_SERVICE);
            var stopServicePendingIntent = PendingIntent.GetService(this, 0, stopServiceIntent, 0);

            var builder = new Notification.Action.Builder(null,
                                                          "EXIT",
                                                          stopServicePendingIntent);
            return builder.Build();
        }
    }
}