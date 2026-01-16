using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AIA.Models;
using Wpf.Ui.Controls;
using WpfColor = System.Windows.Media.Color;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace AIA.Views
{
    public partial class ClipboardCardsView : System.Windows.Controls.UserControl
    {
        private int _activeCardIndex = 0;

        public static readonly DependencyProperty ClipboardHistoryProperty =
            DependencyProperty.Register(nameof(ClipboardHistory), typeof(ObservableCollection<DataAsset>),
                typeof(ClipboardCardsView), new PropertyMetadata(null, OnClipboardHistoryChanged));

        public ObservableCollection<DataAsset>? ClipboardHistory
        {
            get => (ObservableCollection<DataAsset>?)GetValue(ClipboardHistoryProperty);
            set => SetValue(ClipboardHistoryProperty, value);
        }

        public event EventHandler<DataAsset>? CardRestoreRequested;

        public ClipboardCardsView()
        {
            InitializeComponent();
        }

        private static void OnClipboardHistoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ClipboardCardsView view)
            {
                if (e.OldValue is ObservableCollection<DataAsset> oldCollection)
                {
                    oldCollection.CollectionChanged -= view.OnCollectionChanged;
                }

                if (e.NewValue is ObservableCollection<DataAsset> newCollection)
                {
                    newCollection.CollectionChanged += view.OnCollectionChanged;
                }

                view._activeCardIndex = 0;
                view.RebuildCards();
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _activeCardIndex = 0;
            RebuildCards();
        }

        private void RebuildCards()
        {
            CardsPanel.Children.Clear();

            if (ClipboardHistory == null || ClipboardHistory.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                return;
            }

            EmptyState.Visibility = Visibility.Collapsed;

            for (int i = 0; i < ClipboardHistory.Count; i++)
            {
                var asset = ClipboardHistory[i];

                if (i == _activeCardIndex)
                {
                    CardsPanel.Children.Add(CreateExpandedCard(asset, i));
                }
                else
                {
                    CardsPanel.Children.Add(CreateCollapsedCard(asset, i));
                }
            }
        }

        private Border CreateExpandedCard(DataAsset asset, int index)
        {
            var card = new Border
            {
                Width = 260,
                Height = 180, // Fixed height to prevent expansion
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(WpfColor.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x4C, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 0, 4),
                Tag = index,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 2,
                    Opacity = 0.3,
                    Color = Colors.Black
                }
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Content preview area
            var contentBorder = new Border
            {
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                ClipToBounds = true, // This ensures content doesn't overflow
                Background = new SolidColorBrush(WpfColor.FromArgb(0x19, 0x00, 0x00, 0x00)),
                MinHeight = 90,
                MaxHeight = 110 // Limit maximum height
            };

            var contentGrid = new Grid();

            // Add content based on type
            if (asset.AssetType == DataAssetType.ClipboardImage && asset.Thumbnail != null)
            {
                var image = new System.Windows.Controls.Image
                {
                    Source = asset.Thumbnail,
                    Stretch = Stretch.UniformToFill, // Fill the available space
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                contentGrid.Children.Add(image);
            }
            else if (asset.AssetType == DataAssetType.ClipboardText)
            {
                var textBlock = new WpfTextBlock
                {
                    Text = asset.PreviewText,
                    Foreground = new SolidColorBrush(WpfColor.FromArgb(0xCC, 0xCC, 0xCC, 0xCC)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Padding = new Thickness(12),
                    MaxHeight = 110
                };
                contentGrid.Children.Add(textBlock);
            }
            else if (asset.AssetType == DataAssetType.ClipboardFiles)
            {
                var filesPanel = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                filesPanel.Children.Add(new SymbolIcon
                {
                    Symbol = SymbolRegular.FolderOpen24,
                    Foreground = new SolidColorBrush(WpfColor.FromArgb(0xFF, 0x88, 0x88, 0x88)),
                    FontSize = 28
                });
                filesPanel.Children.Add(new WpfTextBlock
                {
                    Text = asset.PreviewText,
                    Foreground = new SolidColorBrush(WpfColor.FromArgb(0xFF, 0xAA, 0xAA, 0xAA)),
                    FontSize = 10,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 220
                });
                contentGrid.Children.Add(filesPanel);
            }

            // Type badge
            var badgeColor = asset.AssetType switch
            {
                DataAssetType.ClipboardImage => WpfColor.FromArgb(0x99, 0x00, 0x78, 0xD4),
                DataAssetType.ClipboardFiles => WpfColor.FromArgb(0x99, 0xFF, 0xA5, 0x00),
                _ => WpfColor.FromArgb(0x99, 0x1E, 0xB7, 0x5F)
            };

            var badgeSymbol = asset.AssetType switch
            {
                DataAssetType.ClipboardImage => SymbolRegular.Image20,
                DataAssetType.ClipboardFiles => SymbolRegular.FolderOpen20,
                _ => SymbolRegular.TextDescription20
            };

            var badge = new Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Padding = new Thickness(6, 3, 6, 3),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(badgeColor),
                Child = new SymbolIcon
                {
                    Symbol = badgeSymbol,
                    FontSize = 12,
                    Foreground = System.Windows.Media.Brushes.White
                }
            };
            System.Windows.Controls.Panel.SetZIndex(badge, 10);
            contentGrid.Children.Add(badge);

            // Timestamp
            var timestamp = new Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8),
                Padding = new Thickness(6, 3, 6, 3),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(WpfColor.FromArgb(0xB3, 0x00, 0x00, 0x00)),
                Child = new WpfTextBlock
                {
                    Text = asset.CapturedAtText,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(WpfColor.FromArgb(0xCC, 0xCC, 0xCC, 0xCC))
                }
            };
            System.Windows.Controls.Panel.SetZIndex(timestamp, 10);
            contentGrid.Children.Add(timestamp);

            contentBorder.Child = contentGrid;
            Grid.SetRow(contentBorder, 0);
            mainGrid.Children.Add(contentBorder);

            // Title
            var titlePanel = new StackPanel { Margin = new Thickness(12, 8, 12, 4) };
            titlePanel.Children.Add(new WpfTextBlock
            {
                Text = asset.Name,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetRow(titlePanel, 1);
            mainGrid.Children.Add(titlePanel);

            // Paste button
            var pasteButton = new Border
            {
                Margin = new Thickness(10, 4, 10, 10),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(WpfColor.FromArgb(0x26, 0x1E, 0xB7, 0x5F)),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x4D, 0x1E, 0xB7, 0x5F)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = asset
            };

            var pasteContent = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 6)
            };
            pasteContent.Children.Add(new SymbolIcon
            {
                Symbol = SymbolRegular.ClipboardPaste20,
                Foreground = new SolidColorBrush(WpfColor.FromArgb(0xFF, 0x1E, 0xB7, 0x5F)),
                FontSize = 14
            });
            pasteContent.Children.Add(new WpfTextBlock
            {
                Text = Services.LocalizationService.Instance["DataAssets_Paste"] ?? "Paste",
                Foreground = new SolidColorBrush(WpfColor.FromArgb(0xFF, 0x1E, 0xB7, 0x5F)),
                FontSize = 11,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            });

            pasteButton.Child = pasteContent;
            pasteButton.MouseLeftButtonUp += PasteButton_Click;
            pasteButton.MouseEnter += (s, e) =>
            {
                if (s is Border b)
                    b.Background = new SolidColorBrush(WpfColor.FromArgb(0x40, 0x1E, 0xB7, 0x5F));
            };
            pasteButton.MouseLeave += (s, e) =>
            {
                if (s is Border b)
                    b.Background = new SolidColorBrush(WpfColor.FromArgb(0x26, 0x1E, 0xB7, 0x5F));
            };

            Grid.SetRow(pasteButton, 2);
            mainGrid.Children.Add(pasteButton);

            card.Child = mainGrid;
            return card;
        }

        private Border CreateCollapsedCard(DataAsset asset, int index)
        {
            var typeColor = asset.AssetType switch
            {
                DataAssetType.ClipboardImage => WpfColor.FromArgb(0xFF, 0x00, 0x78, 0xD4),
                DataAssetType.ClipboardFiles => WpfColor.FromArgb(0xFF, 0xFF, 0xA5, 0x00),
                _ => WpfColor.FromArgb(0xFF, 0x1E, 0xB7, 0x5F)
            };

            var typeSymbol = asset.AssetType switch
            {
                DataAssetType.ClipboardImage => SymbolRegular.Image20,
                DataAssetType.ClipboardFiles => SymbolRegular.FolderOpen20,
                _ => SymbolRegular.TextDescription20
            };

            var card = new Border
            {
                Width = 32,
                Height = 172, // Match expanded card height (180) minus vertical margins (8)
                CornerRadius = new CornerRadius(6, 0, 0, 6), // Only round left corners
                Background = new SolidColorBrush(WpfColor.FromArgb(0x19, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1, 1, 0, 1), // No border on right
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 4, 0, 4),
                Tag = index
            };

            var grid = new Grid();

            // Color strip on left edge
            var colorStrip = new Border
            {
                Width = 3,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(3, 0, 0, 3), // Match parent corner radius
                Background = new SolidColorBrush(typeColor)
            };
            grid.Children.Add(colorStrip);

            // Type icon
            var icon = new SymbolIcon
            {
                Symbol = typeSymbol,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 14,
                Foreground = new SolidColorBrush(WpfColor.FromArgb(0xFF, 0x88, 0x88, 0x88)),
                Margin = new Thickness(4, 0, 0, 0)
            };
            grid.Children.Add(icon);

            card.Child = grid;

            // Hover effects
            card.MouseEnter += (s, e) =>
            {
                if (s is Border b)
                {
                    b.Background = new SolidColorBrush(WpfColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                    b.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                }
            };
            card.MouseLeave += (s, e) =>
            {
                if (s is Border b)
                {
                    b.Background = new SolidColorBrush(WpfColor.FromArgb(0x19, 0xFF, 0xFF, 0xFF));
                    b.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                }
            };

            card.MouseLeftButtonUp += CollapsedCard_Click;

            return card;
        }

        private void CollapsedCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is int index)
            {
                _activeCardIndex = index;
                RebuildCards();
            }
        }

        private void PasteButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is DataAsset asset)
            {
                AnimatePasteButton(element);
                CardRestoreRequested?.Invoke(this, asset);
            }
        }

        private void AnimatePasteButton(FrameworkElement element)
        {
            if (element is not Border border) return;

            var originalBrush = border.Background;
            border.Background = new SolidColorBrush(WpfColor.FromArgb(0x60, 0x1E, 0xB7, 0x5F));

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                border.Background = originalBrush;
            };
            timer.Start();
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer) return;

            // Scroll horizontally based on mouse wheel delta
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
