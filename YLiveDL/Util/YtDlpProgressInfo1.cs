namespace YLiveDL.Util
{
   
    public class YtDlpProgressInfo
    {
        public double Percentage { get; set; } // 0.0 - 1.0
        public string DownloadedSize { get; set; } // เช่น "10.5 MiB"
        public string TotalSize { get; set; } // เช่น "100.0 MiB"
        public string Speed { get; set; } // เช่น "2.34 MiB/s"
        public string Eta { get; set; } // เช่น "00:05"
        public string CurrentStatusMessage { get; set; } // ข้อความสถานะทั่วไปจาก yt-dlp
        public bool IsMerging { get; set; } // true ถ้า yt-dlp กำลังรวมไฟล์
        public string MergeTime { get; set; } // เวลาที่ FFmpeg (หรือ yt-dlp) ประมวลผล (ถ้ามี)
    }
}