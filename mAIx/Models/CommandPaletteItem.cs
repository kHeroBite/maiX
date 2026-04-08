namespace mAIx.Models;

public class CommandPaletteItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Keywords { get; set; } = "";
    public Action? Execute { get; set; }
}
