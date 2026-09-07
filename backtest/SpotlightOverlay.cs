using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace backtest
{
    /// <summary>
    /// Système de Spotlight/Feature Highlight pour le tour guidé du dashboard.
    /// Crée un overlay plein écran avec une découpe "spotlight" autour de l'élément ciblé,
    /// une bulle d'information cyber/neon, et une navigation pas-à-pas.
    /// </summary>
    public class SpotlightOverlay
    {
        private readonly Window _owner;
        private readonly List<SpotlightStep> _steps = new List<SpotlightStep>();
        private int _currentStepIndex = -1;

        // Éléments visuels de l'overlay
        private Grid _overlayRoot;
        private Path _darkOverlay;
        private Border _tooltipCard;
        private Border _glowRing;
        private StackPanel _dotsPanel;
        private Path _arrowPath;
        private Grid _containerGrid;

        // Contrôles de navigation
        private Button _prevBtn, _nextBtn, _skipBtn;
        private TextBlock _titleText, _descText;

        // Couleurs du thème cyber
        private static readonly Color AccentCyan = Color.FromRgb(0x00, 0xBF, 0xFF);
        private static readonly Color BgDark = Color.FromRgb(0x0D, 0x14, 0x1D);
        private static readonly Color OverlayBg = Color.FromArgb(0xCC, 0x0A, 0x11, 0x1A);

        public SpotlightOverlay(Window owner)
        {
            _owner = owner;
        }

        public void AddStep(UIElement target, string title, string description,
            SpotlightPosition position = SpotlightPosition.Bottom,
            double padding = 12,
            Action onBeforeShow = null,
            Action onAfterHide = null)
        {
            _steps.Add(new SpotlightStep
            {
                TargetElement = target,
                Title = title,
                Description = description,
                TooltipPosition = position,
                SpotlightPadding = padding,
                OnBeforeShow = onBeforeShow,
                OnAfterHide = onAfterHide
            });
        }

        public void Show()
        {
            if (_steps.Count == 0) return;

            // Trouver le Grid conteneur principal dans MainWindow
            // MainWindow > Border > Grid
            var mainBorder = (Border)_owner.Content;
            _containerGrid = (Grid)mainBorder.Child;

            // Créer l'arbre visuel
            CreateOverlayVisual();

            // Ajouter au conteneur avec ZIndex maximal
            _containerGrid.Children.Add(_overlayRoot);
            Grid.SetRowSpan(_overlayRoot, 10);
            Grid.SetColumnSpan(_overlayRoot, 10);

            // Animer l'entrée (fade in)
            _overlayRoot.Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            _overlayRoot.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // Première étape après un petit delai
            _overlayRoot.Dispatcher.BeginInvoke(new Action(() => GoToStep(0)),
                System.Windows.Threading.DispatcherPriority.Background);
        }
private void CreateOverlayVisual()
        {
            _overlayRoot = new Grid
            {
                IsHitTestVisible = true
            };
            Panel.SetZIndex(_overlayRoot, 500);

            // 1. Capture-clic : rectangle quasi-transparent interceptant TOUS les clics
            var clickCatcher = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)),
                IsHitTestVisible = true
            };
            clickCatcher.MouseDown += (s, e) => e.Handled = true;
            _overlayRoot.Children.Add(clickCatcher);

            // 2. Overlay sombre avec découpe spotlight (CombinedGeometry)
            _darkOverlay = new Path
            {
                Fill = new SolidColorBrush(OverlayBg),
                IsHitTestVisible = false
            };
            _overlayRoot.Children.Add(_darkOverlay);

            // 3. Anneau lumineux néon autour du spotlight
            _glowRing = new Border
            {
                BorderBrush = new SolidColorBrush(AccentCyan),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(0x15, 0x00, 0xBF, 0xFF)),
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = AccentCyan,
                    BlurRadius = 24,
                    Opacity = 0.6,
                    ShadowDepth = 0
                }
            };
            _overlayRoot.Children.Add(_glowRing);

            // 4. Flèche du tooltip (sera positionnée et dimensionnée dynamiquement)
            _arrowPath = new Path
            {
                Fill = new SolidColorBrush(BgDark),
                IsHitTestVisible = false,
                Stretch = Stretch.None
            };
            _overlayRoot.Children.Add(_arrowPath);

            // 5. Carte tooltip
            _tooltipCard = CreateTooltipCard();
            _overlayRoot.Children.Add(_tooltipCard);

            // 6. Barre de progression en bas
            var bottomBar = CreateBottomBar();
            _overlayRoot.Children.Add(bottomBar);
        }

        private Border CreateTooltipCard()
        {
            var card = new Border
            {
                Background = new SolidColorBrush(BgDark),
                BorderBrush = new SolidColorBrush(AccentCyan),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Width = 330,
                Padding = new Thickness(20, 18, 20, 16),
                IsHitTestVisible = true,
                Effect = new DropShadowEffect
                {
                    Color = AccentCyan,
                    BlurRadius = 15,
                    Opacity = 0.35,
                    ShadowDepth = 0
                }
            };

            var stack = new StackPanel();

            // Titre
            _titleText = new TextBlock
            {
                Foreground = new SolidColorBrush(AccentCyan),
                FontSize = 15,
                FontWeight = FontWeights.Black,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            stack.Children.Add(_titleText);

            // Description
            _descText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                LineHeight = 18
            };
            stack.Children.Add(_descText);

            // Séparateur
            stack.Children.Add(new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xBF, 0xFF)),
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Boutons de navigation
            var navStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // PRÉCÉDENT
            _prevBtn = CreateNavButton("◀  PRÉCÉDENT", false);
            _prevBtn.Click += (s, e) => GoToStep(_currentStepIndex - 1);
            navStack.Children.Add(_prevBtn);

            // SUIVANT / TERMINER
            _nextBtn = CreateNavButton("SUIVANT  →", true);
            _nextBtn.Click += (s, e) =>
            {
                if (_currentStepIndex >= _steps.Count - 1)
                    Close();
                else
                    GoToStep(_currentStepIndex + 1);
            };
            navStack.Children.Add(_nextBtn);

            // IGNORER
            _skipBtn = new Button
            {
                Content = "✕  PASSER",
                FontSize = 10.5,
                Height = 34,
                Padding = new Thickness(12, 0, 12, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1)
            };
            _skipBtn.Click += (s, e) => Close();
            navStack.Children.Add(_skipBtn);

            stack.Children.Add(navStack);
            card.Child = stack;

            return card;
        }
