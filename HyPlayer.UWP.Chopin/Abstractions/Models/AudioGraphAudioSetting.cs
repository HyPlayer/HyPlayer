using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using System;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Render;

namespace HyPlayer.UWP.Chopin.Abstractions.Models
{
    public class AudioGraphAudioSetting : IAudioSettings
    {
        public string DefaultDeviceId { get; set; }
        public double OutputVolume { get; set; } = 1d;
        private AudioGraphSettings _settings;
        public async Task<AudioGraphSettings> GetAudioGraphSettingsAsync()
        {
            if (_settings != null)
            {
                return _settings;
            }
            if (DefaultDeviceId == null)
            {
                var result = new AudioGraphSettings(AudioRenderCategory.Media);
                _settings = result;
                return result;
            }
            else
            {
                var device = await DeviceInformation.CreateFromIdAsync(DefaultDeviceId);
                var result = new AudioGraphSettings(AudioRenderCategory.Media)
                {
                    PrimaryRenderDevice = device,
                };
                _settings = result;
                return result;
            }
        }
    }
}
