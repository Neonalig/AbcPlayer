using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using AbcPlayer;

using Microsoft.Win32;

using static AbcPlayerApp.Associations;

namespace AbcPlayerApp {
    public partial class MainWindow {
        public static FileInfo LastLoadedFile;

        public MainWindow() {
            InitializeComponent();
            PlaybackEngine.OnPlayerStart += b => UpdateUI();
            PlaybackEngine.OnPlayerStop += (b, f) => UpdateUI();
            PlaybackEngine.OnThreadStart += (f, t) => UpdateUI();
            PlaybackEngine.OnPlayerStop += PlayerStop;
            PlaybackEngine.OnPlayerStart += PlayerStart;
            PlaybackEngine.OnThreadStop += t => UpdateUI();

            string[] args = Environment.GetCommandLineArgs();
            if (args != null && args.Length > 1) {
                try {
                    FileInfo playbackFile = new FileInfo(args[1]);
                    if (playbackFile.Exists) {
                        LastLoadedFile = playbackFile;
                        PlaybackEngine.Play(playbackFile);
                    }
                } catch (ArgumentException) { }
            }
        }

        #region Delegates

        void PlayerStart(BeepPlayer player) {
            player.OnUpdate += PlayerUpdate;
        }

        void PlayerStop(BeepPlayer player, bool finished) {
            player.OnUpdate -= PlayerUpdate;
        }

        void UpdateUI() {
            BeepPlayer player = PlaybackEngine.Player;
            bool playing = player != null && player.Playing;
            Dispatcher.Invoke(() => {
                Title = (player == null || string.IsNullOrEmpty(player.Title)) ? "ABC Player" : player.Title;
                ButtonPlay.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
                ButtonStop.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
                VolumeButtonIcon.Opacity = GetVolume() > 0 ? 1 : 0.2;
                VolumeSliderLabel.Content = GetVolume().ToString("P");

                if (playing) {
                    PlaybackEngine.Volume = GetSafeVolume();
                    StatusLabel.Content = player.Duration.ToString(@"h\:mm\:ss\:fff");
                }
            });
        }

        #endregion

        #region AbcPlayer Integration

        public float GetSafeVolume() => (float)(GetVolume() / 4);

        public double GetVolume() => (VolumeSlider.Value - VolumeSlider.Minimum) / VolumeSlider.Maximum;


        public static DirectoryInfo RestorePreviousLocation() {
            try {
                DirectoryInfo directory = new DirectoryInfo(Properties.Settings.Default.PreviousLocation);
                if (directory.Exists) {
                    return directory;
                }
            } catch (ArgumentException) { }
            return new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        }

        public static void BrowseAndPlay() {
            OpenFileDialog ofd = new OpenFileDialog {
                Filter = "ABC Notation File (*.abc)|*.abc|Any File (*.*)|*.*",
                CheckPathExists = true,
                CheckFileExists = true,
                RestoreDirectory = true,
                InitialDirectory = RestorePreviousLocation().FullName,
                Multiselect = false,
                ShowReadOnly = true
            };

            if (ofd.ShowDialog() == true) {
                FileInfo playbackFile = new FileInfo(ofd.FileName);
                if (playbackFile.Exists) {
                    Properties.Settings.Default.PreviousLocation = playbackFile.DirectoryName;
                    Properties.Settings.Default.Save();
                    LastLoadedFile = playbackFile;
                    PlaybackEngine.Play(playbackFile);
                }
            }
        }

        #endregion

        #region XAML Handlers

        void ButtonPlayBrowse_Click(object sender, RoutedEventArgs e) => BrowseAndPlay();

        void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (VolumeSliderLabel == null) { return; } //WPF Weirdness when starting up
            PlaybackEngine.Volume = GetSafeVolume();
            Dispatcher.Invoke(() => {
                double v = GetVolume();
                VolumeSliderLabel.Content = v.ToString("P");
                VolumeButtonIcon.Opacity = v <= 0 ? 0.2 : 1.0;
            });
        }

        void ButtonPlay_Click(object sender, RoutedEventArgs e) {
            BeepPlayer player = PlaybackEngine.Player;
            bool playing = player != null && player.Playing;
            switch (playing) {
                case true:
                    PlaybackEngine.Stop(); //If playing, stop
                    break;
                case false:
                    BrowseAndPlay(); //If not playing, start
                    break;
            }
        }

        void ButtonReset_Click(object sender, RoutedEventArgs e) {
            PlaybackEngine.Stop();
            this.Restart();
        }

        void PlaybackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (PlaybackSliderProgrammatically) { return; }
            BeepPlayer player = PlaybackEngine.Player;
            if (player != null && player.Playing) {
                UpdatingStatus = true;
                TimeSpan display = new TimeSpan((long)(player.PlaybackTime().Ticks * ((PlaybackSlider.Value - PlaybackSlider.Minimum) / PlaybackSlider.Maximum)));
                StatusLabel.Content = display;
            }
        }

        bool UpdatingStatus;
        void PlaybackSlider_DragStarted(object sender, DragStartedEventArgs e) {
            UpdatingStatus = true;
        }

        void PlaybackSlider_DragCompleted(object sender, DragCompletedEventArgs e) {
            UpdatingStatus = false;
            BeepPlayer player = PlaybackEngine.Player;
            if (player != null && player.Playing) {
                TimeSpan seekTime = new TimeSpan((long)(player.PlaybackTime().Ticks * ((PlaybackSlider.Value - PlaybackSlider.Minimum) / PlaybackSlider.Maximum)));
                player.Seek(seekTime);
            }
        }

        void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            PlaybackEngine.Stop();
        }

        void VolumeButton_Click(object sender, RoutedEventArgs e) {
            VolumeSlider.Value = GetVolume() > 0 ? VolumeSlider.Minimum : VolumeSlider.Maximum;
            UpdateUI();
        }

        void TextFolderButton_Click(object sender, RoutedEventArgs e) {
            if (LastLoadedFile != null) {
                if (Keyboard.IsKeyDown(Key.LeftShift)) {
                    try {
                        FileInfo assoc = new FileInfo(AssocQueryString(AssocStr.Executable, ".abc"));
                        FileInfo self = new FileInfo(Assembly.GetExecutingAssembly().Location);
                        if (assoc.Name == self.Name) {
                            _ = Process.Start("notepad.exe", $"\"{LastLoadedFile.FullName}\"");
                        } else {
                            Process.Start($"\"{LastLoadedFile.FullName}\"");
                        }
                    } catch {
                        Process.Start($"\"{LastLoadedFile.FullName}\"");
                    }
                } else {
                    _ = Process.Start("explorer.exe", $"/select,\"{LastLoadedFile.FullName}\"");
                }
            }
        }

        #endregion

        #region Status UI

        bool PlaybackSliderProgrammatically;
        void PlayerUpdate(BeepPlayer sender, TimeSpan elapsed) {
            if (!UpdatingStatus) {
                PlaybackSliderProgrammatically = true;
                Dispatcher.Invoke(() => {
                    PlaybackSlider.Value = elapsed.Ticks / (double)sender.Duration.Ticks * PlaybackSlider.Maximum + PlaybackSlider.Minimum;
                    StatusLabel.Content = elapsed.ToString(@"h\:mm\:ss\:fff");
                });
            }
        }

        #endregion
    }
}
