using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WMM_Control.FFmpeg.helper
{
    public static class FFmpegHelper
    {
        public static void RegisterFFmpegBinaries()
        {
            //获取当前软件启动的位置
            var currentFolder = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            //ffmpeg在项目中放置的位置
            var probe = Path.Combine("FFmpeg", "bin", Environment.Is64BitOperatingSystem ? "x64" : "x86");
            while (currentFolder != null)
            {
                var ffmpegBinaryPath = Path.Combine(currentFolder, probe);
                if (Directory.Exists(ffmpegBinaryPath))
                {
                    //找到dll放置的目录，并赋值给rootPath;
                    ffmpeg.RootPath = ffmpegBinaryPath;
                    return;
                }
                currentFolder = Directory.GetParent(currentFolder)?.FullName;
            }
            //旧版本需要要调用这个方法来注册dll文件，新版本已经会自动注册了
            //ffmpeg.avdevice_register_all();
        }
    }
}
