using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;

namespace FileExplorerUI
{
    public sealed class EntryViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _displayName = string.Empty;
        private string _fullPath = string.Empty;
        private string _type = string.Empty;
        private string _iconGlyph = "\uE8A5";
        private Brush _iconForeground = new SolidColorBrush(Colors.DimGray);
        private ulong _mftRef;
        private string _sizeText = string.Empty;
        private string _modifiedText = string.Empty;
        private bool _isDirectory;
        private bool _isLink;
        private bool _isLoaded;
        private bool _isMetadataLoaded;
        private bool _isPendingCreate;
        private bool _pendingCreateIsDirectory;
        private bool _isHiddenEntry;
        private bool _isSystemEntry;
        private bool _isExplicitlySelected;
        private bool _isKeyboardAnchor;
        private bool _isSelectionActive = true;
        private double _iconOpacity = 1.0;
        private string _pendingName = string.Empty;
        private bool _isNameEditing;
        private long? _sizeBytes;
        private DateTime? _modifiedAt;
        private bool _isGroupHeader;
        private string _groupKey = string.Empty;
        private int _groupItemCount;
        private bool _isGroupExpanded;
        private string _groupHeaderText = string.Empty;
        private Visibility _headerRowVisibility = Visibility.Collapsed;
        private Visibility _detailsRowVisibility = Visibility.Visible;
        private Visibility _listRowVisibility = Visibility.Collapsed;
        private Thickness _detailsGroupHeaderMargin = new(0);

        public event PropertyChangedEventHandler? PropertyChanged;

