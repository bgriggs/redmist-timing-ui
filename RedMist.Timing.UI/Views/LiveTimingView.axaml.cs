using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Models;
using System;

namespace RedMist.Timing.UI.Views;

public partial class LiveTimingView : UserControl, IRecipient<CopyToClipboardRequest>
{
    public LiveTimingView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register(this);
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