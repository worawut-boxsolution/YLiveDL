using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using YLiveDL.Util;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
 
public class YouTubeDownloadService
{
    private readonly YoutubeClient _youtube;

    public YouTubeDownloadService()
    {
        _youtube = new YoutubeClient();
    }


    //public async Task<Video> GetVideoInfoAsync(string videoUrl)
    //{
    //    return await _youtube.Videos.GetAsync(videoUrl);
    //}

    public async Task<VideoInfo> GetVideoInfoAsync(string videoUrl)
    {
        try
        {
            // ดึงข้อมูลวิดีโอพื้นฐาน
            YLiveDL.Util.Video video_ =   new YLiveDL.Util.Video();
            var _video = await _youtube.Videos.GetAsync(videoUrl);
            video_.Id = _video.Id.Value;
            video_.Title = _video.Title;
            video_.Author = _video.Author;
            video_.UploadDate = _video.UploadDate;
            video_.Description = _video.Description;
            video_.Duration = _video.Duration;
            video_.Thumbnails = _video.Thumbnails?.ToList();
            video_.Keywords = _video.Keywords?.ToList();
            video_.Engagement = _video.Engagement;
            // ดึงข้อมูลสตรีมเพื่อคำนวณขนาดไฟล์
            var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoUrl);

            // คำนวณขนาดไฟล์โดยรวมสตรีมวิดีโอและเสียง
            var videoStream = streamManifest.GetVideoStreams().GetWithHighestVideoQuality();
            var audioStream = streamManifest.GetAudioStreams().GetWithHighestBitrate();

            long? totalSize = null;
            if (videoStream != null && audioStream != null)
            {
                totalSize = videoStream.Size.Bytes + audioStream.Size.Bytes;
            }
            else if (videoStream != null)
            {
                totalSize = videoStream.Size.Bytes;
            }

            // สร้างออบเจกต์ VideoInfo
            return new VideoInfo
            {
                VideoData = video_,
                FileSize = totalSize,
                FormattedFileSize = totalSize.HasValue ? FormatFileSize(totalSize.Value) : "ไม่ทราบขนาด"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"เกิดข้อผิดพลาดขณะดึงข้อมูลวิดีโอ: {ex.Message}");
            throw;
        }
    }

    // เมธอดช่วยสำหรับจัดรูปแบบขนาดไฟล์
    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    // เมธอด DownloadVideoAsync ที่มีอยู่เดิม

    public async Task DownloadVideoAsync(string videoUrl, string outputFilePath,
    IProgress<double> progress = null,
    IProgress<string> progressMerg = null,
    CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. ตรวจสอบข้อมูลวิดีโอ
            var video = await _youtube.Videos.GetAsync(videoUrl, cancellationToken);
            Console.WriteLine($"กำลังประมวลผลวิดีโอ: {video.Title}");

            // 2. ดึงข้อมูลสตรีม
            var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoUrl, cancellationToken);

            // 3. พยายามหาสตรีมแบบรวม (muxed) ก่อน
            var muxedStreams = streamManifest.GetMuxedStreams().ToList();

            if (muxedStreams.Any())
            {
                // กรณีที่มีสตรีมแบบรวม
                var streamInfo = muxedStreams.GetWithHighestVideoQuality();
                Console.WriteLine($"พบสตรีมแบบรวม: {(streamInfo as IVideoStreamInfo)?.VideoQuality.Label}");

                await _youtube.Videos.Streams.DownloadAsync(
                    streamInfo,
                    outputFilePath,
                    progress,
                    cancellationToken);
            }
            else
            {
                // 4. กรณีไม่มีสตรีมแบบรวม ให้ใช้สตรีมแบบแยก (adaptive)
                Console.WriteLine("ไม่พบสตรีมแบบรวม กำลังใช้สตรีมแบบแยก...");

                var videoStreamInfo = streamManifest.GetVideoStreams()
                    .Where(s => s.Container == Container.Mp4)
                    .GetWithHighestVideoQuality();

                if (videoStreamInfo == null)
                {
                    throw new Exception("ไม่พบสตรีมวิดีโอที่เหมาะสม");
                }

                // 4. หาสตรีมเสียง (แก้ไขส่วนนี้)
                var audioStreamInfo = streamManifest.GetAudioOnlyStreams()
                    .OrderByDescending(s => s.Bitrate)
                    .FirstOrDefault();

                if (audioStreamInfo == null)
                {
                    throw new Exception("ไม่พบสตรีมเสียงในวิดีโอนี้");
                }

                if (videoStreamInfo != null && audioStreamInfo != null)
                {
                    await DownloadAndMergeStreamsAsync(
                        videoStreamInfo,
                        audioStreamInfo,
                        outputFilePath,
                        progress,
                        progressMerg,
                        cancellationToken);
                }
                else if (videoStreamInfo != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _youtube.Videos.Streams.DownloadAsync(
                        videoStreamInfo,
                        outputFilePath,
                        progress,
                        cancellationToken);
                }
                else
                {
                    throw new Exception("ไม่พบสตรีมวิดีโอหรือเสียงที่เหมาะสม");
                }
            }

            Console.WriteLine("ดาวน์โหลดเสร็จสิ้น!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"เกิดข้อผิดพลาด: {ex.Message}");
            throw;
        }
    }


    public async Task<bool> DownloadVideoAsync2(
    string videoUrl,
    string outputFilePath,
    IProgress<double> progress = null,
  
    CancellationToken cancellationToken = default)
    {
        FileStream fileStream = null;
        Stream youtubeStream = null;

        try
        {
            var youtube = new YoutubeClient();
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl, cancellationToken);
            var streamInfo = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();

            // ตรวจสอบสตรีม
            if (streamInfo == null)
            {
                throw new Exception("No suitable stream found");
            }

            // เปิดสตรีมด้วย using declaration (จะปิดอัตโนมัติเมื่อออกจาก scope)
            youtubeStream = await youtube.Videos.Streams.GetAsync(streamInfo, cancellationToken);
            fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[81920];
            int bytesRead;
            long totalRead = 0;
            var stopwatch = Stopwatch.StartNew();

            while ((bytesRead = await youtubeStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                await fileStream.FlushAsync(); // บังคับเขียนข้อมูลทันที

                totalRead += bytesRead;
                progress?.Report((double)totalRead / streamInfo.Size.Bytes);

                // ตรวจสอบการยกเลิกทุกครั้งที่อ่านข้อมูล
                cancellationToken.ThrowIfCancellationRequested();
            }

            // ปิดสตรีมอย่างชัดเจน (ไม่ต้องรอ using)
            await fileStream.DisposeAsync();
            await youtubeStream.DisposeAsync();

            return true;
        }
        catch (OperationCanceledException)
        {
            // ทำความสะอาดเมื่อยกเลิก
            if (fileStream != null)
            {
                try { await fileStream.DisposeAsync(); } catch { }
                try { File.Delete(outputFilePath); } catch { }
            }
            if (youtubeStream != null)
            {
                try { await youtubeStream.DisposeAsync(); } catch { }
            }
            throw;
        }
        catch (Exception)
        {
            // ทำความสะอาดเมื่อ error
            if (fileStream != null)
            {
                try { await fileStream.DisposeAsync(); } catch { }
                try { File.Delete(outputFilePath); } catch { }
            }
            if (youtubeStream != null)
            {
                try { await youtubeStream.DisposeAsync(); } catch { }
            }
            throw;
        }
    }
    private async Task DownloadAndMergeStreamsAsync(
    IVideoStreamInfo videoStreamInfo,  // ระบุประเภทชัดเจน
    IAudioStreamInfo audioStreamInfo,  // ระบุประเภทชัดเจน
    string outputFilePath,
    IProgress<double> progress,
    IProgress<string> progressMerg ,
    CancellationToken cancellationToken)
    {
        // สร้างไฟล์ชั่วคราว
        var videoTempPath = Path.GetTempFileName();
        var audioTempPath = Path.GetTempFileName();

        try
        {
            // ดาวน์โหลดสตรีมภาพ (ใช้ IVideoStreamInfo)
            cancellationToken.ThrowIfCancellationRequested();
            await _youtube.Videos.Streams.DownloadAsync(
                videoStreamInfo,
                videoTempPath,
                progress,
                cancellationToken);

            // ดาวน์โหลดสตรีมเสียง (ใช้ IAudioStreamInfo)
            cancellationToken.ThrowIfCancellationRequested();
            await _youtube.Videos.Streams.DownloadAsync(
                audioStreamInfo,
                audioTempPath,
                null,
                cancellationToken);

            // ใช้ FFmpeg รวมสตรีม
            await MergeWithFfmpegStatusAsync(
                videoTempPath,
                audioTempPath,
                outputFilePath,
                progressMerg,
                cancellationToken);
        }
        finally
        {
            // ลบไฟล์ชั่วคราว
            File.Delete(videoTempPath);
            File.Delete(audioTempPath);
        }
    }

    /*V1 no prrogress status*/
    private async Task MergeWithFfmpegAsync(
        string videoPath,
        string audioPath,
        string outputPath,

        CancellationToken cancellationToken)
    {

        var ffmpegPath = FindFfmpegPath();


        if (string.IsNullOrEmpty(ffmpegPath))
        {
            throw new Exception("ไม่พบ FFmpeg ในระบบ");
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-i \"{videoPath}\" -i \"{audioPath}\" -c:v copy -c:a aac \"{outputPath}\" -y",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = new Process { StartInfo = processStartInfo };


        // บัฟเฟอร์สำหรับเก็บ error output
        var errorOutput = new StringBuilder();
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorOutput.AppendLine(e.Data);
                Console.WriteLine($"FFmpeg Error: {e.Data}");
            }
        };

        // เริ่มกระบวนการ
        if (!process.Start())
        {
            throw new Exception("ไม่สามารถเริ่มกระบวนการ FFmpeg ได้");
        }

        // เริ่มอ่าน error stream แบบ asynchronous
        process.BeginErrorReadLine();

        try
        {
            // สร้าง Task สำหรับรอกระบวนการสิ้นสุด
            var waitForExitTask = process.WaitForExitAsync(cancellationToken);

            // รอทั้งกระบวนการและ cancellation token
            await Task.WhenAny(waitForExitTask, Task.Delay(Timeout.Infinite, cancellationToken));

            if (cancellationToken.IsCancellationRequested)
            {
                // ถ้ายกเลิก ให้ kill กระบวนการ
                try { process.Kill(); } catch { /* ignore */ }
                throw new OperationCanceledException("การรวมไฟล์ถูกยกเลิก");
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"FFmpeg error (Exit code: {process.ExitCode}): {errorOutput.ToString()}");
            }
        }
        finally
        {
            // หยุดอ่าน error stream
            process.CancelErrorRead();
        }
    }
    /*V1 no prrogress status*/

    /*V2 Progress status*/
    public async Task MergeWithFfmpegStatusAsync(
    string videoPath,
    string audioPath,
    string outputPath,
    IProgress<string> progress,
    CancellationToken cancellationToken)
    {
        var ffmpegPath = FindFfmpegPath();
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            progress?.Report("ไม่พบ FFmpeg ในระบบ");
            throw new Exception("ไม่พบ FFmpeg ในระบบ");
        }

        progress?.Report("กำลังเริ่มกระบวนการรวมไฟล์...");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -i \"{videoPath}\" -i \"{audioPath}\" -c:v copy -c:a aac \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };

        var errorOutput = new StringBuilder();
        process.ErrorDataReceived += (sender, e) => {
            if (!string.IsNullOrEmpty(e.Data))
            {
                // อ่านและแปลความหมายความคืบหน้าจาก FFmpeg output
                if (e.Data.Contains("time="))
                {
                    var timeMatch = Regex.Match(e.Data, @"time=(\d{2}:\d{2}:\d{2}.\d{2})");
                    if (timeMatch.Success)
                    {
                        progress?.Report($"กำลังประมวลผล: {timeMatch.Groups[1].Value}");
                    }
                }
                errorOutput.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            progress?.Report("ไม่สามารถเริ่มกระบวนการ FFmpeg ได้");
            throw new Exception("ไม่สามารถเริ่มกระบวนการ FFmpeg ได้");
        }

        process.BeginErrorReadLine();
        progress?.Report("กระบวนการ FFmpeg เริ่มทำงานแล้ว");

        try
        {
            var waitForExitTask = process.WaitForExitAsync(cancellationToken);
            await Task.WhenAny(waitForExitTask, Task.Delay(Timeout.Infinite, cancellationToken));

            if (cancellationToken.IsCancellationRequested)
            {
                progress?.Report("กำลังยกเลิกการรวมไฟล์...");
                try { process.Kill(); } catch { /* ignore */ }
                throw new OperationCanceledException("การรวมไฟล์ถูกยกเลิก");
            }

            if (process.ExitCode != 0)
            {
                progress?.Report($"เกิดข้อผิดพลาดในการรวมไฟล์ (รหัส: {process.ExitCode})");
                throw new Exception($"FFmpeg error: {errorOutput.ToString()}");
            }

            progress?.Report("การรวมไฟล์เสร็จสมบูรณ์!");
        }
        finally
        {
            process.CancelErrorRead();
        }
    }
    /*V2 Progress status*/
    private string FindFfmpegPath()
    {
        // ตรวจสอบในตำแหน่งที่กำหนด
        var customPath = Path.Combine(AppContext.BaseDirectory, "utils", "ffmpeg.exe");
        if (File.Exists(customPath))
        {
            return customPath;
        }

        // ตรวจสอบใน PATH ระบบ
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();

        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, "ffmpeg.exe");
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // ตรวจสอบในตำแหน่งทั่วไปของ Windows
        if (OperatingSystem.IsWindows())
        {
            var programFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe");
            if (File.Exists(programFilesPath))
            {
                return programFilesPath;
            }
        }

        return null;
    }
}