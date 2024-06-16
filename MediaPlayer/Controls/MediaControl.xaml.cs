using ControlzEx.Standard;
using FFmpeg.AutoGen;
using MediaPlayer.FFmpeg.util;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WMM_Control.FFmpeg.util;

namespace MediaPlayer.Controls
{
    /// <summary>
    /// MediaControl.xaml 的交互逻辑
    /// </summary>
    public partial class MediaControl : System.Windows.Controls.UserControl,INotifyPropertyChanged,IDisposable
    {
        // 视频处理类
        private VedioUtil _videoUtil;

        // 音频处理类
        private AudioUtil _audioUtil;

        // 音频播放类
        private WaveOut _waveOutEvent;

        // 音频缓冲区
        private BufferedWaveProvider _bufferedWaveProvider;
        
        // 锁
        private object _lockObject = new object();

        // 音频锁
        private object _audioLockObject = new object();

        // 令牌
        private CancellationTokenSource _currentMinitor = new CancellationTokenSource();
        
        // 滑动条鼠标左键按下
        private bool _isSilderMouseDown = false;
        
        // 滑动条鼠标左键抬起
        private bool _isSilderMouseUp = false;
        
        // 滑动条拖拽
        private bool _isSliderMouseDrag = false;
        
        // 滑动条鼠标按下值
        private double _sliderMouseDownValue = 0d;
        
        // 当前帧时间戳
        private double _frameTimestamp = 0d;

        // 当前音频帧时间戳
        private double _audioFrameTimestamp = 0d;

        // 滑动条计时器
        private System.Timers.Timer _sliderTimer = new System.Timers.Timer();

        // 开始播放时间
        private long _startTime;

        // 视频时间
        private long _vedioTime;

        // 声音时间
        private long _audioTime;

        // 公共时间基
        private AVRational _commonTimebase = new AVRational() { num = 1, den = ffmpeg.AV_TIME_BASE };

        // 开始暂停时间
        private long _stopTime;

        // 等待时间
        private long _awaitTime;


        // 当前时间是否大于等于音频时间基
        private bool _audioIsOver = false;

        // 当前图片
        private WriteableBitmap _currentImg;
        public WriteableBitmap CurrentImg
        {
            get => _currentImg;
            set
            {
                if (_currentImg != value)
                {
                    _currentImg = value;
                    OnPropertyChanged(nameof(CurrentImg));
                }
            }
        }

        // 任务

