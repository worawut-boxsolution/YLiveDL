using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO; // เพิ่ม using สำหรับ Path และ File
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading; // เพิ่ม using สำหรับ CancellationToken และ Timeout
using System.Threading.Tasks;
using YLiveDL.Util;

namespace YLiveDL.Util
{
    public class YtDlpServiceLive
    {
        private string _ytDlpPath;
        private string _ffmpegPath; // เพิ่ม path สำหรับ ffmpeg หากจำเป็นต้องใช้

        public YtDlpServiceLive()
        {
            _ytDlpPath = FindYtDlpPath();
            if (string.IsNullOrEmpty(_ytDlpPath))
            {
                throw new Exception("ไม่พบ yt-dlp.exe ในระบบ โปรดตรวจสอบการติดตั้ง");
            }
            // Uncomment บรรทัดด้านล่าง หากคุณต้องการใช้ ffmpeg แยก และตรวจสอบ path ของมันด้วย
            // _ffmpegPath = FindFfmpegPath();
            // if (string.IsNullOrEmpty(_ffmpegPath))
            // {
            //     throw new Exception("ไม่พบ ffmpeg.exe ในระบบ โปรดตรวจสอบการติดตั้ง");
            // }
        }

        private string FindYtDlpPath()
        {
            // ตรวจสอบในโฟลเดอร์ utils/yt-dlp.exe ก่อน (ถ้ามี)
            var customPath = Path.Combine(AppContext.BaseDirectory, "utils", "yt-dlp.exe");
            if (File.Exists(customPath)) return customPath;

            // ตรวจสอบใน PATH environment variable
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, "yt-dlp.exe");
                if (File.Exists(fullPath)) return fullPath;
            }

