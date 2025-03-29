using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;

namespace YLiveDL.Util
{
    public class Video
    {
        public string Id { get; set; }

        public string Url { get; set; }

        public string Title { get; set; }

        public Author Author { get; set; }

        //
        // Summary:
        //     Video upload date.
        public DateTimeOffset UploadDate { get; set; }

        //
        // Summary:
        //     Video description.
        public string Description { get; set; }

        public TimeSpan? Duration { get; set; }

        public  List<Thumbnail> Thumbnails { get; set; }

        //
        // Summary:
        //     Available search keywords for the video.
        public  List<string> Keywords { get; set; }

        //
        // Summary:
        //     Engagement statistics for the video.
        public Engagement Engagement { get; set; }

        //
        // Summary:
        //     Metadata associated with a YouTube video.

    }
}