        public static EntryViewModel CreateGroupHeader(string groupKey, string groupHeaderText, int groupItemCount, bool isExpanded)
        {
            return new EntryViewModel
            {
                Name = groupHeaderText,
                DisplayName = groupHeaderText,
                GroupKey = groupKey,
                GroupHeaderText = groupHeaderText,
                GroupItemCount = groupItemCount,
                IsGroupExpanded = isExpanded,
                IsGroupHeader = true,
                IsLoaded = false,
                Type = string.Empty,
                SizeText = string.Empty,
                ModifiedText = string.Empty,
                IconGlyph = string.Empty
            };
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                {
                    return;
                }
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName == value)
                {
                    return;
                }
                _displayName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                if (_fullPath == value)
                {
                    return;
                }
                _fullPath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullPath)));
            }
        }

        public bool IsHiddenEntry
        {
            get => _isHiddenEntry;
            set
            {
                if (_isHiddenEntry == value)
                {
                    return;
                }
                _isHiddenEntry = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHiddenEntry)));
            }
        }

        public bool IsSystemEntry
        {
            get => _isSystemEntry;
            set
            {
                if (_isSystemEntry == value)
                {
                    return;
                }
                _isSystemEntry = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSystemEntry)));
            }
        }

        public double IconOpacity
        {
            get => _iconOpacity;
            set
            {
                if (Math.Abs(_iconOpacity - value) < 0.001)
                {
                    return;
                }
                _iconOpacity = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconOpacity)));
            }
        }

        public string Type
        {
            get => _type;
            set
            {
                if (_type == value)
                {
                    return;
                }
                _type = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
            }
        }

        public string IconGlyph
        {
            get => _iconGlyph;
            set
            {
                if (_iconGlyph == value)
                {
                    return;
                }
                _iconGlyph = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconGlyph)));
            }
        }

        public Brush IconForeground
        {
            get => _iconForeground;
            set
            {
                if (ReferenceEquals(_iconForeground, value))
                {
                    return;
                }
                _iconForeground = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconForeground)));
            }
        }

        public ulong MftRef
        {
            get => _mftRef;
            set
            {
                if (_mftRef == value)
                {
                    return;
                }
                _mftRef = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MftRef)));
            }
        }

        public string SizeText
        {
            get => _sizeText;
            set
            {
                if (_sizeText == value)
                {
                    return;
                }
                _sizeText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
            }
        }

        public long? SizeBytes
        {
            get => _sizeBytes;
            set
            {
                if (_sizeBytes == value)
                {
                    return;
                }
                _sizeBytes = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeBytes)));
            }
        }

        public string ModifiedText
        {
            get => _modifiedText;
            set
            {
                if (_modifiedText == value)
                {
                    return;
                }
                _modifiedText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModifiedText)));
            }
        }

        public DateTime? ModifiedAt
        {
            get => _modifiedAt;
            set
            {
                if (_modifiedAt == value)
                {
                    return;
                }
                _modifiedAt = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModifiedAt)));
            }
        }

        public bool IsDirectory
        {
            get => _isDirectory;
            set
            {
                if (_isDirectory == value)
                {
                    return;
                }
                _isDirectory = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirectory)));
            }
        }

        public bool IsLink
        {
            get => _isLink;
            set
            {
                if (_isLink == value)
                {
                    return;
                }
                _isLink = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLink)));
            }
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set
            {
                if (_isLoaded == value)
                {
                    return;
                }
                _isLoaded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoaded)));
            }
        }

        public bool IsGroupHeader
        {
            get => _isGroupHeader;
            set
            {
                if (_isGroupHeader == value)
                {
                    return;
                }
                _isGroupHeader = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGroupHeader)));
            }
        }

        public string GroupKey
        {
            get => _groupKey;
            set
            {
                if (_groupKey == value)
                {
                    return;
                }
                _groupKey = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupKey)));
            }
        }

        public int GroupItemCount
        {
            get => _groupItemCount;
            set
            {
                if (_groupItemCount == value)
                {
                    return;
                }
                _groupItemCount = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupItemCount)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupCountText)));
            }
        }

        public bool IsGroupExpanded
        {
            get => _isGroupExpanded;
            set
            {
                if (_isGroupExpanded == value)
                {
                    return;
                }
                _isGroupExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGroupExpanded)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupExpandGlyph)));
            }
        }

        public string GroupHeaderText
        {
            get => _groupHeaderText;
            set
            {
                if (_groupHeaderText == value)
                {
                    return;
                }
                _groupHeaderText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupHeaderText)));
            }
        }

        public string GroupCountText => _groupItemCount > 0 ? $"({_groupItemCount})" : string.Empty;

        public Visibility HeaderRowVisibility
        {
            get => _headerRowVisibility;
            set
            {
                if (_headerRowVisibility == value)
                {
                    return;
                }
                _headerRowVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeaderRowVisibility)));
            }
        }

        public Visibility DetailsRowVisibility
        {
            get => _detailsRowVisibility;
            set
            {
                if (_detailsRowVisibility == value)
                {
                    return;
                }
                _detailsRowVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsRowVisibility)));
            }
        }

        public Visibility ListRowVisibility
        {
            get => _listRowVisibility;
            set
            {
                if (_listRowVisibility == value)
                {
                    return;
                }
                _listRowVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListRowVisibility)));
            }
        }

        public Thickness DetailsGroupHeaderMargin
        {
            get => _detailsGroupHeaderMargin;
            set
            {
                if (_detailsGroupHeaderMargin.Equals(value))
                {
                    return;
                }
                _detailsGroupHeaderMargin = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsGroupHeaderMargin)));
            }
        }

        public bool IsMetadataLoaded
        {
            get => _isMetadataLoaded;
            set
            {
                if (_isMetadataLoaded == value)
                {
                    return;
                }
                _isMetadataLoaded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMetadataLoaded)));
            }
        }

        public bool IsPendingCreate
        {
            get => _isPendingCreate;
            set
            {
                if (_isPendingCreate == value)
                {
                    return;
                }
                _isPendingCreate = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPendingCreate)));
            }
        }

        public bool PendingCreateIsDirectory
        {
            get => _pendingCreateIsDirectory;
            set
            {
                if (_pendingCreateIsDirectory == value)
                {
                    return;
                }
                _pendingCreateIsDirectory = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingCreateIsDirectory)));
            }
        }

        public bool IsExplicitlySelected
        {
            get => _isExplicitlySelected;
            set
            {
                if (_isExplicitlySelected == value)
                {
                    return;
                }
                _isExplicitlySelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExplicitlySelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowBackground)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowSelectionIndicatorVisibility)));
            }
        }

        public bool IsKeyboardAnchor
        {
            get => _isKeyboardAnchor;
            set
            {
                if (_isKeyboardAnchor == value)
                {
                    return;
                }

                _isKeyboardAnchor = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsKeyboardAnchor)));
            }
        }

        public bool IsSelectionActive
        {
            get => _isSelectionActive;
            set
            {
                if (_isSelectionActive == value)
                {
                    return;
                }

                _isSelectionActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionActive)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionIndicatorBrush)));
            }
        }

        public string PendingName
        {
            get => _pendingName;
            set
            {
                if (_pendingName == value)
                {
                    return;
                }
                _pendingName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingName)));
            }
        }

        public bool IsNameEditing
        {
            get => _isNameEditing;
            set
            {
                if (_isNameEditing == value)
                {
                    return;
                }
                _isNameEditing = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNameEditing)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNameReadOnly)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameEditorBackground)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameEditorBorderThickness)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameDisplayVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameEditorVisibility)));
            }
        }

        public bool IsNameReadOnly => !_isNameEditing;

        public Brush NameEditorBackground => _isNameEditing
            ? new SolidColorBrush(ColorHelper.FromArgb(0x22, 0x80, 0x80, 0x80))
            : new SolidColorBrush(Colors.Transparent);

        public Thickness NameEditorBorderThickness => _isNameEditing ? new Thickness(1) : new Thickness(0);

        public Visibility NameDisplayVisibility => _isNameEditing ? Visibility.Collapsed : Visibility.Visible;

        public Visibility NameEditorVisibility => _isNameEditing ? Visibility.Visible : Visibility.Collapsed;

        public string GroupExpandGlyph => _isGroupExpanded ? "\uE70D" : "\uE76C";

        public Brush SelectionIndicatorBrush => ResolveSelectionIndicatorBrush(_isSelectionActive);

        public Brush RowBackground => _isExplicitlySelected
            ? new SolidColorBrush(ColorHelper.FromArgb(0x14, 0x80, 0x80, 0x80))
            : new SolidColorBrush(Colors.Transparent);

        public Visibility RowSelectionIndicatorVisibility => _isExplicitlySelected ? Visibility.Visible : Visibility.Collapsed;

        private static Brush ResolveSelectionIndicatorBrush(bool isActive)
        {
            string resourceKey = isActive ? "ListViewItemSelectionIndicatorBrush" : "TextFillColorDisabledBrush";
            if (Application.Current.Resources.TryGetValue(resourceKey, out object? value) && value is Brush brush)
            {
                return brush;
            }

            return new SolidColorBrush(isActive ? ColorHelper.FromArgb(0xFF, 0x00, 0x78, 0xD4) : ColorHelper.FromArgb(0xFF, 0xC8, 0xC8, 0xC8));
        }
    }
}
