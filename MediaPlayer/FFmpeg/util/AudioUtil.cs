using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.AxHost;
using System.Windows.Controls;
using System.Xml.Linq;
using System.Net.Sockets;

namespace MediaPlayer.FFmpeg.util
{
    public unsafe class AudioUtil
    {
        // 上下文
        private AVFormatContext* _formatContext;

        // 音频流
        private AVStream* _audioStream;

        // 编解码器上下文
        private AVCodecContext* _codecContext;
        
        // 编解码器
        private AVCodec* _audioCodec;
        
        // 数据包
        private AVPacket* _audioPacket;

        // 数据帧
        private AVFrame* _audioFrame;

        // 音频流索引
        private int _audioStreamIndex;

        // 转换器
        private SwrContext* _srwrContext;


        // 音频持续时间
        public TimeSpan Duration { get; private set; }

        // 编码器id
        public string CodecId { get; private set; }

        // 编码器名称
        public string CodecName { get; private set; }
        
        // 比特率
        public long BitRate { get; private set; }

        // 通道数
        public int AudioChannelNumber { get; private set; }

        // 通道类型布局
        public ulong ChannelTypeLayout { get; private set; }

        // 采样率
        public int SampleRate { get; private set; }

        // 采样格式
        public AVSampleFormat SampleFormat { get; private set; }

        // 采样次数
        public int BitsPerSample { get; private set; }

        // 音频指针
        public IntPtr AudioBuffer { get; private set; }

        // 指针
        public byte* BufferPtr { get; set; }

        // 时间基
        public AVRational TimeBase {  get; private set; }

        
        /// <summary>
        /// 初始化音频数据
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public bool InitAudioData(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            // 分配内存空间
            _formatContext = ffmpeg.avformat_alloc_context();

            var tempFormat = _formatContext;

            // 打开文件
            if (ffmpeg.avformat_open_input(&tempFormat, url, null, null) < 0)
            {
                Debug.WriteLine("读取文件数据失败...");
                return false;
            }

            // 查找流数据
            if (ffmpeg.avformat_find_stream_info(_formatContext, null) < 0)
            {
                Debug.WriteLine("查找文件流数据失败...");
                return false;
            }


            // 查找音频流数据
            AVCodec* codec;
            _audioStreamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &codec, 0);

            _audioStream = _formatContext->streams[_audioStreamIndex];

            // 创建编解码器上下文
            _codecContext = ffmpeg.avcodec_alloc_context3(codec);
            
            // 初始化上下文参数
            if (ffmpeg.avcodec_parameters_to_context(_codecContext, _audioStream->codecpar) < 0)
            {
                Debug.WriteLine("编码器上下文参数初始化失败...");
                return false;
            }

            // 打开编解码器
            if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
            {
                Debug.WriteLine("编码器打开失败...");
                return false;
            }

            Duration = TimeSpan.FromMinutes(_formatContext->duration / 1000);

            CodecId = _audioStream->codecpar->codec_id.ToString();

            CodecName = ffmpeg.avcodec_get_name(_audioStream->codecpar->codec_id);

            TimeBase = _audioStream->time_base;

            BitRate = _codecContext->bit_rate;

            AudioChannelNumber = _codecContext->channels;

            ChannelTypeLayout = _codecContext->channel_layout;

            SampleRate = _codecContext->sample_rate;

            SampleFormat = _codecContext->sample_fmt;

            //采样次数  //获取给定音频参数所需的缓冲区大小。
            BitsPerSample = ffmpeg.av_samples_get_buffer_size(null, 2, _codecContext->frame_size, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);

            AudioBuffer = Marshal.AllocHGlobal(BitsPerSample);

            BufferPtr = (byte*)AudioBuffer;

            // 初始化转换器
            InitConvert((int)ChannelTypeLayout, AVSampleFormat.AV_SAMPLE_FMT_S16, (int)SampleRate, (int)ChannelTypeLayout, SampleFormat, (int)SampleRate);

            _audioPacket = ffmpeg.av_packet_alloc();
            