            return null;
        }

        // หากคุณต้องการใช้ ffmpeg แยกต่างหาก (yt-dlp มักจะใช้ ffmpeg ที่มาพร้อมกับมันเอง)
        private string FindFfmpegPath()
        {
            var customPath = Path.Combine(AppContext.BaseDirectory, "utils", "ffmpeg.exe");
            if (File.Exists(customPath)) return customPath;

            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, "ffmpeg.exe");
                if (File.Exists(fullPath)) return fullPath;
            }

            return null;
        }

        // เมธอดสำหรับดึงข้อมูลวิดีโอ
        public async Task<JObject> GetVideoMetadataAsync(string videoUrl, CancellationToken cancellationToken = default)
        {
            // ใช้ --no-playlist เพื่อไม่ดึงข้อมูล playlist ทั้งหมดหาก URL เป็น playlist
            // ใช้ --extractor-args "youtube:player_client=web" เพื่อหลีกเลี่ยง error บางกรณี
            var arguments = $"--dump-json --no-warnings --flat-playlist --extractor-args \"youtube:player_client=web\" \"{videoUrl}\"";
            var processResult = await RunProcessAsync(_ytDlpPath, arguments, null, cancellationToken);

            if (processResult.ExitCode != 0)
            {
                // โยน Exception พร้อม error output จาก yt-dlp
                throw new Exception($"yt-dlp failed to get metadata (Exit code: {processResult.ExitCode}): {processResult.ErrorOutput}");
            }

            // ตรวจสอบว่า StandardOutput ไม่ว่างเปล่าก่อน Parse
            if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
            {
                throw new Exception("yt-dlp returned empty metadata output.");
            }

            try
            {
                return JObject.Parse(processResult.StandardOutput);
            }
            catch (Newtonsoft.Json.JsonReaderException ex)
            {
                throw new Exception($"Failed to parse yt-dlp metadata JSON: {ex.Message}. Output: {processResult.StandardOutput}");
            }
        }

        // เมธอดสำหรับดาวน์โหลดวิดีโอ พร้อมรายงานความคืบหน้า
        public async Task DownloadVideoAsync(
            string videoUrl,
            string outputPath,
            IProgress<YtDlpProgressInfo> progress,
            CancellationToken cancellationToken = default)
        {
            // -f bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best
            // นี่คือ argument สำหรับดาวน์โหลดวิดีโอคุณภาพดีที่สุด (mp4) และเสียง (m4a) แล้วรวมกัน
            // หรือถ้าไม่มีแบบแยก ก็ดาวน์โหลดแบบรวมที่ดีที่สุด
            // เพิ่ม --ffmpeg-location หากคุณต้องการระบุ path ของ ffmpeg ที่ชัดเจน
            // var arguments = $"-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best\" -o \"{outputPath}\" --progress --newline --no-warnings --ffmpeg-location \"{_ffmpegPath}\" \"{videoUrl}\"";

            // Argument ที่ไม่มี --ffmpeg-location (yt-dlp จะหา ffmpeg เอง หรือใช้ตัวในแพ็คเกจ)
            var arguments = $"-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best\" -o \"{outputPath}\" --progress --newline --no-warnings \"{videoUrl}\"";


            await RunProcessAsync(_ytDlpPath, arguments, (currentOutputLine, currentErrorLine) =>
            {
                var info = new YtDlpProgressInfo();

                // Handle error line first, if present
                if (!string.IsNullOrEmpty(currentErrorLine))
                {
                    Console.WriteLine($"yt-dlp Error: {currentErrorLine}");
                    info.CurrentStatusMessage = $"ข้อผิดพลาดจาก yt-dlp: {currentErrorLine}";
                    info.IsMerging = false; // Error means no merging
                }
                // Only process outputLine if it's not empty and no error line was just processed
                else if (!string.IsNullOrEmpty(currentOutputLine))
                {
                    info.CurrentStatusMessage = currentOutputLine; // Default status message

                    // Try to parse download progress
                    var downloadMatch = Regex.Match(currentOutputLine, @"\[download\]\s+(?<percent>\d+\.?\d*)%\s+of\s+(?<totalSize>\d+\.?\d*\w{2,3})(?:\s+at\s+(?<speed>\d+\.?\d*\w{2,3}/s))?(?:\s+ETA\s+(?<eta>\d{2}:\d{2}))?");
                    if (downloadMatch.Success)
                    {
                        if (double.TryParse(downloadMatch.Groups["percent"].Value, out double percentage))
                        {
                            info.Percentage = percentage / 100.0;
                        }
                        info.DownloadedSize = ""; // yt-dlp often doesn't give downloaded size directly in this progress line
                        info.TotalSize = downloadMatch.Groups["totalSize"].Value;
                        info.Speed = downloadMatch.Groups["speed"].Value;
                        info.Eta = downloadMatch.Groups["eta"].Value;
                        info.IsMerging = false; // Clearly a download progress line
                    }
                    else // Not a standard download progress line, check for other statuses
                    {
                        // Parse merge status (yt-dlp will print [Merger] if it's merging)
                        var mergeMatch = Regex.Match(currentOutputLine, @"\[Merger\]\s*(.*)");
                        if (mergeMatch.Success)
                        {
                            info.IsMerging = true;
                            info.CurrentStatusMessage = $"กำลังรวมไฟล์: {mergeMatch.Groups[1].Value}";
                            var timeMatch = Regex.Match(currentOutputLine, @"(\d{2}:\d{2}:\d{2})");
                            if (timeMatch.Success) info.MergeTime = timeMatch.Groups[1].Value;
                        }
                        else if (currentOutputLine.Contains("[download] Destination:"))
                        {
                            info.CurrentStatusMessage = "ดาวน์โหลดเสร็จสิ้น เตรียมรวมไฟล์...";
                            info.IsMerging = true; // Indicate merging is about to start
                        }
                        else if (currentOutputLine.Contains("[ExtractAudio]") || currentOutputLine.Contains("[Convertor]"))
                        {
                            info.CurrentStatusMessage = currentOutputLine;
                            info.IsMerging = true; // Indicate conversion/extraction phase
                        }
                        // Add more conditions here if yt-dlp has other specific output lines for live streams
                        // that don't match the download progress regex.
                        // For live streams, yt-dlp output can be less predictable.
                        // You might see messages about "Downloading live stream segment" or similar.
                        // The default info.CurrentStatusMessage = currentOutputLine; at the top will catch these.
                    }
                }
                else // Both currentOutputLine and currentErrorLine are null/empty, or not handled specifically
                {
                    // This case should ideally not happen if e.Data is checked in RunProcessAsync.
                    // But if it does, you might want to log it or set a generic status.
                    // info.CurrentStatusMessage = "กำลังรอข้อมูล..."; // Uncomment if you want a default "waiting" message
                }

                progress?.Report(info); // ส่ง YtDlpProgressInfo object กลับไป
            }, cancellationToken);
        }

        // เมธอดสำหรับรัน Process ทั่วไป (ใช้ภายใน)
        private async Task<(int ExitCode, string StandardOutput, string ErrorOutput)> RunProcessAsync(
            string fileName,
            string arguments,
            Action<string, string> outputHandler, // Action to handle live output (standardOutput, errorOutput)
            CancellationToken cancellationToken)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, // สำคัญมากสำหรับภาษาไทย/Unicode
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = processStartInfo };

            var standardOutput = new StringBuilder();
            var errorOutput = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                // ตรวจสอบ e.Data ไม่ให้เป็น null ก่อนใช้งาน
                if (e.Data != null)
                {
                    standardOutput.AppendLine(e.Data);
                    outputHandler?.Invoke(e.Data, null); // ส่ง outputLine (ไม่ส่ง errorLine)
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                // ตรวจสอบ e.Data ไม่ให้เป็น null ก่อนใช้งาน
                if (e.Data != null)
                {
                    errorOutput.AppendLine(e.Data);
                    outputHandler?.Invoke(null, e.Data); // ส่ง errorLine (ไม่ส่ง outputLine)
                }
            };

            if (!process.Start())
            {
                throw new Exception($"ไม่สามารถเริ่มกระบวนการ {fileName} ได้");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                var waitForExitTask = process.WaitForExitAsync(cancellationToken);
                await Task.WhenAny(waitForExitTask, Task.Delay(Timeout.Infinite, cancellationToken)); // Wait indefinitely or until cancelled

                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(); } catch { /* ignore */ }
                    throw new OperationCanceledException("กระบวนการถูกยกเลิก");
                }

                await waitForExitTask; // รอให้แน่ใจว่า Process จบแล้ว และ Output/Error Streams ถูกปิดสนิท

                return (process.ExitCode, standardOutput.ToString(), errorOutput.ToString());
            }
            finally
            {
                // หยุดการอ่าน stream เพื่อป้องกัน resource leaks
                process.CancelOutputRead();
                process.CancelErrorRead();
            }
        }
    }
}
