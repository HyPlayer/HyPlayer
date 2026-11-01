using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Platform.Xaml;

/// <summary>
///     Applies the UI font selected in settings to the application resources and to the element tree.
/// </summary>
public interface IAppFontService
{
    /// <summary>
    ///     The font family that was applied to the UI. Empty means the system default font.
    /// </summary>
    string AppliedFontFamily { get; }

    /// <summary>
    ///     Applies the given font family to the application resources consumed by text and
    ///     control templates. Call this before any page is created so that the whole UI uses it.
    /// </summary>
    void Apply(string fontFamily);

    /// <summary>
    ///     Applies the current font to a root control so that it is inherited by its content.
    /// </summary>
    void ApplyToRoot(Control root);

    /// <summary>
    ///     Applies the current font to the content of the current window.
    /// </summary>
    void ApplyToCurrentWindow();

    /// <summary>
    ///     Sets the current font on text and control elements that still use the framework default.
    ///     Also hooks navigations and list containers so content created later follows as well.
    /// </summary>
    void ApplyToTree(DependencyObject root);

    /// <summary>
    ///     Applies the font now and again once layout and asynchronously loaded content are in place.
    /// </summary>
    void ScheduleTreeApply(DependencyObject root);
}
