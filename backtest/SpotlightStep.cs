using System.Windows;

namespace backtest
{
    /// <summary>
    /// Représente une étape du tour guidé (Spotlight).
    /// </summary>
    public class SpotlightStep
    {
        /// <summary>Élément XAML ciblé par le spotlight.</summary>
        public UIElement TargetElement { get; set; }

        /// <summary>Titre affiché dans la bulle.</summary>
        public string Title { get; set; }

        /// <summary>Description détaillée.</summary>
        public string Description { get; set; }

        /// <summary>Position de la bulle par rapport au spotlight.</summary>
        public SpotlightPosition TooltipPosition { get; set; } = SpotlightPosition.Bottom;

        /// <summary>Padding (px) autour de l'élément cible pour agrandir la zone spotlight.</summary>
        public double SpotlightPadding { get; set; } = 12;

        /// <summary>Action optionnelle à exécuter avant d'afficher cette étape (ex: dérouler un panneau).</summary>
        public System.Action OnBeforeShow { get; set; }

        /// <summary>Action optionnelle à exécuter après avoir quitté cette étape (ex: replier un panneau).</summary>
        public System.Action OnAfterHide { get; set; }
    }

    public enum SpotlightPosition
    {
        Top,
        Bottom,
        Left,
        Right
    }
}