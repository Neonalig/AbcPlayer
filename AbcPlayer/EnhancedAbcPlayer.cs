using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using TextPlayer;
using TextPlayer.ABC;

namespace AbcPlayer {
    /// <summary>
    /// A modified version of the ABCPlayer class (provided by the TextPlayer library) with more programmatic accessibility. For use with the BeepPlayer class (found in PlaybackEngine.cs).
    /// </summary>
    public abstract class EnhancedABCPlayer : EnhancedMusicPlayer {
        int selectedTune = 1;
        readonly bool strict;
        string version;
        int versionMajor;
        int versionMinor;
        Dictionary<int, Tune> tunes;
        bool inTune;
        int tokenIndex;
        readonly int octave;
        TimeSpan nextNote;
        Dictionary<char, int> defaultAccidentals;
        readonly Dictionary<string, int> accidentals;
        readonly AccidentalPropagation accidentalPropagation;
        readonly Dictionary<string, int> tiedNotes;
        double noteLength;
        double meter;
        double spm;

        public static int DefaultOctave { get; set; } = 4;

        public static AccidentalPropagation DefaultAccidentalPropagation { get; set; } = AccidentalPropagation.Octave;

        protected EnhancedABCPlayer(bool strict = false, int? octave = null, AccidentalPropagation? accidentalProp = null) {
            AutoDetectLotro = true;
            Settings = new ABCSettings { MaxSize = 12288 };
            this.strict = strict;
            accidentals = new Dictionary<string, int>();
            tiedNotes = new Dictionary<string, int>();
            this.octave = octave ?? ABCPlayer.DefaultOctave;
            accidentalPropagation = accidentalProp ?? ABCPlayer.DefaultAccidentalPropagation;
        }

        void SetDefaultValues() {
            tokenIndex = 0;
            Title = "";
            meter = 1.0;
            Volume = 90.0 / sbyte.MaxValue;
            noteLength = 0.0;
            tiedNotes.Clear();
        }

        void SetHeaderValues(int index = 0, bool inferNoteLength = false) {
            if (tunes[index].Header.Information.TryGetValue('K', out List<string> stringList)) {
                GetKey(stringList[stringList.Count - 1]);
            }

            if (tunes[index].Header.Information.TryGetValue('M', out stringList)) {
                meter = GetNoteLength(stringList[stringList.Count - 1]);
            }

            if (tunes[index].Header.Information.TryGetValue('T', out stringList)) {
                Title = stringList.FirstOrDefault();
            }

            if (tunes[index].Header.Information.TryGetValue('L', out stringList)) {
                noteLength = GetNoteLength(stringList[stringList.Count - 1]);
            }

            if (inferNoteLength && noteLength == 0.0) {
                noteLength = meter >= 0.75 ? 0.125 : 1.0 / 16.0;
            }

            if (!tunes[index].Header.Information.TryGetValue('Q', out stringList)) {
                return;
            }

            SetTempo(stringList[stringList.Count - 1]);
        }

        void GetKey(string s) {
            defaultAccidentals = Keys.GetAccidentals(s);
        }

        void SetTempo(string s) {
            s = s.Trim();
            if ((!s.Contains("=") || s[0] == 'C') && strict) {
                throw new ABCStrictException("Error setting tempo, must be in the form of 'x/x = nnn' when using strict mode.");
            }

            double num = 0.0;
            Match match;
            if (!s.Contains("=")) {
                match = Regex.Match(s, "\\d+", RegexOptions.IgnoreCase);
                if (!match.Success) {
                    return;
                }

                num = 0.25;
            } else if (s[0] == 'C') {
                match = Regex.Match(s.Substring(s.IndexOf('=')), "\\d+", RegexOptions.IgnoreCase);
                if (!match.Success) {
                    return;
                }

                num = 0.25;
            } else {
                MatchCollection matchCollection1 = Regex.Matches(s, "\"[^\"]*\"", RegexOptions.IgnoreCase);
                for (int index = 0; index < matchCollection1.Count; ++index) {
                    s = s.Replace(matchCollection1[index].Value, "");
                }

                bool flag = false;
                MatchCollection matchCollection2 = Regex.Matches(s, "\\d+/\\d+", RegexOptions.IgnoreCase);
                for (int index = 0; index < matchCollection2.Count; ++index) {
                    num += GetNoteLength(matchCollection2[index].Value);
                    if (s.IndexOf(matchCollection2[index].Value, StringComparison.Ordinal) > s.IndexOf('=')) {
                        flag = true;
                    }
                }
                match = Regex.Match(!flag ? s.Substring(s.IndexOf('=')) : s.Substring(0, s.IndexOf('=')), "\\d+", RegexOptions.IgnoreCase);
                if (!match.Success) {
                    return;
                }
            }
            spm = 60.0 / Math.Min(Settings.MaxTempo * 0.25, Math.Max(Settings.MinTempo * 0.25, Convert.ToDouble(match.Value) * num));
        }

