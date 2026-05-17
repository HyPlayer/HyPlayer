using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace HyPlayer.Classes
{
    public partial class SimpleListItem
    {
        public bool CanPlay { get; set; }
        public string CoverLink { get; set; }
        public string LineOne { get; set; }
        public string LineThree { get; set; }
        public string LineTwo { get; set; }
        public int Order { get; set; } = 0;
        public AppRoute? Route { get; set; }
        public MusicResource? PlayResource { get; set; }
        public string Title { get; set; }

        public Uri CoverUri =>
            Ioc.Default.GetRequiredService<Setting>().noImage
                ? null
                : new Uri((string.IsNullOrEmpty(CoverLink)
                              ? "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg"
                              : CoverLink) +
                          "?param=" +
                          StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM);

        public int DisplayOrder => Order + 1;

        [RelayCommand]
        public async Task Play()
        {
            if (PlayResource is null) return;

            await Ioc.Default.GetRequiredService<IAppNavigator>().PlayAsync(PlayResource);
        }
    }
}
