using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;
using System.Collections.Generic;

namespace RedMist.Timing.UI.TemplateSelectors;

public class PositionsGainedLostTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<string, IDataTemplate> Templates { get; } = [];

    public Control? Build(object? param)
    {
        if (param is CarViewModel vm)
        {
            if (vm.PositionsGainedLost == CarPosition.InvalidPosition)
            {
                return null;
            }
            else if (vm.PositionsGainedLost > 0)
            {
                return Templates["PositionsGained"].Build(param);
            }
            else if (vm.PositionsGainedLost < 0)
            {
                return Templates["PositionsLost"].Build(param);
            }
            return Templates["PositionsNeutral"].Build(param);
        }

        return null;
    }

    public bool Match(object? data)
    {
        return data is CarViewModel;
    }
}