        double GetNoteLength(string s) {
            Match match = Regex.Match(s, "\\d+/\\d+", RegexOptions.IgnoreCase);
            if (!match.Success) {
                return -1.0;
            }

            string[] strArray = match.Value.Split('/');
            return Math.Min(Settings.LongestNote, Math.Max(Settings.ShortestNote, Convert.ToDouble(strArray[0]) / Convert.ToDouble(strArray[1])));
        }

        public override void Play(TimeSpan currentTime) {
            Play(currentTime, 1);
        }

        public virtual void Play(TimeSpan currentTime, int track) {
            if (tunes == null || tunes.Count < 2) {
                return;
            }

            selectedTune = track;
            if (selectedTune == 0) {
                selectedTune = 1;
            }

            base.Play(currentTime);
            SetDefaultValues();
            nextNote = lastTime;
            SetHeaderValues(0, false);
            SetHeaderValues(selectedTune, true);
            StartMeasure();
        }

        protected virtual void StartMeasure() {
            accidentals.Clear();
        }

        public override void Update(TimeSpan currentTime) {
            if (tokens != null) {
                while (currentTime >= nextNote && tokenIndex < tokens.Count) {
                    ReadNextNote();
                }

                if (currentTime >= nextNote && tokenIndex >= tokens.Count) {
                    Stop();
                }
            }
            base.Update(currentTime);
        }

        bool IsTiedNote(int _tokenIndex) => _tokenIndex + 1 < tokens.Count && tokens[_tokenIndex + 1][0] == '-';

        bool IsPlayableNote(string s) {
            switch (s[0]) {
                case '=':
                case '^':
                case '_':
                    Note note = GetNote(s);
                    return note.Type >= 'a' && note.Type <= 'g';
                case 'A':
                case 'B':
                case 'C':
                case 'D':
                case 'E':
                case 'F':
                case 'G':
                case 'a':
                case 'b':
                case 'c':
                case 'd':
                case 'e':
                case 'f':
                case 'g':
                    return true;
                default:
                    return false;
            }
        }

        void ReadNextNote() {
            bool flag1 = false;
            bool flag2 = false;
            List<ABCNote> abcNoteList = new List<ABCNote>();
            for (; !flag1 && tokenIndex < tokens.Count; ++tokenIndex) {
                string token = tokens[tokenIndex];
                char ch = token[0];
                if (ch == '[' && token == "[") {
                    ch = '!';
                }

                switch (ch) {
                    case '!':
                        flag2 = true;
                        abcNoteList.Clear();
                        break;
                    case '+':
                        GetDynamics(token);
                        break;
                    case ':':
                    case '[':
                    case '|':
                        if (ch == '[' && token.EndsWith("]") && token.Length > 2 && token[2] == ':' && token[1] != '|' && token[1] != ':') {
                            InlineInfo(token);
                            break;
                        }
                        StartMeasure();
                        break;
                    case '=':
                    case 'A':
                    case 'B':
                    case 'C':
                    case 'D':
                    case 'E':
                    case 'F':
                    case 'G':
                    case '^':
                    case '_':
                    case 'a':
                    case 'b':
                    case 'c':
                    case 'd':
                    case 'e':
                    case 'f':
                    case 'g':
                        Note note1 = GetNote(token);
                        if (!flag2) {
                            nextNote += note1.Length;
                            Note note2 = TieNote(new ABCNote(note1, tokenIndex));
                            if (note2.Type != 'r') {
                                ValidateAndPlayNote(note2, 0);
                            }

                            flag1 = true;
                            break;
                        }
                        abcNoteList.Add(new ABCNote(note1, tokenIndex));
                        break;
                    case 'Z':
                    case 'x':
                    case 'z':
                        Note rest = GetRest(token);
                        if (!flag2) {
                            nextNote += rest.Length;
                            flag1 = true;
                            break;
                        }
                        abcNoteList.Add(new ABCNote(rest, tokenIndex));
                        break;
                    case ']':
                        if (flag2) {
                            flag1 = true;
                            flag2 = false;
                            nextNote += GetChord(abcNoteList);
                            PlayChord(abcNoteList, nextNote);
                        }
                        break;
                }
            }
        }

