using System;
using System.Collections.Generic;
using System.ComponentModel;
using AIA.Models.Automation;

namespace AIA.Views.Automation
{
    /// <summary>
    /// ViewModel wrapper for AutomationChainTrigger that provides the list of available automations
    /// </summary>
    public class ChainTriggerViewModel : INotifyPropertyChanged
    {
        private readonly AutomationChainTrigger? _trigger;
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public ChainTriggerViewModel(AutomationChainTrigger? trigger, IReadOnlyList<AutomationTask> availableAutomations)
        {
            _trigger = trigger;
            AvailableAutomations = availableAutomations;

            if (_trigger != null)
            {
                _trigger.PropertyChanged += (s, e) => 
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(e.PropertyName));
            }
        }

        public IReadOnlyList<AutomationTask> AvailableAutomations { get; }

        public Guid SourceAutomationId
        {
            get => _trigger?.SourceAutomationId ?? Guid.Empty;
            set
            {
                if (_trigger != null)
                {
                    _trigger.SourceAutomationId = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SourceAutomationId)));
                }
            }
        }

        public bool RequireSuccess
        {
            get => _trigger?.RequireSuccess ?? true;
            set
            {
                if (_trigger != null)
                {
                    _trigger.RequireSuccess = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RequireSuccess)));
                }
            }
        }

        public string Name
        {
            get => _trigger?.Name ?? string.Empty;
            set
            {
                if (_trigger != null)
                {
                    _trigger.Name = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                }
            }
        }

        public int DebounceMsec
        {
            get => _trigger?.DebounceMsec ?? 500;
            set
            {
                if (_trigger != null)
                {
                    _trigger.DebounceMsec = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DebounceMsec)));
                }
            }
        }
    }
}
