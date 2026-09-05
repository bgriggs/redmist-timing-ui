using BigMission.Shared.Auth;
using BigMission.Shared.SignalR;
using BigMission.Shared.Utilities;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.InCarDriverMode;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

/// <summary>
/// Client for communicating with the cloud SignalR hub.
/// </summary>
public class HubClient : HubClientBase
{
    private ActiveConnection? active;
    private ILogger Logger { get; }
    private int? subscribedEventId;
    private (int eventId, string car)? subscribedInCarDriverEventIdAndCar;
    private int? subscribedControlLogEventId;
    private (int eventId, string car)? subscribedCarControlLog;
    private readonly Debouncer debouncer = new(TimeSpan.FromMilliseconds(5));
    private readonly IConfiguration configuration;
    private readonly EventAccessCodeStore accessCodeStore;
    private long sessionUpdateCount;
    private long lastEventMessageTicks;

    /// <summary>
    /// Whether the hub subscription is currently up.
    /// </summary>
    /// <remarks>
    /// Read by the live timing screen to decide whether it still needs to poll for a full session
    /// state. This is the connection's own state rather than anything inferred from the data, so it
    /// stays true through a legitimately quiet feed and goes false the moment the transport drops.
    /// </remarks>
    public virtual bool IsConnected => Volatile.Read(ref active)?.Hub.State == HubConnectionState.Connected;

    /// <summary>
    /// When the hub last delivered on the subscribed event's timing stream, or null if it never has.
    /// </summary>
    /// <remarks>
    /// Stamped where the messages arrive rather than where they are applied. The live timing screen
    /// routes its own polled results through the same recipients the hub feeds, so a clock kept at
    /// the receiving end would be reset by the poll and could never tell the screen to stop polling.
    ///
    /// Control logs deliberately do not stamp it. They arrive for the same subscription but only
    /// sporadically, and a clock they could advance would let one vouch for a timing feed that had
    /// stopped.
    ///
    /// Not reset on unsubscribe, and the bound rather than the tidiness is the argument. Carrying a
    /// timestamp into the next event can only make that event look healthy for as long as the value
    /// stays young - and if its subscription is broken nothing refreshes it, so it ages past the
    /// threshold within a tick or two. One skipped poll is the whole exposure. The other direction,
    /// a screen opened during an outage seeing a stale timestamp and polling, is the right answer
    /// anyway.
    /// </remarks>
    public virtual DateTime? LastEventMessageUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref lastEventMessageTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    private void StampEventMessage()
        => Interlocked.Exchange(ref lastEventMessageTicks, DateTime.UtcNow.Ticks);


