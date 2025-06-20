using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.Views;
using RedMist.TimingCommon.Models;
using System;
using System.IO;
using System.Reactive.Linq;
using System.Reflection;
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

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "RedMist.Timing.UI.appsettings.json";
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException("Configuration file not found.");
        builder.Configuration.AddJsonStream(stream);
        builder.Configuration.AddUserSecrets(assembly);

#if RELEASE
        // Add secrets for release builds
        var secretsResourceName = "RedMist.Timing.UI.secrets.release.json";
        using var secretsStream = assembly.GetManifestResourceStream(secretsResourceName) ?? throw new FileNotFoundException("Secrets configuration file not found.");
        builder.Configuration.AddJsonStream(secretsStream);
#endif

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddDebug();
        });
        services.AddSingleton(loggerFactory);

        ConfigureServices(services);
        ConfigureViewModels(services);
        //ConfigureViews(services);

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

            // Check for event ID passed into command line and jump to that event.
            if (desktop.Args?.Length > 0 && int.TryParse(desktop.Args[0], out var eventId))
            {
                var routerEvent = new RouterEvent { Path = "EventStatus", Data = eventId };
                WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
                //Observable.Timer(TimeSpan.FromMilliseconds(5000)).Subscribe(_ => Dispatcher.UIThread.Post(() => WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent))));
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var vm = _host.Services.GetRequiredService<MainViewModel>();
            var mainView = new MainView { DataContext = vm };
            singleViewPlatform.MainView = mainView;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
       => _ = _host!.StopAsync(_cancellationTokenSource!.Token);

    public T GetService<T>() where T : class
        => _host!.Services.GetRequiredService<T>();

    [Transient(typeof(EventClient))]
    [Singleton(typeof(HubClient))]
    [Transient(typeof(OrganizationClient))]
    [Singleton(typeof(ViewSizeService))]
    internal static partial void ConfigureServices(IServiceCollection services);

    [Singleton(typeof(MainViewModel))]
    [Singleton(typeof(EventsListViewModel))]
    [Singleton(typeof(LiveTimingViewModel))]
    internal static partial void ConfigureViewModels(IServiceCollection services);
}
