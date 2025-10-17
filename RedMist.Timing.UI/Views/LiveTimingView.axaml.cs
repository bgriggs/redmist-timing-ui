using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using System;

namespace RedMist.Timing.UI.Views;

public partial class LiveTimingView : UserControl, IRecipient<CopyToClipboardRequest>
{
    public LiveTimingView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register(this);
        Loaded += LiveTimingView_Loaded;
    }

    private void LiveTimingView_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Find the organization logo image and add click handler
        if (this.FindControl<Image>("OrganizationLogoImage") is Image logoImage)
        {
            logoImage.PointerPressed += LogoImage_PointerPressed;
        }
    }

    private void LogoImage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is LiveTimingViewModel viewModel)
        {
            viewModel.OnOrganizationLogoClicked();
        }
    }

    public async void Receive(CopyToClipboardRequest message)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null && !string.IsNullOrWhiteSpace(message.Text))
            {
                await topLevel.Clipboard.SetTextAsync(message.Text);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error copying to clipboard: {ex}");
        }
    }
}