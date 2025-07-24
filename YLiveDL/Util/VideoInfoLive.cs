using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YLiveDL.Util
{
    
    public class VideoInfoLive
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string Duration { get; set; }
        public bool IsLiveStream { get; set; }
        public string ThumbnailUrl { get; set; }
        public string Url { get; set; }
    }
}