        void InlineInfo(string s) {
            s = s.Substring(1, s.Length - 2).Trim();
            ABCInfo? nullable = ABCInfo.Parse(s);
            if (!nullable.HasValue) {
                return;
            }

            switch (nullable.Value.Identifier) {
                case 'Q':
                    SetTempo(nullable.Value.Text);
                    break;
                case 'L':
                    noteLength = GetNoteLength(nullable.Value.Text);
                    break;
                default: {
                    if (nullable.Value.Identifier != 'K') {
                        return;
                    }

                    GetKey(nullable.Value.Text);
                    break;
                }
            }
        }

        void PlayChord(List<ABCNote> notes, TimeSpan time) {
            List<Note> notes1 = new List<Note>(notes.Count);
            notes1.AddRange(notes.Select(TieNote).Where(note => note.Type != 'r'));
            PlayChord(notes1, time);
        }

        protected virtual void PlayChord(List<Note> notes, TimeSpan time) {
            for (int index = 0; index < notes.Count; ++index) {
                ValidateAndPlayNote(notes[index], index + 1);
            }
        }

        protected virtual Note TieNote(ABCNote note) {
            string key = note.BaseNote.Type.ToString(CultureInfo.InvariantCulture) + note.BaseNote.Octave.ToString(CultureInfo.InvariantCulture);
            if (tiedNotes.TryGetValue(key, out int num) && num > 0) {
                tiedNotes[key]--;
                note.BaseNote.Type = 'r';
                return note.BaseNote;
            }
            if (IsTiedNote(note.TokenIndex)) {
                for (int _tokenIndex = note.TokenIndex + 1; _tokenIndex < tokens.Count; ++_tokenIndex) {
                    if (IsPlayableNote(tokens[_tokenIndex])) {
                        Note note1 = GetNote(tokens[_tokenIndex]);
                        if (note1.Type == note.BaseNote.Type && note1.Octave == note.BaseNote.Octave) {
                            if (tiedNotes.ContainsKey(key)) {
                                tiedNotes[key]++;
                            } else {
                                tiedNotes[key] = 1;
                            }

                            note.BaseNote.Length += note1.Length;
                            if (!IsTiedNote(_tokenIndex)) {
                                break;
                            }
                        }
                    }
                }
            }
            return note.BaseNote;
        }

        protected virtual void ValidateAndPlayNote(Note note, int channel) {
            if (note.Octave < Settings.MinOctave) {
                note.Octave = Settings.MinOctave;
            } else if (note.Octave > Settings.MaxOctave) {
                note.Octave = Settings.MaxOctave;
            }

            note.Volume = Math.Max(0.0f, Math.Min(note.Volume, 1f));
            if (Muted) {
                return;
            }

            PlayNote(note, channel, nextNote);
        }

