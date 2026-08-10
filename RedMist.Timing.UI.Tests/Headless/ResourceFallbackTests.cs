using Avalonia.Media;
using RedMist.Timing.UI.Converters;
using RedMist.Timing.UI.Tests.ViewModels;
using RedMist.TimingCommon.Models;
using System.Globalization;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers what the timing grid does when a themed resource cannot be found.
/// </summary>
/// <remarks>
/// A missed lookup returns <c>AvaloniaProperty.UnsetValue</c>, not null, so the obvious
/// <c>(IBrush?)FindResource(...) ?? Brushes.Black</c> throws InvalidCastException and the fallback
/// beside it never runs. These getters are evaluated by the renderer while it draws a row, so a
/// renamed or theme-specific resource key turns into a crash rather than a wrong color.
///
/// This is only reproducible under the headless session: everywhere else <c>Application.Current</c>
/// is null, the null-conditional short-circuits, and the fallback appears to work. The test app
/// deliberately loads no resource dictionaries, so every lookup here misses.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ResourceFallbackTests
{
    [TestMethod]
    public Task CarRowColors_FallBackWhenTheResourceIsMissing() => HeadlessTest.OnDispatcher(() =>
    {
        var car = TestViewModelFactory.CreateCar();

        Assert.AreEqual(Colors.Transparent, car.RowBackground);
        Assert.AreEqual(Brushes.Black, car.LapDataColor);
        Assert.AreEqual(Brushes.Black, car.BestLapDataColor);
    });

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task LapRowColor_FallsBackWhenTheResourceIsMissing(bool isBestLap) => HeadlessTest.OnDispatcher(() =>
    {
        // Both branches read a different resource key, so both need the fallback.
        var lap = new RedMist.Timing.UI.ViewModels.CarDetails.LapViewModel(new CarPosition
        {
            Number = "1",
            LastLapCompleted = 3,
            LastLapTime = "00:01:22.500",
        })
        {
            IsBestLap = isBestLap,
        };

        Assert.AreEqual(Brushes.Black, lap.TimeColor);
    });

    /// <summary>
    /// The flag banner runs through a value converter, so a throw here happens inside the binding
    /// pipeline while the flag is changing - the worst possible moment.
    /// </summary>
    [TestMethod]
    [DataRow(Flags.Green)]
    [DataRow(Flags.Yellow)]
    [DataRow(Flags.Red)]
    [DataRow(Flags.Black)]
    [DataRow(Flags.White)]
    [DataRow(Flags.Checkered)]
    public Task FlagBrush_FallsBackWhenTheResourceIsMissing(Flags flag) => HeadlessTest.OnDispatcher(() =>
    {
        var converter = new FlagToBrushConverter();

        var brush = converter.Convert(flag, typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.AreEqual(Brushes.Transparent, brush);
    });
}
