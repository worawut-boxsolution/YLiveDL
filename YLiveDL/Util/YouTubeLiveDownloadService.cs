using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YoutubeExplode.Exceptions;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;

namespace YLiveDL.Util
{
    public class YouTubeLiveDownloadService
    {
        public async Task<LiveVideoInfo> GetVideoInfoAsync(string videoUrl)
        {
            var youtube = new YoutubeClient();
            var video = await youtube.Videos.GetAsync(videoUrl);

            return new LiveVideoInfo
            {
                Title = video.Title,
                Author = video.Author.Title,
                ThumbnailUrl = video.Thumbnails.TryGetWithHighestResolution()?.Url,
                IsLive = video.Duration == null,
                FileSize = await EstimateFileSize(videoUrl)
            };
        }

        public async Task DownloadLiveStreamAsync(
            string liveUrl,
            string outputPath,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                var youtube = new YoutubeClient();
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(liveUrl, cancellationToken);

                // เลือกสตรีมที่ดีที่สุด
                var streamInfo = streamManifest.GetMuxedStreams().FirstOrDefault()
                               ?? streamManifest.GetVideoStreams().GetWithHighestVideoQuality();

                if (streamInfo == null)
                    throw new Exception("ไม่พบสตรีมที่เหมาะสม");

                await youtube.Videos.Streams.DownloadAsync(streamInfo, outputPath, progress, cancellationToken);
            }
            catch (YoutubeExplodeException ex) when (ex.Message.Contains("cipher manifest"))
            {
                // ใช้วิธีสำรองเมื่อพบข้อผิดพลาด cipher
                await DownloadLiveStreamFallback(liveUrl, outputPath, progress, cancellationToken);
            }
        }

        private async Task DownloadLiveStreamFallback(
     string liveUrl,
     string outputPath,
     IProgress<double> progress,
     CancellationToken cancellationToken)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    // Set a reasonable timeout for live streaming (you can adjust this)
                    httpClient.Timeout = TimeSpan.FromHours(2);

                    using (var response = await httpClient.GetAsync(
                        liveUrl,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var receivedBytes = 0L;
                        var buffer = new byte[8192];
                        var isMoreToRead = true;

                        await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                        await using (var fileStream = new FileStream(
                            outputPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            buffer.Length,
                            true))
                        {
                            while (isMoreToRead)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                                if (read == 0)
                                {
                                    isMoreToRead = false;
                                }
                                else
                                {
                                    await fileStream.WriteAsync(buffer, 0, read, cancellationToken);

                                    receivedBytes += read;
                                    if (totalBytes > 0)
                                    {
                                        var progressPercentage = (double)receivedBytes / totalBytes * 100;
                                        progress?.Report(progressPercentage);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Clean up if the operation was canceled
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                throw;
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                throw new Exception($"Failed to download live stream: {ex.Message}", ex);
            }
        }
        public async Task<long> EstimateFileSize(string videoUrl)
        {
            try
            {
                var youtube = new YoutubeClient();

                // 1. ดึงข้อมูลวิดีโอพื้นฐาน
                var video = await youtube.Videos.GetAsync(videoUrl);

                // 2. ดึงข้อมูลสตรีม
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);

                // 3. เลือกสตรีมที่ดีที่สุด
                var streamInfo = streamManifest.GetMuxedStreams().FirstOrDefault()
                               ?? streamManifest.GetVideoStreams().GetWithHighestVideoQuality()
                               ?? streamManifest.GetAudioStreams().GetWithHighestBitrate();

                if (streamInfo == null)
                    throw new Exception("ไม่พบสตรีมที่เหมาะสมสำหรับการประมาณขนาด");

                // 4. คำนวณขนาดไฟล์โดยประมาณ
                if (video.Duration.HasValue)
                {
                    // สำหรับวิดีโอปกติ: ใช้ bitrate และระยะเวลา
                    var bitrate = GetEstimatedBitrate(streamInfo);
                    var durationSeconds = video.Duration.Value.TotalSeconds;
                    return (long)(bitrate * durationSeconds / 8); // แปลงจาก bits เป็น bytes
                }
                else
                {
                    // สำหรับสตรีมสด: ใช้ขนาดสตรีมปัจจุบันเป็นแนวทาง
                    return streamInfo.Size.Bytes;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ไม่สามารถประมาณขนาดไฟล์ได้: {ex.Message}");
                return 0; // คืนค่า 0 หากไม่สามารถประมาณได้
            }
        }

        private double GetEstimatedBitrate(IStreamInfo streamInfo)
        {
            // ค่า bitrate โดยประมาณตามประเภทและคุณภาพสตรีม
            return streamInfo switch
            {
                IVideoStreamInfo videoStream => videoStream.VideoQuality.Label switch
                {
                    "144p" => 200_000,   // 200 kbps
                    "240p" => 400_000,
                    "360p" => 800_000,
                    "480p" => 1_200_000,
                    "720p" => 2_500_000,
                    "1080p" => 5_000_000,
                    "1440p" => 8_000_000,
                    "2160p" => 15_000_000,
                    _ => 1_000_000 // ค่าเริ่มต้น
                },
                IAudioStreamInfo audioStream => audioStream.Bitrate.BitsPerSecond,
                _ => 1_000_000 // ค่าเริ่มต้นสำหรับสตรีมประเภทอื่น
            };
        }
    }

    public class LiveVideoInfo
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string ThumbnailUrl { get; set; }
        public bool IsLive { get; set; }
        public long? FileSize { get; set; }
    }
}
