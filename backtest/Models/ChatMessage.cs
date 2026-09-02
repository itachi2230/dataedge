using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace backtest.Models
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _sender;
        private string _text;
        private DateTime _timestamp;

        public string Sender
        {
            get => _sender;
            set
            {
                if (_sender != value)
                {
                    _sender = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsUser));
                    OnPropertyChanged(nameof(Alignment));
                    OnPropertyChanged(nameof(BubbleColor));
                    OnPropertyChanged(nameof(BorderColor));
                }
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (_timestamp != value)
                {
                    _timestamp = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsUser => Sender == "User";

        // Conversion propre pour WPF
        public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        public Brush BubbleColor => (Brush)new BrushConverter().ConvertFromString(IsUser ? "#1A2432" : "#0D131A");
        public Brush BorderColor => (Brush)new BrushConverter().ConvertFromString(IsUser ? "Cyan" : "#FF00FF03");

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}