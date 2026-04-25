namespace NorthFileUI
{
    public sealed class SidebarTreeEntry
    {
        public SidebarTreeEntry(string name, string fullPath, string iconGlyph = "\uE8B7")
        {
            Name = name;
            FullPath = fullPath;
            IconGlyph = iconGlyph;
        }

        public string Name { get; }
        public string FullPath { get; }
        public string IconGlyph { get; }
    }
}
