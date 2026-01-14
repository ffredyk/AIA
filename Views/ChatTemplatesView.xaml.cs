using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using AIA.Models;

namespace AIA.Views
{
    public partial class ChatTemplatesView : System.Windows.Controls.UserControl
    {
        public ChatTemplatesView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            TemplatesControl.Loaded += TemplatesControl_Loaded;
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        /// <summary>
        /// Event raised when a template is clicked
        /// </summary>
        public event EventHandler<ChatMessageTemplate>? TemplateClicked;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old ViewModel's collection if exists
            if (e.OldValue is OverlayViewModel oldViewModel)
            {
                oldViewModel.ChatMessageTemplates.CollectionChanged -= OnTemplatesCollectionChanged;
            }

            // Subscribe to new ViewModel's collection
            if (e.NewValue is OverlayViewModel newViewModel)
            {
                newViewModel.ChatMessageTemplates.CollectionChanged += OnTemplatesCollectionChanged;
                UpdateEmptyState();
            }
        }

        private void OnTemplatesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateEmptyState();
            // Update colors when items change
            UpdateTemplateColors();
        }

        private void TemplatesControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTemplateColors();
        }

        private void UpdateTemplateColors()
        {
            // This method updates the colors of template items after they're rendered
            // to avoid the binding errors with frozen SolidColorBrush objects
            if (TemplatesControl.ItemsSource == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var item in TemplatesControl.Items)
                {
                    if (item is ChatMessageTemplate template)
                    {
                        var container = TemplatesControl.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
                        if (container != null)
                        {
                            // Find the Border in the template
                            var border = FindVisualChild<Border>(container);
                            if (border != null && border.Tag == template)
                            {
                                // Get the color from the converter
                                var converter = TryFindResource("ColorConverter") as IValueConverter;
                                if (converter != null)
                                {
                                    var color = converter.Convert(template.Color, typeof(System.Windows.Media.Color), null, System.Globalization.CultureInfo.CurrentCulture);
                                    if (color is System.Windows.Media.Color c)
                                    {
                                        border.Background = new SolidColorBrush(c) { Opacity = 0.15 };
                                        border.BorderBrush = new SolidColorBrush(c) { Opacity = 0.4 };
                                        
                                        // Update icon background
                                        var iconBorder = FindVisualChild<Border>(border, b => b.Width == 36 && b.Height == 36);
                                        if (iconBorder != null)
                                        {
                                            iconBorder.Background = new SolidColorBrush(c) { Opacity = 0.3 };
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private T? FindVisualChild<T>(DependencyObject parent, Func<T, bool>? predicate = null) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild && (predicate == null || predicate(typedChild)))
                {
                    return typedChild;
                }

                var result = FindVisualChild<T>(child, predicate);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void TemplateButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is ChatMessageTemplate template)
            {
                // Raise event for parent to handle
                TemplateClicked?.Invoke(this, template);
            }
        }

        private void UpdateEmptyState()
        {
            if (ViewModel?.ChatMessageTemplates != null && ViewModel.ChatMessageTemplates.Count > 0)
            {
                EmptyState.Visibility = Visibility.Collapsed;
                TemplatesControl.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyState.Visibility = Visibility.Visible;
                TemplatesControl.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            if (e.Property.Name == nameof(DataContext))
            {
                UpdateEmptyState();
            }
        }
    }
}
