using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using MahApps.Metro.Controls;
using ControlzEx.Standard;
using System.Windows.Media.Animation;

namespace WMM_Control.FFmpeg.util
{
    public class VedioUtil : IDisposable
    {
        unsafe private AVFormatContext* format;
        unsafe private AVCodecContext* codeContext;
        unsafe private AVStream* vedioStream;
        //媒体数据包
        unsafe private AVPacket* packet;
        //媒体帧数据
        unsafe public AVFrame* frame;
        //图像转换器
        unsafe private SwsContext* convert;
        //帧，数据指针
        IntPtr FrameBufferPtr;
        byte_ptrArray4 TargetData;
        int_array4 TargetLinesize;
        private object SyncLock = new object();
        private int vedioIndex;


        public TimeSpan Duration { set; get; }
        public object CodecId { set; get; }
        public string CodecName { set; get; }
        public int Bitrate { set; get; }
        public double FrameRate { set; get; }
        public int FrameWidth {  set; get; }
        public int FrameHeight { set; get; }
        public TimeSpan FrameDuration { get; set; }
        public AVRational Time_base { set; get; }
        public AVRational Avg_frame_rate { set; get; }
        public bool IsStopPlay { set; get; } = false;

        /// <summary>
        /// 初始化视频
        /// </summary>
        /// <param name="filePath"></param>
        public void InitVedio(string filePath)
        {
            unsafe
            {
                Debug.WriteLine(ffmpeg.RootPath);
                int error = 0;
                // 初始化媒体格式上下文
                format = ffmpeg.avformat_alloc_context();

                if (format == null)
                {
                    Debug.WriteLine("初始化媒体上下文失败！");
                }

                var tempFormat = format;
                // 读取文件数据
                error = ffmpeg.avformat_open_input(&tempFormat, filePath, null, null);
                if (error < 0)
                {
                    Debug.WriteLine("文件不存在！");
                    return;
                }

                // 获取所有流
                ffmpeg.avformat_find_stream_info(format, null);

                // 解码器
                AVCodec* vCodec = null;

                // 查找视频流索引
                vedioIndex = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &vCodec, 0);

                if (vedioIndex < 0)
                {
                    Debug.WriteLine("视频不存在！");
                    return;
                }

                vedioStream = format->streams[vedioIndex];

                // 创建解码器上下文
                codeContext = ffmpeg.avcodec_alloc_context3(vCodec);

                //将视频流里面的解码器参数设置到 解码器上下文中
                error = ffmpeg.avcodec_parameters_to_context(codeContext, format->streams[vedioIndex]->codecpar);

                if (error < 0)
                {
                    Debug.WriteLine("设置解码器参数失败");
                    return;
                }

                // 打开解码器
                error = ffmpeg.avcodec_open2(codeContext, vCodec, null);

                if (error < 0)
                {
                    Debug.WriteLine("打开解码器失败");
                    return;
                }

                IsStopPlay = false;

                AVPixelFormat* pix_fmts = vCodec->pix_fmts;
                
                Duration = TimeSpan.FromMilliseconds(format->duration / 1000);
                
                CodecId = vedioStream->codecpar->codec_id.ToString();
                
                CodecName = ffmpeg.avcodec_get_name(vedioStream->codecpar->codec_id);
                
                Bitrate = (int)vedioStream->codecpar->bit_rate;
                
                FrameRate = ffmpeg.av_q2d(vedioStream->r_frame_rate);
                
                FrameWidth = vedioStream->codecpar->width;
                
                FrameHeight = vedioStream->codecpar->height;
                
                FrameDuration = TimeSpan.FromMilliseconds(1000 / FrameRate);
                
                Time_base = vedioStream->time_base;
                
                Avg_frame_rate = vedioStream->avg_frame_rate;

                var result = InitConvert(FrameWidth,FrameHeight,codeContext->pix_fmt,FrameWidth,FrameHeight, AVPixelFormat.AV_PIX_FMT_BGR0);
                if (result)
                {
                    //从内存中分配控件给 packet 和frame
                    packet = ffmpeg.av_packet_alloc();
                    frame = ffmpeg.av_frame_alloc();
                }
            }
        }

        /// <summary>
        /// 获取编解码器时间基
        /// </summary>
        /// <returns></returns>
        public unsafe AVRational GetPktTimeBase()
        {
            return codeContext->pkt_timebase;
        }