        Note GetRest(string s) {
            s = s.Trim();
            Note note = new Note { Type = 'r' };
            if (s[0] != 'Z') {
                note.Length = new TimeSpan((long)(spm * ModifyNoteLength(s) * 10000000.0));
            } else {
                Match match = Regex.Match(s, "\\d+");
                double num = 1.0;
                if (match.Success && match.Value.Length > 0) {
                    num = Convert.ToDouble(match.Value);
                }

                if (num <= 0.0) {
                    num = 1.0;
                }

                note.Length = new TimeSpan((long)(spm * num * 10000000.0));
            }
            return note;
        }

        Note GetNote(string s) {
            s = s.Trim();
            int? nullable = new int?();
            Match match1 = Regex.Match(s, "\\^+", RegexOptions.IgnoreCase);
            if (match1.Success) {
                nullable = match1.Value.Length;
            }

            Match match2 = Regex.Match(s, "_+", RegexOptions.IgnoreCase);
            if (match2.Success) {
                nullable = -match2.Value.Length;
            }

            if (Regex.Match(s, "=+", RegexOptions.IgnoreCase).Success) {
                nullable = 0;
            }

            int noteOctave = octave;
            foreach (char t in s) {
                switch (t) {
                    case ',':
                        --noteOctave;
                        break;
                    case '\'':
                        ++noteOctave;
                        break;
                }
            }
            if (LotroCompatible) {
                --noteOctave;
            }

            string str = Regex.Match(s, "[a-g]", RegexOptions.IgnoreCase).Value;
            if (str.ToLowerInvariant() == str) {
                ++noteOctave;
            }

            string upperInvariant = str.ToUpperInvariant();
            char key = str.ToUpperInvariant()[0];
            if (accidentalPropagation == AccidentalPropagation.Octave) {
                upperInvariant += noteOctave.ToString();
            }

            if (nullable.HasValue && accidentalPropagation != AccidentalPropagation.Not) {
                accidentals[upperInvariant] = nullable.Value;
            }

            int steps = 0;
            if (defaultAccidentals.ContainsKey(key)) {
                steps = defaultAccidentals[key];
            }

            if (accidentals.ContainsKey(upperInvariant)) {
                steps = accidentals[upperInvariant];
            }

            Note note = new Note {
                Type = str.ToLowerInvariant()[0],
                Octave = noteOctave,
                Volume = (float)Volume
            };
            Step(ref note, steps);
            if (note.Octave < Settings.MinOctave) {
                note.Octave = Settings.MinOctave;
            } else if (note.Octave > Settings.MaxOctave) {
                note.Octave = Settings.MaxOctave;
            }

            note.Length = new TimeSpan((long)(spm * ModifyNoteLength(s) * 10000000.0));
            return note;
        }

        double ModifyNoteLength(string s) {
            bool flag = false;
            string s1 = "";
            double num1 = 1.0;
            foreach (char t in s) {
                if (t >= '0' && t <= '9') {
                    s1 += t.ToString();
                } else if (t == '/') {
                    if (!flag && !s1.IsNullOrWhiteSpace()) {
                        num1 = Convert.ToDouble(s1);
                    } else if (flag && !s1.IsNullOrWhiteSpace()) {
                        num1 /= Convert.ToDouble(s1);
                    } else if (flag) {
                        num1 /= 2.0;
                    }

                    s1 = "";
                    flag = true;
                }
            }
            if (num1 == 0.0) {
                num1 = 1.0;
            }

            if (s1.IsEmpty() & flag) {
                s1 = "2";
            }

            if (!s1.IsEmpty()) {
                double num2 = Convert.ToDouble(s1);
                if (num2 > 0.0) {
                    if (flag) {
                        num1 /= num2;
                    } else {
                        num1 *= num2;
                    }
                } else {
                    num1 = 1.0;
                }
            }
            return noteLength * num1;
        }

