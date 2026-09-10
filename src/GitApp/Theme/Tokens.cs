namespace GitApp.Theme;

/// <summary>
/// Layout and type tokens.
///
/// Note what is absent: a colour palette. The React Native build needed three
/// hand-written palettes and a theme hook, because no PlatformColor under
/// Fabric returned dark-theme values. MAUI follows the system light, dark and
/// high contrast themes on its own, verified in docs/SPIKE-MAUI.md, so colour
/// belongs in resource dictionaries and AppThemeBinding rather than here.
///
/// The rule that survives: never write a literal colour in a view. Under MAUI
/// that means AppThemeBinding or a system brush, not a hex string.
/// </summary>
public static class Tokens
{
    /// <summary>Fluent's 4px spacing grid.</summary>
    public static class Space
    {
        public const double Xs = 4;
        public const double Sm = 8;
        public const double Md = 12;
        public const double Lg = 16;
        public const double Xl = 20;
        public const double Xxl = 24;
    }

    /// <summary>Fluent corner radii: 4 for controls, 8 for surfaces.</summary>
    public static class Radius
    {
        public const double Control = 4;
        public const double Surface = 8;
    }

    /// <summary>
    /// The Fluent type ramp. Sizes only; the family comes from the platform so
    /// each OS uses its own system face.
    /// </summary>
    public static class Type
    {
        public const double Caption = 12;
        public const double Body = 14;
        public const double BodyLarge = 18;
        public const double Subtitle = 20;
        public const double Title = 28;
    }

    /// <summary>
    /// Control metrics. The 32px minimum height is a Fluent standard and a
    /// pointer target floor; do not shrink it to fit more rows on screen.
    /// </summary>
    public static class Metrics
    {
        public const double ControlHeight = 32;
        public const double SidebarWidth = 280;
    }
}
