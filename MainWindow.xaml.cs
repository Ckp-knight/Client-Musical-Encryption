using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // ToggleButton
using System.Windows.Documents;           // TextElement
using System.Windows.Media;               // Brush

namespace Musical_Encryption_Project;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Guards the two-way copy between a PasswordBox and its visible TextBox
    // so the change events don't bounce back and forth.
    private bool _sync;

    // Guards the "reveal all" fan-out so setting one eye doesn't re-enter endlessly.
    private bool _revealSync;

    // Every password field on the window: the masked box, its visible twin, and its eye.
    private readonly List<(PasswordBox pb, TextBox tb, ToggleButton eye)> _passwordFields = new();

    // The one object in this project that talks to the server.
    private readonly ApiClient _api = new();

    // Set once login succeeds. Not shown anywhere yet, but this is what future
    // requests (e.g. encrypting something) will send as the X-User-Id header.
    private string? _loggedInUsername;

    // Filled into the login fields by the logo's admin quick-fill (see LogoButton_Click).
    private const string AdminUsername = "admin";
    private const string AdminPassword = "Admin1234";

    public MainWindow()
    {
        InitializeComponent();

        WirePasswordField(PasswordBox, PasswordText, PasswordReveal, validate: false);
        WirePasswordField(NewPasswordBox, NewPasswordText, NewPasswordReveal, validate: true);
        WirePasswordField(ConfirmPasswordBox, ConfirmPasswordText, ConfirmPasswordReveal, validate: true);
        NewUsernameTextBox.TextChanged += (_, _) => Validate();
        CreateAccountButton.Click += async (_, _) => await CreateAccountAsync();
        LoginButton.Click += async (_, _) => await LoginAsync();
        ExitButton.Click += (_, _) => SignOut();
        LogoButton.Click += (_, _) => FillAdminCredentials();
        Closed += (_, _) => _api.Dispose();

        Validate(); // start: all rules red, "Create Account" disabled
    }

    // Sends the new account to the server and shows the result under the button.
    private async System.Threading.Tasks.Task CreateAccountAsync()
    {
        CreateAccountButton.IsEnabled = false;
        CreateAccountStatusText.Foreground = (Brush)FindResource("TextColor");
        CreateAccountStatusText.Text = "Creating account...";

        var result = await _api.RegisterAsync(NewUsernameTextBox.Text, NewPasswordBox.Password);

        CreateAccountStatusText.Foreground = (Brush)FindResource(result.Success ? "AccentGreen" : "RuleRed");
        CreateAccountStatusText.Text = result.Message;

        if (result.Success)
        {
            NewUsernameTextBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            NavToLogin.IsChecked = true; // account created -> back to the login screen
        }

        Validate(); // re-checks the rules and restores the button's enabled state
    }

    // Checks the username/password against the server and, on success, switches to the app screen.
    private async System.Threading.Tasks.Task LoginAsync()
    {
        LoginButton.IsEnabled = false;
        LoginStatusText.Foreground = (Brush)FindResource("TextColor");
        LoginStatusText.Text = "Signing in...";

        var result = await _api.LoginAsync(UsernameTextBox.Text, PasswordBox.Password);

        LoginButton.IsEnabled = true;

        if (result.Success)
        {
            _loggedInUsername = result.Username;
            LoginStatusText.Text = string.Empty;
            UsernameTextBox.Clear();
            PasswordBox.Clear();
            NavToApp.IsChecked = true; // logged in -> leave the login/create-account screens
        }
        else
        {
            LoginStatusText.Foreground = (Brush)FindResource("RuleRed");
            LoginStatusText.Text = result.Message;
        }
    }

    // "Exit" on the app screen: forget who's signed in and go back to the login screen.
    private void SignOut()
    {
        _loggedInUsername = null;
        NavToLogin.IsChecked = true;
    }

    // Logo click: jump to the login screen and drop in the admin test credentials,
    // so there's no need to create/type a user every time during development.
    private void FillAdminCredentials()
    {
        NavToLogin.IsChecked = true;
        UsernameTextBox.Text = AdminUsername;
        PasswordBox.Password = AdminPassword;
    }

    // Keeps the PasswordBox and the visible TextBox in sync, and hooks the eye
    // button into the shared "reveal every password field" toggle.
    private void WirePasswordField(PasswordBox pb, TextBox tb, ToggleButton eye, bool validate)
    {
        _passwordFields.Add((pb, tb, eye));

        pb.PasswordChanged += (_, _) =>
        {
            if (_sync) return;
            _sync = true;
            tb.Text = pb.Password;
            _sync = false;
            if (validate) Validate();
        };

        tb.TextChanged += (_, _) =>
        {
            if (_sync) return;
            _sync = true;
            pb.Password = tb.Text;
            _sync = false;
            if (validate) Validate();
        };

        eye.Checked += (_, _) => ApplyReveal(true);
        eye.Unchecked += (_, _) => ApplyReveal(false);
    }

    // One eye controls them all: reveal or mask every password field together.
    private void ApplyReveal(bool reveal)
    {
        if (_revealSync) return;
        _revealSync = true;

        foreach (var (pb, tb, eye) in _passwordFields)
        {
            eye.IsChecked = reveal;
            tb.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
            pb.Visibility = reveal ? Visibility.Collapsed : Visibility.Visible;
        }

        _revealSync = false;
    }

    // Re-checks every create-account rule and enables the button only when all pass.
    private void Validate()
    {
        string u = NewUsernameTextBox.Text;
        string p = NewPasswordBox.Password;
        string c = ConfirmPasswordBox.Password;

        bool len = u.Length is >= 3 and <= 20;
        bool chars = u.Length > 0 && u.All(ch => ch < 128 && char.IsLetterOrDigit(ch));
        bool start = u.Length > 0 && char.IsAsciiLetter(u[0]);
        bool pwLen = p.Length >= 8;
        bool upper = p.Any(char.IsUpper);
        bool lower = p.Any(char.IsLower);
        bool digit = p.Any(char.IsDigit);
        bool match = c.Length > 0 && c == p;

        SetRule(RuleLen, RuleLenIcon, len);
        SetRule(RuleChars, RuleCharsIcon, chars);
        SetRule(RuleStart, RuleStartIcon, start);
        SetRule(RulePwLen, RulePwLenIcon, pwLen);
        SetRule(RulePwUpper, RulePwUpperIcon, upper);
        SetRule(RulePwLower, RulePwLowerIcon, lower);
        SetRule(RulePwDigit, RulePwDigitIcon, digit);
        SetRule(RuleMatch, RuleMatchIcon, match);

        CreateAccountButton.IsEnabled =
            len && chars && start && pwLen && upper && lower && digit && match;
    }

    // Colours a rule row green when met, red when not, and swaps its check/cross icon.
    private void SetRule(StackPanel row, TextBlock icon, bool ok)
    {
        Brush brush = (Brush)FindResource(ok ? "AccentGreen" : "RuleRed");
        row.SetValue(TextElement.ForegroundProperty, brush);
        icon.Text = ok ? "✓" : "✗";
    }
}
