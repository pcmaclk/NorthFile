using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace NorthFileUI.Controls;

public enum ModalActionDialogResult
{
    Primary,
    Tertiary,
    Secondary
}

public sealed partial class ModalActionDialog : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ModalActionDialog), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(ModalActionDialog), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PrimaryTextProperty =
        DependencyProperty.Register(nameof(PrimaryText), typeof(string), typeof(ModalActionDialog), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SecondaryTextProperty =
        DependencyProperty.Register(nameof(SecondaryText), typeof(string), typeof(ModalActionDialog), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TertiaryTextProperty =
        DependencyProperty.Register(nameof(TertiaryText), typeof(string), typeof(ModalActionDialog), new PropertyMetadata(string.Empty));

    private TaskCompletionSource<ModalActionDialogResult>? _completionSource;

    public ModalActionDialog()
    {
        this.InitializeComponent();
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Margin = new Thickness(-8);
        Visibility = Visibility.Collapsed;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string PrimaryText
    {
        get => (string)GetValue(PrimaryTextProperty);
        set => SetValue(PrimaryTextProperty, value);
    }

    public string SecondaryText
    {
        get => (string)GetValue(SecondaryTextProperty);
        set => SetValue(SecondaryTextProperty, value);
    }

    public string TertiaryText
    {
        get => (string)GetValue(TertiaryTextProperty);
        set => SetValue(TertiaryTextProperty, value);
    }

    public Visibility SecondaryButtonVisibility =>
        string.IsNullOrWhiteSpace(SecondaryText)
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility TertiaryButtonVisibility =>
        string.IsNullOrWhiteSpace(TertiaryText)
            ? Visibility.Collapsed
            : Visibility.Visible;

    public async Task<ModalActionDialogResult> ShowAsync(string title, string message, string primaryText, string secondaryText, string tertiaryText = "")
    {
        Title = title;
        Message = message;
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        TertiaryText = tertiaryText;
        Bindings?.Update();
        Visibility = Visibility.Visible;
        UpdateLayout();
        PrimaryButton.Focus(FocusState.Programmatic);
        _completionSource = new TaskCompletionSource<ModalActionDialogResult>();
        return await _completionSource.Task;
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        Close(ModalActionDialogResult.Primary);
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        Close(ModalActionDialogResult.Secondary);
    }

    private void TertiaryButton_Click(object sender, RoutedEventArgs e)
    {
        Close(ModalActionDialogResult.Tertiary);
    }

    private void Close(ModalActionDialogResult result)
    {
        Visibility = Visibility.Collapsed;
        _completionSource?.TrySetResult(result);
        _completionSource = null;
    }
}
