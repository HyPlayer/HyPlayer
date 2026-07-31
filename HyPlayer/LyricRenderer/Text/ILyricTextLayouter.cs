#nullable enable

namespace HyPlayer.LyricRenderer.Text;

public interface ILyricTextLayouter
{
    LyricTextLayoutSnapshot CreateLayout(LyricTextLayoutRequest request);
}