using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.TimingCommon.Models.InCarVideo;
using System.Collections.Generic;

namespace RedMist.Timing.UI.Models;

/// <summary>
/// This message is sent when the in-car video metadata changes.
/// </summary>
/// <param name="videoMetadata">latest video metadata</param>
public class InCarVideoMetadataNotification(List<VideoMetadata> videoMetadata) : ValueChangedMessage<List<VideoMetadata>>(videoMetadata)
{
}
