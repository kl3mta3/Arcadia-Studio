using System.IO;
using NAudio.Wave;
using Wysicraft.Core.Audio;
namespace Wysicraft.Designer;

/// <summary>Saves sound to a file on disk: MP3 (with the encoder built into Windows), WAV or Ogg Vorbis.</summary>
static class AudioExport
{
    public static void Save(string file, float[][] channels, int sampleRate)
    {
        switch (Path.GetExtension(file).ToLowerInvariant())
        {
            case ".wav": File.WriteAllBytes(file, AudioFiles.Wav(channels, sampleRate)); return;
            case ".ogg": File.WriteAllBytes(file, AudioFiles.Ogg(channels, sampleRate)); return;
            case ".mp3":
                using (var wav = new WaveFileReader(new MemoryStream(AudioFiles.Wav(channels, sampleRate))))
                {
                    try { MediaFoundationEncoder.EncodeToMp3(wav, file, 192000); }
                    catch (Exception ex) when (ex is not IOException) { throw new InvalidOperationException("Windows couldn't make an MP3 here (" + ex.Message + "). Export as WAV or Ogg instead."); }
                }
                return;
            default: throw new InvalidOperationException("Export as .mp3, .wav or .ogg.");
        }
    }
}
