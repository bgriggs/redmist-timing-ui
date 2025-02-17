using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.Views;
using System.Collections.Generic;
using System.Threading;

namespace RedMist.Timing.UI;

public partial class App : Application
{
    private IHost? _host;
    private CancellationTokenSource? _cancellationTokenSource;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);

        // Dependency injection: https://github.com/stevemonaco/AvaloniaViewModelFirstDemos
        // NuGet source: https://pkgs.dev.azure.com/dotnet/CommunityToolkit/_packaging/CommunityToolkit-Labs/nuget/v3/index.json
        var locator = new ViewLocator();
        DataTemplates.Add(locator);

        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddDebug();
        });
        services.AddSingleton(loggerFactory);

        var tempConfig = new Dictionary<string, string?>
        {
             { "Server:Url", "http://10.0.0.6:5179/TimingAndScoring" },
             { "Hub:Url", "http://10.0.0.6:5179/ts-hub" },
             { "Keycloak:AuthServerUrl", "https://sunnywood.redmist.racing/dev/auth/" },
             { "Keycloak:Realm", "redmist" },
             { "Keycloak:ClientId", "***REMOVED***" },
             { "Keycloak:ClientSecret", "***REMOVED***" },
        };
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(tempConfig)
            .Build();
        services.AddSingleton(config);

        ConfigureServices(services);
        ConfigureViewModels(services);
        ConfigureViews(services);

        services.AddSingleton(service => new MainWindow
        {
            DataContext = service.GetRequiredService<MainViewModel>()
        });


        _host = builder.Build();
        _cancellationTokenSource = new();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
            desktop.ShutdownRequested += OnShutdownRequested;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var vm = _host.Services.GetRequiredService<MainViewModel>();
            var mainView = _host.Services.GetRequiredService<MainView>();
            mainView.DataContext = vm;
            singleViewPlatform.MainView = mainView;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
       => _ = _host!.StopAsync(_cancellationTokenSource!.Token);

    [Transient(typeof(EventClient))]
    [Singleton(typeof(HubClient))]
    internal static partial void ConfigureServices(IServiceCollection services);

    [Singleton(typeof(MainViewModel))]
    [Singleton(typeof(EventsListViewModel))]
    [Singleton(typeof(EventStatusViewModel))]
    ////[Singleton(typeof(QuarterViewModelFactory), typeof(IQuarterViewModelFactory))]
    internal static partial void ConfigureViewModels(IServiceCollection services);

    [Singleton(typeof(MainView))]
    [Singleton(typeof(EventsListView))]
    internal static partial void ConfigureViews(IServiceCollection services);
}
