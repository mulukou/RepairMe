﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace RepairMe
{
    public unsafe class EventHandler : IDisposable
    {
        private readonly EquipmentScanner equipmentScanner;
        private readonly ManualResetEvent manualResetEvent;
        private readonly DalamudPluginInterface pi;
        private const int CooldownMilliseconds = 1000;
        private AtkUnitBase* addonLoading;
        internal EquipmentData EquipmentScannerLastEquipmentData;
        private CancellationTokenSource? eventLoopTokenSource;

        public bool IsActive => IsLoggedIn && !IsLoading;
        private bool IsLoggedIn => pi.ClientState.IsLoggedIn;
        private bool IsLoading
        {
            get
            {
                if(addonLoading==null) SetAddonNowLoading();
                try
                {
                    return addonLoading->IsVisible;
                }
                catch (Exception e1)
                {
                    PluginLog.Debug(e1, "NowLoading is being problematic");
                    try
                    {
                        SetAddonNowLoading();
                        return addonLoading->IsVisible;
                    }
                    catch (Exception e2)
                    {
                        PluginLog.Debug(e2, "NowLoading is nowhere to be found");
                        return false;
                    }
                }
            }
        }

        public EventHandler(DalamudPluginInterface pluginInterface, Configuration configuration,
            EquipmentScanner equipmentScanner)
        {
            pi = pluginInterface;

            this.equipmentScanner = equipmentScanner;
            manualResetEvent = new ManualResetEvent(false);

            SetAddonNowLoading();

            equipmentScanner.NotificationTarget = Notify;

            pi.ClientState.OnLogin += ClientStateOnOnLogin;
            pi.ClientState.OnLogout += ClientStateOnOnLogout;
        }

        private void SetAddonNowLoading()
        {
            addonLoading = (AtkUnitBase*) pi.Framework.Gui.GetUiObjectByName("NowLoading", 1);
        }

        public void Dispose()
        {
            pi.ClientState.OnLogin -= ClientStateOnOnLogin;
            pi.ClientState.OnLogout -= ClientStateOnOnLogout;

            manualResetEvent?.Dispose();
            eventLoopTokenSource?.Cancel();
            eventLoopTokenSource?.Dispose();
        }

        private void ClientStateOnOnLogin(object sender, EventArgs e)
        {
            SetAddonNowLoading();
            Notify();
        }

        private void ClientStateOnOnLogout(object sender, EventArgs e)
        {
            Block();
        }

        private void Notify()
        {
            manualResetEvent.Set();
        }

        private void Block()
        {
            manualResetEvent.Reset();
        }

        public void Start()
        {
            eventLoopTokenSource ??= new CancellationTokenSource();

            Task.Run(() => EventLoop(eventLoopTokenSource.Token))
                .ContinueWith(t =>
                {
                    if (t.Exception == null) return;
                    var aggException = t.Exception.Flatten();
                    foreach (var exception in aggException.InnerExceptions)
                    {
                        if (exception is OperationCanceledException || exception is ObjectDisposedException)
                            continue;
                        PluginLog.Error(exception, "RepairMe stopped unexpectedly. Restart it to continue using it.");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void EventLoop(CancellationToken token)
        {
            try
            {
                do
                {
                    WaitHandle.WaitAny(new[] {token.WaitHandle, manualResetEvent} /*, TimeSpan.FromSeconds(10)*/);

                    EquipmentScannerLastEquipmentData = equipmentScanner.BuildEquipmentData;
#if DEBUG
                pi.Framework.Gui.Chat.Print($"RepairMe update @ {DateTime.Now.ToString("HH:mm:ss")}");
#endif

                    Block();
                } while (!token.IsCancellationRequested);
            }
            catch (Exception e)
            {
                PluginLog.Fatal(e, "prevented EventHandler crash");
            }
        }
    }
}