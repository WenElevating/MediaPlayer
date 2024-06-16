using FFmpeg.AutoGen;
using System;
using System.Diagnostics;
using System.Security.Policy;
using System.Threading;

namespace MediaPlayer.FFmpeg.util
{
    public unsafe class VedioPushUtil:IDisposable
    {
        
        private AVFormatContext* _formatContext;

        private AVStream* _vedioStream;

        private int _vedioIndex;

        private AVFormatContext* _outputContext = null;

        private AVPacket packet;

        private int frame_index = 0;

        public void Dispose()
        {
            ffmpeg.avformat_free_context(_outputContext);
            ffmpeg.avformat_free_context(_formatContext);
        }

        /// <summary>
        /// 初始化推流
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="url"></param>
        public void PushVedio(string filePath, string url)
        {
            // 初始化封装和解封装格式
            ffmpeg.av_register_all();

            // 初始化网络库
            ffmpeg.avformat_network_init();

            // 设置日志级别
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);


            // 封装上下文
            _formatContext = ffmpeg.avformat_alloc_context();
            var format = _formatContext;

            // 打开文件
            if (ffmpeg.avformat_open_input(&format, filePath, null, null) < 0)
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
                }
            }

            ffmpeg.av_dump_format(_formatContext, 0, filePath, 0);

            // 输出格式
            var tempOutFormat = _outputContext;
            // 握手
            if (ffmpeg.avformat_alloc_output_context2(&tempOutFormat, null, "flv", url) < 0)
            {
                Debug.WriteLine("alloc output context failed!");
                return;
            }
            Debug.WriteLine("alloc output context success!");

            var oFormat = tempOutFormat->oformat;

            // 复制输入流配置并创建输出流
            for (int i = 0; i < _formatContext->nb_streams; i++)
            {
                // 输入流
                var inStream = _formatContext->streams[i];
                AVStream* outStream = ffmpeg.avformat_new_stream(tempOutFormat, inStream->codec->codec);
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

            ffmpeg.av_dump_format(tempOutFormat, 0, url, 1);

            if ((oFormat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                // 连接
                if (ffmpeg.avio_open(&tempOutFormat->pb, url, ffmpeg.AVIO_FLAG_WRITE) < 0)
                {
                    Debug.WriteLine("connect rtmp server failed!");
                    return;
                }
            }

            // 写入文件头
            if (ffmpeg.avformat_write_header(tempOutFormat, null) < 0)
            {
                Debug.WriteLine("write header failed!");
                return;
            }

            var startTime = ffmpeg.av_gettime();
            while (true)
            {
                AVStream* inputStream, outputStream;
                var pack = packet;

                // 读一帧
                int error = ffmpeg.av_read_frame(_formatContext, &pack);
                if (error != 0 || error == ffmpeg.AVERROR_EOF)
                {
                    Debug.WriteLine("read frame end!");
                    break;
                }

                // No pts
                if (pack.pts == ffmpeg.AV_NOPTS_VALUE)
                {
                    // time base
                    AVRational s_time_base = _formatContext->streams[_vedioIndex]->time_base;

                    // 2帧之间的持续时间，举例第一帧持续时间：时间基1/1000000微秒，帧率为1/25帧，一帧是40000微秒。 一帧占多少格子
                    Int64 calc_duration = (long)(ffmpeg.AV_TIME_BASE / ffmpeg.av_q2d(_formatContext->streams[_vedioIndex]->r_frame_rate));

                    // 参数
                    // 时间戳，当前帧索引 * 一帧持续时间 （微秒） / 时间基（秒）* 公共时间基（转成微秒） 时间基是1/25这种格式，意思是1s = 25格 那当前时间 * 25 = 当前时间所在格数
                    // 算当前帧占了多少个格子
                    pack.pts = (long)(frame_index * calc_duration / (double)(ffmpeg.av_q2d(s_time_base) * ffmpeg.AV_TIME_BASE));
                    pack.dts = pack.pts;
                    // 一帧占了多少格子
                    // 一帧持续时间（微秒） / 时间基（秒）* 公共时间基（转成微秒) // 一帧持续时间
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
                outputStream = tempOutFormat->streams[pack.stream_index];

                // copy packet
                // 转换PTS/DTS（Convert PTS/DTS）
                pack.pts = ffmpeg.av_rescale_q_rnd(pack.pts, inputStream->time_base, outputStream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                pack.dts = ffmpeg.av_rescale_q_rnd(pack.dts, inputStream->time_base, outputStream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                pack.duration = ffmpeg.av_rescale_q(pack.duration, inputStream->time_base, outputStream->time_base);
                pack.pos = -1;

                // 视频流
                if (pack.stream_index == _vedioIndex)
                {
                    Debug.WriteLine("send packet to rtmp server");
                    frame_index++;
                }

                if (ffmpeg.av_interleaved_write_frame(tempOutFormat, &pack) < 0)
                {
                    Debug.WriteLine("Error muxing packet!");
                    break;
                }

                var tempPack = &pack;
                ffmpeg.av_packet_free(&tempPack);
                Thread.Sleep(1);
            }

            // 写入文件尾
            ffmpeg.av_write_trailer(tempOutFormat);

        }


        /// <summary>
        /// 推流h264帧
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="url"></param>
        public void PushH264Raw(string filePath, string url)
        {
            // 初始化封装和解封装格式
            ffmpeg.av_register_all();

            // 初始化网络库
            ffmpeg.avformat_network_init();

            // 设置日志级别
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);


            // 封装上下文
            _formatContext = ffmpeg.avformat_alloc_context();
            var format = _formatContext;

            // 打开文件
            if (ffmpeg.avformat_open_input(&format, filePath, null, null) < 0)
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
                }
            }

            ffmpeg.av_dump_format(_formatContext, 0, filePath, 0);

            // 输出格式
            var tempOutFormat = _outputContext;
            // 查找匹配格式,
            if (ffmpeg.avformat_alloc_output_context2(&tempOutFormat, null, "flv", url) < 0)
            {
                Debug.WriteLine("alloc output context failed!");
                return;
            }
            Debug.WriteLine("alloc output context success!");

            var oFormat = tempOutFormat->oformat;

            // 复制输入流配置并创建输出流
            for (int i = 0; i < _formatContext->nb_streams; i++)
            {
                // 输入流
                var inStream = _formatContext->streams[i];
                AVStream* outStream = ffmpeg.avformat_new_stream(tempOutFormat, inStream->codec->codec);
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

            ffmpeg.av_dump_format(tempOutFormat, 0, url, 1);

            if ((oFormat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                // 连接
                if (ffmpeg.avio_open(&tempOutFormat->pb, url, ffmpeg.AVIO_FLAG_WRITE) < 0)
                {
                    Debug.WriteLine("connect rtmp server failed!");
                    return;
                }
            }

            // 写入文件头
            if (ffmpeg.avformat_write_header(tempOutFormat, null) < 0)
            {
                Debug.WriteLine("write header failed!");
                return;
            }

            var startTime = ffmpeg.av_gettime();
            while (true)
            {
                AVStream* inputStream, outputStream;
                var pack = packet;

                // 读一帧
                int error = ffmpeg.av_read_frame(_formatContext, &pack);
                if (error != 0 || error == ffmpeg.AVERROR_EOF)
                {
                    Debug.WriteLine("read frame end!");
                    break;
                }

                // No pts
                if (pack.pts == ffmpeg.AV_NOPTS_VALUE)
                {
                    // time base
                    AVRational s_time_base = _formatContext->streams[_vedioIndex]->time_base;

                    // 2帧之间的持续时间，举例第一帧持续时间：时间基1/1000000微秒，帧率为1/25帧，一帧是40000微秒。 一帧占多少格子
                    Int64 calc_duration = (long)(ffmpeg.AV_TIME_BASE / ffmpeg.av_q2d(_formatContext->streams[_vedioIndex]->r_frame_rate));

                    // 参数
                    // 时间戳，当前帧索引 * 一帧持续时间 （微秒） / 时间基（秒）* 公共时间基（转成微秒） 时间基是1/25这种格式，意思是1s = 25格 那当前时间 * 25 = 当前时间所在格数
                    // 算当前帧占了多少个格子
                    pack.pts = (long)(frame_index * calc_duration / (double)(ffmpeg.av_q2d(s_time_base) * ffmpeg.AV_TIME_BASE));
                    pack.dts = pack.pts;
                    // 一帧占了多少格子
                    // 一帧持续时间（微秒） / 时间基（秒）* 公共时间基（转成微秒) // 一帧持续时间
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
                outputStream = tempOutFormat->streams[pack.stream_index];

                // copy packet
                // 转换PTS/DTS（Convert PTS/DTS）
                pack.pts = ffmpeg.av_rescale_q_rnd(pack.pts, inputStream->time_base, outputStream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                pack.dts = ffmpeg.av_rescale_q_rnd(pack.dts, inputStream->time_base, outputStream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                pack.duration = ffmpeg.av_rescale_q(pack.duration, inputStream->time_base, outputStream->time_base);
                pack.pos = -1;

                // 视频流
                if (pack.stream_index == _vedioIndex)
                {
                    //Debug.WriteLine("send packet to rtmp server");
                    frame_index++;
                }

                if (ffmpeg.av_interleaved_write_frame(tempOutFormat, &pack) < 0)
                {
                    Debug.WriteLine("Error muxing packet!");
                    break;
                }

                var tempPack = &pack;
                ffmpeg.av_packet_free(&tempPack);
                Thread.Sleep(1);
            }

            // 写入文件尾
            ffmpeg.av_write_trailer(tempOutFormat);
        }
    }
}