        // 是否播放
        private bool _isPlaying = false;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                OnPropertyChanged(nameof(IsPlaying));
            }
        }

        // 当前播放时间
        private string _currentPlayTime = "100";
        public string CurrentPlayTime
        {
            get => _currentPlayTime;
            set
            {
                _currentPlayTime = value;
                OnPropertyChanged(nameof(CurrentPlayTime));
            }
        }

        // 总共播放时间
        private string _totalPlayTime;
        public string TotalPlayTime
        {
            get => _totalPlayTime;
            set
            {
                _totalPlayTime = value;
                OnPropertyChanged(nameof(TotalPlayTime));
            }
        }

        // 当前播放文件路径
        private string _currentPlayFilePath;
        public string CurrentPlayFilePath
        {
            get => _currentPlayFilePath;
            set
            {
                _currentPlayFilePath = value;
                OnPropertyChanged(nameof(CurrentPlayFilePath));
            }
        }

        // 滑条总时长
        private double _sliderTotalValue;
        public double SliderTotalValue
        {
            get => _sliderTotalValue;
            set
            {
                _sliderTotalValue = value;
                OnPropertyChanged(nameof(SliderTotalValue));
            }
        }

        // 滑条当前时长
        private double _sliderValue;
        public double SliderValue
        {
            get => _sliderValue;
            set
            {
                _sliderValue = value;
                OnPropertyChanged(nameof(SliderValue));
            }
        }

        public MediaControl()
        {
            InitializeComponent();
            
            DataContext = this;
            
            _videoUtil = new VedioUtil();

            _audioUtil = new AudioUtil();
            
            CurrentPlayTime = TimeSpan.FromSeconds(0).ToString();
            
            TotalPlayTime = TimeSpan.FromSeconds(0).ToString();
            
            SliderValue = 0;
            
            SliderTotalValue = 100;
            
            _sliderTimer.Interval = 1000;
            
            _sliderTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
            {
                if (_currentMinitor.IsCancellationRequested)
                {
                    _sliderTimer.Stop();
                    return;
                }

                if (!_isSilderMouseDown && _frameTimestamp != 0 && IsPlaying)
                {
                    System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        //Debug.WriteLine("鼠标松开了...");
                        CurrentPlayTime = TimeSpan.FromMilliseconds(_frameTimestamp).ToString();
                        SliderValue = _frameTimestamp / 1000;
                    });
                }
            };
            _sliderTimer.Start();
        }

        #region PropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); ;
        }
        #endregion

        /// <summary>
        /// 上传视频
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Upload_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();

            openFileDialog.CheckFileExists = true;
            
            openFileDialog.CheckPathExists = true;
            
            openFileDialog.Filter = "mp4 files (*.mp4)|*.mp4|h264 files (*.h264)|*.h264";
            
            var result = openFileDialog.ShowDialog();
            if (result != DialogResult.OK)
            {
                return;
            }

            string fileName = openFileDialog.FileName;

            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            {
                return;
            }

            _videoUtil.InitVedio(fileName);

            _audioUtil.InitAudioData(fileName);

            Debug.WriteLine("持续时间：" + _videoUtil.Duration);

            _waveOutEvent = new WaveOut();

            // 初始化音频缓冲区
            _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat());

            _waveOutEvent.Init(_bufferedWaveProvider);

            _waveOutEvent.Play();

            // 总时长
            TotalPlayTime = _videoUtil.Duration.ToString();
            
            // 滑动条总长度
            SliderTotalValue = _videoUtil.Duration.TotalSeconds;
            
            // 当前文件名称
            CurrentPlayFilePath = fileName;
            
            // 当前图像
            CurrentImg = new WriteableBitmap(GetInitImageSource(_videoUtil.FrameWidth, _videoUtil.FrameHeight));
            
            // 播放视频
            PlayAudio(_currentMinitor.Token);

            PlayVedio(_currentMinitor.Token);

        }

        /// <summary>
        /// 播放视频
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PlayVedio_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (false == IsPlaying)
            {
                IsPlaying = true;
            }

            if (_stopTime != 0)
            {
                _awaitTime = ffmpeg.av_gettime() - _stopTime;
            }
            else
            {
                _startTime = ffmpeg.av_gettime();
            }
        }

        /// <summary>
        /// 暂停播放视频
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Pause_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            IsPlaying = false;
            _stopTime = ffmpeg.av_gettime();
        }

        /// <summary>
        /// 停止播放视频
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Stop_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            IsPlaying = false;
            
            _videoUtil.Dispose();
            
            CurrentPlayTime = TimeSpan.FromSeconds(0).ToString();
            
            TotalPlayTime = TimeSpan.FromSeconds(0).ToString();
            
            SliderValue = 0;
            
            CurrentPlayFilePath = "请先上传文件...";
            
            CurrentImg = new WriteableBitmap(GetInitImageSource(_videoUtil.FrameWidth, _videoUtil.FrameHeight));
        }

        /// <summary>
        /// 初始化封面
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        private BitmapSource GetInitImageSource(int width,int height)
        {
            Bitmap bitmap = new Bitmap(width,height);
            
            Graphics graphics = Graphics.FromImage(bitmap);
            
            graphics.Clear(System.Drawing.Color.Black);
            
            BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(bitmap.GetHbitmap(),IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            
            graphics.Dispose();
            
            bitmap.Dispose();
            
            return bitmapSource;
        } 

        /// <summary>
        /// 播放视频
        /// </summary>
        /// <param name="token"></param>
        private void PlayVedio(CancellationToken token)
        {
            Task.Run(() =>
            {
                unsafe
                {
                    var lastFrameTimestamp = 0d;
                    var systemTimeBase = 0d;
                    while (!token.IsCancellationRequested)
                    {
                        if (IsPlaying)
                        {
                            if (_videoUtil.TryReadNextFrame(out var frame))
                            {
                                var timeBase = _videoUtil.GetTimeBase();

                                // 帧率
                                long currentVedioTime = ffmpeg.av_rescale_q(frame.pts, timeBase, _commonTimebase);
                                _vedioTime = ffmpeg.av_gettime() - _startTime - _awaitTime;

                                long sleepTime = 0;
                                if (_audioIsOver && _audioTime < _vedioTime)
                                {
                                    sleepTime += _vedioTime - _audioTime;
                                }

                                if (_vedioTime > 0 && _vedioTime < currentVedioTime)
                                {
                                    sleepTime += currentVedioTime - _vedioTime;
                                }

                                if (sleepTime > 0)
                                {
                                    Thread.Sleep(TimeSpan.FromMilliseconds(sleepTime / 1000));
                                }

                                byte[] data = _videoUtil.FrameConvertBytes(&frame);

                                int stride = (_videoUtil.FrameWidth * PixelFormats.Bgra32.BitsPerPixel) / 8;

                                if (System.Windows.Application.Current != null)
                                {
                                    System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        BitmapSource source = BitmapSource.Create(_videoUtil.FrameWidth, _videoUtil.FrameHeight, 96, 96, PixelFormats.Bgra32, null, data, stride);
                                        if (source != null)
                                        {
                                            CurrentImg = new WriteableBitmap(source);
                                        }
                                    });
                                }
                            }
                            else
                            {
                                _videoUtil.Dispose();
                            }
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                    }

                }

            }, token);
        }

        /// <summary>
        /// 播放音频
        /// </summary>
        /// <param name="cancellationToken"></param>
        private void PlayAudio(CancellationToken cancellationToken)
        {
            Task.Run(() =>
            {
                var lastFrameTimestamp = 0d;
                var systemTimeBase = 0d;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (IsPlaying)
                    {
                        if (_audioUtil.TryNextAudioFrame(out var frame))
                        {
                            unsafe
                            {
                                var timeBase = _audioUtil.GetTimeBase();

                                long currentAudioTime = ffmpeg.av_rescale_q(frame.pts, timeBase, _commonTimebase);
                                _audioTime = ffmpeg.av_gettime() - _startTime;

                                long sleepTime = currentAudioTime - _audioTime;

                                if (_awaitTime > 0)
                                {
                                    sleepTime -= _awaitTime;
                                }

                                if (_audioTime > 0 && sleepTime > 0)
                                {
                                    _audioIsOver = true;
                                    Thread.Sleep(TimeSpan.FromMilliseconds(sleepTime / 1000));
                                }
                                else
                                {
                                    _audioIsOver = false;
                                }

                                // 帧数据转为byte数组
                                byte[] data = _audioUtil.FrameConvertBytes(&frame);

                                if (data == null)
                                {
                                    continue;
                                }

                                // 如果里面数据小于2s清除
                                if (_bufferedWaveProvider.BufferLength <= _bufferedWaveProvider.BufferedBytes + data.Length)
                                {
                                    _bufferedWaveProvider.ClearBuffer();
                                }

                                if (System.Windows.Application.Current != null)
                                {
                                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        _bufferedWaveProvider.AddSamples(data, 0, data.Length);
                                    });
                                }
                            }
                        }
                        else
                        {
                            _audioUtil.Dispose();
                        }
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
            });
        }

        /// <summary>
        /// 视频跳转到指定位置
        /// </summary>
        private void SkipTime(double skipPosition)
        {
            lock (_lockObject)
            {
                // 获取刻度/长度
                double timestamp = skipPosition / ffmpeg.av_q2d(_videoUtil.Time_base);

                Debug.WriteLine($"time -> {timestamp}");

                Task.Run(() =>
                {
                    _videoUtil.TrySeekTime(timestamp);
                });
            }
        }

        /// <summary>
        /// 音频跳转
        /// </summary>
        /// <param name="skipPosition"></param>
        private void AudioSkipTime(double skipPosition)
        {
            lock (_audioLockObject)
            {
                // 获取刻度/长度
                double timestamp = skipPosition / ffmpeg.av_q2d(_audioUtil.TimeBase);

                Debug.WriteLine($"time -> {timestamp}");

                Task.Run(() =>
                {
                    _audioUtil.TrySeekTime(timestamp);
                });
            }
        }

        public void Dispose()
        {
            _currentMinitor.Cancel();
            _sliderTimer.Close();
            _sliderTimer.Dispose();
        }

        /// <summary>
        /// 视频跳转
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSliderMouseDrag)
            {
                IsPlaying = false;

                Slider slider = (Slider)sender;
                
                double value = slider.Value;
                
                Debug.WriteLine($"current value -> {value}");
                
                CurrentPlayTime = TimeSpan.FromMilliseconds(value).ToString();
                
                SliderValue = value;
                
                SkipTime(value);

                AudioSkipTime(value);

                IsPlaying = true;
            }
            //BindingOperations.SetBinding(VedioPlayerSlider, Slider.ValueProperty, new System.Windows.Data.Binding("SliderValue"));
            _isSilderMouseDown = false;

            _isSliderMouseDrag = false;
            
            _isSilderMouseUp = true;
            
            Thread.Sleep(250);
            
            _isSilderMouseUp = false;
        }

        /// <summary>
        /// 滑动条拖拽
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Slider_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isSilderMouseDown)
            {
                _isSliderMouseDrag = true;
                
                Slider slider = (Slider)sender;
                
                double value = slider.Value;
                
                Debug.WriteLine($"current value -> {value}");
                
                SkipTime(value);

                AudioSkipTime(value);
                //BindingOperations.ClearBinding(VedioPlayerSlider, Slider.ValueProperty);
            }

        }

        /// <summary>
        /// 滑动条鼠标按下
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Slider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isSilderMouseDown = true;
            
            Slider slider = (Slider)sender;
            
            double value = slider.Value;
            
            _sliderMouseDownValue = value;
        }


        private string _pushUrl = "rtmp://192.168.4.11/live/0";

        /// <summary>
        /// 推流
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void PushStream_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 选择文件
            OpenFileDialog openFileDialog = new OpenFileDialog();
            
            openFileDialog.Filter = "mp4 files (*.mp4)|*.mp4|h264 (*.h264)|*.h264";
            
            openFileDialog.CheckFileExists = true;

            openFileDialog.CheckPathExists = true;

            openFileDialog.ShowDialog();

            // 文件名
            var fileName = openFileDialog.FileName;

            if (string.IsNullOrEmpty(fileName))
            {
                return;
            }

            Pusher pusher = new Pusher();
            pusher.Init("rtsp://test:admin12345@192.168.0.233:554/h264/ch1/main/av_stream", "rtmp://192.168.4.11/live/0");
            await pusher.PushStream();
        }
    }
}
