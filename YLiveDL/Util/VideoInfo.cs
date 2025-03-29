using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YLiveDL.Util
{
 
    public class VideoInfo
    {
        public Video VideoData { get; set; } = new Video();
        public long? FileSize { get; set; } // ขนาดไฟล์โดยประมาณ (ไบต์)
        public string FormattedFileSize { get; set; } // ขนาดไฟล์ที่จัดรูปแบบแล้ว (เช่น "125 MB")
    }
}
