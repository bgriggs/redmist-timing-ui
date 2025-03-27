using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using RedMist.Timing.UI.ViewModels;
using System.Collections.Generic;

namespace RedMist.Timing.UI.TemplateSelectors;

public class PitIconTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<string, IDataTemplate> Templates { get; } = [];

    public Control? Build(object? param)
    {
        if (param is PitStates p)
        {
            if (p == PitStates.EnteredPit)
            {
                return Templates["EnteredPitTemplate"].Build(param);
            }
            else if (p == PitStates.PitSF)
            {
                return Templates["PitSfTemplate"].Build(param);
            }
            else if (p == PitStates.ExitedPit)
            {
                return Templates["ExitedPitTemplate"].Build(param);
            }
            else if (p == PitStates.InPit)
            {
                return Templates["InPitTemplate"].Build(param);
            }
        }
        return Templates["NoPit"].Build(param); 
    }

    public bool Match(object? data)
    {
        return data is PitStates;
    }
}
