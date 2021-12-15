﻿using GrinPlusPlus.Api;
using Prism.Commands;
using Prism.Navigation;
using Prism.Services;
using Prism.Services.Dialogs;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace GrinPlusPlus.ViewModels
{
    public class ProfilePageViewModel : ViewModelBase
    {
        private bool _reachable;
        public bool Reachable
        {
            get { return _reachable; }
            set { SetProperty(ref _reachable, value); }
        }

        private string _slatepackAddress = string.Empty;
        public string SlatepackAddress
        {
            get { return _slatepackAddress; }
            set { SetProperty(ref _slatepackAddress, value); }
        }

        private bool StopTimer = false;

        public DelegateCommand CopyAddressCommand => new DelegateCommand(CopyAddress);

        private async void CopyAddress()
        {
            await Clipboard.SetTextAsync(SlatepackAddress);
        }

        public ProfilePageViewModel(INavigationService navigationService, IDataProvider dataProvider, IDialogService dialogService, IPageDialogService pageDialogService)
            : base(navigationService, dataProvider, dialogService, pageDialogService)
        {
            Reachable = Settings.Reachable;

            Task.Factory.StartNew(async () =>
            {
                SlatepackAddress = await SecureStorage.GetAsync("slatepack_address").ConfigureAwait(false);
            });

        }

        public override void OnNavigatedTo(INavigationParameters parameters)
        {
            Device.StartTimer(TimeSpan.FromSeconds(5), () =>
            {
                Reachable = Settings.Reachable;

                return !StopTimer;
            });
        }

        public override void OnNavigatedFrom(INavigationParameters parameters)
        {
            StopTimer = true;
        }
    }
}
