using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>profile.json ルートの gaze セクションです。</summary>
    [System.Serializable]
    public sealed class GazeSectionDto
    {
        public List<GazeChannelDto> channels;
    }
}
