using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MediaPlayer.FFmpeg.util
{
    public unsafe class VedioParseTest
    {

        private AVFormatContext* _formatContext;
        private AVStream* _vedioStream;
        private AVCodecContext* _codecContext;
        private AVPacket* _packet;
        private AVFrame* _frame;

        // 转换器
        private SwsContext* _wsContext;
        IntPtr _frameBufferPtr;
        byte_ptrArray4 _targetData;
        int_array4 _targetLinesize;
        private object _lockObject = new object();

        public int _vedioIndex {  get; set; }
        public TimeSpan Duration { get; set; }
        public string CodecId { get; set; }
        public string CodecName { get; set; }
        public int BitRate { get; set; }
        public double FrameRate {  get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public TimeSpan FrameDuration {  get; set; }

        public void InitWithVedio(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            // 分配上下文空间
            _formatContext = ffmpeg.avformat_alloc_context();

            // 打开文件
            var tempFormat = _formatContext;

            // 读取文件信息
            if (ffmpeg.avformat_open_input(&tempFormat, url, null, null) < 0)
            {
                Debug.WriteLine("文件不存在");
                return;
            }

            ffmpeg.avformat_find_stream_info(_formatContext, null);

            AVCodec* vCodec = null;
            // 查找视频流
            _vedioIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &vCodec, 0);

            if (_vedioIndex < 0)
            {
                Debug.WriteLine("视频不存在！");
                return;
            }

            // 视频流信息
            _vedioStream = _formatContext->streams[_vedioIndex];

            // 创建编解码器上下文
            _codecContext = ffmpeg.avcodec_alloc_context3(vCodec);

            // 将编解码参数加载到上下文
            if (ffmpeg.avcodec_parameters_to_context(_codecContext, _vedioStream->codecpar) < 0)
            {
                Debug.WriteLine("设置编解码器参数失败！");
                return;
            }

            // 打开编解码器
            if (ffmpeg.avcodec_open2(_codecContext, vCodec, null) < 0)
            {
                Debug.WriteLine("编解码器打开失败！");
                return;
            }

            Duration = TimeSpan.FromMilliseconds(_formatContext->duration / 1000);
            CodecId = _vedioStream->codecpar->codec_id.ToString();
            CodecName = ffmpeg.avcodec_get_name(_vedioStream->codecpar->codec_id);
            BitRate = (int)_vedioStream->codecpar->bit_rate;
            FrameRate = ffmpeg.av_q2d(_vedioStream->r_frame_rate);
            FrameWidth = _vedioStream->codecpar->width;
            FrameHeight = _vedioStream->codecpar->height;
            FrameDuration = TimeSpan.FromMilliseconds(1000 / FrameRate);
            if (!InitConvert(FrameWidth, FrameHeight, _codecContext->pix_fmt, FrameWidth, FrameHeight, AVPixelFormat.AV_PIX_FMT_RGB0))
            {
                _packet = ffmpeg.av_packet_alloc();
                _frame = ffmpeg.av_frame_alloc();
            }
        }


        public bool InitConvert(int sourceWidth, int sourceHeight, AVPixelFormat sourceFormat, int targetWidth, int targetHeight, AVPixelFormat targetFormat)
        {
            unsafe
            {
                //根据输入参数和输出参数初始化转换器
                _wsContext = ffmpeg.sws_getContext(sourceWidth, sourceHeight, sourceFormat, targetWidth, targetHeight, targetFormat, ffmpeg.SWS_FAST_BILINEAR, null, null, null);
                if (_wsContext == null)
                {
                    Debug.WriteLine("创建转换器失败");
                    return false;
                }
                //获取转换后图像的 缓冲区大小
                var bufferSize = ffmpeg.av_image_get_buffer_size(targetFormat, targetWidth, targetHeight, 1);
                //创建一个指针
                _frameBufferPtr = Marshal.AllocHGlobal(bufferSize);
                _targetData = new byte_ptrArray4();
                _targetLinesize = new int_array4();
                ffmpeg.av_image_fill_arrays(ref _targetData, ref _targetLinesize, (byte*)_frameBufferPtr, targetFormat, targetWidth, targetHeight, 1);
                return true;
            }
        }

        public bool TryNextFrame(out AVFrame currentFrame)
        {
            lock (_lockObject)
            {
                int result = -1;
                ffmpeg.av_frame_unref(_frame);
                while (true)
                {
                    // 释放包
                    ffmpeg.av_packet_unref(_packet);

                    // 读取帧
                    result = ffmpeg.av_read_frame(_formatContext, _packet);

                    if (result == ffmpeg.AVERROR_EOF || result < 0)
                    {
                        currentFrame = *_frame;
                        return false;
                    }

                    // 发送包到解码器
                    ffmpeg.avcodec_send_packet(_codecContext, _packet);

                    // 从解码器收到帧
                    result = ffmpeg.avcodec_receive_frame(_codecContext, _frame);

                    if (result < 0)
                    {
                        continue;
                    }

                    currentFrame = *_frame;
                    return true;
                }
            }

        }
    }
}