        public override void Load(string str) {
            if (str.Length > validationSettings.MaxSize) {
                //throw new SongSizeException("Song exceeded maximum length of " + validationSettings.MaxSize);
                Debug.WriteLine("Warning >> Load(" + str.MergeLines().Truncate(32) + ")", "Song exceeded maximum length of " + validationSettings.MaxSize);
            }

            tunes = new Dictionary<int, Tune> { { 0, new Tune() } };
            using (StringReader stringReader = new StringReader(str)) {
                string rawLine = stringReader.ReadLine();
                if (rawLine == null) {
                    return;
                }

                if (!rawLine.StartsWith("%abc")) {
                    if (strict) {
                        throw new ABCStrictException("Error reading ABC notation, file didn't start with '%abc'.");
                    }
                } else {
                    if (rawLine.Length < 6 && strict) {
                        throw new ABCStrictException("Error reading ABC notation, file lacks version information.");
                    }

                    if (rawLine.Length >= 6) {
                        version = rawLine.Substring(5, rawLine.Length - 5);
                    }

                    if (version != null) {
                        string[] strArray = version.Split('.');
                        versionMajor = Convert.ToInt32(strArray[0]);
                        versionMinor = Convert.ToInt32(strArray[1]);
                        if ((versionMajor < 2 || versionMajor == 2 && versionMinor < 1) && strict) {
                            throw new ABCStrictException("Error reading ABC notation, strict mode does not allow for versions lower than 2.1, version was " + version + ".");
                        }
                    }
                }
                for (; rawLine != null; rawLine = stringReader.ReadLine()) {
                    if (rawLine != null) {
                        Interpret(rawLine);
                    }
                }
                ParseTune("");
            }
            foreach (KeyValuePair<int, Tune> tune in tunes.Where(tune => tune.Key > 0)) {
                selectedTune = tune.Key;
                if (tokens != null && tokens.Count > 0) {
                    CalculateDuration(tune.Value);
                }
            }
            selectedTune = 1;
            SetDefaultValues();
        }

        public List<Note> readNotes = new List<Note>();

        protected virtual void CalculateDuration(Tune tune) {
            SetDefaultValues();
            SetHeaderValues(0, false);
            SetHeaderValues(selectedTune, true);
            TimeSpan zero = TimeSpan.Zero;
            bool flag = false;
            List<ABCNote> chordNotes = new List<ABCNote>();
            readNotes = new List<Note>();
            while (tokenIndex < tokens.Count) {
                string token = tokens[tokenIndex];
                char ch = token[0];
                if (ch == '[' && token == "[") {
                    ch = '!';
                }

                switch (ch) {
                    case '!':
                        flag = true;
                        chordNotes.Clear();
                        break;
                    case ':':
                    case '[':
                    case '|':
                        if (ch == '[' && token.EndsWith("]") && token.Length > 2 && token[2] == ':' && token[1] != '|' && token[1] != ':') {
                            InlineInfo(token);
                        }
                        break;
                    case '=':
                    case 'A':
                    case 'B':
                    case 'C':
                    case 'D':
                    case 'E':
                    case 'F':
                    case 'G':
                    case '^':
                    case '_':
                    case 'a':
                    case 'b':
                    case 'c':
                    case 'd':
                    case 'e':
                    case 'f':
                    case 'g':
                        Note note = GetNote(token);
                        readNotes.Add(note);
                        if (!flag) {
                            zero += note.Length;
                            break;
                        }
                        chordNotes.Add(new ABCNote(note, tokenIndex));
                        break;
                    case 'Z':
                    case 'x':
                    case 'z':
                        Note rest = GetRest(token);
                        readNotes.Add(rest);
                        if (!flag) {
                            zero += rest.Length;
                            break;
                        }
                        chordNotes.Add(new ABCNote(rest, tokenIndex));
                        break;
                    case ']':
                        if (flag) {
                            flag = false;
                            TimeSpan chord = GetChord(chordNotes);
                            zero += chord;
                        }
                        break;
                }
                ++tokenIndex;
                if (zero > Settings.MaxDuration) {
                    //throw new SongDurationException("Song exceeded maximum duration " + Settings.MaxDuration);
                    Debug.WriteLine($"Warning >> ({zero} > {Settings.MaxDuration})", "Song exceeded maximum duration " + Settings.MaxDuration);
                }
            }
            tunes[selectedTune].Duration = zero;
        }

