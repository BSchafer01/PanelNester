using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PanelNester.Desktop;

public partial class ThemedPromptWindow : Window
{
    private ThemedPromptWindow(ThemedPromptOptions options)
    {
        InitializeComponent();

        Title = string.IsNullOrWhiteSpace(options.Title) ? "OptiFab" : options.Title;
        DialogTitleTextBlock.Text = Title.ToUpperInvariant();
        HeadlineTextBlock.Text = options.Headline;
        MessageTextBlock.Text = options.Message;

        ConfigureTone(options.Tone);
        ConfigureButtons(options);

        PreviewKeyDown += HandlePreviewKeyDown;
        Loaded += (_, _) => FocusDefaultButton();
    }

    private ThemedPromptResult Result { get; set; } = ThemedPromptResult.Cancel;

    internal static ThemedPromptResult Show(Window owner, ThemedPromptOptions options)
    {
        var dialog = new ThemedPromptWindow(options)
        {
            Owner = owner
        };

        dialog.ShowDialog();
        return dialog.Result;
    }

    private void ConfigureTone(ThemedPromptTone tone)
    {
        switch (tone)
        {
            case ThemedPromptTone.Error:
                ToneBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x1F, 0x22));
                ToneBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
                ToneBadgeTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x95, 0x8A));
                ToneBadgeTextBlock.Text = "!";
                break;
            case ThemedPromptTone.Warning:
                ToneBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x2F, 0x12));
                ToneBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA1, 0x00));
                ToneBadgeTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xF7, 0xC9, 0x48));
                ToneBadgeTextBlock.Text = "!";
                break;
            default:
                ToneBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x2C, 0x45));
                ToneBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
                ToneBadgeTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0xD0, 0xFF));
                ToneBadgeTextBlock.Text = "i";
                break;
        }
    }

    private void ConfigureButtons(ThemedPromptOptions options)
    {
        PrimaryButton.Content = options.PrimaryButtonText.ToUpperInvariant();
        PrimaryButton.Visibility = string.IsNullOrWhiteSpace(options.PrimaryButtonText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        PrimaryButton.IsDefault = options.DefaultResult == ThemedPromptResult.Primary;

        SecondaryButton.Content = options.SecondaryButtonText?.ToUpperInvariant() ?? string.Empty;
        SecondaryButton.Visibility = string.IsNullOrWhiteSpace(options.SecondaryButtonText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        SecondaryButton.IsDefault = options.DefaultResult == ThemedPromptResult.Secondary;

        CancelButton.Content = options.CancelButtonText?.ToUpperInvariant() ?? string.Empty;
        CancelButton.Visibility = string.IsNullOrWhiteSpace(options.CancelButtonText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        CancelButton.IsDefault = options.DefaultResult == ThemedPromptResult.Cancel;
        CancelButton.IsCancel = CancelButton.Visibility == Visibility.Visible;
    }

    private void FocusDefaultButton()
    {
        if (PrimaryButton.IsDefault && PrimaryButton.Visibility == Visibility.Visible)
        {
            PrimaryButton.Focus();
            return;
        }

        if (SecondaryButton.IsDefault && SecondaryButton.Visibility == Visibility.Visible)
        {
            SecondaryButton.Focus();
            return;
        }

        if (CancelButton.Visibility == Visibility.Visible)
        {
            CancelButton.Focus();
            return;
        }

        PrimaryButton.Focus();
    }

    private void HandlePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (CancelButton.Visibility == Visibility.Visible)
            {
                SetResultAndClose(ThemedPromptResult.Cancel);
            }
            else
            {
                SetResultAndClose(ThemedPromptResult.Primary);
            }

            e.Handled = true;
        }
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e) =>
        SetResultAndClose(ThemedPromptResult.Primary);

    private void SecondaryButton_Click(object sender, RoutedEventArgs e) =>
        SetResultAndClose(ThemedPromptResult.Secondary);

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        SetResultAndClose(ThemedPromptResult.Cancel);

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        var closeResult = CancelButton.Visibility == Visibility.Visible
            ? ThemedPromptResult.Cancel
            : ThemedPromptResult.Primary;
        SetResultAndClose(closeResult);
    }

    private void SetResultAndClose(ThemedPromptResult result)
    {
        Result = result;
        DialogResult = true;
    }
}

internal sealed class ThemedPromptOptions
{
    public string Title { get; init; } = "OptiFab";

    public string Headline { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string PrimaryButtonText { get; init; } = "OK";

    public string? SecondaryButtonText { get; init; }

    public string? CancelButtonText { get; init; }

    public ThemedPromptTone Tone { get; init; } = ThemedPromptTone.Info;

    public ThemedPromptResult DefaultResult { get; init; } = ThemedPromptResult.Primary;
}

internal enum ThemedPromptTone
{
    Info,
    Warning,
    Error
}

internal enum ThemedPromptResult
{
    Primary,
    Secondary,
    Cancel
}
