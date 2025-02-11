﻿using System;
using System.IO;

namespace AbcPlayer {
    /// <summary>
    /// A modified version of the IMusicPlayer interface (provided by the TextPlayer  library) with more programmatic accessibility. For use with the EnhancedMusicPlayer class.
    /// </summary>
    internal interface IEnhancedMusicPlayer {
        void FromFile(string file);

        void Load(string str);
        void Load(StreamReader stream);

        void Play();
        void Play(TimeSpan currentTime);

        void Stop();

        void Update();
        void Update(TimeSpan currentTime);

        void Seek(TimeSpan position);
        void Seek(TimeSpan currentTime, TimeSpan position);

        void Mute();
        void Unmute();

        bool Playing { get; }
        bool Muted { get; }

        TimeSpan Duration { get; }
        TimeSpan Elapsed { get; }
    }
}