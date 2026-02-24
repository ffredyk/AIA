using Microsoft.Extensions.Logging;
using AIA.Mobile.Data;
using AIA.Mobile.Pages;
using AIA.Mobile.ViewModels;
using CommunityToolkit.Maui;

namespace AIA.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register database
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "aia.db3");
        builder.Services.AddSingleton(s => new AiaDatabase(dbPath));

        // Register ViewModels
        builder.Services.AddTransient<TasksViewModel>();
        builder.Services.AddTransient<RemindersViewModel>();
        builder.Services.AddTransient<DataBanksViewModel>();
        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Register Pages
        builder.Services.AddTransient<TasksPage>();
        builder.Services.AddTransient<RemindersPage>();
        builder.Services.AddTransient<DataBanksPage>();
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
