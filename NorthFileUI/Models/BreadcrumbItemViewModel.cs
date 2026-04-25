using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;

namespace NorthFileUI
{
    public sealed class BreadcrumbItemViewModel : INotifyPropertyChanged
    {
        private string _label = string.Empty;
        private string _fullPath = string.Empty;
        private string _iconGlyph = string.Empty;
        private bool _hasChildren;
        private bool _isLast;
        private double _measuredWidth;
        private Visibility _chevronVisibility = Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Label
        {
            get => _label;
            set
            {
                if (_label == value)
                {
                    return;
                }
                _label = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconVisibility)));
            }
        }

        public bool HasChildren
        {
            get => _hasChildren;
            set
            {
                if (_hasChildren == value)
                {
                    return;
                }
                _hasChildren = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChildren)));
            }
        }

        public bool IsLast
        {
            get => _isLast;
            set
            {
                if (_isLast == value)
                {
                    return;
                }
                _isLast = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLast)));
            }
        }

        public Visibility ChevronVisibility
        {
            get => _chevronVisibility;
            set
            {
                if (_chevronVisibility == value)
                {
                    return;
                }
                _chevronVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChevronVisibility)));
            }
        }

        public Visibility IconVisibility => string.IsNullOrWhiteSpace(_iconGlyph)
            ? Visibility.Collapsed
            : Visibility.Visible;

        public double MeasuredWidth
        {
            get => _measuredWidth;
            set
            {
                if (Math.Abs(_measuredWidth - value) < 0.1)
                {
                    return;
                }

                _measuredWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MeasuredWidth)));
            }
        }

        public BreadcrumbItemViewModel Clone()
        {
            return new BreadcrumbItemViewModel
            {
                Label = Label,
                FullPath = FullPath,
                IconGlyph = IconGlyph,
                HasChildren = HasChildren,
                IsLast = IsLast,
                ChevronVisibility = ChevronVisibility,
                MeasuredWidth = MeasuredWidth
            };
        }
    }
}
