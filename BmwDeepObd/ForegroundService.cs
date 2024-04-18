﻿using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace BmwDeepObd
{
    [Android.App.Service(
        Label = "@string/app_name",
        Name = ActivityCommon.AppNameSpace + "." + nameof(ForegroundService),
        ForegroundServiceType = Android.Content.PM.ForegroundService.TypeConnectedDevice
    )]
    public class ForegroundService : Android.App.Service
    {
#if DEBUG
        private static readonly string Tag = typeof(ForegroundService).FullName;
#endif
        public const int ServiceRunningNotificationId = 10000;
        public const string BroadcastMessageKey = "broadcast_message";
        public const string BroadcastStopComm = "stop_communication";
        public const string NotificationBroadcastAction = ActivityCommon.AppNameSpace + ".Notification.Action";
        public const string ActionBroadcastCommand = ActivityCommon.AppNameSpace + ".Action.Command";

        public const string ActionStartService = "ForegroundService.action.START_SERVICE";
        public const string ActionStopService = "ForegroundService.action.STOP_SERVICE";
        public const string ActionShowMainActivity = "ForegroundService.action.SHOW_MAIN_ACTIVITY";
        public const string StartComm = "StartComm";

        private bool _isStarted;
        private ActivityCommon _activityCommon;
        private ActivityCommon.InstanceDataCommon _instanceData;
        private Handler _stopHandler;
        private Timer _commTimer;
        private Java.Lang.Runnable _stopRunnable;

        public ActivityCommon ActivityCommon => _activityCommon;

        public override void OnCreate()
        {
            base.OnCreate();
#if DEBUG
            Android.Util.Log.Info(Tag, "OnCreate: the service is initializing.");
#endif
            _stopHandler = new Handler(Looper.MainLooper);
            _stopRunnable = new Java.Lang.Runnable(StopEdiabasThread);
            _activityCommon = new ActivityCommon(this, null, BroadcastReceived);
            _activityCommon.SetLock(ActivityCommon.LockType.Cpu);
            _instanceData = new ActivityCommon.InstanceDataCommon();

            lock (ActivityCommon.GlobalLockObject)
            {
                EdiabasThread ediabasThread = ActivityCommon.EdiabasThread;
                if (ediabasThread != null)
                {
                    ediabasThread.ActiveContext = this;
                }
            }
        }

        public override Android.App.StartCommandResult OnStartCommand(Intent intent, Android.App.StartCommandFlags flags, int startId)
        {
            if (intent?.Action == null)
            {
                return Android.App.StartCommandResult.Sticky;
            }

            switch (intent.Action)
            {
                case ActionStartService:
                {
                    bool startComm = intent.GetBooleanExtra(StartComm, false);
                    if (_isStarted)
                    {
#if DEBUG
                        Android.Util.Log.Info(Tag, "OnStartCommand: The service is already running.");
#endif
                    }
                    else
                    {
#if DEBUG
                        Android.Util.Log.Info(Tag, "OnStartCommand: The service is starting.");
#endif
                        RegisterForegroundService();
                        if (startComm)
                        {
                            StartCommTimer();
                        }
                        _isStarted = true;
                    }
                    break;
                }

                case ActionStopService:
                {
#if DEBUG
                    Android.Util.Log.Info(Tag, "OnStartCommand: The service is stopping.");
#endif
                    SendStopCommBroadcast();
                    StopEdiabasThread(false);

                    if (!ActivityCommon.CommActive)
                    {
                        if (_isStarted)
                        {
                            try
                            {
                                if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                                {
                                    StopForeground(Android.App.StopForegroundFlags.Remove);
                                }
                                else
                                {
#pragma warning disable CS0618
#pragma warning disable CA1422
                                    StopForeground(true);
#pragma warning restore CA1422
#pragma warning restore CS0618
                                }

                                StopSelf();
                            }
                            catch (Exception)
                            {
                                // ignored
                            }

                            _isStarted = false;
                        }
                    }
                    break;
                }

                case ActionShowMainActivity:
                {
#if DEBUG
                    Android.Util.Log.Info(Tag, "OnStartCommand: Show main activity");
#endif
                    ShowMainActivity();
                    break;
                }
            }

            // This tells Android not to restart the service if it is killed to reclaim resources.
            return Android.App.StartCommandResult.Sticky;
        }

        public override IBinder OnBind(Intent intent)
        {
            // Return null because this is a pure started service. A hybrid service would return a binder.
            return null;
        }

        public override void OnDestroy()
        {
            // We need to shut things down.
            //Log.Info(Tag, "OnDestroy: The started service is shutting down.");

            // Remove the notification from the status bar.
            NotificationManagerCompat notificationManager = NotificationManagerCompat.From(this);
            notificationManager.Cancel(ServiceRunningNotificationId);
            _activityCommon?.SetLock(ActivityCommon.LockType.None);
            DisconnectEdiabasEvents();
            lock (ActivityCommon.GlobalLockObject)
            {
                EdiabasThread ediabasThread = ActivityCommon.EdiabasThread;
                if (ediabasThread != null)
                {
                    ediabasThread.ActiveContext = null;
                }
            }

            _activityCommon?.Dispose();
            _activityCommon = null;
            _isStarted = false;

            StopCommTimer();
            if (_stopHandler != null)
            {
                try
                {
                    _stopHandler.RemoveCallbacksAndMessages(null);
                }
                catch (Exception)
                {
                    // ignored
                }
                _stopHandler = null;
            }
            base.OnDestroy();
        }

        private void RegisterForegroundService()
        {
            try
            {
                Android.App.Notification notification = new NotificationCompat.Builder(this, ActivityCommon.NotificationChannelCommunication)
                    .SetContentTitle(Resources.GetString(Resource.String.app_name))
                    .SetContentText(Resources.GetString(Resource.String.service_notification))
                    .SetSmallIcon(Resource.Drawable.ic_stat_obd)
                    .SetContentIntent(BuildIntentToShowMainActivity())
                    .SetOngoing(true)
                    .AddAction(BuildStopServiceAction())
                    .SetPriority(NotificationCompat.PriorityLow)
                    .SetCategory(NotificationCompat.CategoryService)
                    .Build();

                // Enlist this instance of the service as a foreground service
                ServiceCompat.StartForeground(this, ServiceRunningNotificationId, notification, (int) Android.Content.PM.ForegroundService.TypeConnectedDevice);
            }
#pragma warning disable CS0168 // Variable ist deklariert, wird jedoch niemals verwendet
            catch (Exception ex)
#pragma warning restore CS0168 // Variable ist deklariert, wird jedoch niemals verwendet
            {
                // ignored
#if DEBUG
                Android.Util.Log.Info(Tag, string.Format("RegisterForegroundService exception: {0}", ex.Message));
#endif
            }
        }

        private void SendStopCommBroadcast()
        {
            Intent broadcastIntent = new Intent(NotificationBroadcastAction);
            broadcastIntent.PutExtra(BroadcastMessageKey, BroadcastStopComm);
            InternalBroadcastManager.InternalBroadcastManager.GetInstance(this).SendBroadcast(broadcastIntent);
        }

        private void ShowMainActivity()
        {
            try
            {
                Intent intent = new Intent(this, typeof(ActivityMain));
                //intent.SetAction(Intent.ActionMain);
                //intent.AddCategory(Intent.CategoryLauncher);
                intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.NewTask | ActivityFlags.ClearTop);
                intent.PutExtra(ActivityMain.ExtraShowTitle, true);
                StartActivity(intent);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void ConnectEdiabasEvents()
        {
            if (ActivityCommon.EdiabasThread != null)
            {
                ActivityCommon.EdiabasThread.ThreadTerminated += ThreadTerminated;
            }
        }

        private void DisconnectEdiabasEvents()
        {
            if (ActivityCommon.EdiabasThread != null)
            {
                ActivityCommon.EdiabasThread.ThreadTerminated -= ThreadTerminated;
            }
        }

        private void ThreadTerminated(object sender, EventArgs e)
        {
            PostStopEdiabasThread();
        }

        private void PostStopEdiabasThread()
        {
            if (_stopHandler == null)
            {
                return;
            }

            if (!_stopHandler.HasCallbacks(_stopRunnable))
            {
                _stopHandler.Post(_stopRunnable);
            }
        }

        private void StopEdiabasThread()
        {
            if (_stopHandler == null)
            {
                return;
            }
            StopEdiabasThread(true);
        }

        private void StopEdiabasThread(bool wait)
        {
            lock (ActivityCommon.GlobalLockObject)
            {
                if (ActivityCommon.EdiabasThread != null)
                {
                    if (!ActivityCommon.EdiabasThread.ThreadStopping())
                    {
                        ConnectEdiabasEvents();
                        ActivityCommon.EdiabasThread.StopThread(wait);
                    }
                }
            }
            if (wait)
            {
                if (_isStarted)
                {
                    try
                    {
                        if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                        {
                            StopForeground(Android.App.StopForegroundFlags.Remove);
                        }
                        else
                        {
#pragma warning disable CS0618
#pragma warning disable CA1422
                            StopForeground(true);
#pragma warning restore CA1422
#pragma warning restore CS0618
                        }

                        StopSelf();
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                    _isStarted = false;
                }
                lock (ActivityCommon.GlobalLockObject)
                {
                    DisconnectEdiabasEvents();
                    if (ActivityCommon.EdiabasThread != null)
                    {
                        ActivityCommon.EdiabasThread.Dispose();
                        ActivityCommon.EdiabasThread = null;
                    }
                }
            }
        }

        private void StartCommTimer()
        {
            if (_commTimer == null)
            {
                _commTimer = new Timer(CommTimerCallback, null, 0, 1000);
            }
        }

        private void StopCommTimer()
        {
            if (_commTimer != null)
            {
                _commTimer.Dispose();
                _commTimer = null;
            }
            _instanceData = null;
        }

        private void CommTimerCallback(object state)
        {
            if (_instanceData == null)
            {
                if (!_activityCommon.IsExStorageAvailable())
                {
#if DEBUG
                    Android.Util.Log.Info(Tag, "CommTimerCallback: No external storage");
#endif
                }

                string settingsFile = ActivityCommon.GetSettingsFileName();
                if (!string.IsNullOrEmpty(settingsFile) && File.Exists(settingsFile))
                {
                    ActivityCommon.InstanceDataCommon instanceData = new ActivityCommon.InstanceDataCommon();
                    if (!_activityCommon.GetSettings(instanceData, settingsFile, ActivityCommon.SettingsMode.All, true))
                    {
#if DEBUG
                        Android.Util.Log.Info(Tag, "CommTimerCallback: GetSettings failed");
#endif
                        return;
                    }

                    if (!_activityCommon.UpdateDirectories(instanceData))
                    {
#if DEBUG
                        Android.Util.Log.Info(Tag, "CommTimerCallback: UpdateDirectories failed");
#endif
                        return;
                    }
                    _instanceData = instanceData;
#if DEBUG
                    Android.Util.Log.Info(Tag, "CommTimerCallback: GetSettings Ok");
#endif
                }
            }

            if (_instanceData != null)
            {
#if DEBUG
                Android.Util.Log.Info(Tag, "CommTimerCallback: Valid instance");
#endif
            }
        }

        /// <summary>
        /// Builds a PendingIntent that will display the main activity of the app. This is used when the 
        /// user taps on the notification; it will take them to the main activity of the app.
        /// </summary>
        /// <returns>The content intent.</returns>
        private Android.App.PendingIntent BuildIntentToShowMainActivity()
        {
            Intent showMainActivityIntent = new Intent(this, GetType());
            showMainActivityIntent.SetAction(ActionShowMainActivity);
            Android.App.PendingIntentFlags intentFlags = Android.App.PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                intentFlags |= Android.App.PendingIntentFlags.Mutable;
            }
            Android.App.PendingIntent pendingIntent = Android.App.PendingIntent.GetService(this, 0, showMainActivityIntent, intentFlags);
            return pendingIntent;
        }

        /// <summary>
        /// Builds the Notification.Action that will allow the user to stop the service via the
        /// notification in the status bar
        /// </summary>
        /// <returns>The stop service action.</returns>
        private NotificationCompat.Action BuildStopServiceAction()
        {
            Intent stopServiceIntent = new Intent(this, GetType());
            stopServiceIntent.SetAction(ActionStopService);
            Android.App.PendingIntentFlags intentFlags = 0;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                intentFlags |= Android.App.PendingIntentFlags.Mutable;
            }
            Android.App.PendingIntent stopServicePendingIntent = Android.App.PendingIntent.GetService(this, 0, stopServiceIntent, intentFlags);

            var builder = new NotificationCompat.Action.Builder(Resource.Drawable.ic_stat_cancel,
                GetText(Resource.String.service_stop_comm),
                stopServicePendingIntent);
            return builder.Build();
        }

        private void BroadcastReceived(Context context, Intent intent)
        {
            if (intent == null)
            {
                return;
            }
            string action = intent.Action;
            switch (action)
            {
                case ActionBroadcastCommand:
                {
                    HandleActionBroadcast(intent);
                    HandleCustomBroadcast(context, intent);
                    break;
                }
            }
        }

        private void HandleActionBroadcast(Intent intent)
        {
            string request = intent.GetStringExtra("action");
            if (string.IsNullOrEmpty(request))
            {
                return;
            }
            string[] requestList = request.Split(':');
            if (requestList.Length < 1)
            {
                return;
            }
            if (string.Compare(requestList[0], "new_page", StringComparison.OrdinalIgnoreCase) == 0)
            {
                if (requestList.Length < 2)
                {
                    return;
                }
                JobReader.PageInfo pageInfoSel = null;
                foreach (JobReader.PageInfo pageInfo in ActivityCommon.JobReader.PageList)
                {
                    if (string.Compare(pageInfo.Name, requestList[1], StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        pageInfoSel = pageInfo;
                        break;
                    }
                }
                if (pageInfoSel == null)
                {
                    return;
                }
                if (!ActivityCommon.CommActive)
                {
                    return;
                }
                EdiabasThread ediabasThread = ActivityCommon.EdiabasThread;
                if (ediabasThread == null)
                {
                    return;
                }
                if (ediabasThread.JobPageInfo != pageInfoSel)
                {
                    ActivityCommon.EdiabasThread.CommActive = true;
                    ediabasThread.JobPageInfo = pageInfoSel;
                }
            }
        }

        private void HandleCustomBroadcast(Context context, Intent intent)
        {
            try
            {
                EdiabasThread ediabasThread = ActivityCommon.EdiabasThread;
                // ReSharper disable once UseNullPropagation
                if (ediabasThread == null)
                {
                    return;
                }
                JobReader.PageInfo pageInfo = ediabasThread.JobPageInfo;
                if (pageInfo.ClassObject == null)
                {
                    return;
                }
                Type pageType = pageInfo.ClassObject.GetType();
                MethodInfo broadcastReceived = pageType.GetMethod("BroadcastReceived", new[] { typeof(JobReader.PageInfo), typeof(Context), typeof(Intent) });
                if (broadcastReceived == null)
                {
                    return;
                }
                object[] args = { pageInfo, context, intent };
                broadcastReceived.Invoke(pageInfo.ClassObject, args);
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}