    public HubClient(ILoggerFactory loggerFactory, IConfiguration configuration, EventAccessCodeStore accessCodeStore)
        : base(loggerFactory, configuration)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        ConnectionStatusChanged += HubClient_ConnectionStatusChanged;
        this.configuration = configuration;
        this.accessCodeStore = accessCodeStore;
    }


    protected override HubConnection GetConnection()
    {
        string hubUrl = configuration["Hub:Url"] ?? throw new InvalidOperationException("Hub URL is not configured.");
        string authUrl = configuration["Keycloak:AuthServerUrl"] ?? throw new InvalidOperationException("Keycloak URL is not configured.");
        string realm = configuration["Keycloak:Realm"] ?? throw new InvalidOperationException("Keycloak realm is not configured.");

        var builder = new HubConnectionBuilder().WithUrl(hubUrl, delegate (HttpConnectionOptions options)
        {
            options.AccessTokenProvider = async delegate
            {
                try
                {
                    var clientId = GetClientId();
                    var clientSecret = GetClientSecret();
                    return await KeycloakServiceToken.RequestClientToken(authUrl, realm, clientId, clientSecret);
                }
                catch (Exception exception)
                {
                    // Warning, not error: the connection retries on its own, and a persistent
                    // failure still surfaces as the connect error the base class logs.
                    Logger.LogWarning(exception, "Failed to get server hub access token");
                    return null;
                }
            };
        })
        .WithAutomaticReconnect(new InfiniteRetryPolicy())
        .TryAddMessagePack();

        var hubConnection = builder.Build();

        InitializeStateLogging(hubConnection);
        return hubConnection;
    }

    private void HubClient_ConnectionStatusChanged(HubConnectionState obj)
    {
        // The event carries no connection, and a retired connection's retry loop can still raise it,
        // so the current one is read here and everything downstream works on that instance.
        var connection = Volatile.Read(ref active);
        if (connection is null || connection.Hub.State != HubConnectionState.Connected)
            return;

        _ = debouncer.ExecuteAsync(async () =>
        {
            try
            {
                await ResubscribeAsync(connection);
            }
            catch (Exception ex)
            {
                // Nothing awaits the debounced task, so rethrowing only produces an
                // unobserved task exception. The log is the useful signal.
                Logger.LogError(ex, "Failed to restore hub subscriptions");
            }
        });
    }

    /// <summary>
    /// Re-issues every subscription this client is meant to be holding.
    /// </summary>
    /// <remarks>
    /// Server-side subscriptions live on the connection, so a reconnect starts from nothing. This
    /// runs on every transition to Connected, which also covers the subscriptions that could not be
    /// sent in the first place: StartConnection returns while the transport is still negotiating, so
    /// a view that subscribes immediately after entering an event is normally too early. That used
    /// to throw, get logged as an error, and leave the subscription silently missing until the user
    /// navigated away and back - which is where the control log would simply stop arriving.
    /// </remarks>
    private async Task ResubscribeAsync(ActiveConnection connection)
    {
        if (subscribedEventId is { } eventId)
        {
            var accessCode = accessCodeStore.Get(eventId);
            var subscribed = string.IsNullOrEmpty(accessCode)
                ? await TryInvokeAsync(connection, "SubscribeToEventV2", eventId)
                : await TryInvokeAsync(connection, "SubscribeToEventV2WithCode", eventId, accessCode);

            // Announced only once the server has actually taken the subscription, because the point
            // of the announcement is that the delta stream has resumed with a gap behind it. The
            // server sends no state on subscribe, so whoever is showing this event has to ask for a
            // whole one; see HubResubscribedNotification.
            if (subscribed)
            {
                WeakReferenceMessenger.Default.Send(new HubResubscribedNotification(eventId));
            }
        }
        else if (subscribedInCarDriverEventIdAndCar is { } inCar)
        {
            await TryInvokeAsync(connection, "SubscribeToInCarDriverEventV2", inCar.eventId, inCar.car,
                accessCodeStore.Get(inCar.eventId));
        }

        // Control logs ride on the same connection, so they are restored alongside the event
        // rather than waiting for the view to ask again. The two are independent - opening a car's
        // details while the control log tab is up holds both at once - so neither is an else of
        // the other, and one handler serves both because they share the ReceiveControlLog message.
        if (subscribedControlLogEventId is not null || subscribedCarControlLog is not null)
        {
            RegisterControlLogHandler(connection);
        }

        if (subscribedControlLogEventId is { } controlLogEventId)
        {
            await TryInvokeAsync(connection, "SubscribeToControlLogs", controlLogEventId,
                accessCodeStore.Get(controlLogEventId));
        }

        if (subscribedCarControlLog is { } carControlLog)
        {
            await TryInvokeAsync(connection, "SubscribeToCarControlLogs", carControlLog.eventId,
                carControlLog.car, accessCodeStore.Get(carControlLog.eventId));
        }
    }

    /// <summary>
    /// Invokes a hub method if the connection can carry it, and reports only the failures worth reporting.
    /// </summary>
    /// <remarks>
    /// A phone loses its connection whenever it changes network or sleeps, and SignalR answers an
    /// invoke on a connection that is not active by throwing. Neither that nor a cancellation during
    /// teardown is a fault, so both are logged below the level that raises a Sentry issue; anything
    /// else still reports as an error. Checking the state first leaves a small window where it
    /// changes underneath us, which is what the catch is for.
    /// </remarks>
    private async Task<bool> TryInvokeAsync(ActiveConnection? connection, string method, params object?[] args)
    {
        var hub = connection?.Hub;
        if (hub is null || hub.State != HubConnectionState.Connected)
        {
            Logger.LogInformation("Skipped {Method}: connection is {State}", method,
                hub is null ? "none" : hub.State.ToString());
            return false;
        }

        try
        {
            await hub.InvokeCoreAsync(method, args);
            Logger.LogInformation("Invoked {Method}", method);
            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
            || (ex is InvalidOperationException && hub.State != HubConnectionState.Connected))
        {
            // The connection went away between the check above and the call. An
            // InvalidOperationException while it is still connected is a real defect, so that one
            // is deliberately left to the error path below.
            Logger.LogWarning(ex, "Could not invoke {Method}: connection is {State}", method, hub.State);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to invoke {Method}", method);
            return false;
        }
    }

    /// <summary>
    /// Starts a new connection and publishes it as the current one.
    /// </summary>
    private ActiveConnection StartNewConnection()
    {
        var cancellation = new CancellationTokenSource();
        try
        {
            var connection = new ActiveConnection(StartConnection(cancellation.Token), cancellation);
            Volatile.Write(ref active, connection);
            return connection;
        }
        catch
        {
            // Nothing took ownership of it, so nothing else will release it.
            cancellation.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Retires one specific connection.
    /// </summary>
    /// <remarks>
    /// Takes the connection rather than reading the field, because callers reach here after a
    /// network round-trip: a slow unsubscribe for the event the user just left would otherwise
    /// dispose whatever connection happened to be current by then, which is the one for the event
    /// they have since opened. The field is only cleared if it still points at this connection.
    /// </remarks>
    private async Task DisposeConnectionAsync(ActiveConnection? connection)
    {
        if (connection is null || !connection.ClaimDisposal())
            return;

        Interlocked.CompareExchange(ref active, null, connection);

        // Cancel before disposing. HubClientBase retries the initial connect every ReconnectDelay
        // until it succeeds or its token is canceled, and that loop calls StartAsync on this
        // connection. Disposing a connection that never connected without stopping the loop leaves
        // it logging ObjectDisposedException as a connect error every five seconds for the rest of
        // the session - which is exactly the network conditions this teardown happens under.
        try
        {
            await connection.Cancellation.CancelAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to stop hub connection retries");
        }

        connection.Cancellation.Dispose();

        try
        {
            await connection.Hub.DisposeAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to dispose hub connection");
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    #region Car Timing Status

    public async Task SubscribeToEventAsync(int eventId)
    {
        // Recorded before the first await so a concurrent teardown cannot overtake it. The two
        // modes share one connection, so entering an event ends whatever the previous one held.
        subscribedEventId = eventId;
        subscribedInCarDriverEventIdAndCar = null;

        // Only another event's control log is stale. This method runs after a status refresh that
        // can take seconds, and the control log tab is tappable throughout, so by now it may have
        // already recorded its subscription for this event. Clearing that would strand the tab for
        // good: it only initializes on the transition into being selected, and it already is.
        if (subscribedControlLogEventId != eventId)
            subscribedControlLogEventId = null;
        if (subscribedCarControlLog is { } carControlLog && carControlLog.eventId != eventId)
            subscribedCarControlLog = null;

        await DisposeConnectionAsync(Volatile.Read(ref active));

        try
        {
            var connection = StartNewConnection();

            connection.Hub.Remove("ReceiveSessionPatch");
            connection.Hub.On("ReceiveSessionPatch", (SessionStatePatch ssp) => ProcessSessionMessage(ssp));

            connection.Hub.Remove("ReceiveCarPatches");
            connection.Hub.On("ReceiveCarPatches", (CarPositionPatch[] cpps) => ProcessCarPatches(cpps));

            connection.Hub.Remove("ReceiveReset");
            connection.Hub.On("ReceiveReset", ProcessReset);

            // Normally a no-op that logs a skip, because the transport is still negotiating and the
            // status handler will do the real work. It matters in the case where the connection got
            // there first, which would otherwise leave the subscription with nothing left to fire it.
            await ResubscribeAsync(connection);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to subscribe to event");
        }
    }

    public async Task UnsubscribeFromEventAsync(int eventId)
    {
        var connection = Volatile.Read(ref active);

        // Whether this call is still the one that owns the connection. Leaving an event is queued
        // on the thread pool, so it can land after the user has opened the next one - and then it
        // must clear nothing and dispose nothing. Declining leaks neither: whoever took the intent
        // owns that connection's teardown.
        var owned = subscribedEventId == eventId;
        if (owned)
            subscribedEventId = null;
        if (subscribedControlLogEventId == eventId)
            subscribedControlLogEventId = null;
        if (subscribedCarControlLog?.eventId == eventId)
            subscribedCarControlLog = null;

        await TryInvokeAsync(connection, "UnsubscribeFromEventV2", eventId);

        // Released whether or not the server was reachable to be told. Skipping the dispose on a
        // failed invoke left the old connection reconnecting forever and still delivering patches
        // for an event the user had left.
        if (owned)
            await DisposeConnectionAsync(connection);
    }

    internal void ProcessSessionMessage(SessionStatePatch sessionStatePatch)
    {
        try
        {
            StampEventMessage();
            sessionUpdateCount++;
            Logger.LogInformation("RX Session Patch {c}", sessionUpdateCount);
            WeakReferenceMessenger.Default.Send(new SessionStatusNotification(sessionStatePatch));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process session message.");
        }
    }

    internal void ProcessCarPatches(CarPositionPatch[] carPatches)
    {
        try
        {
            StampEventMessage();
            Logger.LogInformation("RX Car Patches: {c}", carPatches.Length);
            WeakReferenceMessenger.Default.Send(new CarStatusNotification(carPatches));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process car patches.");
        }
    }

    internal void ProcessReset()
    {
        try
        {
            StampEventMessage();
            Logger.LogInformation("RX Reset");
            WeakReferenceMessenger.Default.Send(new ResetNotification());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process reset message.");
        }
    }

    #endregion

    #region Control Logs

    public async Task SubscribeToControlLogsAsync(int eventId)
    {
        // Recorded before the invoke so that a connection which is not ready yet, or drops later,
        // gets the subscription replayed rather than losing it.
        subscribedControlLogEventId = eventId;

        var connection = Volatile.Read(ref active);
        RegisterControlLogHandler(connection);
        await TryInvokeAsync(connection, "SubscribeToControlLogs", eventId, accessCodeStore.Get(eventId));
    }

    public async Task UnsubscribeFromControlLogsAsync(int eventId)
    {
        if (subscribedControlLogEventId == eventId)
            subscribedControlLogEventId = null;

        await TryInvokeAsync(Volatile.Read(ref active), "UnsubscribeFromControlLogs", eventId);
    }

    public async Task SubscribeToCarControlLogsAsync(int eventId, string carNum)
    {
        subscribedCarControlLog = (eventId, carNum);

        var connection = Volatile.Read(ref active);
        RegisterControlLogHandler(connection);
        await TryInvokeAsync(connection, "SubscribeToCarControlLogs", eventId, carNum,
            accessCodeStore.Get(eventId));
    }

    public async Task UnsubscribeFromCarControlLogsAsync(int eventId, string carNum)
    {
        // A single slot, so only the car that is actually leaving may clear it. Expanding one car
        // then another used to let the first one's teardown drop the second one's subscription.
        if (subscribedCarControlLog == (eventId, carNum))
            subscribedCarControlLog = null;

        await TryInvokeAsync(Volatile.Read(ref active), "UnsubscribeFromCarControlLogs", eventId, carNum);
    }

    /// <summary>
    /// Points the control log message back at this client, replacing any handler from a previous connection.
    /// </summary>
    private void RegisterControlLogHandler(ActiveConnection? connection)
    {
        if (connection is null)
            return;

        try
        {
            connection.Hub.Remove("ReceiveControlLog");
            connection.Hub.On("ReceiveControlLog", (CarControlLogs s) => ProcessControlLogs(s));
        }
        catch (ObjectDisposedException ex)
        {
            // Retired underneath us. The subscription intent stands, so the next connection picks
            // it up; throwing here would take out the invokes that follow in ResubscribeAsync.
            Logger.LogWarning(ex, "Could not attach the control log handler: connection was disposed");
        }
    }

    private void ProcessControlLogs(CarControlLogs ccl)
    {
        try
        {
            Logger.LogInformation("RX Control Logs: {cl} car {cn}", ccl.ControlLogEntries.Count, ccl.CarNumber);
            WeakReferenceMessenger.Default.Send(new ControlLogNotification(ccl));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process control log message");
        }
    }

    #endregion

    #region Driver Mode

    public async Task SubscribeToInCarDriverEventAsync(int eventId, string car)
    {
        // See SubscribeToEventAsync: recorded before the first await, and this mode replaces
        // whatever the previous connection was holding.
        subscribedInCarDriverEventIdAndCar = (eventId, car);
        subscribedEventId = null;
        subscribedControlLogEventId = null;
        subscribedCarControlLog = null;

        await DisposeConnectionAsync(Volatile.Read(ref active));

        try
        {
            var connection = StartNewConnection();

            connection.Hub.Remove("ReceiveInCarUpdateV2");
            connection.Hub.On("ReceiveInCarUpdateV2", (InCarPayload s) => ProcessInCarPayload(s));

            await ResubscribeAsync(connection);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to subscribe to in-car driver event");
        }
    }

    public async Task UnsubscribeFromInCarDriverEventAsync(int eventId, string car)
    {
        var connection = Volatile.Read(ref active);

        // See UnsubscribeFromEventAsync: only the owner clears the intent and retires the connection.
        var owned = subscribedInCarDriverEventIdAndCar == (eventId, car);
        if (owned)
            subscribedInCarDriverEventIdAndCar = null;

        await TryInvokeAsync(connection, "UnsubscribeFromInCarDriverEventV2", eventId, car);

        if (owned)
            await DisposeConnectionAsync(connection);
    }

    private void ProcessInCarPayload(InCarPayload payload)
    {
        try
        {
            if (payload == null)
                return;
            Logger.LogInformation("RX InCarPayload: {c}", payload.Cars.Count);
            WeakReferenceMessenger.Default.Send(new InCarPositionUpdate(payload));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process in-car payload");
        }
    }

    #endregion

    /// <summary>
    /// A hub connection together with the token that stops its initial-connect retry loop.
    /// </summary>
    /// <remarks>
    /// The two have to be retired together, so they are held together. See
    /// <see cref="DisposeConnectionAsync"/> for what happens when they are not.
    /// </remarks>
    private sealed class ActiveConnection(HubConnection hub, CancellationTokenSource cancellation)
    {
        private int disposalClaimed;

        public HubConnection Hub { get; } = hub;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        /// <summary>
        /// Returns true for the first caller only, so overlapping teardowns tear down once.
        /// </summary>
        public bool ClaimDisposal() => Interlocked.Exchange(ref disposalClaimed, 1) == 0;
    }
}