        void GetDynamics(string token) {
            if (token.Length <= 1) {
                return;
            }

            string str = token.Substring(1, token.Length - 2);
            switch (str) {
                case "ppp":
                case @"pppp":
                    Volume = 30.0 / sbyte.MaxValue;
                    break;
                case "pp":
                    Volume = 45.0 / sbyte.MaxValue;
                    break;
                case "p":
                    Volume = 60.0 / sbyte.MaxValue;
                    break;
                case "mp":
                    Volume = 75.0 / sbyte.MaxValue;
                    break;
                case "mf":
                    Volume = 90.0 / sbyte.MaxValue;
                    break;
                case "f":
                    Volume = 105.0 / sbyte.MaxValue;
                    break;
                case "ff":
                    Volume = 120.0 / sbyte.MaxValue;
                    break;
                default: {
                    if (str != "fff" && str != @"ffff") {
                        return;
                    }

                    Volume = 1.0;
                    break;
                }
            }
        }

        TimeSpan GetChord(List<ABCNote> chordNotes) {
            if (chordNotes.Count <= 0) {
                return TimeSpan.Zero;
            }

            TimeSpan timeSpan = TimeSpan.MaxValue;
            for (int index = chordNotes.Count - 1; index >= 0; --index) {
                ABCNote chordNote = chordNotes[index];
                timeSpan = new TimeSpan((long)(Math.Min(timeSpan.TotalSeconds, chordNote.BaseNote.Length.TotalSeconds) * 10000000.0));
                if (chordNote.BaseNote.Type == 'r') {
                    chordNotes.RemoveAt(index);
                }
            }
            if (timeSpan == TimeSpan.MaxValue) {
                timeSpan = TimeSpan.Zero;
            }

            if (chordNotes.Count > Settings.MaxChordNotes) {
                chordNotes.RemoveRange(Settings.MaxChordNotes, chordNotes.Count - Settings.MaxChordNotes);
            }

            return timeSpan;
        }

        void Interpret(string rawLine) {
            if (AutoDetectLotro && LotroAutoDetect.IsLotroMarker(rawLine)) {
                LotroCompatible = true;
            }

            string str = rawLine.Split('%')[0].Trim();
            if (!inTune) {
                ParseHeader(str);
            } else {
                if (str.IsNullOrWhiteSpace() && rawLine != str || !strict && str.IsNullOrWhiteSpace() && (tunes[tunes.Count - 1].RawCode == null || tunes[tunes.Count - 1].RawCode.Length == 0)) {
                    return;
                }

                ParseTune(str);
            }
        }

        void ParseHeader(string line) {
            Tune tune = tunes[tunes.Count - 1];
            ABCInfo? nullable = ABCInfo.Parse(line);
            if (!nullable.HasValue) {
                return;
            }

            ABCInfo abcInfo = nullable.Value;
            if (abcInfo.Identifier == 'T' && strict && (tune.Header.Information.Count != 1 || tune.Header.Information.Count <= 0 || !tune.Header.Information.ContainsKey('X'))) {
                throw new ABCStrictException("Error reading ABC notation, 'T:title' information field is only allowed after 'X:number' field in strict mode.");
            }

            if (abcInfo.Identifier == 'X') {
                tune = new Tune();
                tunes.Add(tunes.Count, tune);
            } else if (abcInfo.Identifier == 'K') {
                inTune = true;
            }

            tune.Header.AddInfo(nullable.Value);
        }

