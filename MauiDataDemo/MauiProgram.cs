// MauiProgram.cs

using Microsoft.Extensions.Logging;

namespace MauiDataDemo;

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
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif
		builder.Logging.AddFilter("MauiDataDemo.*", LogLevel.Trace);

		builder.Services.AddTransient<MainPage>();

		return builder.Build();
	}
}
