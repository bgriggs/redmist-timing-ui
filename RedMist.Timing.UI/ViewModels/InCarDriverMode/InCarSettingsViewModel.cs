using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Maui.Storage;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.InCarDriverMode;

public partial class InCarSettingsViewModel : ObservableValidator
{
    private const string CAR_NUM_KEY = "DriverModeCarNumber";
    private const string IN_CLASS_KEY = "DriverModeIsInClassOnly";

    [Required]
    [StringLength(8, MinimumLength = 1)]
    [ObservableProperty]
    private string carNumber = string.Empty;

    public LargeObservableCollection<EventViewModel> Events { get; } = [];

    public EventViewModel? SelectedEvent { get; set; }

    [ObservableProperty]
    private bool isInClassOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string message = string.Empty;
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    [ObservableProperty]
    private bool isLoading = false;

    [ObservableProperty]
    private bool isPositionsVisible = false;
    [ObservableProperty]
    private InCarPositionsViewModel inCarPositionsViewModel;

    private readonly EventClient eventClient;
    private readonly HubClient hubClient;


    public InCarSettingsViewModel(EventClient eventClient, HubClient hubClient)
    {
        this.eventClient = eventClient;
        this.hubClient = hubClient;
        inCarPositionsViewModel = new(hubClient, eventClient);
    }


    public async Task Initialize()
    {
        IsLoading = true;
        try
        {
            var events = await eventClient.LoadRecentEventsAsync();
            if (events != null)
            {
                if (events.Count == 0)
                {
                    Message = "No events found";
                }
                else
                {
                    // Order the live events at the top
                    var vms = new List<EventViewModel>();
                    foreach (var e in events.Where(e => e.IsLive).OrderByDescending(e => DateTime.ParseExact(e.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                    {
                        vms.Add(new EventViewModel(e, null));
                    }
                    foreach (var e in events.Where(e => !e.IsLive).OrderByDescending(e => DateTime.ParseExact(e.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                    {
                        vms.Add(new EventViewModel(e, null));
                    }

                    Dispatcher.UIThread.Post(() => Events.SetRange(vms));
                }
            }
            else
            {
                Message = "No events found - null";
            }

            TryLoadSettings();
        }
        catch (Exception ex)
        {
            Message = $"Error loading events: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

    public void BackToSettings()
    {
        IsPositionsVisible = false;
        try
        {
            InCarPositionsViewModel.Unsubscribe();
        }
        catch { }
    }


    public void Ok()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            var errors = GetErrors();
            var sb = new StringBuilder();
            foreach (var e in errors)
            {
                sb.AppendLine(e.ErrorMessage);
            }
            Message = sb.ToString().TrimEnd();
        }
        else
        {
            Message = string.Empty;

            TrySaveSettings();

            // Show driver mode content
            IsPositionsVisible = true;
            InCarPositionsViewModel = new InCarPositionsViewModel(hubClient, eventClient);
            InCarPositionsViewModel.Initialize(SelectedEvent?.EventModel.Id ?? 0, CarNumber, IsInClassOnly);
        }
    }

    private void TryLoadSettings()
    {
        try
        {
            CarNumber = Preferences.Get(CAR_NUM_KEY, string.Empty) ?? string.Empty;
            IsInClassOnly = Preferences.Get(IN_CLASS_KEY, false);
        }
        catch
        {
            // Ignore errors in loading settings
        }
    }

    private void TrySaveSettings()
    {
        try
        {
            Preferences.Set(CAR_NUM_KEY, CarNumber);
            Preferences.Set(IN_CLASS_KEY, IsInClassOnly);
        }
        catch
        {
        }
    }
}