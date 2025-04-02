using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Models;

public class CompetitorMetadataNotification(CompetitorMetadata cm) : ValueChangedMessage<CompetitorMetadata>(cm)
{
}