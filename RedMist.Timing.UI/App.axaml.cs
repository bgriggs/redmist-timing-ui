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
using Microsoft.Extensions.Logging.Abstractions;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.Views;
using Sentry;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI;

public partial class App : Application
{
    private IHost? _host;
    private CancellationTokenSource? _cancellationTokenSource;
    private ILogger? _logger;

    /// <summary>
    /// Factory for creating platform-specific screen wake service.
    /// Set by platform projects (Android/iOS) before app initialization.
    /// </summary>
    public static Func<IScreenWakeService>? ScreenWakeServiceFactory { get; set; }


    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Set up global exception handlers as early as possible
        SetupGlobalExceptionHandlers();

#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if DEBUG
        //McpRuntimeInspectorExtension.Initialize();
#endif

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

            // Every catch block in the app reports through ILogger, so hanging Sentry off the
            // logging pipeline captures all of them without touching the call sites. The SDK itself
            // is started by the platform head, hence InitializeSdk = false - this provider only
            // attaches to the existing hub.
            //
            // Breadcrumbs start at Warning rather than Information for the same reason
            // InMemoryLogProvider keeps a separate problem buffer: HubClient logs a line per session
            // patch, roughly once a second, which would fill the breadcrumb ring with routine
            // traffic and push out the context that actually explains a crash.
            builder.AddSentry(o =>
            {
                o.InitializeSdk = false;
                o.MinimumEventLevel = LogLevel.Error;
                o.MinimumBreadcrumbLevel = LogLevel.Warning;
            });
        });
        services.AddSingleton(loggerFactory);

        // Add in-memory log provider for UI display
        var inMemoryLogProvider = new InMemoryLogProvider(50);
        services.AddSingleton(inMemoryLogProvider);
        loggerFactory.AddProvider(inMemoryLogProvider);

        // After the in-app log provider is attached, deliberately. A debug build has no Sentry - the
        // DSN comes from the same release-only secrets file - so the log viewer inside the app is
        // the only place this line can be read on a device, which is where the question "why did
        // nothing load" actually gets asked.
        ReportBlankRequiredConfiguration(builder.Configuration, loggerFactory);

        // Add HttpClient factory
        services.AddHttpClient();

        ConfigureServices(services);

        // Register preferences service
        services.AddSingleton<IPreferencesService, PreferencesService>();

        // Register screen wake service (platform-specific if available, otherwise no-op)
        if (ScreenWakeServiceFactory is not null)
        {
            services.AddSingleton(ScreenWakeServiceFactory());
        }
        else
        {
            services.AddSingleton<IScreenWakeService, NoOpScreenWakeService>();
        }

        // Register version check services
        services.AddSingleton<IPlatformDetectionService, PlatformDetectionService>();
        services.AddSingleton<IUpdateMessageService, UpdateMessageService>();
        services.AddSingleton<IVersionCheckService, VersionCheckService>();

        ConfigureViewModels(services);
        //ConfigureViews(services);

        services.AddSingleton(service => new MainWindow
        {
            DataContext = service.GetRequiredService<MainViewModel>()
        });

        _host = builder.Build();
        _cancellationTokenSource = new();

        // Initialize logger after host is built
        _logger = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<App>();

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

    private void SetupGlobalExceptionHandlers()
    {
        // Handle unhandled exceptions on the UI thread (Avalonia-specific)
        Dispatcher.UIThread.UnhandledException += OnUIThreadUnhandledException;

        // Handle unhandled exceptions from the AppDomain (general .NET)
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // Handle unobserved task exceptions (async operations)
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Handle first chance exceptions (optional - for debugging)
        // AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    /// <summary>
    /// Handles an exception that reached the top of the Avalonia dispatcher loop.
    /// </summary>
    /// <remarks>
    /// This used to set Handled = true unconditionally. That is why the native crashes were
    /// unattributable: a fault that should have produced a managed stack trace was swallowed, the
    /// app carried on with whatever state the aborted operation left behind, and the process
    /// eventually died somewhere unrelated - by which point the tombstone showed nothing but
    /// libmonosgen frames. Suppressing the crash did not avoid it, it only removed the evidence.
    ///
    /// The exception is now reported, the queue is flushed while the process is still alive, and
    /// the fault is allowed to terminate the app so the report names the real cause. Set
    /// Sentry:CrashOnUnhandledUiException to false to go back to suppressing, which still reports
    /// first - the option exists so that a crash loop found at an event can be turned off in a
    /// build without reworking this handler.
    /// </remarks>
    private void OnUIThreadUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            // Gated on IsEnabled as well as the setting: with no DSN provisioned there is nothing
            // to report the crash, so terminating would cost the user their session and produce no
            // evidence in exchange - strictly worse than the suppression this replaced. A build
            // without reporting therefore behaves as it always did.
            var fatal = CrashReporting.IsEnabled && CrashReporting.CrashOnUnhandledUiException;

            // On the fatal path the Sentry event comes from CaptureFatal, which marks it unhandled
            // and ends the session as crashed. Logging at Error as well would report the same fault
            // a second time, so it drops to Warning: still on the on-device display, still a
            // breadcrumb, but not a duplicate event.
            LogException("UI Thread Unhandled Exception", e.Exception,
                fatal ? LogLevel.Warning : LogLevel.Error);

            if (fatal)
            {
                CrashReporting.CaptureFatal(e.Exception, "Dispatcher.UnhandledException");

                // The process is about to end, so the background queue will not get a chance to
                // deliver on its own. Only worth blocking for on this path - in the suppress path
                // below the app keeps running and a flush would just stall the UI thread.
                CrashReporting.Flush(TimeSpan.FromSeconds(2));

                // Leave Handled false: let it propagate and take the process down.
                return;
            }

            e.Handled = true;
            ShowErrorToUser("An unexpected error occurred. The application will continue running.", e.Exception);
        }
        catch (Exception ex)
        {
            // Fallback logging if logger fails
            System.Diagnostics.Debug.WriteLine($"Critical error in exception handler: {ex}");
            Console.WriteLine($"Critical error in exception handler: {ex}");
        }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is Exception exception)
            {
                // If the process is terminating, we can't prevent it, but we can report it. The
                // event comes from CaptureFatal for the same reason as the dispatcher path, so the
                // log drops to Warning rather than raising a duplicate. CaptureFatal is idempotent
                // per exception instance, so a fault that already reported on the dispatcher on its
                // way up is not counted twice here.
                if (e.IsTerminating)
                {
                    LogException("Application is terminating due to unhandled exception", exception,
                        LogLevel.Warning);
                    CrashReporting.CaptureFatal(exception, "AppDomain.UnhandledException");
                    CrashReporting.Flush(TimeSpan.FromSeconds(2));
                }
                else
                {
                    LogException("AppDomain Unhandled Exception", exception, LogLevel.Error);
                }
            }
            else
            {
                LogException("AppDomain Unhandled Exception (Non-Exception object)", new Exception($"Unknown exception object: {e.ExceptionObject}"));
            }
        }
        catch (Exception ex)
        {
            // Fallback logging if logger fails
            System.Diagnostics.Debug.WriteLine($"Critical error in AppDomain exception handler: {ex}");
            Console.WriteLine($"Critical error in AppDomain exception handler: {ex}");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            // Cancellation is how the app shuts down background work, so reporting it as an error
            // would bury the real faults under noise from every navigation away from a live event.
            // Downgraded rather than dropped: it still reaches the on-device log, but sits below
            // both the Sentry event and breadcrumb thresholds. Note that an HttpClient timeout is
            // not cancellation - see CrashReporting.IsDeliberateCancellation.
            LogException("Unobserved Task Exception", e.Exception,
                CrashReporting.IsDeliberateCancellation(e.Exception)
                    ? LogLevel.Information
                    : LogLevel.Error);

            // Mark as observed to prevent crash
            e.SetObserved();
        }
        catch (Exception ex)
        {
            // Fallback logging if logger fails
            System.Diagnostics.Debug.WriteLine($"Critical error in Task exception handler: {ex}");
            Console.WriteLine($"Critical error in Task exception handler: {ex}");
        }
    }

    /// <param name="level">Error raises a Sentry event through the logging provider. Warning does
    /// not, and is used on paths where <see cref="CrashReporting.CaptureFatal"/> reports the event
    /// itself - it still reaches the on-device display and rides along as a breadcrumb.</param>
    private void LogException(string context, Exception exception, LogLevel level = LogLevel.Error)
    {
        try
        {
            if (_logger != null)
            {
                // Reaches Sentry through the logging provider registered in
                // OnFrameworkInitializationCompleted.
                _logger.Log(level, exception, "Global Exception Handler: {Context}", context);
            }
            else
            {
                // No host yet, so there is no logging pipeline to carry this to Sentry. Faults
                // during startup are the ones least likely to be reproducible on a developer
                // machine, so report them directly rather than losing them.
                //
                // Gated on the level for the same reason the logging path is: a terminal fault is
                // reported by CaptureFatal, and capturing here as well would raise a second issue
                // for one crash. It would also turn the Information-level cancellation downgrade
                // into a full error event, which is precisely what that downgrade avoids.
                if (level >= LogLevel.Error)
                {
                    SentrySdk.CaptureException(exception, scope => scope.SetTag("handler", context));
                }

                System.Diagnostics.Debug.WriteLine($"Global Exception Handler - {context}: {exception}");
                Console.WriteLine($"Global Exception Handler - {context}: {exception}");
            }
        }
        catch
        {
            // Last resort fallback
            System.Diagnostics.Debug.WriteLine($"Failed to log exception: {exception}");
            Console.WriteLine($"Failed to log exception: {exception}");
        }
    }

    private static void ShowErrorToUser(string message, Exception exception)
    {
        try
        {
#if DEBUG
            message += $"\n\nDebug Info: {exception.Message}";
#endif

            System.Diagnostics.Debug.WriteLine($"Error shown to user: {message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to show error to user: {ex}");
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
       => _ = _host!.StopAsync(_cancellationTokenSource!.Token);

    /// <summary>
    /// Names any required setting that is missing or blank, once, at startup.
    /// </summary>
    /// <remarks>
    /// The Keycloak realm, client id and client secret ship blank in appsettings.json, to be filled
    /// in by the secrets file that only a release build embeds. The "?? throw" guards where they are
    /// read never fire on those, because an empty string is not null, so a build without the secrets
    /// went on to ask Keycloak for a token as client "" in realm "" and the failure surfaced layers
    /// away with nothing naming a setting. The other keys below already carry values, and are here
    /// because they are read through the same kind of guard and share the same gap.
    ///
    /// Reported rather than thrown. These clients are built while the first view model is resolved,
    /// so throwing would stop the app from starting, and a debug build on a device has no way to
    /// supply the values - user secrets come from a path that exists on a developer's machine, not
    /// on a phone. Running with the server-backed parts broken beats not running at all when the
    /// work is on everything else.
    ///
    /// Key names only. The values are credentials.
    /// </remarks>
    private static void ReportBlankRequiredConfiguration(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        string[] requiredKeys =
        [
            "Server:EventUrl",
            "Server:OrganizationUrl",
            "Server:SponsorUrl",
            "Hub:Url",
            "Keycloak:AuthServerUrl",
            "Keycloak:Realm",
            "Keycloak:ClientId",
            "Keycloak:ClientSecret",
            "Cdn:ArchiveUrl",
        ];

        var blank = requiredKeys.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToArray();
        if (blank.Length == 0)
        {
            return;
        }

        loggerFactory.CreateLogger(nameof(App)).LogError(
            "Configuration missing or blank: {Keys}. Server and hub requests will fail to authenticate.",
            string.Join(", ", blank));
    }

    public T GetService<T>() where T : class
        => _host!.Services.GetRequiredService<T>();

    /// <summary>
    /// Resolves a logger for types that aren't constructed through DI, such as views.
    /// </summary>
    /// <remarks>
    /// Falls back to a no-op logger when there is no host - the designer, and the window between
    /// Initialize and OnFrameworkInitializationCompleted - so callers never have to null-check.
    /// Prefer constructor injection wherever the type is built by the container.
    /// </remarks>
    public static ILogger GetLogger(string category)
    {
        try
        {
            if (Current is App { _host: not null } app)
            {
                return app._host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);
            }
        }
        catch
        {
            // Fall through to the no-op logger below.
        }

        return NullLogger.Instance;
    }

    [Transient(typeof(EventClient))]
    [Singleton(typeof(HubClient))]
    [Transient(typeof(OrganizationClient))]
    [Transient(typeof(SponsorClient))]
    [Singleton(typeof(ViewSizeService))]
    [Singleton(typeof(EventContext))]
    [Singleton(typeof(OrganizationIconCacheService))]
    [Singleton(typeof(SponsorIconCacheService))]
    [Singleton(typeof(SponsorsService))]
    [Singleton(typeof(SponsorRotatorViewModel))]
    [Singleton(typeof(EventAccessCodeStore))]
    internal static partial void ConfigureServices(IServiceCollection services);

    [Singleton(typeof(MainViewModel))]
    [Singleton(typeof(EventsListViewModel))]
    [Singleton(typeof(LiveTimingViewModel))]
    internal static partial void ConfigureViewModels(IServiceCollection services);
}