        /// <summary>
        /// 获取视频流时间基
        /// </summary>
        /// <returns></returns>
        public unsafe AVRational GetTimeBase()
        {
            if (format == null)
            {
                return default;
            }
            return format->streams[vedioIndex]->time_base;
        }

        public bool InitConvert(int sourceWidth, int sourceHeight, AVPixelFormat sourceFormat, int targetWidth, int targetHeight, AVPixelFormat targetFormat)
        {
            unsafe
            {
                //根据输入参数和输出参数初始化转换器
                convert = ffmpeg.sws_getContext(sourceWidth, sourceHeight, sourceFormat, targetWidth, targetHeight, targetFormat, ffmpeg.SWS_FAST_BILINEAR, null, null, null);
                
                if (convert == null)
                {
                    Debug.WriteLine("创建转换器失败");
                    return false;
                }
                
                //获取转换后图像的 缓冲区大小
                var bufferSize = ffmpeg.av_image_get_buffer_size(targetFormat, targetWidth, targetHeight, 1);
                
                //创建一个指针
                FrameBufferPtr = Marshal.AllocHGlobal(bufferSize);
                
                TargetData = new byte_ptrArray4();
                
                TargetLinesize = new int_array4();
                
                ffmpeg.av_image_fill_arrays(ref TargetData, ref TargetLinesize, (byte*)FrameBufferPtr, targetFormat, targetWidth, targetHeight, 1);
                
                return true;
            }
        }

        /// <summary>
        /// 视频跳转
        /// </summary>
        /// <param name="timestamp"></param>
        public unsafe void TrySeekTime(double timestamp)
        {
            ffmpeg.av_seek_frame(format, vedioIndex, (long)timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD | ffmpeg.AVSEEK_FLAG_FRAME);
            Debug.WriteLine(packet->pts);
        }


        public bool TryReadNextFrame(out AVFrame outFrame)
        {   
            lock (SyncLock)
            {
                unsafe
                {
                    if (IsStopPlay)
                    {
                        outFrame = *frame;
                        MessageBox.Show("已结束播放，请重新上传视频！");
                        return false;
                    }

                    int result = -1;
                    
                    // 清理上一帧数据
                    ffmpeg.av_frame_unref(frame);
                    
                    while (true)
                    {
                        // 清理上一帧数据包
                        ffmpeg.av_packet_unref(packet);
                        
                        // 读取下一帧，返回一个int 查看读取数据包的状态
                        result = ffmpeg.av_read_frame(format,packet);
                        
                        // 读取了最后一帧了，没有数据了，退出读取帧
                        if (result == ffmpeg.AVERROR_EOF || result < 0)
                        {
                            outFrame = *frame;
                            return false;
                        }

                        //判断读取的帧数据是否是视频数据，不是则继续读取
                        if (packet->stream_index != vedioIndex)
                        {
                            continue;
                        }

                        // 将包数据发给解码器
                        ffmpeg.avcodec_send_packet(codeContext,packet);
                        
                        // 从解码器接收解码后的帧
                        result = ffmpeg.avcodec_receive_frame(codeContext,frame);
                        if (result < 0)
                        {
                            continue;
                        }
                        
                        outFrame = *frame;
                        
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// 视频帧转字节数组
        /// </summary>
        /// <param name="sourceFrame"></param>
        /// <returns></returns>
        unsafe public byte[] FrameConvertBytes(AVFrame* sourceFrame)
        {
            // 利用转换器将yuv 图像数据转换成指定的格式数据
            ffmpeg.sws_scale(convert, sourceFrame->data, sourceFrame->linesize, 0, sourceFrame->height, TargetData, TargetLinesize);
            
            var data = new byte_ptrArray8();
            
            data.UpdateFrom(TargetData);
            
            var linesize = new int_array8();
            
            linesize.UpdateFrom(TargetLinesize);
            
            //创建一个字节数据，将转换后的数据从内存中读取成字节数组
            byte[] bytes = new byte[FrameWidth * FrameHeight * 4];
            
            Marshal.Copy((IntPtr)data[0], bytes, 0, bytes.Length);
            
            return bytes;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public unsafe void Dispose()
        {
            // 释放上下文
            if (false == IsStopPlay)
            {
                ffmpeg.avformat_free_context(format);
                
                ffmpeg.av_free(frame);
                
                ffmpeg.av_free(packet);
                
                ffmpeg.av_free(codeContext);
                
                ffmpeg.av_free(convert);
                
                IsStopPlay = true;
            }
        }
    }
}
