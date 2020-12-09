﻿using GrinPlusPlus.Api;
using GrinPlusPlus.Models;
using GrinPlusPlus.Resources;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;
using Prism.Commands;
using Prism.Navigation;
using Prism.Services;
using Prism.Services.Dialogs;
using System;
using System.Threading;
using Xamarin.Essentials;

namespace GrinPlusPlus.ViewModels
{
    public class SendingGrinsPageViewModel : ViewModelBase
    {
        private CancellationTokenSource _cancel;

        private double _amount = 0;
        public double Amount
        {
            get { return _amount; }
            set { SetProperty(ref _amount, value); }
        }

        private string _fee = "----------";
        public string Fee
        {
            get { return _fee; }
            set { SetProperty(ref _fee, value); }
        }

        private string _address = string.Empty;
        public string Address
        {
            get { return _address.Trim(); }
            set { SetProperty(ref _address, value); }
        }

        private string _message = string.Empty;
        public string Message
        {
            get { return _message; }
            set { SetProperty(ref _message, value); }
        }

        private bool _sendMax = false;
        public bool SendMax
        {
            get { return _sendMax; }
            set { SetProperty(ref _sendMax, value); }
        }

        public DelegateCommand SendUsingTorCommand => new DelegateCommand(SendUsingTor);

        async void SendUsingTor()
        {
            if (Amount <= 0 || string.IsNullOrEmpty(Address))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    var message = string.Empty;
                    if (Amount <= 0)
                    {
                        message = AppResources.ResourceManager.GetString("AmountCantBeNull");
                    }
                    else if (string.IsNullOrEmpty(Address))
                    {
                        message = AppResources.ResourceManager.GetString("AddressCantBeNull");
                    }
                    await PageDialogService.DisplayAlertAsync("Error", message, "OK");
                });
                return;
            }

            if (await CrossFingerprint.Current.IsAvailableAsync(true))
            {
                _cancel = new CancellationTokenSource();

                var wallet = (await SecureStorage.GetAsync("username")).ToUpper();
                var message = AppResources.ResourceManager.GetString("ConfirmIdentity");

                var dialogConfig = new AuthenticationRequestConfiguration(wallet, message)
                {
                    CancelTitle = null,
                    FallbackTitle = null,
                    AllowAlternativeAuthentication = true
                };

                var result = await CrossFingerprint.Current.AuthenticateAsync(dialogConfig, _cancel.Token);

                if (!result.Authenticated)
                {
                    return;
                }
            }

            await NavigationService.NavigateAsync("SendGrinsUsingTorPage",
                new NavigationParameters
                {
                    { "address", Address },
                    { "message", Message },
                    { "amount", Amount },
                    { "max", SendMax }
                }
            );
        }

        public SendingGrinsPageViewModel(INavigationService navigationService, IDataProvider dataProvider, IDialogService dialogService, IPageDialogService pageDialogService)
            : base(navigationService, dataProvider, dialogService, pageDialogService)
        {
        }

        public override void OnNavigatedTo(INavigationParameters parameters)
        {
            if (parameters.ContainsKey("max"))
            {
                SendMax = (bool)parameters["max"];
            }

            if (parameters.ContainsKey("amount"))
            {
                Amount = (double)parameters["amount"];
            }

            if (parameters.ContainsKey("address"))
            {
                Address = (string)parameters["address"];
            }

            if (parameters.ContainsKey("message"))
            {
                Message = (string)parameters["message"];
            }

            if (!SendMax)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        FeeEstimation estimation = await DataProvider.EstimateFee(await SecureStorage.GetAsync("token"), Amount);
                        Fee = (estimation.Fee / Math.Pow(10, 9)).ToString("F9");
                    }
                    catch (Exception ex)
                    {
                        await PageDialogService.DisplayAlertAsync("Error", ex.Message, "OK");
                    }
                });
            }
        }

        public DelegateCommand CancelCommand => new DelegateCommand(Cancel);

        async void Cancel()
        {
            await NavigationService.GoBackToRootAsync();
        }
    }
}
