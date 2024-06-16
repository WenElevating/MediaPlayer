using FFmpeg.AutoGen;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WMM_Control.FFmpeg.helper;

namespace MediaPlayer.FFmpeg.util
{
    public class Pusher
    {
        private ConcurrentQueue<AVPacket> _vedioPacketQueue = new ConcurrentQueue<AVPacket>();

        private CancellationTokenSource _readFrameToken;

        private CancellationTokenSource _sendPacketToken;

        private FFmepgPushStream _pushStream;

        private Task _currentTask;

        public unsafe void Init(string sourceUrl,string targetUrl)
        {
            if (string.IsNullOrEmpty(sourceUrl) || string.IsNullOrEmpty(targetUrl))
            {
                return;
            }

            _pushStream = new FFmepgPushStream();

            _pushStream.Init(sourceUrl, targetUrl);
        }

        /// <summary>
        /// 推流
        /// </summary>
        public async Task PushStream()
        {
            _readFrameToken = new CancellationTokenSource();
            int frame_index = 0;
            long lastPts = 0;
            await Task.Run(() =>
            {
                while (!_readFrameToken.IsCancellationRequested)
                {
                    AVPacket packet = default;
                    _pushStream.TryNextPacket(ref packet, ref frame_index, ref lastPts);
                    if (packet.pts == 0)
                    {
                        continue;
                    }
                    OnPacket(packet);
                }
            });

        }

        /// <summary>
        /// 接收包
        /// </summary>
        /// <param name="packet"></param>
        private void OnPacket(AVPacket packet)
        {
            if (_vedioPacketQueue != null)
            {
                _vedioPacketQueue.Enqueue(packet);

                if (_vedioPacketQueue.Count > 0 && _currentTask == null)
                {
                    // 启动推流线程
                    _sendPacketToken = new CancellationTokenSource();
                    _currentTask = new Task(PacketPushThread, _sendPacketToken.Token);
                    _currentTask.Start();
                }
            }
        }

        /// <summary>
        /// 推流线程
        /// </summary>
        private unsafe void PacketPushThread()
        {
            long lastPts = -1;
            while (!_sendPacketToken.IsCancellationRequested)
            {
                if (_vedioPacketQueue.TryDequeue(out var pack))
                {
                    if (pack.pts < lastPts)
                    {
                        continue;
                    }
                    else
                    {
                        lastPts = pack.pts;
                    }
                    _pushStream.PushPacket(pack);
                }
                
            }
            Console.ReadKey();
        }


    }
}
