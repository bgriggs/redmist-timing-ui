using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using System;

namespace RedMist.Timing.UI.Views;

public partial class LiveTimingView : UserControl, IRecipient<CopyToClipboardRequest>
{
    private static ILogger Logger => App.GetLogger(nameof(LiveTimingView));

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
            Logger.LogError(ex, "Error copying to clipboard");
        }
    }

    private void LegendDismiss_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is LiveTimingViewModel viewModel)
        {
            viewModel.IsLegendVisible = false;
        }
    }

    /// <summary>Breathing room between the timing table and the edge of the view.</summary>
    private const double TableMargin = 10;

    /// <summary>Below this the organization logo is dropped rather than crowding the table.</summary>
    private const double LogoMinimumWidth = 424;

    /// <summary>
    /// The width to cap the timing table at inside a view of <paramref name="viewWidth"/>, or null
    /// when that is not a width anything can be laid out at.
    /// </summary>
    /// <remarks>
    /// Avalonia rejects a negative or NaN MaxWidth, and the caller runs inside an arrange pass, so
    /// handing it one does not mislay the layout - it reaches the dispatcher's unhandled handler,
    /// which the app treats as fatal. Subtracting the margin from a small width is enough to do
    /// that, and small widths are not hypothetical: a foldable part way through a fold reports zero,
    /// which is how this crashed on a Galaxy Z Fold.
    /// </remarks>
    internal static double? TableMaxWidthFor(double viewWidth)
    {
        if (double.IsNaN(viewWidth) || viewWidth <= 0)
        {
            return null;
        }

        return Math.Max(0, viewWidth - TableMargin);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // Nothing usable at this size, and the next pass brings a real one. Leaving the last good
        // values in place beats collapsing the table to nothing and restoring it a moment later,
        // which during a fold is a visible flinch.
        if (TableMaxWidthFor(e.NewSize.Width) is not double availableWidth)
        {
            return;
        }

        tableHeader.MaxWidth = availableWidth;
        this.tableBody.MaxWidth = availableWidth;

        OrganizationLogoImage.IsVisible = e.NewSize.Width > LogoMinimumWidth;
    }
}