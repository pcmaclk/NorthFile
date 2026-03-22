using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FileExplorerUI.Controls;

public sealed class EntryItemMetrics : INotifyPropertyChanged
{
    private double _rowHeight = 30;
    private double _iconColumnWidth = 18;
    private double _iconFontSize = 14;
    private double _nameFontSize = 14;
    private double _iconTextSpacing = 8;
    private double _nameTrailingSpacing = 4;
    private double _groupHeaderHeight = 28;
    private double _groupHeaderFontSize = 12;
    private double _groupCountFontSize = 12;
    private double _groupGlyphWidth = 18;
    private double _groupGlyphFontSize = 12;
    private double _groupHeaderSpacing = 4;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double RowHeight
    {
        get => _rowHeight;
        set => SetField(ref _rowHeight, value);
    }

    public double IconColumnWidth
    {
        get => _iconColumnWidth;
        set => SetField(ref _iconColumnWidth, value);
    }

    public double IconFontSize
    {
        get => _iconFontSize;
        set => SetField(ref _iconFontSize, value);
    }

    public double NameFontSize
    {
        get => _nameFontSize;
        set => SetField(ref _nameFontSize, value);
    }

    public double IconTextSpacing
    {
        get => _iconTextSpacing;
        set => SetField(ref _iconTextSpacing, value);
    }

    public double NameTrailingSpacing
    {
        get => _nameTrailingSpacing;
        set => SetField(ref _nameTrailingSpacing, value);
    }

    public double GroupHeaderHeight
    {
        get => _groupHeaderHeight;
        set => SetField(ref _groupHeaderHeight, value);
    }

    public double GroupHeaderFontSize
    {
        get => _groupHeaderFontSize;
        set => SetField(ref _groupHeaderFontSize, value);
    }

    public double GroupCountFontSize
    {
        get => _groupCountFontSize;
        set => SetField(ref _groupCountFontSize, value);
    }

    public double GroupGlyphWidth
    {
        get => _groupGlyphWidth;
        set => SetField(ref _groupGlyphWidth, value);
    }

    public double GroupGlyphFontSize
    {
        get => _groupGlyphFontSize;
        set => SetField(ref _groupGlyphFontSize, value);
    }

    public double GroupHeaderSpacing
    {
        get => _groupHeaderSpacing;
        set => SetField(ref _groupHeaderSpacing, value);
    }

    public static EntryItemMetrics CreatePreset(EntryViewDensityMode mode)
    {
        return mode switch
        {
            EntryViewDensityMode.Compact => new EntryItemMetrics
            {
                RowHeight = 26,
                IconColumnWidth = 16,
                IconFontSize = 12,
                NameFontSize = 13,
                IconTextSpacing = 6,
                NameTrailingSpacing = 4,
                GroupHeaderHeight = 24,
                GroupHeaderFontSize = 11,
                GroupCountFontSize = 11,
                GroupGlyphWidth = 16,
                GroupGlyphFontSize = 11,
                GroupHeaderSpacing = 4
            },
            EntryViewDensityMode.Large => new EntryItemMetrics
            {
                RowHeight = 36,
                IconColumnWidth = 20,
                IconFontSize = 16,
                NameFontSize = 15,
                IconTextSpacing = 10,
                NameTrailingSpacing = 6,
                GroupHeaderHeight = 32,
                GroupHeaderFontSize = 13,
                GroupCountFontSize = 13,
                GroupGlyphWidth = 20,
                GroupGlyphFontSize = 13,
                GroupHeaderSpacing = 6
            },
            _ => new EntryItemMetrics()
        };
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
