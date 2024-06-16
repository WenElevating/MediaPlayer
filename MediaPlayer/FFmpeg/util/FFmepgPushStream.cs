using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WMM_Control.FFmpeg.helper;

namespace MediaPlayer.FFmpeg.util
{
    public unsafe class FFmepgPushStream
    {
        private AVFormatContext* _formatContext = null;

        private AVFormatContext* _outputContext = null;

        private int _vedioIndex;

        public void Init(string sourceUrl, string targetUrl)
        {
            // 获取输出格式类型
            string formatType = GetFormatType(targetUrl);

            // 注册ffmpeg库
            FFmpegHelper.RegisterFFmpegBinaries();

            // 初始化封装和解封装格式
            ffmpeg.av_register_all();

            // 初始化网络库
            ffmpeg.avformat_network_init();

            // 设置日志级别
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);


            // 封装上下文
            _formatContext = ffmpeg.avformat_alloc_context();
            var format = _formatContext;

            AVDictionary* options = null;

            ffmpeg.av_dict_set(&options, "rtsp_transport", "tcp", 0);
            ffmpeg.av_dict_set(&options, "stimeout", "10000000", 0);
            ffmpeg.av_dict_set(&options, "flvflags", "no_duration_filesize", 0);

            // 打开文件
            if (ffmpeg.avformat_open_input(&format, sourceUrl, null, &options) < 0)
            {
                Debug.WriteLine("open file failed !");
                return;
            }
            Debug.WriteLine("open file success!");

            // 获取视频数据
            if (ffmpeg.avformat_find_stream_info(format, null) < 0)
            {
                Debug.WriteLine("get stream info failed!");
                return;
            }
            Debug.WriteLine("get stream info success!");

            // 视频流位置
            for (int i = 0; i < _formatContext->nb_streams; i++)
            {
                if (_formatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    _vedioIndex = i;
                    break;
                }
            }

            ffmpeg.av_dump_format(_formatContext, 0, sourceUrl, 0);

            CreateOutputContext(targetUrl, formatType);

            // 写入文件头
            if (ffmpeg.avformat_write_header(_outputContext, null) < 0)
            {
                Debug.WriteLine("write header failed!");
                return;
            }
        }

        /// <summary>
        /// 创建并赋值输出上下文
        /// </summary>
        /// <param name="url"></param>
        /// <param name="formatType"></param>
        private void CreateOutputContext(string url, string formatType = "flv")
        {
            var tempFormat = _outputContext;
            // 握手
            if (ffmpeg.avformat_alloc_output_context2(&tempFormat, null, formatType, url) < 0)
            {
                Debug.WriteLine("alloc output context failed!");
                return;
            }
            Debug.WriteLine("alloc output context success!");
            _outputContext = tempFormat;

            var oFormat = _outputContext->oformat;

            // 复制输入流配置并创建输出流
            for (int i = 0; i < _formatContext->nb_streams; i++)
            {
                // 输入流
                var inStream = _formatContext->streams[i];
                AVStream* outStream = ffmpeg.avformat_new_stream(_outputContext, inStream->codec->codec);
                if (outStream == null)
                {
                    Debug.WriteLine("create new output stream failed!");
                    return;
                }

                // 复制编解码上下文
                //ffmpeg.avcodec_parameters_from_context(outStream->codecpar, inStream->codec);
                if (ffmpeg.avcodec_copy_context(outStream->codec, inStream->codec) < 0)
                {
                    Debug.WriteLine("copy parameters data failed!");
                    return;
                }

                outStream->codec->codec_tag = 0;
                if ((oFormat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                {
                    outStream->codec->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                }

            }

            ffmpeg.av_dump_format(_outputContext, 0, url, 1);

            if ((oFormat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                // 连接
                if (ffmpeg.avio_open(&_outputContext->pb, url, ffmpeg.AVIO_FLAG_WRITE) < 0)
                {
                    Debug.WriteLine("connect rtmp server failed!");
                    return;
                }
            }

        }

        public unsafe void TryNextPacket(ref AVPacket packet,ref int frame_index,ref long lastPts)
        {
            AVStream* inputStream, outputStream;
            AVPacket pack = packet;

            // 读一帧
            int error = ffmpeg.av_read_frame(_formatContext, &pack);
            
            if (error != 0 || error == ffmpeg.AVERROR_EOF)
            {
                Debug.WriteLine("read frame end!");
                return;
            }

            // No pts
            if (pack.pts == ffmpeg.AV_NOPTS_VALUE)
            {
                // pts/dts/duration
                AVRational s_time_base = _formatContext->streams[_vedioIndex]->time_base;
                Int64 calc_duration = (long)(ffmpeg.AV_TIME_BASE / ffmpeg.av_q2d(_formatContext->streams[_vedioIndex]->r_frame_rate));
                pack.pts = (long)(frame_index * calc_duration / (double)(ffmpeg.av_q2d(s_time_base) * ffmpeg.AV_TIME_BASE));
                pack.dts = pack.pts;
                pack.duration = (long)(calc_duration / (double)(ffmpeg.av_q2d(s_time_base) * ffmpeg.AV_TIME_BASE));
            }

            // 延迟
            if (pack.stream_index == _vedioIndex)
            {
                AVRational s_time_base = _formatContext->streams[_vedioIndex]->time_base;

                AVRational time_base_q = new AVRational()
                {
                    num = 1,

                    den = ffmpeg.AV_TIME_BASE
                };

                // 调整pts从一个时间基到另一个时间基
                Int64 pts_time = ffmpeg.av_rescale_q(pack.pts, s_time_base, time_base_q);

                Int64 now_time = ffmpeg.av_gettime();

                if (pts_time > now_time)
                {
                    Thread.Sleep((int)(pts_time - now_time));
                }
            }

            inputStream = _formatContext->streams[pack.stream_index];
            outputStream = _outputContext->streams[pack.stream_index];

            // copy packet
            // 转换PTS/DTS（Convert PTS/DTS）
            pack.pts = ffmpeg.av_rescale_q_rnd(pack.pts, inputStream->time_base, outputStream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
            pack.dts = ffmpeg.av_rescale_q_rnd(pack.dts, inputStream->time_base, outputStream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
            pack.duration = ffmpeg.av_rescale_q(pack.duration, inputStream->time_base, outputStream->time_base);
            pack.pos = -1;

            Debug.WriteLine(pack.pts);
            // 视频流
            if (pack.stream_index == _vedioIndex)
            {
                //Debug.WriteLine("send packet to rtmp server");
                frame_index++;
            }

            if (lastPts > pack.pts)
            {
                return;
            }
            else
            {
                lastPts = pack.pts;
            }
            packet = pack;
        }

        public int PushPacket(AVPacket packet)
        {
            if (ffmpeg.av_interleaved_write_frame(_outputContext, &packet) < 0)
            {
                Debug.WriteLine("Error muxing packet!");
                return -1;
            }

            var tempPack = &packet;
            ffmpeg.av_packet_free(&tempPack);
            Thread.Sleep(1);
            return 0;
        }

        private string GetFormatType(string url)
        {
            if (url.StartsWith("rtmp://"))
            {
                return "flv";
            }

            if (url.StartsWith("rtsp://"))
            {
                return "rtsp";
            }

            if (url.StartsWith("udp://"))
            {
                return "h264";
            }

            if (url.StartsWith("rtp://"))
            {
                return "rtp";
            }

            return null;
        }
    }
}