private Button CreateNavButton(string text, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = new SolidColorBrush(isPrimary ? AccentCyan : Color.FromRgb(0x88, 0xCC, 0xFF)),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(isPrimary ? AccentCyan : Color.FromRgb(0x44, 0x88, 0xBB)),
                BorderThickness = new Thickness(1.5)
            };

            // Template avec effet néon
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;

            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0xBF, 0xFF))));
            template.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xBF, 0xFF))));
            template.Triggers.Add(pressedTrigger);

            var disabledTrigger = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(Button.OpacityProperty, 0.3));
            template.Triggers.Add(disabledTrigger);

            btn.Template = template;
            return btn;
        }

        private Border CreateBottomBar()
        {
            var bar = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18)
            };
            _dotsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            bar.Child = _dotsPanel;
            return bar;
        }

        private void GoToStep(int index)
        {
            if (index < 0 || index >= _steps.Count) return;

            // Nettoyer l'étape précédente
            if (_currentStepIndex >= 0 && _currentStepIndex < _steps.Count)
                _steps[_currentStepIndex].OnAfterHide?.Invoke();

            _currentStepIndex = index;
            var step = _steps[index];
            step.OnBeforeShow?.Invoke();

            // Mettre à jour le spotlight et le tooltip
            UpdateSpotlightAndTooltip(step);

            // Navigation
            _prevBtn.IsEnabled = index > 0;
            _nextBtn.Content = index == _steps.Count - 1 ? "✓  TERMINER" : "SUIVANT  →";

            // Dots
            UpdateDots(index);
        }
private Rect GetElementBounds(UIElement element)
        {
            try
            {
                var transform = element.TransformToAncestor(_containerGrid);
                return transform.TransformBounds(
                    new Rect(new Size(element.RenderSize.Width, element.RenderSize.Height)));
            }
            catch
            {
                return new Rect(0, 0, 100, 100);
            }
        }

        private void UpdateSpotlightAndTooltip(SpotlightStep step)
        {
            var element = step.TargetElement;
            var padding = step.SpotlightPadding;
            var bounds = GetElementBounds(element);

            double sx = bounds.X - padding;
            double sy = bounds.Y - padding;
            double sw = bounds.Width + padding * 2;
            double sh = bounds.Height + padding * 2;

            double overlayW = _containerGrid.ActualWidth;
            double overlayH = _containerGrid.ActualHeight;

            // Découpe spotlight : CombinedGeometry excluant le trou
            var outerRect = new RectangleGeometry(new Rect(0, 0, overlayW, overlayH));
            var innerRect = new RectangleGeometry(new Rect(sx, sy, sw, sh));
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, outerRect, innerRect);
            _darkOverlay.Data = combined;

            // Anneau néon autour du spotlight
            _glowRing.Width = sw + 8;
            _glowRing.Height = sh + 8;
            _glowRing.HorizontalAlignment = HorizontalAlignment.Left;
            _glowRing.VerticalAlignment = VerticalAlignment.Top;
            _glowRing.Margin = new Thickness(sx - 4, sy - 4, 0, 0);

            // Contenu tooltip
            _titleText.Text = step.Title;
            _descText.Text = step.Description;

            // Mesure du tooltip
            _tooltipCard.Measure(new Size(330, double.PositiveInfinity));
            double tw = Math.Max(220, Math.Min(330, _tooltipCard.DesiredSize.Width));
            double th = Math.Max(80, _tooltipCard.DesiredSize.Height);

            double tipX, tipY;

            switch (step.TooltipPosition)
            {
                case SpotlightPosition.Top:
                    tipX = sx + sw / 2 - tw / 2;
                    tipY = sy - th - 18;
                    tipX = Clamp(tipX, 10, overlayW - tw - 10);
                    tipY = Math.Max(10, tipY);
                    break;

                case SpotlightPosition.Right:
                    tipX = sx + sw + 18;
                    tipY = sy + sh / 2 - th / 2;
                    tipX = Math.Min(tipX, overlayW - tw - 10);
                    tipY = Clamp(tipY, 10, overlayH - th - 60);
                    break;

                case SpotlightPosition.Left:
                    tipX = sx - tw - 18;
                    tipY = sy + sh / 2 - th / 2;
                    tipX = Math.Max(10, tipX);
                    tipY = Clamp(tipY, 10, overlayH - th - 60);
                    break;

                default: // Bottom
                    tipX = sx + sw / 2 - tw / 2;
                    tipY = sy + sh + 18;
                    tipX = Clamp(tipX, 10, overlayW - tw - 10);
                    tipY = Math.Min(tipY, overlayH - th - 60);
                    break;
            }

            _tooltipCard.Width = tw;
            _tooltipCard.HorizontalAlignment = HorizontalAlignment.Left;
            _tooltipCard.VerticalAlignment = VerticalAlignment.Top;
            _tooltipCard.Margin = new Thickness(tipX, tipY, 0, 0);

            // Flèche
            PositionArrow(step, tipX, tipY, sx, sy, sw, sh, tw, th);
        }

        private double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
