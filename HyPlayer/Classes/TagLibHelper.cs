using System;
using TagLib;

namespace HyPlayer.Classes
{
    public static class TagLibHelper
    {
        public static File Create(File.IFileAbstraction abstraction, string extensions)
        {
            return extensions switch
            {
                ".flac" => new TagLib.Flac.File(abstraction),
                ".mp3" => new TagLib.Mpeg.AudioFile(abstraction),
                ".ape" => new TagLib.Ape.File(abstraction),
                ".m4a" => new TagLib.Mpeg4.File(abstraction),
                ".wav" => new TagLib.Riff.File(abstraction),
                _ => throw new ArgumentOutOfRangeException(nameof(extensions)),
            };
        }
    }
}
