﻿using Microsoft.Extensions.Logging;
using BlazorDownloadFile;
using YoutubeExplode;
using YLiveDL.Util;
 
namespace YLiveDL
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddBlazorDownloadFile();

            builder.Services.AddSingleton<YtDlpService>();
            builder.Services.AddSingleton<YtDlpServiceLive>();
            builder.Services.AddSingleton<YouTubeDownloadService>();
            builder.Services.AddSingleton<YoutubeClient>();
            builder.Services.AddSingleton<YouTubeLiveDownloadService>();
            //builder.Services.AddSingleton<IFileSaver, FileSaver>();
#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();

    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