private void PositionArrow(SpotlightStep step, double tipX, double tipY,
            double sx, double sy, double sw, double sh, double tw, double th)
        {
            const double size = 10;
            PathFigure figure = null;
            double arrowX = 0, arrowY = 0;

            switch (step.TooltipPosition)
            {
                case SpotlightPosition.Top:
                    figure = new PathFigure
                    {
                        StartPoint = new Point(0, 0),
                        Segments = new PathSegmentCollection
                        {
                            new LineSegment(new Point(size, size), true),
                            new LineSegment(new Point(size * 2, 0), true)
                        },
                        IsClosed = true
                    };
                    arrowX = tipX + tw / 2 - size;
                    arrowY = tipY + th;
                    break;

                case SpotlightPosition.Right:
                    figure = new PathFigure
                    {
                        StartPoint = new Point(size, 0),
                        Segments = new PathSegmentCollection
                        {
                            new LineSegment(new Point(0, size), true),
                            new LineSegment(new Point(size, size * 2), true)
                        },
                        IsClosed = true
                    };
                    arrowX = tipX - size;
                    arrowY = tipY + th / 2 - size;
                    break;

                case SpotlightPosition.Left:
                    figure = new PathFigure
                    {
                        StartPoint = new Point(0, 0),
                        Segments = new PathSegmentCollection
                        {
                            new LineSegment(new Point(size, size), true),
                            new LineSegment(new Point(0, size * 2), true)
                        },
                        IsClosed = true
                    };
                    arrowX = tipX + tw;
                    arrowY = tipY + th / 2 - size;
                    break;

                default: // Bottom
                    figure = new PathFigure
                    {
                        StartPoint = new Point(0, size),
                        Segments = new PathSegmentCollection
                        {
                            new LineSegment(new Point(size, 0), true),
                            new LineSegment(new Point(size * 2, size), true)
                        },
                        IsClosed = true
                    };
                    arrowX = tipX + tw / 2 - size;
                    arrowY = tipY - size;
                    break;
            }

            if (figure != null)
            {
                _arrowPath.Data = new PathGeometry { Figures = new PathFigureCollection { figure } };
                _arrowPath.HorizontalAlignment = HorizontalAlignment.Left;
                _arrowPath.VerticalAlignment = VerticalAlignment.Top;
                _arrowPath.Margin = new Thickness(arrowX, arrowY, 0, 0);
            }
        }

        private void UpdateDots(int activeIndex)
        {
            _dotsPanel.Children.Clear();
            for (int i = 0; i < _steps.Count; i++)
            {
                var dot = new Ellipse
                {
                    Width = i == activeIndex ? 10 : 7,
                    Height = i == activeIndex ? 10 : 7,
                    Fill = i == activeIndex
                        ? new SolidColorBrush(AccentCyan)
                        : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    StrokeThickness = 1,
                    Margin = new Thickness(5, 0, 5, 0)
                };
                if (i == activeIndex)
                {
                    dot.Effect = new DropShadowEffect
                    {
                        Color = AccentCyan,
                        BlurRadius = 8,
                        Opacity = 0.6,
                        ShadowDepth = 0
                    };
                }
                _dotsPanel.Children.Add(dot);
            }
        }

        public void Close()
        {
            if (_currentStepIndex >= 0 && _currentStepIndex < _steps.Count)
                _steps[_currentStepIndex].OnAfterHide?.Invoke();

            // Fade out
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.25))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                _containerGrid?.Children.Remove(_overlayRoot);
            };
            _overlayRoot.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}