        void ParseTune(string line) {
            Tune tune = tunes[tunes.Count - 1];
            if (tune.RawCode == null) {
                tune.RawCode = new StringBuilder(1024);
            }

            if (!line.IsNullOrWhiteSpace()) {
                switch (line.Trim()[0]) {
                    case 'I':
                        break;
                    case 'K':
                    case 'L':
                    case 'Q':
                        tune.RawCode.Append("[").Append(line.Trim()).Append("]");
                        break;
                    case 'M':
                        break;
                    case 'N':
                        break;
                    case 'O':
                        break;
                    case 'P':
                        break;
                    case 'R':
                        break;
                    case 'T':
                        break;
                    case 'U':
                        break;
                    case 'V':
                        break;
                    case 'W':
                        break;
                    case 'm':
                        break;
                    case 'r':
                        break;
                    case 's':
                        break;
                    case 'w':
                        break;
                    default:
                        tune.RawCode.Append(line);
                        break;
                }
            } else {
                StringBuilder code = new StringBuilder(1024);
                List<char> charList = new List<char> {
          '\\',
          '\n',
          '\r',
          '\t'
        };
                if (tune.RawCode.Length == 0) {
                    tune.Tokens = new List<string>();
                } else {
                    for (int index = 0; index < tune.RawCode.Length; ++index) {
                        if (!charList.Contains(tune.RawCode[index])) {
                            code.Append(tune.RawCode[index]);
                        }
                    }
                    tune.Tokens = Tokenize(code);
                }
            }
        }

