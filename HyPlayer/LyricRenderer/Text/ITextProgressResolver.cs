#nullable enable

namespace HyPlayer.LyricRenderer.Text;

public interface ITextProgressResolver
{
    TextRenderFrame Resolve(long currentTime, long lineStartTime, long lineEndTime, LyricTextLayoutSnapshot layout);
}
