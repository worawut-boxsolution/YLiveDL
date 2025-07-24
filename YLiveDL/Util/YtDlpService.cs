using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YLiveDL.Util;
namespace YLiveDL.Util
{
    public class YtDlpService
    {
        private string _ytDlpPath;

        public YtDlpService()
        {
            // ค้นหา yt-dlp.exe คล้าย FindFfmpegPath() ของคุณ
            _ytDlpPath = FindYtDlpPath();
            if (string.IsNullOrEmpty(_ytDlpPath))
            {
                throw new Exception("ไม่พบ yt-dlp.exe ในระบบ โปรดตรวจสอบการติดตั้ง");
            }
        }

        private string FindYtDlpPath()
        {
            // Implement logic to find yt-dlp.exe (similar to FindFfmpegPath)
            var customPath = Path.Combine(AppContext.BaseDirectory, "utils", "yt-dlp.exe");
            if (File.Exists(customPath)) return customPath;

            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, "yt-dlp.exe");
                if (File.Exists(fullPath)) return fullPath;
            }

            return null;
        }

        // เมธอดสำหรับดึงข้อมูลวิดีโอ
        public async Task<JObject> GetVideoMetadataAsync(string videoUrl, CancellationToken cancellationToken = default)
        {
            var arguments = $"--dump-json --no-warnings --flat-playlist \"{videoUrl}\"";
            var processResult = await RunProcessAsync(_ytDlpPath, arguments, null, cancellationToken);

            if (processResult.ExitCode != 0)
            {
                throw new Exception($"yt-dlp failed to get metadata (Exit code: {processResult.ExitCode}): {processResult.ErrorOutput}");
            }

            return JObject.Parse(processResult.StandardOutput);
        }

        // เมธอดสำหรับดาวน์โหลดวิดีโอ พร้อมรายงานความคืบหน้า
        //public async Task DownloadVideoAsync(string videoUrl, string outputPath, IProgress<double> progress, IProgress<string> progressMerg, CancellationToken cancellationToken = default)
        //{
        //    // -f bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best
        //    // นี่คือ argument สำหรับดาวน์โหลดวิดีโอคุณภาพดีที่สุด (mp4) และเสียง (m4a) แล้วรวมกัน
        //    // หรือถ้าไม่มีแบบแยก ก็ดาวน์โหลดแบบรวมที่ดีที่สุด
        //    var arguments = $"-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best\" -o \"{outputPath}\" --progress --newline --no-warnings \"{videoUrl}\"";

        //    await RunProcessAsync(_ytDlpPath, arguments, (outputLine, errorLine) =>
        //    {
        //        // Logic to parse progress from outputLine/errorLine
        //        // yt-dlp output for progress looks like:
        //        // [download] 10.1% of 12.34MiB at 5.67MiB/s ETA 00:02
        //        // [Merger] Merging formats into "output.mp4"
        //        var downloadMatch = Regex.Match(outputLine, @"\[download\]\s+(\d+\.?\d*)%");
        //        if (downloadMatch.Success && progress != null)
        //        {
        //            if (double.TryParse(downloadMatch.Groups[1].Value, out double percentage))
        //            {
        //                progress.Report(percentage / 100.0);
        //            }
        //        }

        //        if (outputLine.Contains("[Merger]") && progressMerg != null)
        //        {
        //            progressMerg.Report(outputLine); // รายงานสถานะการรวมไฟล์
        //        }
        //        else if (!string.IsNullOrEmpty(outputLine) && !outputLine.StartsWith("[download]")) // แสดงข้อความอื่นๆ ที่ไม่ใช่ progress
        //        {
        //            progressMerg?.Report(outputLine);
        //        }

        //        if (!string.IsNullOrEmpty(errorLine))
        //        {
        //            Console.WriteLine($"yt-dlp Error: {errorLine}");
        //            progressMerg?.Report($"ข้อผิดพลาดจาก yt-dlp: {errorLine}");
        //        }

        //    }, cancellationToken);
        //}
        // ใน YtDlpService.cs
        // ... (ส่วนอื่นๆ ของ YtDlpService)

        public async Task DownloadVideoAsync(
            string videoUrl,
            string outputPath,
            IProgress<YtDlpProgressInfo> progress, // เปลี่ยนเป็น IProgress<YtDlpProgressInfo>
            CancellationToken cancellationToken = default)
        {
            // ... (logic หา arguments)
            var arguments = $"-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best\" -o \"{outputPath}\" --progress --newline --no-warnings \"{videoUrl}\"";


            await RunProcessAsync(_ytDlpPath, arguments, (outputLine, errorLine) =>
            {
                var info = new YtDlpProgressInfo();
                info.CurrentStatusMessage = outputLine; // Default to raw output

                // Parse download progress
                var downloadMatch = Regex.Match(outputLine, @"\[download\]\s+(?<percent>\d+\.?\d*)%\s+of\s+(?<totalSize>\d+\.?\d*\w{2,3})(?:\s+at\s+(?<speed>\d+\.?\d*\w{2,3}/s))?(?:\s+ETA\s+(?<eta>\d{2}:\d{2}))?");
                if (downloadMatch.Success)
                {
                    if (double.TryParse(downloadMatch.Groups["percent"].Value, out double percentage))
                    {
                        info.Percentage = percentage / 100.0;
                    }
                    info.DownloadedSize = ""; // yt-dlp doesn't directly give downloaded size in this format, only percentage and total
                    info.TotalSize = downloadMatch.Groups["totalSize"].Value;
                    info.Speed = downloadMatch.Groups["speed"].Value;
                    info.Eta = downloadMatch.Groups["eta"].Value;
                }

                // Parse merge status (yt-dlp will print [Merger] if it's merging)
                var mergeMatch = Regex.Match(outputLine, @"\[Merger\]\s*(.*)");
                if (mergeMatch.Success)
                {
                    info.IsMerging = true;
                    info.CurrentStatusMessage = $"กำลังรวมไฟล์: {mergeMatch.Groups[1].Value}";
                    // Attempt to extract time from merge message if available (e.g., "[Merger] 100% of 00:10:00")
                    var timeMatch = Regex.Match(outputLine, @"(\d{2}:\d{2}:\d{2})");
                    if (timeMatch.Success) info.MergeTime = timeMatch.Groups[1].Value;
                }
                else if (outputLine.Contains("[download] Destination:")) // Signifies download complete, merging might start next
                {
                    info.CurrentStatusMessage = "ดาวน์โหลดเสร็จสิ้น เตรียมรวมไฟล์...";
                    info.IsMerging = true;
                }
                else if (outputLine.Contains("[ExtractAudio]") || outputLine.Contains("[Convertor]"))
                {
                    info.CurrentStatusMessage = outputLine;
                    info.IsMerging = true; // Indicate merging/conversion phase
                }

                // Handle errors from stderr
                if (!string.IsNullOrEmpty(errorLine))
                {
                    Console.WriteLine($"yt-dlp Error: {errorLine}");
                    info.CurrentStatusMessage = $"ข้อผิดพลาดจาก yt-dlp: {errorLine}"; // แสดงข้อผิดพลาดใน status message
                }

                progress?.Report(info); // ส่ง YtDlpProgressInfo object กลับไป
            }, cancellationToken);
        }

        // ... (RunProcessAsync เหมือนเดิม)
        // เมธอดสำหรับรัน Process ทั่วไป (ใช้ภายใน)
        private async Task<(int ExitCode, string StandardOutput, string ErrorOutput)> RunProcessAsync(
            string fileName,
            string arguments,
            Action<string, string> outputHandler, // Action to handle live output
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
                if (!string.IsNullOrEmpty(e.Data))
                {
                    standardOutput.AppendLine(e.Data);
                    outputHandler?.Invoke(e.Data, null); // ส่ง output ไปยัง handler
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorOutput.AppendLine(e.Data);
                    outputHandler?.Invoke(null, e.Data); // ส่ง error output ไปยัง handler
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
                await Task.WhenAny(waitForExitTask, Task.Delay(Timeout.Infinite, cancellationToken));

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
                process.CancelOutputRead();
                process.CancelErrorRead();
            }
        }
    }
}

