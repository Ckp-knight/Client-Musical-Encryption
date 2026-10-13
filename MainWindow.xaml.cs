using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // ToggleButton
using System.Windows.Documents;           // TextElement
using System.Windows.Input;               // Keyboard, FocusNavigationDirection, TraversalRequest
using System.Windows.Media;               // Brush
using System.Windows.Shapes;              // Ellipse
using System.Windows.Threading;           // DispatcherPriority

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

        // Keep the designer's 820x660 default on normal screens, but never open larger
        // than the actual work area (small laptops, netbooks, RDP sessions). WorkArea is
        // reported in the same device-independent units as Width/Height, so no DPI
        // conversion is needed here.
        ClampStartupSizeToWorkArea();

        WirePasswordField(PasswordBox, PasswordText, PasswordReveal, validate: false);
        WirePasswordField(NewPasswordBox, NewPasswordText, NewPasswordReveal, validate: true);
        WirePasswordField(ConfirmPasswordBox, ConfirmPasswordText, ConfirmPasswordReveal, validate: true);
        WirePasswordField(OldPasswordBox, OldPasswordText, OldPasswordReveal, validate: false, onChange: ValidateChangePassword);
        WirePasswordField(ChangeNewPasswordBox, ChangeNewPasswordText, ChangeNewPasswordReveal, validate: false, onChange: ValidateChangePassword);
        WirePasswordField(ChangeConfirmPasswordBox, ChangeConfirmPasswordText, ChangeConfirmPasswordReveal, validate: false, onChange: ValidateChangePassword);
        NewUsernameTextBox.TextChanged += (_, _) => Validate();
        CreateAccountButton.Click += async (_, _) => await CreateAccountAsync();
        LoginButton.Click += async (_, _) => await LoginAsync();
        ContinueWithoutAccountButton.Click += (_, _) => ContinueWithoutAccount();
        ExitButton.Click += (_, _) => SignOut();
        LogoButton.Click += (_, _) => FillAdminCredentials();
        ChangePasswordButton.Click += (_, _) => OpenChangePasswordScreen();
        ChangePasswordSubmitButton.Click += async (_, _) => await ChangePasswordAsync();
        Closed += (_, _) => _api.Dispose();

        // Autofocus the first field of whichever screen becomes active, like a typical
        // website form. NavToLogin/NavToLoginFromChangePassword cover every "back to
        // login" path (successful login/create/change-password, sign out, admin
        // quick-fill, and the plain back-link click); NavToCreateUser is a plain
        // radio-button click with no dedicated code-behind method to hook into instead.
        NavToCreateUser.Checked += (_, _) => FocusDeferred(NewUsernameTextBox);
        NavToLogin.Checked += (_, _) => FocusDeferred(UsernameTextBox);
        NavToLoginFromChangePassword.Checked += (_, _) => FocusDeferred(UsernameTextBox);
        // NavToLogin's IsChecked="True" in XAML is set during InitializeComponent(), before
        // the handler above is attached, so the very first screen needs its own focus call.
        Loaded += (_, _) => FocusDeferred(UsernameTextBox);

        PreviewKeyDown += Window_PreviewKeyDown;

        // The strength-meter label's text (Weak/Medium/Strong) is set imperatively in
        // code-behind, unlike every other bilingual string here which is pure XAML
        // DataTriggers — so it needs an explicit re-run to pick up a language switch.
        LangToggle.Checked += (_, _) => { Validate(); ValidateChangePassword(); };
        LangToggle.Unchecked += (_, _) => { Validate(); ValidateChangePassword(); };

        Validate(); // start: all rules red, "Create Account" disabled
        ValidateChangePassword(); // start: all rules red, "Change Password" disabled
    }

    // Shrinks (never grows) the startup size to fit the primary monitor's work area, so
    // the window never opens larger than a small laptop/netbook/RDP screen can show.
    private void ClampStartupSizeToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 20;

        if (Width > workArea.Width - margin)
            Width = Math.Max(MinWidth, workArea.Width - margin);

        if (Height > workArea.Height - margin)
            Height = Math.Max(MinHeight, workArea.Height - margin);
    }

    // Focuses an element after the current screen's Visibility change has actually been
    // laid out — calling .Focus() immediately after a Visibility flip can be silently
    // dropped otherwise, since the element isn't hit-testable until layout catches up.
    private static void FocusDeferred(FrameworkElement element) =>
        element.Dispatcher.BeginInvoke(new Action(() => element.Focus()), DispatcherPriority.ContextIdle);

    // Up/Down move focus the same way Tab/Shift+Tab do, and Enter on one of the
    // "Screen" nav links (Create account / Back to login) activates that link instead
    // of falling through to the current screen's IsDefault submit button — without
    // this, Tabbing to "Create account" and pressing Enter would submit Login instead
    // of switching screens, since a RadioButton doesn't consume Enter itself.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
                break;
            case Key.Up:
                (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
                e.Handled = true;
                break;
            case Key.Enter when Keyboard.FocusedElement is RadioButton { GroupName: "Screen" } navLink:
                navLink.IsChecked = true;
                e.Handled = true;
                break;
        }
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

    // "Continue without" on the login screen: skip the server entirely and enter the app
    // as a local-only "Unknown" user. No network call is made, so this works fully offline;
    // nothing typed here ever reaches the server.
    private void ContinueWithoutAccount()
    {
        _loggedInUsername = "Unknown";
        LoginStatusText.Text = string.Empty;
        UsernameTextBox.Clear();
        PasswordBox.Clear();
        NavToApp.IsChecked = true;
    }

    // "Exit" on the app screen: forget who's signed in and go back to the login screen.
    private void SignOut()
    {
        _loggedInUsername = null;
        NavToLogin.IsChecked = true;
    }

    // Pencil icon next to the login password field: opens the Change Password screen,
    // carrying over whatever username was already typed on the login screen.
    private void OpenChangePasswordScreen()
    {
        ChangePasswordUsernameTextBox.Text = UsernameTextBox.Text;
        NavToChangePassword.IsChecked = true;
        FocusDeferred(OldPasswordBox); // username is already filled in, so start typing here
    }

    // Verifies the old password with the server and, on success, applies the new one.
    private async System.Threading.Tasks.Task ChangePasswordAsync()
    {
        ChangePasswordSubmitButton.IsEnabled = false;
        ChangePasswordStatusText.Foreground = (Brush)FindResource("TextColor");
        ChangePasswordStatusText.Text = "Changing password...";

        var result = await _api.ChangePasswordAsync(
            ChangePasswordUsernameTextBox.Text, OldPasswordBox.Password, ChangeNewPasswordBox.Password);

        ChangePasswordStatusText.Foreground = (Brush)FindResource(result.Success ? "AccentGreen" : "RuleRed");
        ChangePasswordStatusText.Text = result.Message;

        if (result.Success)
        {
            OldPasswordBox.Clear();
            ChangeNewPasswordBox.Clear();
            ChangeConfirmPasswordBox.Clear();
            NavToLogin.IsChecked = true; // password changed -> back to the login screen
        }

        ValidateChangePassword(); // re-checks the rules and restores the button's enabled state
    }

    // Re-checks every change-password rule and enables the button only when all pass.
    private void ValidateChangePassword()
    {
        string old = OldPasswordBox.Password;
        string p = ChangeNewPasswordBox.Password;
        string c = ChangeConfirmPasswordBox.Password;

        bool pwLen = p.Length >= 8;
        bool upper = p.Any(char.IsUpper);
        bool lower = p.Any(char.IsLower);
        bool digit = p.Any(char.IsDigit);
        bool match = c.Length > 0 && c == p;

        UpdatePasswordStrength(
            pwLen, upper, lower, digit,
            CpRulePwLenDot, CpRulePwUpperDot, CpRulePwLowerDot, CpRulePwDigitDot,
            new[] { CpPwStrengthSeg1, CpPwStrengthSeg2, CpPwStrengthSeg3, CpPwStrengthSeg4 },
            CpPwStrengthLabel);
        SetRule(CpRuleMatch, CpRuleMatchIcon, match);

        ChangePasswordSubmitButton.IsEnabled = old.Length > 0 && pwLen && match;
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
    // button into the shared "reveal every password field" toggle. By default a
    // change re-runs the create-account Validate(); pass onChange to re-run a
    // different screen's validator instead (e.g. ValidateChangePassword).
    private void WirePasswordField(PasswordBox pb, TextBox tb, ToggleButton eye, bool validate, Action? onChange = null)
    {
        _passwordFields.Add((pb, tb, eye));

        void RunValidation()
        {
            if (onChange is not null) onChange();
            else if (validate) Validate();
        }

        pb.PasswordChanged += (_, _) =>
        {
            if (_sync) return;
            _sync = true;
            tb.Text = pb.Password;
            _sync = false;
            RunValidation();
        };

        tb.TextChanged += (_, _) =>
        {
            if (_sync) return;
            _sync = true;
            pb.Password = tb.Text;
            _sync = false;
            RunValidation();
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
        UpdatePasswordStrength(
            pwLen, upper, lower, digit,
            RulePwLenDot, RulePwUpperDot, RulePwLowerDot, RulePwDigitDot,
            new[] { PwStrengthSeg1, PwStrengthSeg2, PwStrengthSeg3, PwStrengthSeg4 },
            PwStrengthLabel);
        SetRule(RuleMatch, RuleMatchIcon, match);

        CreateAccountButton.IsEnabled = len && chars && start && pwLen && match;
    }

    // Colours a rule row green when met, red when not, and swaps its check/cross icon.
    private void SetRule(StackPanel row, TextBlock icon, bool ok)
    {
        Brush brush = (Brush)FindResource(ok ? "AccentGreen" : "RuleRed");
        row.SetValue(TextElement.ForegroundProperty, brush);
        icon.Text = ok ? "✓" : "✗";
    }

    // Colours a composition hint dot: grey when unmet, green when met. These are
    // informational only — unlike SetRule, they never turn red, since the only
    // hard password requirement is the minimum length (checked separately).
    private void SetHint(Ellipse dot, bool ok)
    {
        dot.Fill = (Brush)FindResource(ok ? "AccentGreen" : "MeterTrack");
    }

    // Updates a password's strength meter (4 segments + label) and its 4 composition
    // hint dots, from the same booleans Validate()/ValidateChangePassword() already
    // compute. Score 0-4 = how many of the 4 booleans are true.
    private void UpdatePasswordStrength(
        bool pwLen, bool upper, bool lower, bool digit,
        Ellipse lenDot, Ellipse upperDot, Ellipse lowerDot, Ellipse digitDot,
        Border[] segments, TextBlock label)
    {
        SetHint(lenDot, pwLen);
        SetHint(upperDot, upper);
        SetHint(lowerDot, lower);
        SetHint(digitDot, digit);

        int score = new[] { pwLen, upper, lower, digit }.Count(ok => ok);
        bool empty = !pwLen && !upper && !lower && !digit;

        string level = empty ? "" : score <= 1 ? "weak" : score <= 3 ? "medium" : "strong";
        string resourceKey = level switch { "weak" => "RuleRed", "medium" => "AccentAmber", "strong" => "AccentGreen", _ => "MeterTrack" };
        Brush fillBrush = (Brush)FindResource(resourceKey);

        Brush trackBrush = (Brush)FindResource("MeterTrack");
        for (int i = 0; i < segments.Length; i++)
            segments[i].Background = i < score ? fillBrush : trackBrush;

        bool he = LangToggle.IsChecked == true;
        label.Text = level switch
        {
            "weak" => he ? "חלשה" : "Weak",
            "medium" => he ? "בינונית" : "Medium",
            "strong" => he ? "חזקה" : "Strong",
            _ => "",
        };
        label.Foreground = empty ? (Brush)FindResource("TextColor") : fillBrush;
    }
}
