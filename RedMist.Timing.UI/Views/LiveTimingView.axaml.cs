using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RedMist.Timing.UI.Views;

public partial class LiveTimingView : UserControl, IRecipient<CopyToClipboardRequest>
{
    private static ILogger Logger => App.GetLogger(nameof(LiveTimingView));

    public LiveTimingView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register(this);
        Loaded += LiveTimingView_Loaded;
        // The grid under the results tab is replaced per session, so the view outlives more than one
        // view model and each one has to be told where to ask what is on screen.
        DataContextChanged += (_, _) => OfferOnScreenCars();
    }

    private void LiveTimingView_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Find the organization logo image and add click handler
        if (this.FindControl<Image>("OrganizationLogoImage") is Image logoImage)
        {
            logoImage.PointerPressed += LogoImage_PointerPressed;
        }

        OfferOnScreenCars();
    }

    private void OfferOnScreenCars()
    {
        if (DataContext is LiveTimingViewModel timing)
        {
            timing.OnScreenCars = OnScreenCars;
        }
    }

    /// <summary>
    /// The car rows the viewer can currently see, in the order the table shows them, or null when
    /// that cannot be worked out.
    /// </summary>
    /// <remarks>
    /// Read off the realized rows rather than the model, because the viewer may have searched,
    /// grouped by class, folded a class shut or sorted by fastest lap, and "what I am looking at" is
    /// the thing worth sending. A row counts when any part of it is inside the scroll viewport, which
    /// is the same rule the web app applies to its DOM rows.
    ///
    /// The list is not virtualized - the table is a plain <see cref="ItemsControl"/> in a
    /// <see cref="ScrollViewer"/>, so every row is realized whether or not it is visible - which is
    /// why this measures bounds instead of asking for realized items. Only the visible list is walked:
    /// the hidden one keeps its rows in the tree, and both would be counted otherwise.
    ///
    /// Null rather than an empty list when the table has not been realized yet, so the view model can
    /// tell "nothing visible" from "cannot say" and fall back rather than draw an empty card. An
    /// expanded row measures tall, including its details panel, so a car whose row has scrolled off
    /// while its panel is still on screen counts as visible - which is the answer that matches what
    /// the viewer sees.
    /// </remarks>
    /// <summary>
    /// Whether a row whose top edge sits at <paramref name="top"/> within a viewport
    /// <paramref name="viewportHeight"/> tall has any part of it on screen.
    /// </summary>
    /// <remarks>
    /// Its own method so the arithmetic can be tested - constructing the view needs resource
    /// dictionaries the headless test application deliberately does not load, the same constraint
    /// <see cref="TableMaxWidthFor"/> is factored out for.
    ///
    /// A row above the viewport has a negative top; one below has a top past the height. Both edges
    /// are exclusive, so a row resting exactly on either boundary contributes no visible pixels and
    /// does not count. A zero-height row - one that has not been arranged - never counts.
    /// </remarks>
    internal static bool IsRowOnScreen(double top, double height, double viewportHeight)
    {
        if (double.IsNaN(top) || double.IsNaN(height) || height <= 0)
        {
            return false;
        }

        return top + height > 0 && top < viewportHeight;
    }

    private IReadOnlyList<CarViewModel>? OnScreenCars()
    {
        var host = DataContext is LiveTimingViewModel { IsFlat: true } ? flatCarRows : (Control)groupedCarRows;
        if (!host.IsVisible || tableScroller.Bounds.Height <= 0)
        {
            return null;
        }

        var viewportHeight = tableScroller.Bounds.Height;
        var visible = new List<CarViewModel>();

        // Visual-tree order is display order here: both panels lay their items out top to bottom, and
        // a class group's rows sit inside its own expander.
        foreach (var row in host.GetVisualDescendants().OfType<Expander>())
        {
            // Effectively rather than merely IsVisible: a folded class keeps its rows in the tree with
            // IsVisible true and only the group's content border hidden, so the rows of every closed
            // class would be counted as on screen.
            if (row.DataContext is not CarViewModel car || !row.IsEffectivelyVisible)
            {
                continue;
            }

            if (row.TranslatePoint(new Point(0, 0), tableScroller) is not Point top)
            {
                continue;
            }

            if (IsRowOnScreen(top.Y, row.Bounds.Height, viewportHeight))
            {
                visible.Add(car);
            }
        }

        return visible;
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