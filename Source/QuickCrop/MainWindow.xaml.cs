using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace QuickCrop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // The part of the rectangle the mouse is over.
        private enum HitType
        {
            None, Body, TopLeft, TopRight, BottomLeft, BottomRight, Left, Right, Top, Bottom, ScrollWE
        };

        // The part of the rectangle under the mouse.
        HitType MouseHitType = HitType.None;

        // True if a drag is in progress.
        private bool CropDragInProgress = false;

        private bool TrimDragInProgress = false;

        //Remembers whether the video was playing when scrubbing starts (slider on mouse down preview).
        bool ffmpegWasPlayingOnScrubStart = false;

        // The drag's last point.
        private Point LastPoint;

        DispatcherTimer videoPositionUpdateTimer;

        bool paused = false;

        string filePath;

        int x = 100, y = 100, width = 100, height = 100;

        bool showGuidelines = true;

        string ffmpegDir;

        bool muted = true;
        double unmutedVolume = 0.5d;
        private const double MIN_UNMUTED_VOLUME = 0.1d;
        const string PLAY_ICON = "▶",
            PAUSE_ICON = "⏸",
            MUTE_ICON = "🔇",
            UNMUTE_ICON = "🔊";

        public MainWindow()
        {
            InitializeComponent();

            ffmpegPlayer.Volume = 0;

            videoPositionUpdateTimer = new DispatcherTimer();
            videoPositionUpdateTimer.Interval = TimeSpan.FromSeconds(0.1);
            videoPositionUpdateTimer.Tick += VideoPositionUpdate;
            videoPositionUpdateTimer.Start();

            if (Directory.Exists(Directory.GetCurrentDirectory() + "\\ffmpeg"))
            {
                Console.WriteLine("FFmpeg directory found in application folder.");

                mainGrid.Children.Remove(ffmpegAlertGrid);
                ffmpegAlertGrid.Visibility = Visibility.Hidden;
                ffmpegDir = Directory.GetCurrentDirectory() + "\\ffmpeg";
                Unosquare.FFME.Library.FFmpegDirectory = ffmpegDir;
            }
            else
            { 
                string path = Properties.Settings.Default.ffmpegPath;

                if (!String.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    var files = Directory.GetFiles(Properties.Settings.Default.ffmpegPath).Select(t => Path.GetFileName(t));

                    if (files.Contains("ffmpeg.exe"))
                    {
                        mainGrid.Children.Remove(ffmpegAlertGrid);
                        ffmpegAlertGrid.Visibility = Visibility.Hidden;
                        ffmpegDir = path;
                        Unosquare.FFME.Library.FFmpegDirectory = path;
                    }
                }
            }
        }

        private void VideoPositionUpdate(object sender, EventArgs e)
        {
            if (ffmpegPlayer.NaturalDuration.HasValue)
            {
                videoPositionSlider.ValueChanged -= VideoPositionSlider_ValueChanged;
                videoPositionSlider.Value = ffmpegPlayer.Position.TotalSeconds;
                videoPositionSlider.ValueChanged += VideoPositionSlider_ValueChanged;
            }
        }

        // Return a HitType value to indicate what is at the point.
        private HitType SetHitType(Rectangle rect, Point point)
        {
            double left = Canvas.GetLeft(rect);
            double top = Canvas.GetTop(rect);
            double right = left + rect.Width;
            double bottom = top + rect.Height;

            //A small constant allowing for give/take of grabbing handles from 10 less/more pixels away.
            const double GAP = 10;

            if (point.X < left - GAP)
                return HitType.None;
            if (point.X > right + GAP)
                return HitType.None;
            if (point.Y < top - GAP)
                return HitType.None;
            if (point.Y > bottom + GAP)
                return HitType.None;

            if (point.X - left < GAP)
            {
                // Left edge.
                if (point.Y - top < GAP) 
                    return HitType.TopLeft;
                if (bottom - point.Y < GAP) 
                    return HitType.BottomLeft;
                return HitType.Left;
            }
            if (right - point.X < GAP)
            {
                // Right edge.
                if (point.Y - top < GAP) 
                    return HitType.TopRight;
                if (bottom - point.Y < GAP) 
                    return HitType.BottomRight;
                return HitType.Right;
            }

            if (point.Y - top < GAP) 
                return HitType.Top;
            if (bottom - point.Y < GAP) 
                return HitType.Bottom;
            
            return HitType.Body;
        }

        // Set a mouse cursor appropriate for the current hit type.
        private void SetMouseCursor()
        {
            // See what cursor we should display.
            Cursor desired_cursor = Cursors.Arrow;

            if (ffmpegAlertGrid.Visibility != Visibility.Visible)
            { 
                switch (MouseHitType)
                {
                    case HitType.None:
                        desired_cursor = Cursors.Arrow;
                        break;
                    case HitType.Body:
                        desired_cursor = Cursors.ScrollAll;
                        break;
                    case HitType.ScrollWE:
                        desired_cursor = Cursors.ScrollWE;
                        break;
                    case HitType.TopLeft:
                    case HitType.BottomRight:
                        desired_cursor = Cursors.SizeNWSE;
                        break;
                    case HitType.BottomLeft:
                    case HitType.TopRight:
                        desired_cursor = Cursors.SizeNESW;
                        break;
                    case HitType.Top:
                    case HitType.Bottom:
                        desired_cursor = Cursors.SizeNS;
                        break;
                    case HitType.Left:
                    case HitType.Right:
                        desired_cursor = Cursors.SizeWE;
                        break;
                }
            }
            
            // Display the desired cursor.
            if (Cursor != desired_cursor) 
                Cursor = desired_cursor;
        }

        // Start dragging.
        private void canvas1_MouseDown(object sender, MouseButtonEventArgs e)
        {
            MouseHitType = SetHitType(dragRectangle, Mouse.GetPosition(canvas1));
            SetMouseCursor();

            if (MouseHitType == HitType.None)
                return;

            if (e.RightButton == MouseButtonState.Pressed || e.ClickCount == 2)
            {
                ExpandCropArea(MouseHitType);
            }
            else
            { 
                LastPoint = Mouse.GetPosition(canvas1);
                CropDragInProgress = true;
            }
        }

        // If a drag is in progress, continue the drag.
        // Otherwise display the correct cursor.
        private void canvas1_MouseMove(object sender, MouseEventArgs e)
        {
            Console.WriteLine("canvas1_MouseMove");

            if (!CropDragInProgress)
            {
                MouseHitType = SetHitType(dragRectangle, Mouse.GetPosition(canvas1));
                SetMouseCursor();
            }
            else
            {
                // See how much the mouse has moved.
                Point point = Mouse.GetPosition(canvas1);
                double offset_x = point.X - LastPoint.X;
                double offset_y = point.Y - LastPoint.Y;

                // Get the rectangle's current position.
                double new_x = Canvas.GetLeft(dragRectangle);
                double new_y = Canvas.GetTop(dragRectangle);
                double new_width = dragRectangle.Width;
                double new_height = dragRectangle.Height;

                // Update the rectangle.
                switch (MouseHitType)
                {
                    case HitType.Body:
                        new_x += offset_x;
                        new_y += offset_y;
                        break;
                    case HitType.TopLeft:
                        new_x += offset_x;
                        new_y += offset_y;
                        new_width -= offset_x;
                        new_height -= offset_y;
                        break;
                    case HitType.TopRight:
                        new_y += offset_y;
                        new_width += offset_x;
                        new_height -= offset_y;
                        break;
                    case HitType.BottomRight:
                        new_width += offset_x;
                        new_height += offset_y;
                        break;
                    case HitType.BottomLeft:
                        new_x += offset_x;
                        new_width -= offset_x;
                        new_height += offset_y;
                        break;
                    case HitType.Left:
                        new_x += offset_x;
                        new_width -= offset_x;
                        break;
                    case HitType.Right:
                        new_width += offset_x;
                        break;
                    case HitType.Bottom:
                        new_height += offset_y;
                        break;
                    case HitType.Top:
                        new_y += offset_y;
                        new_height -= offset_y;
                        break;
                }

                // Clamp square to be within video limits
                new_width = Math.Clamp(new_width, 10, ffmpegPlayer.ActualWidth);
                new_height = Math.Clamp(new_height, 10, ffmpegPlayer.ActualHeight);

                new_x = Math.Clamp(new_x, 0, ffmpegPlayer.ActualWidth - new_width);
                new_y = Math.Clamp(new_y, 0, ffmpegPlayer.ActualHeight - new_height);

                SetCropArea(new_x, new_y, new_width, new_height);

                // Save the mouse's new location.
                LastPoint = point;
            }
        }

        private void ExpandCropArea(HitType mouseHitType)
        {
            if (mouseHitType == HitType.Left)
                SetCropArea(0, y, x + width, height);
            else if (mouseHitType == HitType.Right)
                SetCropArea(x, y, ffmpegPlayer.ActualWidth - x, height);
            else if (mouseHitType == HitType.Top)
                SetCropArea(x, 0, width, y + height);
            else if (mouseHitType == HitType.Bottom)
                SetCropArea(x, y, width, ffmpegPlayer.ActualHeight - y);
            else if (mouseHitType == HitType.TopLeft)
                SetCropArea(0, 0, x + width, y + height);
            else if (mouseHitType == HitType.TopRight)
                SetCropArea(x, 0, ffmpegPlayer.ActualWidth - x, y + height);
            else if (mouseHitType == HitType.BottomLeft)
                SetCropArea(0, y, x + width, ffmpegPlayer.ActualHeight - y);
            else if (mouseHitType == HitType.BottomRight)
                SetCropArea(x, y, ffmpegPlayer.ActualWidth - x, ffmpegPlayer.ActualHeight - y);
        }

        private void SetCropArea(double new_x, double new_y, double new_width, double new_height)
        {
            x = (int)new_x;
            y = (int)new_y;
            width = (int)new_width;
            height = (int)new_height;

            // Update the rectangle.
            Canvas.SetLeft(dragRectangle, new_x);
            Canvas.SetTop(dragRectangle, new_y);

            dragRectangle.Width = new_width;
            dragRectangle.Height = new_height;

            rectangleDarken.Rect = new Rect(0, 0, darkenPath.ActualWidth, darkenPath.ActualHeight);

            /// Okay, we are adding and subtracting 1s because apparently if rectangleGeometryExclude reaches the same width 
            /// and height of the path it's in then the paths's width and height to 0 somehow, and we can't fix it. This doesnt show any 
            /// visual glitches because the dragRectangle's stroke covers it up, so let's just leave it...
            rectangleGeometryExclude.Rect = new Rect(new_x + 1, new_y + 1, new_width - 1, new_height - 1);

            verticalLineLeft.X1 = new_x + new_width / 3;
            verticalLineLeft.X2 = new_x + new_width / 3; 
            verticalLineLeft.Y1 = new_y;
            verticalLineLeft.Y2 = new_y + new_height;

            verticalLineRight.X1 = new_x + (new_width / 3) * 2;
            verticalLineRight.X2 = new_x + (new_width / 3) * 2;
            verticalLineRight.Y1 = new_y;
            verticalLineRight.Y2 = new_y + new_height;

            horizontalLineTop.X1 = new_x;
            horizontalLineTop.X2 = new_x + new_width;
            horizontalLineTop.Y1 = new_y + (new_height / 3);
            horizontalLineTop.Y2 = new_y + (new_height / 3);

            horizontalLineBottom.X1 = new_x;
            horizontalLineBottom.X2 = new_x + new_width;
            horizontalLineBottom.Y1 = new_y + (new_height / 3) * 2;
            horizontalLineBottom.Y2 = new_y + (new_height / 3) * 2;

            //Moving small 'handle' squares on the edges & corners of the crop square.

            double xLeft = new_x - 2;
            double xCenter = new_x + (new_width / 2) - 2;
            double xRight = new_x + new_width - 3;

            double yTop = new_y - 2;
            double yCenter = new_y + new_height / 2;
            double yBottom = new_y + new_height - 3;

            Canvas.SetLeft(topLeftSquare, xLeft);
            Canvas.SetTop(topLeftSquare, yTop);
            Canvas.SetLeft(topCenterSquare, xCenter);
            Canvas.SetTop(topCenterSquare, yTop);
            Canvas.SetLeft(topRightSquare, xRight);
            Canvas.SetTop(topRightSquare, yTop);

            Canvas.SetLeft(middleLeftSquare, xLeft);
            Canvas.SetTop(middleLeftSquare, yCenter);
            //NoCenter
            Canvas.SetLeft(middleRightSquare, xRight);
            Canvas.SetTop(middleRightSquare, yCenter);

            Canvas.SetLeft(bottomLeftSquare, xLeft);
            Canvas.SetTop(bottomLeftSquare, yBottom);
            Canvas.SetLeft(bottomCenterSquare, xCenter);
            Canvas.SetTop(bottomCenterSquare, yBottom);
            Canvas.SetLeft(bottomRightSquare, xRight);
            Canvas.SetTop(bottomRightSquare, yBottom);

            UpdateInfoLabel();
        }

        // Stop dragging.
        private void canvas1_MouseUp(object sender, MouseButtonEventArgs e)
        {
            CropDragInProgress = false;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Console.WriteLine("Window_SizeChanged!");
            rectangleDarken.Rect = new Rect(0, 0, darkenPath.ActualWidth, darkenPath.ActualHeight);
        }

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFile(Utilities.OpenFile());
        }

        private void OpenFile(string filePath)
        {
            if (filePath != null)
            {
                Console.WriteLine($"Opening file {filePath}.");
                this.filePath = filePath;
                ffmpegPlayer.Source = new Uri(filePath);
            }
        }

        private void FfmpegPlayer_MediaOpened(object sender, Unosquare.FFME.Common.MediaOpenedEventArgs e)
        {
            Console.WriteLine("Media Opened!");

            //TODO: Find a work around for this garbage.
            const int ADD_WIDTH = 16, 
                ADD_HEIGHT = 23 + 32 - (8 * 2);

            //TODO: Can actually grab the controls height instead of updating a constant.
            const int CONTROLS_HEIGHT = 170;//94;
            Console.WriteLine($"CONTROLS_HEIGHT: {CONTROLS_HEIGHT} mainGrid.RowDefinitions[1].ActualHeight: {mainGrid.RowDefinitions[1].ActualHeight}");

            Width = ffmpegPlayer.NaturalVideoWidth + ADD_WIDTH;

            Height = ffmpegPlayer.NaturalVideoHeight + ADD_HEIGHT + CONTROLS_HEIGHT;

            /// Crop to this:
            ///  _________________
            /// |    _________    |
            /// |   |         |   |
            /// |   |_________|   |
            /// |_________________|

            SetCropArea(ffmpegPlayer.NaturalVideoWidth / 4, 
                ffmpegPlayer.NaturalVideoHeight / 4, 
                ffmpegPlayer.NaturalVideoWidth / 2, 
                ffmpegPlayer.NaturalVideoHeight / 2);

#if !DEBUG
            // Resizing can be useful for debugging so only turn it off in release mode.
            ResizeMode = ResizeMode.NoResize;
#endif

            videoPositionSlider.IsEnabled = true;
            videoVolumeSlider.IsEnabled = true;
            playPauseButton.IsEnabled = true;
            muteUnmuteButton.IsEnabled = true;

            videoPositionSlider.Maximum = ffmpegPlayer.NaturalDuration.Value.TotalSeconds;
            videoPositionSlider.Value = 0;
            playPauseButton.Content = PAUSE_ICON;
            paused = false;

            UpdateInfoLabel();

            Console.WriteLine($"NaturalVideoWidth: {ffmpegPlayer.NaturalVideoWidth} NaturalVideoHeight: {ffmpegPlayer.NaturalVideoHeight}");
            Console.WriteLine($"Video ActualWidth: {ffmpegPlayer.ActualWidth} Video ActualHeight: {ffmpegPlayer.ActualHeight}");
            Console.WriteLine($"Window Width: {Width} Window Height: {Height}");
        }

        private void UpdateInfoLabel()
        {
            var ogWidth = ffmpegPlayer.NaturalVideoWidth;
            var ogHeight = ffmpegPlayer.NaturalVideoHeight;
            var cropWidth = width;
            var cropHeight = height;
            infoLabel.Content = $"Video Dimensions: {ogWidth}x{ogHeight}, Crop Dimensions: {cropWidth}x{cropHeight}.";
        }

        int videoVolumeSliderValueChangedSkips = 0;

        private void VideoVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (videoVolumeSliderValueChangedSkips > 0)
            {
                videoVolumeSliderValueChangedSkips--;
            }
            else
            { 
                ffmpegPlayer.Volume = videoVolumeSlider.Value;

                if (ffmpegPlayer.Volume == 0)
                {
                    muted = true;
                    unmutedVolume = MIN_UNMUTED_VOLUME;
                    muteUnmuteButton.Content = MUTE_ICON;
                }
                else
                {
                    muted = false;
                    unmutedVolume = Math.Clamp(ffmpegPlayer.Volume, MIN_UNMUTED_VOLUME, 1);
                    muteUnmuteButton.Content = UNMUTE_ICON;
                }
            }
        }

        private void VideoPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Console.WriteLine("VideoPositionSlider_ValueChanged");
            if (ffmpegPlayer.NaturalDuration.HasValue)
            {
                ffmpegPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            }
        }

        private void VideoPositionSlider_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Console.WriteLine("VideoPositionSlider_MouseDown");

            ffmpegWasPlayingOnScrubStart = ffmpegPlayer.IsPlaying;
            ffmpegPlayer.Pause();

            videoPositionUpdateTimer.Stop();
        }

        private void VideoPositionSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Console.WriteLine("VideoPositionSlider_MouseUp");

            if (ffmpegWasPlayingOnScrubStart)
                ffmpegPlayer.Play();

            videoPositionUpdateTimer.Start();
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            paused = !paused;

            if (paused)
            {
                ffmpegPlayer.Pause();
                playPauseButton.Content = PLAY_ICON;
            }
            else
            {
                ffmpegPlayer.Play();
                playPauseButton.Content = PAUSE_ICON;
            }
        }

        int frameCount = 0;

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            //string filters = "WEBM|*.webm|MP4|*.mp4|MKV|*.mkv|AVI|*.avi|FLV|*.flv";
            string filters = String.Join('|', Utilities.VideoExtensions.Select(t => t.ToUpper() + "|" + "*." + t));

            string savePath = Utilities.SaveFileDialog(filters);

            bool showFFMPEG = MessageBox.Show("Display the FFMPEG encoding window?", 
                "Display FFMPEG?", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question) == MessageBoxResult.Yes;

            if (savePath != null)
            {
                ProcessStartInfo startInfo = new ProcessStartInfo {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                Process frameCountProcess = new Process { StartInfo = startInfo };
                frameCountProcess.EnableRaisingEvents = true;
                frameCountProcess.StartInfo.FileName = $"{ffmpegDir}\\ffprobe.exe";
                frameCountProcess.StartInfo.Arguments = $"-v error -select_streams v:0 -show_entries stream=nb_frames -of default=nokey=1:noprint_wrappers=1 \"{filePath}\"";
                Console.WriteLine("Get frame count arguments: " + frameCountProcess.StartInfo.Arguments);
                frameCountProcess.Exited += FrameCountFinish;
                frameCount = 0;
                frameCountProcess.Start();

                startInfo.CreateNoWindow = !showFFMPEG;

                Process cropProcess = new Process { StartInfo = startInfo };
                cropProcess.EnableRaisingEvents = true;

                cropProcess.StartInfo.FileName = $"{ffmpegDir}\\ffmpeg.exe";
                cropProcess.StartInfo.Arguments = $"-y -i \"{filePath}\" -preset ultrafast -filter:v \"crop={width}:{height}:{x}:{y}\" -progress - -nostats \"{savePath}\"";
                Console.WriteLine($"Ffmpeg arguments: {cropProcess.StartInfo.Arguments}.");
                cropProcess.OutputDataReceived += FFMPEG_OutputDataReceived;
                cropProcess.Exited += CropFinish;

                saveButton.IsEnabled = false;
                saveButton.Content = "Cropping...";
                cropProcess.Start();
                cropProcess.BeginOutputReadLine();
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                OpenFile(files[0]);
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            //canvas1_MouseMove(sender, e);
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            verticalLineLeft.Visibility = Visibility.Visible;
            verticalLineRight.Visibility = Visibility.Visible;
            horizontalLineTop.Visibility = Visibility.Visible;
            horizontalLineBottom.Visibility = Visibility.Visible;
        }

        private void guidelinesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            verticalLineLeft.Visibility = Visibility.Hidden;
            verticalLineRight.Visibility = Visibility.Hidden;
            horizontalLineTop.Visibility = Visibility.Hidden;
            horizontalLineBottom.Visibility = Visibility.Hidden;
        }

        private void locateFFmpegButton_Click(object sender, RoutedEventArgs e)
        {
            string path = Utilities.OpenFolder();

            if (string.IsNullOrWhiteSpace(path))
                return;

            var files = Directory.GetFiles(path).Select(t => Path.GetFileName(t));

            if (files.Contains("ffmpeg.exe"))
            {
                mainGrid.Children.Remove(ffmpegAlertGrid);
                Unosquare.FFME.Library.FFmpegDirectory = Properties.Settings.Default.ffmpegPath;
                ffmpegPlayer.BeginInit();

                Properties.Settings.Default.ffmpegPath = path;
                Properties.Settings.Default.Save();

                // 'Reload' the window so that FFmpeg gets loaded properly.
                Application.Current.ShutdownMode = ShutdownMode.OnLastWindowClose;
                new MainWindow().Show();
                Close();
            }
            else
            {
                MessageBox.Show(
                    "'ffmpeg.exe' cannot be found in the provided folder. Please try again.", 
                    "'ffmpeg.exe' not found!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void trimBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            MouseHitType = SetHitType(trimBarRectangle, Mouse.GetPosition(trimBarCanvas));

            if (MouseHitType == HitType.None)
                return;

            if (MouseHitType == HitType.Body)
                MouseHitType = HitType.ScrollWE;

            SetMouseCursor();

            if (e.RightButton == MouseButtonState.Pressed || e.ClickCount == 2)
            {
                //TODO: Add "expand crop area" functionality for the trim functionality.
                //ExpandCropArea(MouseHitType);
            }
            else
            {
                LastPoint = Mouse.GetPosition(trimBarCanvas);
                TrimDragInProgress = true;
            }
        }

        private void trimBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!TrimDragInProgress)
            {
                Console.WriteLine($"trimBar_MouseMove !DragInProgress Mouse.GetPosition: {Mouse.GetPosition(trimBarCanvas).ToString()} MouseHitType: {MouseHitType.ToString()}");

                MouseHitType = SetHitType(trimBarRectangle, Mouse.GetPosition(trimBarCanvas));

                if (MouseHitType != HitType.Left && MouseHitType != HitType.Body && MouseHitType != HitType.Right)
                    MouseHitType = HitType.None;

                if (MouseHitType == HitType.Body)
                    MouseHitType = HitType.ScrollWE;

                SetMouseCursor();
            }
            else
            {
                Console.WriteLine($"trimBar_MouseMove DragInProgress Mouse.GetPosition: {Mouse.GetPosition(trimBarCanvas).ToString()} MouseHitType: {MouseHitType.ToString()}");

                // See how much the mouse has moved.
                Point point = Mouse.GetPosition(trimBarCanvas);
                double offset_x = point.X - LastPoint.X;

                // Get the rectangle's current position.
                double new_x = Canvas.GetLeft(trimBarRectangle);
                double new_width = trimBarRectangle.Width;

                // Update the rectangle.
                switch (MouseHitType)
                {
                    case HitType.ScrollWE:
                        new_x += offset_x;
                        break;
                    case HitType.Left:
                        new_x += offset_x;
                        new_width -= offset_x;
                        break;
                    case HitType.Right:
                        new_width += offset_x;
                        break;
                }

                // Clamp square to be within video limits
                new_width = Math.Clamp(new_width, 10, trimBarCanvas.ActualWidth);

                new_x = Math.Clamp(new_x, 0, trimBarCanvas.ActualWidth - new_width);

                SetTrimBarArea(new_x, new_width);

                // Save the mouse's new location.
                LastPoint = point;
            }
        }

        private void SetTrimBarArea(double new_x, double new_width)
        {
            x = (int)new_x;
            width = (int)new_width;

            // Update the rectangle.
            Canvas.SetLeft(trimBarRectangle, new_x);

            trimBarRectangle.Width = new_width;

            trimBarDarken.Rect = new Rect(0, 0, trimBarDarkenPath.ActualWidth, trimBarDarkenPath.ActualHeight);

            /// Okay, we are adding and subtracting 1s because apparently if rectangleGeometryExclude reaches the same width 
            /// and height of the path it's in then the paths's width and height to 0 somehow, and we can't fix it. This doesnt show any 
            /// visual glitches because the dragRectangle's stroke covers it up, so let's just leave it...
            trimBarExclude.Rect = new Rect(new_x + 1, 0, new_width - 2, trimBarExclude.Rect.Height);

            //Moving small 'handle' squares on the edges & corners of the crop square.

            double xLeft = new_x - 2;
            double xRight = new_x + new_width - 3;

            Canvas.SetLeft(trimBarLeftHandle, xLeft);
            Canvas.SetLeft(trimBarRightHandle, xRight);

            //UpdateInfoLabel();
        }

        private void trimBar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            TrimDragInProgress = false;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Properties.Settings.Default.Save();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetCropArea(ffmpegPlayer.ActualWidth / 4,
               ffmpegPlayer.ActualHeight / 4,
               ffmpegPlayer.ActualWidth / 2,
               ffmpegPlayer.ActualHeight / 2);
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (muted)
            {
                muted = false;
                muteUnmuteButton.Content = UNMUTE_ICON;
                ffmpegPlayer.Volume = unmutedVolume;
            }
            else
            {
                muted = true;
                muteUnmuteButton.Content = MUTE_ICON;
                unmutedVolume = Math.Clamp(ffmpegPlayer.Volume, MIN_UNMUTED_VOLUME, 1);
                ffmpegPlayer.Volume = 0;
            }

            videoVolumeSliderValueChangedSkips = 1;
            videoVolumeSlider.Value = ffmpegPlayer.Volume;
        }

        private void FrameCountFinish(object sender, EventArgs e)
        {
            Console.WriteLine("FrameCountFinish fired!");

            Process process = (Process)sender;
            string stdout = process.StandardOutput.ReadToEnd();
            Console.WriteLine("FrameCountFinish stdout retrieved: " + stdout);
            int.TryParse(stdout, out frameCount);
        }

        private void FFMPEG_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine("FFMPEG_OutputDataReceived fired!");

            if (e.Data != null)
            {
                Console.WriteLine("FFMPEG_OutputDataReceived e.Data is not null! The data: " + e.Data);

                if (e.Data.StartsWith("frame="))
                {
                    int frame = int.Parse(e.Data.Replace("frame=", ""));
                    double percent = Math.Round(((float)frame / (float)frameCount) * 100, 2);
                
                    Application.Current.Dispatcher.Invoke(() => {
                        if (frameCount == 0)
                            saveButton.Content = "Cropping...";
                        else
                            saveButton.Content = "Cropping... " + frame + "/" + frameCount + " frames (" + percent + "%)";
                    });
                }
            }
        }

        private void CropFinish(object sender, EventArgs e)
        {
            Console.WriteLine("CropFinish fired!");

            Application.Current.Dispatcher.Invoke(() =>
            {
                saveButton.IsEnabled = true;
                saveButton.Content = "Save As Cropped";
            });
        }
    }
}