        static List<string> Tokenize(StringBuilder code) {
            List<char> charList1 = new List<char> {
        '|',
        ':',
        '[',
        '{',
        ']',
        '}',
        'z',
        'x',
        'Z',
        'A',
        'B',
        'C',
        'D',
        'E',
        'F',
        'G',
        'a',
        'b',
        'c',
        'd',
        'e',
        'f',
        'g',
        '_',
        '=',
        '^',
        '<',
        '>',
        '(',
        ' ',
        '-',
        '"',
        '+'
      };
            List<char> charList2 = new List<char> {
        'A',
        'B',
        'C',
        'D',
        'E',
        'F',
        'G',
        'a',
        'b',
        'c',
        'd',
        'e',
        'f',
        'g'
      };
            List<char> charList3 = new List<char> {
        '|',
        ':',
        '[',
        ']',
        '0',
        '1',
        '2',
        '3',
        '4',
        '5',
        '6',
        '7',
        '8',
        '9'
      };
            List<char> charList4 = new List<char> {
        '(',
        ':',
        '0',
        '1',
        '2',
        '3',
        '4',
        '5',
        '6',
        '7',
        '8',
        '9'
      };
            List<char> charList5 = new List<char> {
        'I',
        'K',
        'L',
        'M',
        'm',
        'N',
        'P',
        'Q',
        'R',
        'r',
        's',
        'T',
        'U',
        'V',
        'W',
        'w'
      };
            List<string> stringList1 = new List<string>();
            StringBuilder stringBuilder1 = new StringBuilder(code.Length);
            for (int index = 0; index < code.Length; ++index) {
                if (charList1.Contains(code[index])) {
                    if (stringBuilder1.Length > 0) {
                        stringList1.Add(stringBuilder1.ToString());
                    }

                    stringBuilder1.Length = 0;
                }
                stringBuilder1.Append(code[index]);
            }
            if (stringBuilder1.Length > 0) {
                stringList1.Add(stringBuilder1.ToString());
            }

            List<string> stringList2 = new List<string>();
            StringBuilder stringBuilder2 = new StringBuilder(10);
            for (int index1 = 0; index1 < stringList1.Count; ++index1) {
                stringBuilder2.Length = 0;
                switch (stringList1[index1][0]) {
                    case '^': {
                        while (stringList1[index1][0] == '^' || charList2.Contains(stringList1[index1][0])) {
                            stringBuilder2.Append(stringList1[index1]);
                            if (!charList2.Contains(stringList1[index1][0])) {
                                ++index1;
                                if (index1 >= stringList1.Count) {
                                    break;
                                }
                            } else {
                                break;
                            }
                        }

                        break;
                    }
                    case '+': {
                        stringBuilder2.Append(stringList1[index1]);
                        ++index1;
                        while (index1 < stringList1.Count) {
                            stringBuilder2.Append(stringList1[index1]);
                            if (stringList1[index1][0] != '+') {
                                ++index1;
                                if (index1 >= stringList1.Count) {
                                    break;
                                }
                            } else {
                                break;
                            }
                        }

                        break;
                    }
                    case '_': {
                        while (stringList1[index1][0] == '_' || charList2.Contains(stringList1[index1][0])) {
                            stringBuilder2.Append(stringList1[index1]);
                            if (!charList2.Contains(stringList1[index1][0])) {
                                ++index1;
                                if (index1 >= stringList1.Count) {
                                    break;
                                }
                            } else {
                                break;
                            }
                        }

                        break;
                    }
                    case '=': {
                        stringBuilder2.Length = 0;
                        stringBuilder2.Append("=");
                        while (stringList1[index1][0] == '=' || charList2.Contains(stringList1[index1][0])) {
                            if (charList2.Contains(stringList1[index1][0])) {
                                stringBuilder2.Append(stringList1[index1]);
                                break;
                            }
                            ++index1;
                            if (index1 >= stringList1.Count) {
                                break;
                            }
                        }

                        break;
                    }
                    case '[' when (stringList1[index1].Length > 1 && charList5.Contains(stringList1[index1][1]) || index1 < stringList1.Count - 1 && charList5.Contains(stringList1[index1 + 1][0])): {
                        char? nullable = new char?();
                        if (stringList1[index1].Length > 1) {
                            nullable = stringList1[index1][1];
                        } else if (index1 < stringList1.Count - 1) {
                            nullable = stringList1[index1 + 1][0];
                        }

                        if (nullable.HasValue && charList5.Contains(nullable.Value)) {
                            stringBuilder2.Length = 0;
                            stringBuilder2.Append(stringList1[index1]);
                            int index2 = index1 + 1;
                            while (stringBuilder2[stringBuilder2.Length - 1] != ']') {
                                stringBuilder2.Append(stringList1[index2]);
                                ++index2;
                                if (index2 >= stringList1.Count) {
                                    break;
                                }
                            }
                            index1 = index2 - 1;
                        }

                        break;
                    }
                    default: {
                        if (stringList1[index1][0] == '[' && index1 < stringList1.Count - 1 && charList3.Contains(stringList1[index1 + 1][0]) && stringList1[index1 + 1][0] != ']' || stringList1[index1][0] == '|' || stringList1[index1][0] == ':' || stringList1[index1][0] == '0' || stringList1[index1][0] == '1' || stringList1[index1][0] == '2' || stringList1[index1][0] == '3' || stringList1[index1][0] == '4' || stringList1[index1][0] == '5' || stringList1[index1][0] == '6' || stringList1[index1][0] == '7' || stringList1[index1][0] == '8' || stringList1[index1][0] == '9') {
                            while (charList3.Contains(stringList1[index1][0]) && (index1 <= 0 || stringList1[index1][0] != '[' || stringList1[index1 - 1][0] != '|')) {
                                stringBuilder2.Append(stringList1[index1]);
                                ++index1;
                                if (index1 >= stringList1.Count) {
                                    break;
                                }
                            }
                            --index1;
                        } else {
                            switch (stringList1[index1][0]) {
                                case '(': {
                                    while (charList4.Contains(stringList1[index1][0])) {
                                        stringBuilder2.Append(stringList1[index1]);
                                        ++index1;
                                        if (index1 >= stringList1.Count) {
                                            break;
                                        }
                                    }
                                    --index1;
                                    break;
                                }
                                case '"': {
                                    ++index1;
                                    while (stringList1[index1][0] != '"') {
                                        ++index1;
                                        if (index1 >= stringList1.Count) {
                                            break;
                                        }
                                    }

                                    break;
                                }
                                default:
                                    stringBuilder2.Length = 0;
                                    stringBuilder2.Append(stringList1[index1]);
                                    break;
                            }
                        }

                        break;
                    }
                }

                if (stringBuilder2.Length > 0) {
                    stringList2.Add(stringBuilder2.ToString());
                }
            }
            return stringList2;
        }

        public ABCSettings Settings { get; set; }

        public double Volume { get; set; }

        public override TimeSpan Duration => duration;

        List<string> tokens => tunes[selectedTune].Tokens;

        TimeSpan duration => tunes[selectedTune].Duration;

        internal override ValidationSettings validationSettings => Settings;

        public bool AutoDetectLotro { get; set; }

        public bool LotroCompatible { get; set; }

        public string Title { get; private set; }
    }
}