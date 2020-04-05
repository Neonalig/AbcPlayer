using System;
using System.Diagnostics;
using System.IO;
using System.Text;

using TextPlayer;

namespace AbcPlayer {
    /// <summary>
    /// A modified version of the MusicPlayer class (provided by the TextPlayer library) with more programmatic accessibility. For use with the EnhancedAbcPlayer class.
    /// </summary>
    public abstract class EnhancedMusicPlayer : IEnhancedMusicPlayer {
        static readonly object timeLock = new object();
        public static readonly Stopwatch time = new Stopwatch();
        protected TimeSpan lastTime;
        protected TimeSpan startTime;
        bool muted;

        static EnhancedMusicPlayer() {
            time.Start();
        }

        protected EnhancedMusicPlayer(ValidationSettings validationSettings, TimeSpan duration) {
            this.validationSettings = validationSettings;
            Duration = duration;
        }

        protected EnhancedMusicPlayer() { }

        public static TimeSpan Time {
            get {
                lock (timeLock) {
                    return time.Elapsed;
                }
            }
        }

        public void FromFile(string file) {
            using (StreamReader stream = new StreamReader(file)) {
                Load(stream);
            }
        }

        public abstract void Load(string str);

        public void Load(StreamReader stream) {
            StringBuilder stringBuilder = new StringBuilder();
            char[] buffer = new char[1024];
            while (!stream.EndOfStream) {
                int charCount = stream.ReadBlock(buffer, 0, buffer.Length);
                if (stringBuilder.Length + charCount > validationSettings.MaxSize) {
                    //throw new SongSizeException("Song exceeded maximum length of " + validationSettings.MaxSize);
                    Debug.WriteLine("Warning >> Load(" + stream.ToString().Truncate(32) + ")", "Song exceeded maximum length of " + validationSettings.MaxSize);
                }

                stringBuilder.Append(buffer, 0, charCount);
            }
            Load(stringBuilder.ToString());
        }

        public virtual void Play() {
            Play(MusicPlayer.Time);
        }

        public virtual void Play(TimeSpan currentTime) {
            if (Playing) {
                throw new InvalidOperationException(GetType() + " was already playing.");
            }

            Playing = true;
            lastTime = currentTime;
            startTime = currentTime;
        }

        public virtual void Stop() {
            Playing = false;
            startTime = TimeSpan.Zero;
            lastTime = TimeSpan.Zero;
        }

        public virtual void Update() {
            Update(MusicPlayer.Time);
        }

        public virtual void Update(TimeSpan currentTime) {
            if (!Playing) {
                return;
            }

            lastTime = currentTime;
        }

        public virtual void Seek(TimeSpan position) {
            Seek(MusicPlayer.Time, position);
        }

        public virtual void Seek(TimeSpan currentTime, TimeSpan position) {
            int num = Muted ? 1 : 0;
            Stop();
            Mute();
            Play(currentTime - position);
            Update(currentTime);
            if (num != 0) {
                return;
            }

            Unmute();
        }

        protected abstract void PlayNote(Note note, int channel, TimeSpan time);

        protected static void Step(ref Note note, int steps) {
            if (steps == 0) {
                return;
            }

            if (steps > 0) {
                for (int index = 0; index < steps; ++index) {
                    switch (note.Type) {
                        case 'a':
                            if (!note.Sharp) {
                                note.Sharp = true;
                                break;
                            }
                            note.Type = 'b';
                            note.Sharp = false;
                            break;
                        case 'b':
                            note.Type = 'c';
                            ++note.Octave;
                            break;
                        case 'c':
                            if (!note.Sharp) {
                                note.Sharp = true;
                                break;
                            }
                            note.Type = 'd';
                            note.Sharp = false;
                            break;
                        case 'd':
                            if (!note.Sharp) {
                                note.Sharp = true;
                                break;
                            }
                            note.Type = 'e';
                            note.Sharp = false;
                            break;
                        case 'e':
                            note.Type = 'f';
                            break;
                        case 'f':
                            if (!note.Sharp) {
                                note.Sharp = true;
                                break;
                            }
                            note.Type = 'g';
                            note.Sharp = false;
                            break;
                        case 'g':
                            if (!note.Sharp) {
                                note.Sharp = true;
                                break;
                            }
                            note.Type = 'a';
                            note.Sharp = false;
                            break;
                    }
                }
            } else {
                for (int index = 0; index < Math.Abs(steps); ++index) {
                    switch (note.Type) {
                        case 'a':
                            if (note.Sharp) {
                                note.Sharp = false;
                                break;
                            }
                            note.Type = 'g';
                            note.Sharp = true;
                            break;
                        case 'b':
                            note.Type = 'a';
                            note.Sharp = true;
                            break;
                        case 'c':
                            if (note.Sharp) {
                                note.Sharp = false;
                                break;
                            }
                            note.Type = 'b';
                            --note.Octave;
                            break;
                        case 'd':
                            if (note.Sharp) {
                                note.Sharp = false;
                                break;
                            }
                            note.Type = 'c';
                            note.Sharp = true;
                            break;
                        case 'e':
                            note.Type = 'd';
                            note.Sharp = true;
                            break;
                        case 'f':
                            if (note.Sharp) {
                                note.Sharp = false;
                                break;
                            }
                            note.Type = 'e';
                            break;
                        case 'g':
                            if (note.Sharp) {
                                note.Sharp = false;
                                break;
                            }
                            note.Type = 'f';
                            note.Sharp = true;
                            break;
                    }
                }
            }
        }

        public virtual void Mute() {
            muted = true;
        }

        public virtual void Unmute() {
            muted = false;
        }

        public bool Playing { get; set; }

        public bool Muted {
            get => muted;
            set {
                if (muted == value) {
                    return;
                }

                if (value) {
                    Mute();
                } else {
                    Unmute();
                }
            }
        }

        internal virtual ValidationSettings validationSettings { get; }

        public virtual TimeSpan Duration { get; }

        public virtual TimeSpan Elapsed => lastTime - startTime;
    }
}