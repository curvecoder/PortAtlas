using PortAtlas.Gui.Core.Catalog;

namespace PortAtlas.Gui.Core.ViewModels;

/// <summary>
/// A non-empty section of options in a command form.
/// </summary>
/// <param name="Group">Section identifier.</param>
/// <param name="Options">Option view models belonging to this section.</param>
public sealed record OptionGroupViewModel(OptionGroup Group, IReadOnlyList<OptionViewModel> Options)
{
    /// <summary>
    /// Whether the section is open. The section of other options starts folded so that the usual ones stay in view.
    /// </summary>
    public bool IsExpanded { get; set; } = Group != OptionGroup.Other;

    /// <summary>
    /// Header shown above the section.
    /// </summary>
    public string Title => Group switch
    {
        OptionGroup.MapRendering => "Map rendering",
        OptionGroup.Other => "Other options",
        _ => Group.ToString()
    };
}
