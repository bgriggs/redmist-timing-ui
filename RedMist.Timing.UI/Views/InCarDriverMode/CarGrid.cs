using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RedMist.Timing.UI.ViewModels.InCarDriverMode;
using System.Collections.ObjectModel;

namespace RedMist.Timing.UI.Views.InCarDriverMode;

public class CarGrid : Grid
{
    public static readonly StyledProperty<ObservableCollection<CarViewModel>> CarsProperty =
            AvaloniaProperty.Register<CarGrid, ObservableCollection<CarViewModel>>(nameof(Cars));

    public ObservableCollection<CarViewModel> Cars
    {
        get => GetValue(CarsProperty);
        set => SetValue(CarsProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate> ItemTemplateProperty =
            AvaloniaProperty.Register<CarGrid, IDataTemplate>(nameof(ItemTemplate));

    public IDataTemplate ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CarsProperty)
        {
            Cars.CollectionChanged += Cars_CollectionChanged;
        }
    }

    private void Cars_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RowDefinitions.Clear();
        Children.Clear();

        foreach (var car in Cars)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var carControl = new ContentControl { Content = car, ContentTemplate = ItemTemplate };
            SetRow(carControl, RowDefinitions.Count - 1);
            Children.Add(carControl);
        }

        InvalidateMeasure();
        InvalidateArrange();

        if (Parent is Grid g)
        {
            g.InvalidateMeasure();
            g.InvalidateArrange();
        }
    }
}
