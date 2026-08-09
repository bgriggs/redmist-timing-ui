using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RedMist.Timing.UI.ViewModels.InCarDriverMode;
using System.Collections.ObjectModel;

namespace RedMist.Timing.UI.Views.InCarDriverMode;

public class CarGrid : Grid
{
    public static readonly StyledProperty<ObservableCollection<CarViewModel>?> CarsProperty =
            AvaloniaProperty.Register<CarGrid, ObservableCollection<CarViewModel>?>(nameof(Cars));

    public ObservableCollection<CarViewModel>? Cars
    {
        get => GetValue(CarsProperty);
        set => SetValue(CarsProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
            AvaloniaProperty.Register<CarGrid, IDataTemplate?>(nameof(ItemTemplate));

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CarsProperty)
        {
            // Detach from the old collection first: without this, swapping view models leaves stale
            // handlers behind that rebuild the grid once per swap. Either value can be null -
            // Avalonia pushes the property's default back when a binding is detached or the
            // DataContext goes null - so both sides are treated as optional.
            if (change.OldValue is ObservableCollection<CarViewModel> oldCars)
            {
                oldCars.CollectionChanged -= Cars_CollectionChanged;
            }

            if (change.NewValue is ObservableCollection<CarViewModel> newCars)
            {
                newCars.CollectionChanged += Cars_CollectionChanged;
            }

            RebuildRows();
        }
        else if (change.Property == ItemTemplateProperty)
        {
            // The template may resolve after the collection does; re-render with it applied.
            RebuildRows();
        }
    }

    private void Cars_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => RebuildRows();

    private void RebuildRows()
    {
        RowDefinitions.Clear();
        Children.Clear();

        var cars = Cars;
        if (cars is null)
        {
            return;
        }

        foreach (var car in cars)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var carControl = new ContentControl { Content = car, ContentTemplate = ItemTemplate };
            SetRow(carControl, RowDefinitions.Count - 1);
            Children.Add(carControl);
        }
    }
}
