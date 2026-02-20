using Avalonia;
using Avalonia.Animation;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Animations;

/// <summary>
/// A PageSlide transition that slides in the reverse direction (new content enters from the left).
/// </summary>
public class ReversePageSlide : PageSlide
{
    public override Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        return base.Start(from, to, !forward, cancellationToken);
    }
}
