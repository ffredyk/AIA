using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace AIA.Models
{
    public class DataBankCategory : INotifyPropertyChanged
    {
        private Guid _id;
        private string _name = string.Empty;
        private string _description = string.Empty;
        private string _color = "#0078D4";
        private DateTime _createdDate;
        private int _entryCount;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        public string Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(nameof(Color)); }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(nameof(CreatedDate)); }
        }

        public int EntryCount
        {
            get => _entryCount;
            set 
            { 
                _entryCount = value; 
                OnPropertyChanged(nameof(EntryCount));
                OnPropertyChanged(nameof(EntryCountText));
            }
        }

        public string EntryCountText => EntryCount == 1 ? "1 entry" : $"{EntryCount} entries";

        public DataBankCategory()
        {
            Id = Guid.NewGuid();
            CreatedDate = DateTime.Now;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
