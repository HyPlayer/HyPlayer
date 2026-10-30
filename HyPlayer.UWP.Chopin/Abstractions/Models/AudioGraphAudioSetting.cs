using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using System;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Devices;
using Windows.Media.Render;

namespace HyPlayer.UWP.Chopin.Abstractions.Models
{
    public class AudioGraphAudioSetting : IAudioSettings
    {
        public string DefaultDeviceId { get; set; } = string.Empty;
        public double OutputVolume { get; set; } = 1d;
        public bool AutoFallback { get; set; } = false;
        public bool EnableFFTProcessing { get; set; } = false;

        public string ResolvedDeviceId
        {
            get
            {
                if (!string.IsNullOrEmpty(DefaultDeviceId)) return DefaultDeviceId;
                try
                {
                    return MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default) ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }
        private AudioGraphSettings _settings;
        public async Task<AudioGraphSettings> GetAudioGraphSettingsAsync()
        {
            if (_settings != null)
            {
                return _settings;
            }
            if (string.IsNullOrEmpty(DefaultDeviceId))
            {
                var result = new AudioGraphSettings(AudioRenderCategory.Media);
                var defaultId = ResolvedDeviceId;
                if (!string.IsNullOrEmpty(defaultId))
                {
                    try
                    {
                        result.PrimaryRenderDevice = await DeviceInformation.CreateFromIdAsync(defaultId);
                    }
                    catch
                    {
                    }
                }
                _settings = result;
                return result;
            }
            else
            {
                var device = await DeviceInformation.CreateFromIdAsync(DefaultDeviceId);
                AudioGraphSettings result = null;
                if (device.IsEnabled || !AutoFallback)
                {
                    result = new AudioGraphSettings(AudioRenderCategory.Media)
                    {
                        PrimaryRenderDevice = device,
                    };
                }
                else
                {
                    result = new AudioGraphSettings(AudioRenderCategory.Media);
                }
                _settings = result;
                return result;
            }
        }
    }
}