            _audioFrame = ffmpeg.av_frame_alloc();

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="occ">输出的通道类型</param>
        /// <param name="osf">输出的采样格式</param>
        /// <param name="osr">输出的采样率</param>
        /// <param name="icc">输入的通道类型</param>
        /// <param name="isf">输入的采样格式</param>
        /// <param name="isr">输入的采样率</param>
        /// <returns></returns>
        private bool InitConvert(int occ, AVSampleFormat osf, int osr, int icc, AVSampleFormat isf, int isr)
        {
            //创建一个重采样转换器
            _srwrContext = ffmpeg.swr_alloc();
            
            //设置重采样转换器参数
            _srwrContext = ffmpeg.swr_alloc_set_opts(_srwrContext, occ, osf, osr, icc, isf, isr, 0, null);
            
            if (_srwrContext == null)
                return false;
            
            //初始化重采样转换器
            ffmpeg.swr_init(_srwrContext);
            
            return true;
        }

        /// <summary>
        /// 音频帧转字节数组
        /// </summary>
        /// <param name="sourceFrame"></param>
        /// <returns></returns>
        public byte[] FrameConvertBytes(AVFrame* sourceFrame)
        {
            var tempBufferPtr = BufferPtr;
            
            //重采样音频
            var outputSamplesPerChannel = ffmpeg.swr_convert(_srwrContext, &tempBufferPtr, 19200, sourceFrame->extended_data, sourceFrame->nb_samples);
            
            //获取重采样后的音频数据大小
            var outPutBufferLength = ffmpeg.av_samples_get_buffer_size(null, 2, outputSamplesPerChannel, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);
            
            if (outputSamplesPerChannel < 0)
                return null;
            
            byte[] bytes = new byte[outPutBufferLength];
            
            //从内存中读取转换后的音频数据
            Marshal.Copy(AudioBuffer, bytes, 0, bytes.Length);
            
            return bytes;
        }


        /// <summary>
        /// 获取下一帧音频数据
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        public bool TryNextAudioFrame(out AVFrame frame)
        {
            // 清除上一帧和数据包
            ffmpeg.av_frame_unref(_audioFrame);
            ffmpeg.av_packet_unref(_audioPacket);

            while (true)
            {
                int res = ffmpeg.av_read_frame(_formatContext, _audioPacket);

                if (res < 0)
                {
                    frame = *_audioFrame;
                    return false;
                }

                //判断读取的帧数据是否是视频数据，不是则继续读取
                if (_audioPacket->stream_index != _audioStreamIndex)
                {
                    continue;
                }

                res = ffmpeg.avcodec_send_packet(_codecContext, _audioPacket);
                if (res < 0 || res == ffmpeg.AVERROR_EOF)
                {
                    frame = *_audioFrame;
                    return false;
                }

                res = ffmpeg.avcodec_receive_frame(_codecContext, _audioFrame);
                if (res < 0 || res == ffmpeg.AVERROR_EOF)
                {
                    frame = *_audioFrame;
                    return false;
                }

                frame = *_audioFrame;
                return true;
            }
        }


        /// <summary>
        /// 获取编解码器时间基
        /// </summary>
        /// <returns></returns>
        public unsafe AVRational GetPktTimeBase()
        {
            return _codecContext->pkt_timebase;
        }

        /// <summary>
        /// 获取音频流时间基
        /// </summary>
        /// <returns></returns>
        public unsafe AVRational GetTimeBase()
        {
            if (_formatContext == null)
            {
                return default;
            }
            return _formatContext->streams[_audioStreamIndex]->time_base;
        }

        /// <summary>
        /// 视频跳转
        /// </summary>
        /// <param name="timestamp"></param>
        public unsafe void TrySeekTime(double timestamp)
        {
            ffmpeg.av_seek_frame(_formatContext, _audioStreamIndex, (long)timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD | ffmpeg.AVSEEK_FLAG_FRAME);
            Debug.WriteLine(_audioPacket->pts);
        }

        public unsafe void Dispose()
        {
            // 释放上下文
            ffmpeg.avformat_free_context(_formatContext);

            ffmpeg.av_free(_audioFrame);

            ffmpeg.av_free(_audioPacket);

            ffmpeg.av_free(_audioCodec);

            ffmpeg.av_free(_srwrContext);
        }
    }
}
