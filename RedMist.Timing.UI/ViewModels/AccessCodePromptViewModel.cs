using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

/// <summary>
/// Prompts the viewer for a 1-7 digit access code before letting them into a private event.
/// </summary>
public partial class AccessCodePromptViewModel : ObservableObject
{
    private static readonly Regex CodePattern = new(@"^[0-9]{1,7}$", RegexOptions.Compiled);

    private readonly EventClient eventClient;
    private readonly EventAccessCodeStore store;
    private readonly ILogger logger;
    private readonly int eventId;
    private readonly Func<Task> onSuccess;
    private readonly Action onCancel;

    public string EventName { get; }
    public string OrganizationName { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool isValidating;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);


    public AccessCodePromptViewModel(int eventId, string eventName, string organizationName,
        EventClient eventClient, EventAccessCodeStore store, ILoggerFactory loggerFactory,
        Func<Task> onSuccess, Action onCancel)
    {
        this.eventId = eventId;
        EventName = string.IsNullOrEmpty(eventName) ? "Private Event" : eventName;
        OrganizationName = organizationName;
        this.eventClient = eventClient;
        this.store = store;
        this.onSuccess = onSuccess;
        this.onCancel = onCancel;
        logger = loggerFactory.CreateLogger(GetType().Name);
    }


    private bool CanContinue() => !IsValidating && CodePattern.IsMatch(Code ?? string.Empty);

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task Continue()
    {
        ErrorMessage = string.Empty;
        IsValidating = true;
        try
        {
            // Store the candidate code first so EventClient attaches it as the header.
            store.Set(eventId, Code);

            // Probe a gated endpoint to confirm the code. LoadEventStatus returns 401
            // (translated to EventAccessDeniedException) if the code is wrong.
            try
            {
                await eventClient.LoadEventStatusAsync(eventId);
            }
            catch (EventAccessDeniedException)
            {
                store.Clear(eventId);
                ErrorMessage = "Incorrect access code. Please try again.";
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Network error validating access code for event {EventId} — assuming code is valid", eventId);
                // Network/server hiccup: keep the code; the data screens will re-prompt if it actually fails.
            }

            await onSuccess();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error validating access code for event {EventId}", eventId);
            ErrorMessage = "Unable to validate code. Please try again.";
        }
        finally
        {
            IsValidating = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        onCancel();
    }
}
