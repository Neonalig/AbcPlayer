using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NAudio.Wave;

using TextPlayer;

namespace AbcPlayer {
    #region Core Playback Functionality

    public static class PlaybackEngine {
        public static Thread PlaybackThread;
        public static BeepPlayer Player;

#pragma warning disable 0169
#pragma warning disable 0649
        public delegate void DlgOnThreadStart(FileInfo file, Thread thread);
        public delegate void DlgOnThreadStop(Thread thread);
        public static DlgOnThreadStart OnThreadStart;
        public static DlgOnThreadStop OnThreadStop;

        public delegate void DlgOnPlayerStart(BeepPlayer player);
        public delegate void DlgOnPlayerStop(BeepPlayer player, bool finished);
        public static DlgOnPlayerStart OnPlayerStart;
        public static DlgOnPlayerStop OnPlayerStop;
#pragma warning restore 0649
#pragma warning restore 0169

        /// <summary>
        /// Plays the specified .abc file in the asynchronous 'PlaybackThread'
        /// </summary>
        /// <param name="File"></param>
        public static void Play(FileInfo File) {
            Stop();
            void PThread() { PlayFile(File); }

            PlaybackThread = new Thread(PThread);
            PlaybackThread.Start();

            OnThreadStart?.Invoke(File, PlaybackThread);
        }

        /// <summary>
        /// Returns true if there is a BeatPlayer instantiated, and if it is actively playing a tune
        /// </summary>
        /// <returns></returns>
        public static bool IsPlaying() => Player != null && Player.Playing;

        /// <summary>
        /// Modifies the BeatPlayer's volume if it is instantiated
        /// </summary>
        public static float Volume {
            get => Player?.PlaybackVolume ?? -1;
            set {
                if (Player != null) {
                    Player.PlaybackVolume = value;
                }
            }
        }

        /// <summary>
        /// Stops the currently playing BeatPlayer if it is instantiated, along with any threads
        /// </summary>
        public static void Stop() {
            if (IsPlaying()) {
                Player.Stop();
                OnPlayerStop?.Invoke(Player, false);
            } else if (IsThreadPlaying()) {
                ThreadStop();
            }
        }

        /// <summary>
        /// Returns true if the PlaybackThread is instantiated and is currently alive
        /// </summary>
        /// <returns></returns>
        public static bool IsThreadPlaying() => PlaybackThread != null && PlaybackThread.IsAlive;

        /// <summary>
        /// Stops the PlaybackThread if it is instantiated
        /// </summary>
        public static void ThreadStop() {
            if (IsThreadPlaying()) {
                OnThreadStop?.Invoke(PlaybackThread);
                try { PlaybackThread.Abort(); } catch (ThreadAbortException) { }
                PlaybackThread = null;
            }
        }

        /// <summary>
        /// Invokes the OnPlayerStop delegate with the 'finished' status
        /// </summary>
        internal static void PlayFinish() => OnPlayerStop?.Invoke(Player, true);

        /// <summary>
        /// Plays the specified .abc file in the current thread synchronously. See the 'Play' function for monitored asynchronous playback
        /// </summary>
        /// <param name="file"></param>
        internal static void PlayFile(FileInfo file) {
            Player = new BeepPlayer();
            Player.FromFile(file.FullName);

            Player.Play();
            OnPlayerStart?.Invoke(Player);

            while (Player.Playing) {
                Player.Update();
                Thread.Sleep(1);
            }
        }
    }

    #endregion

    #region TextPlayer Integration
    public class BeepPlayer : EnhancedABCPlayer {
        /// <summary>
        /// The volume to play notes at (0.0 > 1.0)
        /// </summary>
        public float PlaybackVolume = 0.25f;

        /// <summary>
        /// The duration of the currently playing song
        /// </summary>
        /// <returns></returns>
        public TimeSpan PlaybackTime() => base.Duration;

        /// <summary>
        /// Called per BeepPlayer update
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="CurrentTime">The current time in the song</param>
        public delegate void DlgOnUpdate(BeepPlayer Sender, TimeSpan CurrentTime);
        public DlgOnUpdate OnUpdate;

        public override void Update() {
            base.Update();
            OnUpdate?.Invoke(this, base.Elapsed);
        }

        protected override void PlayNote(Note note, int channel, TimeSpan time) {
            //if (channel <= 1) {
            SineBeep.Play((int)note.GetFrequency(), (int)note.Length.TotalMilliseconds, PlaybackVolume);
            //}
        }
    }
    #endregion

    #region Audio/Beep Production (ft. markheath.net)
    //Below is a modified version of the project 'Playback of Sine Wave in NAudio' from markheath.net
    //https://markheath.net/post/playback-of-sine-wave-in-naudio

    internal class SineBeep {
        public static void Play(float frequency, int duration = 1, float volume = 0.25f) {
            //ThreadPool.QueueUserWorkItem(PooledPlayAsync, new object[] { frequency, duration, volume }); //.NET 1.1+
            Task.Run(() => PlayAsync(frequency, duration, volume)); //.NET 4.5+
        }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        public static void PooledPlayAsync(object arg) {
            object[] args = arg as object[];
            PlayAsync((float)args[0], (int)args[1], (float)args[2]);
        }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        public static async Task PlayAsync(float frequency, int duration = 1, float volume = 0.25f) {
            SineWaveProvider32 waveProvider = new SineWaveProvider32 {
                Frequency = frequency,
                Amplitude = volume,
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2) //48kHz, stereo
            };
            WaveOut wave = new WaveOut();
            wave.Init(waveProvider);
            wave.Play();
            await Task.Delay(duration).ConfigureAwait(false);
            wave.Stop();
            wave.Dispose();
        }
    }

    public abstract class WaveProvider32 : IWaveProvider {
        protected WaveProvider32()
            : this(44100, 1) {
        }

        protected WaveProvider32(int sampleRate, int channels) {
            SetWaveFormat(sampleRate, channels);
        }

        public void SetWaveFormat(int sampleRate, int channels) {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public int Read(byte[] buffer, int offset, int count) {
            WaveBuffer waveBuffer = new WaveBuffer(buffer);
            int samplesRequired = count / 4;
            int samplesRead = Read(waveBuffer.FloatBuffer, offset / 4, samplesRequired);
            return samplesRead * 4;
        }

        public abstract int Read(float[] buffer, int offset, int sampleCount);

        public WaveFormat WaveFormat { get; set; }
    }

    public class SineWaveProvider32 : WaveProvider32 {
        int sample;

        public SineWaveProvider32() {
            Frequency = 1000;
            Amplitude = 0.25f;
        }

        public float Frequency { get; set; }
        public float Amplitude { get; set; }

        public override int Read(float[] buffer, int offset, int sampleCount) {
            int sampleRate = WaveFormat.SampleRate;
            for (int n = 0; n < sampleCount; n++) {
                buffer[n + offset] = (float)(Amplitude * Math.Sin((2 * Math.PI * sample * Frequency) / sampleRate));
                sample++;
                if (sample >= sampleRate) {
                    sample = 0;
                }
            }
            return sampleCount;
        }
    }

    #endregion
}