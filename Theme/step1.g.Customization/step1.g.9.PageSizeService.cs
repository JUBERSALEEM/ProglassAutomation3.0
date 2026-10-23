using System.Windows;

namespace ProGlassAutomation.Customization;

public enum PageSize { Compact, Cozy, Comfortable, Spacious }

public sealed record PageCustomizationState(PageSize Size = PageSize.Compact)
{
    public string? Validate() => Enum.IsDefined(Size) ? null : "Unsupported page size.";
}

public sealed record PageStyleCard(PageSize Size, string Name, string Caption);

/// <summary>
/// Shared page-sizing overlay. Four compact layouts written into the active
/// theme package (spacing, padding, control size, font size), so every page on
/// every theme follows the live choice. Runs after the active engine so themes
/// keep their own colours and material. Not a material engine.
/// </summary>
public static class PageSizeService
{
    public static readonly IReadOnlyList<PageStyleCard> Cards =
    [
        new(PageSize.Compact, "Compact", "Dense layout. Tight 8px cards, 13px body text, 32px controls. Default."),
        new(PageSize.Cozy, "Cozy", "A step airier. 10px cards, 13.5px body text, 34px controls."),
        new(PageSize.Comfortable, "Comfortable", "Relaxed layout. 12px cards, 14px body text, 36px controls."),
        new(PageSize.Spacious, "Spacious", "Open layout. 16px cards, 15px body text, 40px controls.")
    ];

    public static void Apply(ResourceDictionary package, PageCustomizationState state)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Validate() is not null) throw new ArgumentException(state.Validate());
        (double caption, double body, double section, double title, double display,
         double small, double medium, double large, double sectionSpace, double pageSpace,
         double minHeight, double markSize,
         Thickness control, Thickness card, Thickness plate, Thickness page,
         Thickness choice, Thickness compact, Thickness mark, Thickness selection,
         Thickness item, Thickness inline, Thickness sectionMargin) = state.Size switch
        {
            PageSize.Compact => (12, 13, 16, 20, 28, 4, 8, 16, 24, 32, 32, 18,
                new Thickness(10, 5), new Thickness(16), new Thickness(12), new Thickness(16),
                new Thickness(8), new Thickness(8, 4), new Thickness(4), new Thickness(10, 5, 32, 5),
                new Thickness(0, 0, 0, 8), new Thickness(0, 0, 8, 4), new Thickness(0, 0, 0, 12)),
            PageSize.Cozy => (12.5, 13.5, 17, 21, 30, 5, 10, 18, 28, 36, 34, 19,
                new Thickness(11, 6), new Thickness(18), new Thickness(13), new Thickness(18),
                new Thickness(9), new Thickness(10, 6), new Thickness(5), new Thickness(11, 6, 34, 6),
                new Thickness(0, 0, 0, 10), new Thickness(0, 0, 10, 5), new Thickness(0, 0, 0, 14)),
            PageSize.Comfortable => (13, 14, 18, 23, 32, 6, 12, 20, 32, 40, 36, 20,
                new Thickness(12, 7), new Thickness(20), new Thickness(14), new Thickness(20),
                new Thickness(10), new Thickness(12, 8), new Thickness(6), new Thickness(12, 7, 36, 7),
                new Thickness(0, 0, 0, 12), new Thickness(0, 0, 12, 6), new Thickness(0, 0, 0, 16)),
            _ => (13.5, 15, 20, 26, 36, 8, 16, 24, 40, 48, 40, 22,
                new Thickness(14, 8), new Thickness(24), new Thickness(16), new Thickness(24),
                new Thickness(12), new Thickness(16, 10), new Thickness(8), new Thickness(14, 8, 40, 8),
                new Thickness(0, 0, 0, 16), new Thickness(0, 0, 16, 8), new Thickness(0, 0, 0, 20))
        };
        Set(package, "FontSizeCaption", caption);
        Set(package, "FontSizeBody", body);
        Set(package, "FontSizeSection", section);
        Set(package, "FontSizeTitle", title);
        Set(package, "FontSizeDisplay", display);
        Set(package, "SpacingSmall", small);
        Set(package, "SpacingMedium", medium);
        Set(package, "SpacingLarge", large);
        Set(package, "SpacingSection", sectionSpace);
        Set(package, "SpacingPage", pageSpace);
        Set(package, "ControlMinHeight", minHeight);
        Set(package, "ChoiceMarkSize", markSize);
        Set(package, "PaddingControl", control);
        Set(package, "PaddingCard", card);
        Set(package, "PaddingPlate", plate);
        Set(package, "PaddingPage", page);
        Set(package, "PaddingChoice", choice);
        Set(package, "PaddingCompact", compact);
        Set(package, "PaddingMark", mark);
        Set(package, "PaddingSelection", selection);
        Set(package, "MarginItem", item);
        Set(package, "MarginInline", inline);
        Set(package, "MarginSection", sectionMargin);
    }

    // In-place replace wherever the key already lives; a top-level duplicate would
    // fail ResourceValidation (keys must be unique across merged dictionaries).
    private static void Set(ResourceDictionary dictionary, string key, object value)
    {
        if (Replace(dictionary, key, value)) return;
        dictionary[key] = value;
    }

    private static bool Replace(ResourceDictionary dictionary, string key, object value)
    {
        foreach (object existing in dictionary.Keys)
        {
            if (!Equals(existing, key)) continue;
            dictionary[key] = value;
            return true;
        }
        foreach (ResourceDictionary child in dictionary.MergedDictionaries)
            if (Replace(child, key, value)) return true;
        return false;
    }
}
