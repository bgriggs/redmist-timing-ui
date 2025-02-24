using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.ViewModels.CarDetails;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class DetailsViewModel : ObservableObject
{
    private readonly int eventId;
    private readonly string carNumber;
    private readonly EventClient serverClient;

    [ObservableProperty]
    private bool isLoading = false;

    public LapsListViewModel LapList { get; } = new LapsListViewModel();

    public DetailsViewModel(int eventId, string carNumber, EventClient serverClient)
    {
        this.eventId = eventId;
        this.carNumber = carNumber;
        this.serverClient = serverClient;
    }

    public async Task Initialize()
    {
        try
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            var carPositions = await serverClient.LoadCarLapsAsync(eventId, carNumber);
            LapList.UpdateLaps(carPositions);
            Debug.WriteLine($"Car positions loaded: {carPositions.Count}");
        }
        catch (Exception)
        {
            // Handle exceptions
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    public void UpdateLaps(List<CarPosition> carPositions)
    {
        LapList.UpdateLaps(carPositions);
    }
}
