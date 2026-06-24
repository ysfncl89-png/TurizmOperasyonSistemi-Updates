using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Input;
using System.Collections.Generic;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using TurizmOperasyonSistemi.Services;
using System.Text.RegularExpressions;

namespace TurizmOperasyonSistemi.Views
{
    public partial class RegistrationsWizardView : Page
    {
        // For RegistrationPeriodGuide combobox incremental search/filter
        private ICollectionView _registrationPeriodGuideView;
        private string _periodGuideBuffer = string.Empty; // holds typed letters or digits
        // no timer: buffer cleared only by explicit keys (Backspace/Escape) or closing dropdown

        // For Period combobox on the "Tur" step (same incremental search behavior as guide)
        private ICollectionView _periodComboView;
        private string _periodComboBuffer = string.Empty;
        private string _periodComboNumericBuffer = string.Empty;
        // For RegistrationBrowser's period filter combobox inside RegistrationBrowserContentBorder
        private ICollectionView _registrationFilterView;
        private string _periodFilterBuffer = string.Empty;
        private string _periodFilterNumericBuffer = string.Empty;

        private bool _isApplyingAutoSelection = false;
        private const int MaxStep = 10;
        private List<AppSetting> _passportArrivedOptionsAll = new();
        private List<string> _visaStatusOptionsAll = new();
        private List<string> _ticketStatusOptionsAll = new();
        private int currentStep = 1;
        private int selectedPersonId = 0;
        private int editingRegistrationId = 0;
        private bool skipPersonSelectionStepForEdit = false;
        private PersonsWizardView _personsWizardView;
        private ICollectionView _personsView;
        private ICollectionView _registrationsView;
        private List<RegistrationListItem> _registrationItems = new();
        private readonly BlurEffect _registrationBrowserBlur = new BlurEffect { Radius = 10 };
        private bool _isSyncingRegistrationPeriodGuide = false;
        private string _selectedVisaStatusChoice = string.Empty;
        private string _selectedTicketStatusChoice = string.Empty;
        private bool _userClickedRegistration = false;
        private bool _cameFromBrowserEdit = false;


        public RegistrationsWizardView()
        {
            InitializeComponent();
            // Ensure database has PassportArrived column
            try
            {
                using (var db = new AppDbContext())
                {
                    db.EnsureRegistrationPassportArrivedColumnExists();
                    db.EnsureRegistrationStatusChoiceColumnsExist();
                    db.NormalizeRegistrationNullValues();
                }



            }
            catch { }

            // after ensuring DB schema, finish constructor initialization
            LoadPersons();
            LoadRegistrations();
            LoadSettingsToComboBoxes();
            InitializePersonsModuleHost();
            UpdateRegistrationActionButtons();
            ShowRegistrationBrowser();
            // Track explicit user clicks on the registrations grid so action buttons only appear after a user click
            try
            {
                RegistrationsDataGrid.PreviewMouseLeftButtonDown -= RegistrationsDataGrid_PreviewMouseLeftButtonDown;
                RegistrationsDataGrid.PreviewMouseLeftButtonDown += RegistrationsDataGrid_PreviewMouseLeftButtonDown;
            }
            catch { }
            // Attach click handlers to all ComboBox controls on this page so clicking anywhere opens the dropdown
            AttachComboBoxClickHandlers(this);

            // Setup incremental search helpers for the registration period guide combobox
            try
            {
                RegistrationPeriodGuideComboBox.DropDownOpened += RegistrationPeriodGuideComboBox_DropDownOpened;
                RegistrationPeriodGuideComboBox.DropDownClosed += RegistrationPeriodGuideComboBox_DropDownClosed;
                RegistrationPeriodGuideComboBox.PreviewTextInput += RegistrationPeriodGuideComboBox_PreviewTextInput;
                RegistrationPeriodGuideComboBox.PreviewKeyDown += RegistrationPeriodGuideComboBox_PreviewKeyDown;

                // no timeout timer — buffer persists until explicit Backspace/Escape or dropdown close
            }
            catch { }

            // Setup the same incremental search for the Period combobox in the "Tur" step
            try
            {
                PeriodComboBox.DropDownOpened += PeriodComboBox_DropDownOpened;
                PeriodComboBox.DropDownClosed += PeriodComboBox_DropDownClosed;
                PeriodComboBox.PreviewTextInput += PeriodComboBox_PreviewTextInput;
                PeriodComboBox.PreviewKeyDown += PeriodComboBox_PreviewKeyDown;
            }
            catch { }

            // Setup incremental search for the Registration browser period filter combobox
            try
            {
                RegistrationPeriodFilterComboBox.DropDownOpened += RegistrationPeriodFilterComboBox_DropDownOpened;
                RegistrationPeriodFilterComboBox.DropDownClosed += RegistrationPeriodFilterComboBox_DropDownClosed;
                RegistrationPeriodFilterComboBox.PreviewTextInput += RegistrationPeriodFilterComboBox_PreviewTextInput;
                RegistrationPeriodFilterComboBox.PreviewKeyDown += RegistrationPeriodFilterComboBox_PreviewKeyDown;
            }
            catch { }
        }

        // Registration period guide helpers (moved outside constructor)
        private string _periodGuideNumericBuffer = string.Empty; // holds digits when user types year after month
        private void RegistrationPeriodGuideComboBox_DropDownOpened(object sender, EventArgs e)
        {
            try
            {
                if (RegistrationPeriodGuideComboBox?.ItemsSource == null) return;
                _registrationPeriodGuideView = CollectionViewSource.GetDefaultView(RegistrationPeriodGuideComboBox.ItemsSource);
                if (_registrationPeriodGuideView != null)
                {
                    _registrationPeriodGuideView.Filter = PeriodGuideFilter;
                }
                _periodGuideBuffer = string.Empty;
            }
            catch { }
        }

        private void RegistrationPeriodGuideComboBox_DropDownClosed(object sender, EventArgs e)
        {
            try
            {
                if (_registrationPeriodGuideView != null)
                {
                    _registrationPeriodGuideView.Filter = null;
                    _registrationPeriodGuideView = null;
                }
                _periodGuideBuffer = string.Empty;
            }
            catch { }
        }

        private void RegistrationPeriodGuideComboBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                if (RegistrationPeriodGuideComboBox == null || !RegistrationPeriodGuideComboBox.IsDropDownOpen) return;

                var ch = (e.Text ?? string.Empty);
                if (string.IsNullOrEmpty(ch)) return;

                if (char.IsDigit(ch[0]))
                {
                    // Always append digit to numeric buffer
                    _periodGuideNumericBuffer += ch;

                    // Refresh view so filtering applies month+year constraints
                    _registrationPeriodGuideView?.Refresh();

                    // Try to highlight the best match among visible items (do not close dropdown)
                    IEnumerable<string> items = RegistrationPeriodGuideComboBox.ItemsSource?.Cast<string>() ?? Enumerable.Empty<string>();
                    if (_registrationPeriodGuideView != null)
                    {
                        try { items = _registrationPeriodGuideView.Cast<string>(); } catch { }
                    }

                    // Find all items that match both month buffer (if present) and whose year token starts with numeric buffer.
                    var matches = items.Where(it =>
                    {
                        if (it == null) return false;
                        if (!string.IsNullOrEmpty(_periodGuideBuffer))
                        {
                            var monthPart = it.Split(' ')[0];
                            if (!Normalize(monthPart).StartsWith(Normalize(_periodGuideBuffer))) return false;
                        }
                        var yearTok = ExtractNumericToken(it);
                        if (string.IsNullOrEmpty(_periodGuideNumericBuffer)) return true;
                        return !string.IsNullOrEmpty(yearTok) && yearTok.StartsWith(_periodGuideNumericBuffer, StringComparison.CurrentCultureIgnoreCase);
                    }).ToList();

                    if (matches.Count == 1)
                    {
                        // Only auto-select when there is a single unambiguous match.
                        RegistrationPeriodGuideComboBox.SelectedItem = matches[0];
                        e.Handled = true;
                    }
                    else
                    {
                        // Multiple candidates remain: do not change selection so user can continue typing to disambiguate.
                        // Optionally, ensure the dropdown shows the filtered candidates (filter already applied).
                        e.Handled = true;
                    }

                    return;
                }

                if (char.IsLetter(ch[0]))
                {
                    // typing a letter clears numeric buffer and updates month filter
                    _periodGuideNumericBuffer = string.Empty;
                    _periodGuideBuffer += ch;
                    _registrationPeriodGuideView?.Refresh();
                    e.Handled = true;
                }
            }
            catch { }
        }

        private void RegistrationPeriodGuideComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (RegistrationPeriodGuideComboBox == null || !RegistrationPeriodGuideComboBox.IsDropDownOpen) return;

                if (e.Key == Key.Back)
                {
                    // Prefer deleting numeric buffer first when present
                    if (!string.IsNullOrEmpty(_periodGuideNumericBuffer))
                    {
                        _periodGuideNumericBuffer = _periodGuideNumericBuffer[..Math.Max(0, _periodGuideNumericBuffer.Length - 1)];
                        e.Handled = true;
                    }
                    else if (!string.IsNullOrEmpty(_periodGuideBuffer))
                    {
                        _periodGuideBuffer = _periodGuideBuffer[..Math.Max(0, _periodGuideBuffer.Length - 1)];
                        _registrationPeriodGuideView?.Refresh();
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    ResetPeriodGuideBuffer();
                    e.Handled = true;
                }
            }
            catch { }
        }

        private bool PeriodGuideFilter(object obj)
        {
            try
            {
                if (obj is not string s) return true;
                // Keep the special "Tüm Dönemler" option visible so user can always select it
                if (s.Equals("Tüm Dönemler", StringComparison.CurrentCultureIgnoreCase)) return true;

                // If user has typed only digits (no month buffer), match items that contain the numeric buffer
                if (string.IsNullOrEmpty(_periodGuideBuffer) && !string.IsNullOrEmpty(_periodGuideNumericBuffer))
                {
                    var yearTok = ExtractNumericToken(s);
                    return !string.IsNullOrEmpty(yearTok) && yearTok.StartsWith(_periodGuideNumericBuffer, StringComparison.CurrentCultureIgnoreCase);
                }

                // If user typed month (letters), require month prefix; if also typed numeric buffer, require that too
                if (!string.IsNullOrEmpty(_periodGuideBuffer))
                {
                    var monthPart = s.Split(' ')[0];
                    var normalizedMonth = Normalize(monthPart);
                    var normalizedBuffer = Normalize(_periodGuideBuffer);
                    bool monthMatches = normalizedMonth.StartsWith(normalizedBuffer);
                    if (!monthMatches) return false;
                    if (!string.IsNullOrEmpty(_periodGuideNumericBuffer))
                    {
                        var yearTok = ExtractNumericToken(s);
                        return !string.IsNullOrEmpty(yearTok) && yearTok.StartsWith(_periodGuideNumericBuffer, StringComparison.CurrentCultureIgnoreCase);
                    }
                    return true;
                }

                return true;
            }
            catch { return true; }
        }

        private void ResetPeriodGuideBuffer()
        {
            try
            {
                _periodGuideBuffer = string.Empty;
                _registrationPeriodGuideView?.Refresh();
            }
            catch { }
        }

        // Period combobox (Tur step) incremental search/filter helpers
        private void PeriodComboBox_DropDownOpened(object sender, EventArgs e)
        {
            try
            {
                if (PeriodComboBox?.ItemsSource == null) return;
                _periodComboView = CollectionViewSource.GetDefaultView(PeriodComboBox.ItemsSource);
                if (_periodComboView != null)
                {
                    _periodComboView.Filter = PeriodFilter;
                }
                _periodComboBuffer = string.Empty;
                _periodComboNumericBuffer = string.Empty;
            }
            catch { }
        }

        private void PeriodComboBox_DropDownClosed(object sender, EventArgs e)
        {
            try
            {
                if (_periodComboView != null)
                {
                    _periodComboView.Filter = null;
                    _periodComboView = null;
                }
                _periodComboBuffer = string.Empty;
                _periodComboNumericBuffer = string.Empty;
            }
            catch { }
        }

        private void PeriodComboBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                if (PeriodComboBox == null || !PeriodComboBox.IsDropDownOpen) return;

                var ch = (e.Text ?? string.Empty);
                if (string.IsNullOrEmpty(ch)) return;

                if (char.IsDigit(ch[0]))
                {
                    _periodComboNumericBuffer += ch;
                    _periodComboView?.Refresh();

                    IEnumerable<string> items = PeriodComboBox.ItemsSource?.Cast<string>() ?? Enumerable.Empty<string>();
                    if (_periodComboView != null)
                    {
                        try { items = _periodComboView.Cast<string>(); } catch { }
                    }

                    var matches = items.Where(it =>
                    {
                        if (it == null) return false;
                        if (!string.IsNullOrEmpty(_periodComboBuffer))
                        {
                            var monthPart = it.Split(' ')[0];
                            if (!Normalize(monthPart).StartsWith(Normalize(_periodComboBuffer))) return false;
                        }
                        var yearTok = ExtractNumericToken(it);
                        if (string.IsNullOrEmpty(_periodComboNumericBuffer)) return true;
                        return !string.IsNullOrEmpty(yearTok) && yearTok.StartsWith(_periodComboNumericBuffer, StringComparison.CurrentCultureIgnoreCase);
                    }).ToList();

                    if (matches.Count == 1)
                    {
                        PeriodComboBox.SelectedItem = matches[0];
                        e.Handled = true;
                    }
                    else
                    {
                        e.Handled = true;
                    }
                    return;
                }

                if (char.IsLetter(ch[0]))
                {
                    _periodComboNumericBuffer = string.Empty;
                    _periodComboBuffer += ch;
                    _periodComboView?.Refresh();
                    e.Handled = true;
                }
            }
            catch { }
        }

        private void PeriodComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (PeriodComboBox == null || !PeriodComboBox.IsDropDownOpen) return;

                if (e.Key == Key.Back)
                {
                    if (!string.IsNullOrEmpty(_periodComboNumericBuffer))
                    {
                        _periodComboNumericBuffer = _periodComboNumericBuffer[..Math.Max(0, _periodComboNumericBuffer.Length - 1)];
                        e.Handled = true;
                    }
                    else if (!string.IsNullOrEmpty(_periodComboBuffer))
                    {
                        _periodComboBuffer = _periodComboBuffer[..Math.Max(0, _periodComboBuffer.Length - 1)];
                        _periodComboView?.Refresh();
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    ResetPeriodComboBuffer();
                    e.Handled = true;
                }
            }
            catch { }
        }

        private bool PeriodFilter(object obj)
        {
            try
            {
                if (obj is not string s) return true;
                if (s.Equals("Tüm Dönemler", StringComparison.CurrentCultureIgnoreCase)) return true;

                if (string.IsNullOrEmpty(_periodComboBuffer) && !string.IsNullOrEmpty(_periodComboNumericBuffer))
                {
                    var yearTok = ExtractNumericToken(s);
                    return !string.IsNullOrEmpty(yearTok) && yearTok.StartsWith(_periodComboNumericBuffer, StringComparison.CurrentCultureIgnoreCase);
                }

                if (!string.IsNullOrEmpty(_periodComboBuffer))
                {
                    var monthPart = s.Split(' ')[0];
                    var normalizedMonth = Normalize(monthPart);
                    var normalizedBuffer = Normalize(_periodComboBuffer);
                    bool monthMatches = normalizedMonth.StartsWith(normalizedBuffer);
                    if (!monthMatches) return false;
                    if (!string.IsNullOrEmpty(_periodComboNumericBuffer))
                    {
                        var yearTok = ExtractNumericToken(s);
                        return !string.IsNullOrEmpty(yearTok) && yearTok.StartsWith(_periodComboNumericBuffer, StringComparison.CurrentCultureIgnoreCase);
                    }
                    return true;
                }

                return true;
            }
            catch { return true; }
        }

        private void ResetPeriodComboBuffer()
        {
            try
            {
                _periodComboBuffer = string.Empty;
                _periodComboNumericBuffer = string.Empty;
                _periodComboView?.Refresh();
            }
            catch { }
        }

        // Registration browser filter combobox (inside RegistrationBrowserContentBorder) helpers
        private void RegistrationPeriodFilterComboBox_DropDownOpened(object sender, EventArgs e)
        {
            try
            {
                if (RegistrationPeriodFilterComboBox?.ItemsSource == null) return;
                _registrationFilterView = CollectionViewSource.GetDefaultView(RegistrationPeriodFilterComboBox.ItemsSource);
                if (_registrationFilterView != null)
                {
                    _registrationFilterView.Filter = RegistrationFilter;
                }
                _periodFilterBuffer = string.Empty;
                _periodFilterNumericBuffer = string.Empty;
            }
            catch { }
        }

        private void RegistrationPeriodFilterComboBox_DropDownClosed(object sender, EventArgs e)
        {
            try
            {
                if (_registrationFilterView != null)
                {
                    _registrationFilterView.Filter = null;
                    _registrationFilterView = null;
                }
                _periodFilterBuffer = string.Empty;
                _periodFilterNumericBuffer = string.Empty;
            }
            catch { }
        }

        private void RegistrationPeriodFilterComboBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                if (RegistrationPeriodFilterComboBox == null || !RegistrationPeriodFilterComboBox.IsDropDownOpen) return;

                var ch = (e.Text ?? string.Empty);
                if (string.IsNullOrEmpty(ch)) return;

                if (char.IsDigit(ch[0]))
                {
                    _periodFilterNumericBuffer += ch;
                    _registrationFilterView?.Refresh();

                    IEnumerable<string> items = RegistrationPeriodFilterComboBox.ItemsSource?.Cast<string>() ?? Enumerable.Empty<string>();
                    if (_registrationFilterView != null)
                    {
                        try { items = _registrationFilterView.Cast<string>(); } catch { }
                    }

                    var matches = items.Where(it =>
                    {
                        if (it == null) return false;
                        if (!string.IsNullOrEmpty(_periodFilterBuffer))
                        {
                            var monthPart = it.Split(' ')[0];
                            if (!Normalize(monthPart).StartsWith(Normalize(_periodFilterBuffer))) return false;
                        }
                        var yearTok = ExtractNumericToken(it);
                        if (string.IsNullOrEmpty(_periodFilterNumericBuffer)) return true;
                        return !string.IsNullOrEmpty(yearTok) && yearTok.StartsWith(_periodFilterNumericBuffer, StringComparison.CurrentCultureIgnoreCase);
                    }).ToList();

                    if (matches.Count == 1)
                    {
                        RegistrationPeriodFilterComboBox.SelectedItem = matches[0];
                        e.Handled = true;
                    }
                    else
                    {
                        e.Handled = true;
                    }
                    return;
                }

                if (char.IsLetter(ch[0]))
                {
                    _periodFilterNumericBuffer = string.Empty;
                    _periodFilterBuffer += ch;
                    _registrationFilterView?.Refresh();
                    e.Handled = true;
                }
            }
            catch { }
        }

        private void RegistrationPeriodFilterComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (RegistrationPeriodFilterComboBox == null || !RegistrationPeriodFilterComboBox.IsDropDownOpen) return;

                if (e.Key == Key.Back)
                {
                    if (!string.IsNullOrEmpty(_periodFilterNumericBuffer))
                    {
                        _periodFilterNumericBuffer = _periodFilterNumericBuffer[..Math.Max(0, _periodFilterNumericBuffer.Length - 1)];
                        e.Handled = true;
                    }
                    else if (!string.IsNullOrEmpty(_periodFilterBuffer))
                    {
                        _periodFilterBuffer = _periodFilterBuffer[..Math.Max(0, _periodFilterBuffer.Length - 1)];
                        _registrationFilterView?.Refresh();
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    ResetRegistrationFilterBuffer();
                    e.Handled = true;
                }
            }
            catch { }
        }

        private bool RegistrationFilter(object obj)
        {
            try
            {
                if (obj is not string s) return true;
                if (s.Equals("Tüm Dönemler", StringComparison.CurrentCultureIgnoreCase)) return true;

                if (string.IsNullOrEmpty(_periodFilterBuffer) && !string.IsNullOrEmpty(_periodFilterNumericBuffer))
                {
                    var yearTok = ExtractNumericToken(s);
                    return !string.IsNullOrEmpty(yearTok) && yearTok.StartsWith(_periodFilterNumericBuffer, StringComparison.CurrentCultureIgnoreCase);
                }

                if (!string.IsNullOrEmpty(_periodFilterBuffer))
                {
                    var monthPart = s.Split(' ')[0];
                    var normalizedMonth = Normalize(monthPart);
                    var normalizedBuffer = Normalize(_periodFilterBuffer);
                    bool monthMatches = normalizedMonth.StartsWith(normalizedBuffer);
                    if (!monthMatches) return false;
                    if (!string.IsNullOrEmpty(_periodFilterNumericBuffer))
                    {
                        var yearTok = ExtractNumericToken(s);
                        return !string.IsNullOrEmpty(yearTok) && yearTok.StartsWith(_periodFilterNumericBuffer, StringComparison.CurrentCultureIgnoreCase);
                    }
                    return true;
                }

                return true;
            }
            catch { return true; }
        }

        private void ResetRegistrationFilterBuffer()
        {
            try
            {
                _periodFilterBuffer = string.Empty;
                _periodFilterNumericBuffer = string.Empty;
                _registrationFilterView?.Refresh();
            }
            catch { }
        }

        // Returns the first numeric token (sequence of digits) found in the text, prefer 4-digit year tokens or the last numeric token
        private string ExtractNumericToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var tokens = text.Split(new[] { ' ', '-', '/', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            // prefer a 4-digit token
            var four = tokens.Reverse().FirstOrDefault(t => t.All(char.IsDigit) && t.Length >= 4);
            if (!string.IsNullOrEmpty(four)) return four;
            // otherwise return the last numeric token with length >= 2
            var last = tokens.Reverse().FirstOrDefault(t => t.All(char.IsDigit) && t.Length >= 1);
            return last ?? string.Empty;
        }

        private void RegistrationsDataGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                // Determine whether a DataGridRow was clicked
                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is System.Windows.Controls.DataGridRow))
                {
                    dep = VisualTreeHelper.GetParent(dep);
                }

                if (dep is System.Windows.Controls.DataGridRow row)
                {
                    // If the user clicked the already-selected row, toggle selection off
                    var clickedItem = row.Item as RegistrationListItem;
                    if (clickedItem != null && RegistrationsDataGrid.SelectedItem is RegistrationListItem sel && sel.Id == clickedItem.Id)
                    {
                        RegistrationsDataGrid.SelectedItem = null;
                        _userClickedRegistration = false;
                        UpdateRegistrationActionButtons();
                        e.Handled = true; // prevent default selection behavior
                        return;
                    }

                    // Otherwise let the selection happen and mark that user clicked
                    _userClickedRegistration = true;
                    // do not call UpdateRegistrationActionButtons here; SelectionChanged will fire
                }
                else
                {
                    // click outside rows -> clear selection
                    RegistrationsDataGrid.SelectedItem = null;
                    _userClickedRegistration = false;
                    UpdateRegistrationActionButtons();
                }
            }
            catch { }
        }

        // Reserved placeholder for upcoming method insertions. Do not remove.

        // Patch alignment no-op: reserved line to ensure context matching for future patches.
        private int ResolveRoomCapacity(string roomType)
        {
            string selectedRoomType = (roomType ?? string.Empty).ToUpper();

            if (selectedRoomType.Contains("SNGL") || selectedRoomType.Contains("SINGLE")) return 1;
            if (selectedRoomType.Contains("DBL") || selectedRoomType.Contains("TWIN")) return 2;
            if (selectedRoomType.Contains("TRPL") || selectedRoomType.Contains("TRİPLE") || selectedRoomType.Contains("TRIPLE")) return 3;
            if (selectedRoomType.Contains("QUAD")) return 4;
            if (selectedRoomType.Contains("QUINT")) return 5;

            return 2;
        }

        private void ComboBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ComboBox cb)
            {
                if (!cb.IsDropDownOpen)
                {
                    cb.IsDropDownOpen = true;
                    e.Handled = true;
                }
            }
        }

        private void AttachComboBoxClickHandlers(DependencyObject root)
        {
            if (root == null)
                return;

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is ComboBox cb)
                {
                    // Remove existing handlers to avoid double subscription
                    cb.PreviewMouseLeftButtonDown -= ComboBox_PreviewMouseLeftButtonDown;
                    cb.PreviewMouseLeftButtonDown += ComboBox_PreviewMouseLeftButtonDown;
                    cb.MouseLeftButtonDown -= ComboBox_PreviewMouseLeftButtonDown;
                    cb.MouseLeftButtonDown += ComboBox_PreviewMouseLeftButtonDown;
                }

                // Recurse
                AttachComboBoxClickHandlers(child);
            }
        }

        private void InitializePersonsModuleHost()
        {
            _personsWizardView = new PersonsWizardView();
            _personsWizardView.SuppressSavedNotification = true;
            _personsWizardView.PersonSelectionChanged += PersonsWizardView_PersonSelectionChanged;
            _personsWizardView.PersonSaved += PersonsWizardView_PersonSaved;
            PersonsModuleFrame.Navigate(_personsWizardView);
        }

        private void PersonsWizardView_PersonSelectionChanged(Person person)
        {
            selectedPersonId = person?.Id ?? 0;
            UpdateNextButtonState();
        }

        private void PersonsWizardView_PersonSaved(int personId)
        {
            if (personId <= 0)
            {
                return;
            }

            selectedPersonId = personId;
            _personsWizardView?.SelectPersonForExternalFlow(personId);

            if (currentStep == 1)
            {
                SwitchToStep(2);
            }
        }

        private void LoadPersons()
        {
            using (var db = new AppDbContext())
            {
                var list = db.Persons.OrderByDescending(x => x.Id).ToList();

                PersonsDataGrid.ItemsSource = list;
                _personsView = CollectionViewSource.GetDefaultView(PersonsDataGrid.ItemsSource);
                _personsView.Filter = FilterPersons;
            }
        }

        private void LoadRegistrations()
        {
            using (var db = new AppDbContext())
            {
                // Load registrations with related Person to decide whether to show TC or Pasaport
                var regs = db.Registrations
                    .Include(r => r.Person)
                    .ToList();

                _registrationItems = regs
                    .Select(x =>
                    {
                        string personFullName = x.Person != null
                            ? ((x.Person.FirstName ?? "") + " " + (x.Person.LastName ?? "")).Trim()
                            : $"Kişi #{x.PersonId}";

                        string personTcOrPassport = "";
                        if (x.Person != null)
                        {
                            var nat = (x.Person.Nationality ?? string.Empty).Trim();
                            var normalized = nat.ToLowerInvariant().Replace(" ", string.Empty)
                                .Replace("ı","i").Replace("ğ","g").Replace("ü","u").Replace("ş","s").Replace("ö","o").Replace("ç","c");

                            // Consider Turkey if nationality explicitly mentions Türkiye/Turkey or Turkish variants
                            bool isTurkey = normalized == "tc" || normalized.Contains("turkiye") || normalized.Contains("turkey") || normalized.StartsWith("tur") || normalized.Contains("turk");

                            // If nationality is not set but TcKimlikNo looks like a valid Turkish ID (11 digits), prefer TC
                            if (!isTurkey && string.IsNullOrWhiteSpace(normalized))
                            {
                                var tc = x.Person.TcKimlikNo ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(tc) && tc.All(char.IsDigit) && tc.Length == 11)
                                {
                                    isTurkey = true;
                                }
                            }

                            personTcOrPassport = isTurkey ? (x.Person.TcKimlikNo ?? string.Empty) : (x.Person.PassportNumber ?? string.Empty);
                        }

                string passportArrivedStr = string.Empty;
                if (x.PassportArrived.HasValue)
                {
                    // Treat negative sentinel as "Belirtilmedi" (unspecified)
                    if (x.PassportArrived.Value < 0)
                    {
                        passportArrivedStr = "Belirtilmedi";
                    }
                    else
                    {
                        // Try to resolve stored integer to AppSetting value when possible
                        // IMPORTANT: restrict lookup to the specific AppSetting type so we don't
                        // accidentally pick an AppSetting from another type (e.g. Cinsiyet) that
                        // happens to share the same Id. This was causing values like "Erkek"
                        // to appear in the Pasaportu Geldi mi column.
                        var setting = db.AppSettings.FirstOrDefault(s => s.Id == x.PassportArrived.Value && s.Type == "Pasaportu Geldi mi ?");
                        if (setting != null)
                        {
                            passportArrivedStr = setting.Value ?? string.Empty;
                        }
                        else
                        {
                            // Fallback to legacy boolean mapping: non-zero => Evet, zero => Hayır
                            passportArrivedStr = x.PassportArrived.Value != 0 ? "Evet" : "Hayır";
                        }
                    }
                }

                        return new RegistrationListItem
                        {
                            Id = x.Id,
                            PersonFullName = personFullName,
                            PersonTc = personTcOrPassport,
                            TourType = x.TourType ?? string.Empty,
                            Period = x.Period ?? string.Empty,
                            GroupType = x.GroupType ?? string.Empty,
                            DaysCount = x.DaysCount ?? string.Empty,
                            VisaRequest = x.VisaRequest ?? string.Empty,
                            TicketRequest = x.TicketRequest ?? string.Empty,
                            VisaStatusChoice = x.VisaStatusChoice ?? string.Empty,
                            TicketStatusChoice = x.TicketStatusChoice ?? string.Empty,
                            CargoRequest = x.CargoRequest ?? string.Empty,
                            PassportArrived = passportArrivedStr,
                            RoomType = x.RoomType ?? string.Empty,
                            DepartureCity = x.DepartureCity ?? string.Empty,
                            TransferStatus = x.HasTransfer ?? string.Empty,
                            TransferCity = x.TransferCity ?? string.Empty,
                            District = x.District ?? string.Empty,
                            TotalPrice = x.TotalPrice,
                            PaidAmount = x.PaidAmount,
                            RemainingDebt = x.RemainingDebt,
                            Currency = x.Currency ?? string.Empty,
                            Reference = x.Reference ?? string.Empty,
                            CreatedDate = x.CreatedDate,
                            IsActive = x.IsActive,
                            Status = string.IsNullOrWhiteSpace(x.Status) ? "Aktif" : x.Status
                        };
                    })
                    .OrderByDescending(x => x.Id)
                    .ToList();
            }

            RegistrationsDataGrid.ItemsSource = _registrationItems;
            _registrationsView = CollectionViewSource.GetDefaultView(RegistrationsDataGrid.ItemsSource);
            _registrationsView.Filter = FilterRegistrations;
            _registrationsView.Refresh();
            UpdateRegistrationResultsState();
        }

        private bool FilterRegistrations(object obj)
        {
            if (obj is not RegistrationListItem item)
                return false;

            var text = Normalize(RegistrationSearchTextBox?.Text ?? "");
            var selectedPeriod = RegistrationPeriodFilterComboBox?.SelectedItem?.ToString() ?? "";
            bool matchesPeriod = string.IsNullOrWhiteSpace(selectedPeriod)
                || selectedPeriod == "Tüm Dönemler"
                || string.Equals(item.Period, selectedPeriod, StringComparison.CurrentCultureIgnoreCase);

            bool matchesText = true;
            if (!string.IsNullOrWhiteSpace(text))
            {
                var tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                // If the user typed mostly digits (TC), search TC field; otherwise search by person full name
                bool tokensAreDigits = tokens.All(t => t.All(char.IsDigit));

                if (tokensAreDigits)
                {
                    matchesText = tokens.All(token =>
                        (item.PersonTc != null && Normalize(item.PersonTc).Contains(token))
                    );
                }
                else
                {
                    matchesText = tokens.All(token =>
                        (item.PersonFullName != null && Normalize(item.PersonFullName).Contains(token))
                    );
                }
            }

            return matchesPeriod && matchesText;
        }

        private bool FilterPersons(object obj)
        {
            if (obj == null) return false;

            var person = obj as Person;
            if (person == null) return false;

            if (person.IsActive == false && ShowPassiveCheckBox.IsChecked != true)
                return false;

            var rawText = SearchTextBox.Text ?? "";
            var text = Normalize(rawText);

            if (string.IsNullOrWhiteSpace(text))
                return true;

            var firstName = Normalize(person.FirstName);
            var lastName = Normalize(person.LastName);
            var fullName = $"{firstName} {lastName}".Trim();
            var fullNameReversed = $"{lastName} {firstName}".Trim();
            var tc = Normalize(person.TcKimlikNo);

            return fullName.Contains(text)
                || fullNameReversed.Contains(text)
                || firstName.Contains(text)
                || lastName.Contains(text)
                || tc.Contains(text);
        }

        private string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            return string.Join(" ",
                input.Trim().ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _personsView?.Refresh();
            CheckSearchValidity();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            _personsView?.Refresh();
            CheckSearchValidity();
        }

        private void RegistrationSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _registrationsView?.Refresh();
            UpdateRegistrationResultsState();
            try
            {
                if (RegistrationBrowserInstructionTextBlock != null)
                {
                    RegistrationBrowserInstructionTextBlock.Visibility = string.IsNullOrWhiteSpace(RegistrationSearchTextBox.Text)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void RegistrationPeriodFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _registrationsView?.Refresh();
            UpdateRegistrationResultsState();
            UpdateRegistrationPeriodGuideState();
            // Keep focus on search box after selecting a period
            FocusRegistrationSearchBox();
        }

        private void RegistrationPeriodGuideComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingRegistrationPeriodGuide || RegistrationPeriodGuideComboBox.SelectedItem == null)
            {
                return;
            }

            _isSyncingRegistrationPeriodGuide = true;
            RegistrationPeriodFilterComboBox.SelectedItem = RegistrationPeriodGuideComboBox.SelectedItem;
            _isSyncingRegistrationPeriodGuide = false;

            UpdateRegistrationPeriodGuideState();
        }

        private void ShowPassiveCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _personsView?.Refresh();
            CheckSearchValidity();
        }

        private void CheckSearchValidity()
        {
            if (_personsView == null) return;

            bool anyItem = _personsView.Cast<object>().Any();
            bool hasSearchText = !string.IsNullOrWhiteSpace(SearchTextBox.Text);

            if (!anyItem && hasSearchText)
            {
                ShowNoPersonPopup();
            }
            else if (QuickAddFormPanel.Visibility != Visibility.Visible)
            {
                HidePersonPopup();
            }
        }

        private void ShowNoPersonPopup()
        {
            PersonPopupOverlay.Visibility = Visibility.Visible;
            NoPersonPanel.Visibility = Visibility.Visible;
            QuickAddFormPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowQuickAddPopup()
        {
            PersonPopupOverlay.Visibility = Visibility.Visible;
            NoPersonPanel.Visibility = Visibility.Collapsed;
            QuickAddFormPanel.Visibility = Visibility.Visible;
        }

        private void HidePersonPopup()
        {
            PersonPopupOverlay.Visibility = Visibility.Collapsed;
            NoPersonPanel.Visibility = Visibility.Collapsed;
            QuickAddFormPanel.Visibility = Visibility.Collapsed;
        }

        private void RegistrationsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateRegistrationActionButtons();
            // Update inline panel visibility when selection changes: hide inline suggestion when a row is selected,
            // show it when no row is selected (provided search/period/results conditions are met).
            try
            {
                UpdateRegistrationResultsState();
            }
            catch { }
        }

        private void RegistrationsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Only open editor when double-clicked on specific columns
            var col = RegistrationsDataGrid.CurrentColumn;
            if (col == null) return;

            var header = col.Header?.ToString() ?? string.Empty;
            var editableHeaders = new[] { "Vize Talebi", "Bilet Talebi", "Vize Durumu", "Bilet Durumu", "Pasaportu Geldi mi" };
            if (!editableHeaders.Contains(header)) return;

            if (RegistrationsDataGrid.SelectedItem is RegistrationListItem item)
            {
                try
                {
                    var mode = RegistrationCellEditorWindow.EditorMode.Both;
                    if (header == "Vize Talebi" || header == "Vize Durumu" || header == "Pasaportu Geldi mi") mode = RegistrationCellEditorWindow.EditorMode.VisaOnly;
                    else if (header == "Bilet Talebi" || header == "Bilet Durumu") mode = RegistrationCellEditorWindow.EditorMode.TicketOnly;

                    var editor = new RegistrationCellEditorWindow(item.Id, mode)
                    {
                        Owner = Window.GetWindow(this)
                    };

                    bool? res = editor.ShowDialog();
                    if (res == true)
                    {
                        LoadRegistrations();
                        SelectRegistration(item.Id);
                    }
                }
                catch
                {
                    // ignore
                }
            // Reset explicit user click flag when search text or period filter changed.
            // This ensures action buttons hide until the user explicitly clicks a row after filtering.
            try
            {
                bool hasSearchText = !string.IsNullOrWhiteSpace(RegistrationSearchTextBox?.Text);
                bool hasPeriod = RegistrationPeriodFilterComboBox?.SelectedItem != null
                    && RegistrationPeriodFilterComboBox.SelectedItem.ToString() != "Tüm Dönemler";

                if (hasSearchText || hasPeriod)
                {
                    // Only keep the explicit-click flag set if a row is still selected and the user had clicked it.
                    _userClickedRegistration = _userClickedRegistration && RegistrationsDataGrid?.SelectedItem is RegistrationListItem;
                }
                else
                {
                    _userClickedRegistration = false;
                }
                UpdateRegistrationActionButtons();
            }
            catch
            {
                // ignore
            }
            }
        }

        private void SetRegistrationPassiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (RegistrationsDataGrid.SelectedItem is not RegistrationListItem selectedRegistration)
            {
                MessageBox.Show("Lütfen işlem yapmak için bir operasyon kaydı seçiniz.");
                return;
            }

            UpdateSelectedRegistrationStatus(selectedRegistration.Status == "Pasif" ? "Aktif" : "Pasif");
        }

        private void DeleteRegistrationButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedRegistration();
        }

        private void SetRegistrationPassiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (RegistrationsDataGrid.SelectedItem is not RegistrationListItem selectedRegistration)
            {
                MessageBox.Show("Lütfen işlem yapmak için bir operasyon kaydı seçiniz.");
                return;
            }

            UpdateSelectedRegistrationStatus(selectedRegistration.Status == "Pasif" ? "Aktif" : "Pasif");
        }

        private void DeleteRegistrationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedRegistration();
        }

        private void UpdateSelectedRegistrationStatus(string status)
        {
            if (RegistrationsDataGrid.SelectedItem is not RegistrationListItem selectedRegistration)
            {
                MessageBox.Show("Lütfen durumunu değiştirmek için bir operasyon kaydı seçiniz.");
                return;
            }

            using (var db = new AppDbContext())
            {
                

                var registration = db.Registrations.FirstOrDefault(x => x.Id == selectedRegistration.Id);
                if (registration == null)
                {
                    MessageBox.Show("Seçilen operasyon kaydı bulunamadı.");
                    return;
                }

                registration.Status = status;
                registration.IsActive = status == "Aktif";
                db.SaveChanges();
                try
                {
                    // Eğer kayıt pasif hale geldiyse, odalama verilerini senkronize et:
                    // Bu kişi odalardan çıkarılsın ve boş kalan odalama kayıtları silinsin.
                    if (!registration.IsActive)
                    {
                        int personId = registration.PersonId;
                        try
                        {
                            var occupants = db.RoomOccupants
                                .Where(o => o.PassengerId == personId)
                                .ToList();

                            var removedAllocDescriptions = new List<string>();

                            if (occupants.Any())
                            {
                                var allocIds = occupants.Select(o => o.RoomAllocationId).Distinct().ToList();
                                db.RoomOccupants.RemoveRange(occupants);

                                foreach (var aid in allocIds)
                                {
                                    // Load allocation with hotel info if available
                                    var alloc = db.RoomAllocations.Include(a => a.Hotel).FirstOrDefault(a => a.Id == aid);
                                    if (alloc != null)
                                    {
                                        // ensure history table exists
                                        try { db.EnsureRoomAllocationHistoryTableExists(); } catch { }
                                        // record history entry for this person's removal from the allocation
                                        try
                                        {
                                            var hist = new TurizmOperasyonSistemi.Models.RoomAllocationHistory
                                            {
                                                RoomAllocationId = alloc.Id,
                                                HotelId = alloc.HotelId,
                                                HotelName = alloc.Hotel != null ? alloc.Hotel.Name : string.Empty,
                                                RoomNumber = alloc.RoomNumber,
                                                RoomType = alloc.RoomType,
                                                TourType = alloc.TourType,
                                                Period = alloc.Period,
                                                GroupType = alloc.GroupType,
                                                DaysCount = alloc.DaysCount,
                                                PersonId = personId,
                                                RegistrationId = registration.Id,
                                                RemovedAt = DateTime.UtcNow,
                                                Restored = false
                                            };
                                            try { db.RoomAllocationHistories.Add(hist); } catch { }
                                        }
                                        catch { }

                                        // record description for notification
                                        try
                                        {
                                            var hotelName = alloc.Hotel != null ? alloc.Hotel.Name : (alloc.HotelId > 0 ? alloc.HotelId.ToString() : "(bilinmiyor otel)");
                                            var roomNum = string.IsNullOrWhiteSpace(alloc.RoomNumber) ? "(oda bilinmiyor)" : alloc.RoomNumber;
                                            removedAllocDescriptions.Add($"{hotelName} - Oda {roomNum}");
                                        }
                                        catch { }

                                        // If no other occupants remain, remove the allocation itself
                                        bool hasOther = db.RoomOccupants.Any(o => o.RoomAllocationId == aid && o.PassengerId != personId);
                                        if (!hasOther)
                                        {
                                            db.RoomAllocations.Remove(alloc);
                                        }
                                    }
                                }

                                db.SaveChanges();

                                // If any allocations were removed because of this deactivation, publish a user-visible notification
                                if (removedAllocDescriptions.Any())
                                {
                                    try
                                    {
                                        var person = db.Persons.FirstOrDefault(p => p.Id == personId);
                                        var personName = person != null ? (person.FirstName + " " + person.LastName).Trim() : "(bilinmiyor)";
                                        var details = string.Join("; ", removedAllocDescriptions);
                                        var msg = $"{personName} kişisinin odalama kayıtları ({details}) silindi; kayıt tekrar aktif yapılana kadar geri yüklenmeyecektir.";
                                        try { System.Diagnostics.Debug.WriteLine($"Publishing allocation.removed for PersonId={personId} Details={details}"); } catch { }
                                        NotificationCenter.Publish("allocation.removed", new { PersonId = personId, PersonName = personName, Details = details, Message = msg, Timestamp = DateTime.UtcNow }, persistToDb: true);
                                        // Also publish a second deactivation notification including allocation details so UI definitely shows it
                                        try
                                        {
                                            var extMsg = $"{personName} kişisinin tur kaydı pasif yapıldı. Silinen odalamalar: {details}";
                                            try { System.Diagnostics.Debug.WriteLine($"Publishing registration.deactivated (extended) for PersonId={personId} details={details}"); } catch { }
                                            NotificationCenter.Publish("registration.deactivated", new { RegistrationId = registration.Id, PersonId = registration.PersonId, PersonName = personName, Tour = registration.TourType, Period = registration.Period, Group = registration.GroupType, Days = registration.DaysCount, Message = extMsg, Timestamp = DateTime.UtcNow }, persistToDb: false);
                                        }
                                        catch { }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch
                        {
                            // ignore cleanup errors
                        }

                        try
                        {
                            NotificationCenter.Publish("RegistrationSaved", new { PersonId = registration.PersonId, AddToPool = registration.IsActive }, persistToDb: false);
                        }
                        catch
                        {
                            // ignore
                        }
                        // If registration was activated (IsActive==true) then check for history to restore/notify
                        if (registration.IsActive)
                        {
                            try
                            {
                                // Ensure history table exists
                                try { db.EnsureRoomAllocationHistoryTableExists(); } catch { }

                                var pending = db.RoomAllocationHistories.Where(h => h.PersonId == registration.PersonId && h.Restored == false).ToList();
                                if (pending != null && pending.Count > 0)
                                {
                                    try
                                    {
                                        var person = db.Persons.FirstOrDefault(p => p.Id == registration.PersonId);
                                        var personName = person != null ? (person.FirstName + " " + person.LastName).Trim() : "(bilinmiyor)";
                                        var details = string.Join("; ", pending.Select(p => (string.IsNullOrWhiteSpace(p.HotelName) ? p.HotelId.ToString() : p.HotelName) + (string.IsNullOrWhiteSpace(p.RoomNumber) ? "" : " - Oda " + p.RoomNumber)));
                                        var msg = $"{personName} kişisinin tur kaydı tekrar aktifleştirildi. Önceden silinen odalamalar ({details}) odalama havuzuna yeniden eklendi.";
                                        NotificationCenter.Publish("allocation.restored", new { PersonId = registration.PersonId, PersonName = personName, Details = details, Message = msg, Timestamp = DateTime.UtcNow }, persistToDb: false);

                                        // mark history as restored
                                        foreach (var h in pending)
                                        {
                                            try { h.Restored = true; h.RestoredAt = DateTime.UtcNow; } catch { }
                                        }
                                        try { db.SaveChanges(); } catch { }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                        try
                        {
                            // publish a user-visible notification about the registration being set to passive
                            try
                            {
                                var person = db.Persons.FirstOrDefault(p => p.Id == registration.PersonId);
                                var personName = person != null ? (person.FirstName + " " + person.LastName).Trim() : "(bilinmiyor)";
                                var tour = registration.TourType ?? "-";
                                var period = registration.Period ?? "-";
                                var group = registration.GroupType ?? "-";
                                var days = registration.DaysCount ?? "-";
                                var msg = $"{personName} kişisinin {tour} + {period} + {group} + {days} gün olan tur kaydı pasif yapıldı.";
                                NotificationCenter.Publish("registration.deactivated", new { RegistrationId = registration.Id, PersonId = registration.PersonId, PersonName = personName, Tour = tour, Period = period, Group = group, Days = days, Message = msg, Timestamp = DateTime.UtcNow });
                            }
                            catch { }
                        }
                        catch { }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            LoadRegistrations();
            SelectRegistration(selectedRegistration.Id);
            UpdateRegistrationActionButtons();
        }

        private void DeleteSelectedRegistration()
        {
            if (RegistrationsDataGrid.SelectedItem is not RegistrationListItem selectedRegistration)
            {
                MessageBox.Show("Lütfen silmek için bir operasyon kaydı seçiniz.");
                return;
            }

            if (MessageBox.Show("Seçilen operasyon kaydı tamamen silinsin mi?", "Kaydı Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            using (var db = new AppDbContext())
            {
                var registration = db.Registrations.FirstOrDefault(x => x.Id == selectedRegistration.Id);
                if (registration == null)
                {
                    MessageBox.Show("Seçilen operasyon kaydı bulunamadı.");
                    return;
                }

                int personId = registration.PersonId;

                // Remove the registration
                db.Registrations.Remove(registration);

                try
                {
                    // Also remove any RoomOccupant entries for this person
                    var occupants = db.RoomOccupants
                        .Where(o => o.PassengerId == personId)
                        .ToList();

                    if (occupants.Any())
                    {
                        var allocIds = occupants.Select(o => o.RoomAllocationId).Distinct().ToList();
                        db.RoomOccupants.RemoveRange(occupants);

                        // For each affected RoomAllocation, if no other occupants remain, remove the allocation
                        foreach (var aid in allocIds)
                        {
                            bool hasOther = db.RoomOccupants.Any(o => o.RoomAllocationId == aid && o.PassengerId != personId);
                            if (!hasOther)
                            {
                                var alloc = db.RoomAllocations.FirstOrDefault(a => a.Id == aid);
                                if (alloc != null)
                                {
                                    db.RoomAllocations.Remove(alloc);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // ignore errors during cleanup
                }

                db.SaveChanges();

                try
                {
                    // Notify other modules (ör. odalama) that this person's registration changed/removed
                    // Pass AddToPool=false so odalama motorü bu kişiyi havuza eklemesin
                    NotificationCenter.Publish("RegistrationSaved", new { PersonId = personId, AddToPool = false }, persistToDb: false);
                }
                catch
                {
                    // ignore notification failures
                }
                try
                {
                    // Also publish a user-visible deletion notification so users see which registration was removed
                    try
                    {
                        var person = db.Persons.FirstOrDefault(p => p.Id == personId);
                        var personName = person != null ? (person.FirstName + " " + person.LastName).Trim() : "(bilinmiyor)";
                        var tour = registration?.TourType ?? "-";
                        var period = registration?.Period ?? "-";
                        var group = registration?.GroupType ?? "-";
                        var days = registration?.DaysCount ?? "-";
                        var msg = $"{personName} kişisinin {tour} + {period} + {group} + {days} gün kaydı silindi.";
                        NotificationCenter.Publish("registration.deleted", new { RegistrationId = registration.Id, PersonId = personId, PersonName = personName, Tour = tour, Period = period, Group = group, Days = days, Message = msg, Timestamp = DateTime.UtcNow });
                    }
                    catch { }
                }
                catch { }
            }

            LoadRegistrations();
            UpdateRegistrationActionButtons();
        }

        private void SelectRegistration(int registrationId)
        {
            var item = _registrationItems.FirstOrDefault(x => x.Id == registrationId);
            if (item != null)
            {
                // Select the item but do not call ScrollIntoView so the grid's scroll position
                // does not jump when selection changes.
                RegistrationsDataGrid.SelectedItem = item;
            }
        }

        private void HidePreview()
        {
            try
            {
                if (Step10Grid != null)
                {
                    Step10Grid.Visibility = Visibility.Collapsed;
                    Step10Grid.Opacity = 0.0;
                    Step10Grid.IsHitTestVisible = false;
                    Panel.SetZIndex(Step10Grid, 0);
                }

                if (PreviewTextBlock != null)
                {
                    PreviewTextBlock.Visibility = Visibility.Collapsed;
                    PreviewTextBlock.Text = string.Empty;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void UpdateRegistrationActionButtons()
        {
            bool hasSelection = RegistrationsDataGrid?.SelectedItem is RegistrationListItem;

            // Context menu items should be enabled when a selection exists (regardless of click method)
            if (DeleteRegistrationMenuItem != null)
                DeleteRegistrationMenuItem.IsEnabled = hasSelection;
            var editMenu = this.FindName("EditRegistrationMenuItem") as MenuItem;
            if (editMenu != null) editMenu.IsEnabled = hasSelection;
            var setPassiveMenu = this.FindName("SetRegistrationPassiveMenuItem") as MenuItem;
            if (setPassiveMenu != null) setPassiveMenu.IsEnabled = hasSelection;

            // Footer buttons visible and enabled as soon as a selection exists
            bool showFooterButtons = hasSelection;

            EditRegistrationButton.Visibility = showFooterButtons ? Visibility.Visible : Visibility.Collapsed;
            SetRegistrationPassiveButton.Visibility = showFooterButtons ? Visibility.Visible : Visibility.Collapsed;
            DeleteRegistrationButton.Visibility = showFooterButtons ? Visibility.Visible : Visibility.Collapsed;

            if (!hasSelection)
            {
                // No selection: ensure defaults
                if (SetRegistrationPassiveButton != null)
                {
                    SetRegistrationPassiveButton.IsEnabled = false;
                    SetRegistrationPassiveButton.Content = "Pasif Yap";
                }

                if (SetRegistrationPassiveMenuItem != null)
                {
                    SetRegistrationPassiveMenuItem.Header = "Pasif Yap";
                }

                if (EditRegistrationButton != null) EditRegistrationButton.IsEnabled = false;
                if (DeleteRegistrationButton != null) DeleteRegistrationButton.IsEnabled = false;

                return;
            }

            var selectedRegistration = (RegistrationListItem)RegistrationsDataGrid.SelectedItem;
            bool canActivate = string.Equals(selectedRegistration.Status, "Pasif", StringComparison.CurrentCultureIgnoreCase);
            string actionText = canActivate ? "Aktif Yap" : "Pasif Yap";

            if (SetRegistrationPassiveButton != null)
            {
                SetRegistrationPassiveButton.IsEnabled = true;
                SetRegistrationPassiveButton.Content = actionText;
            }

            // Adjust button color according to action: if action will activate the record, show green; if will make passive, show danger red.
            try
            {
                if (SetRegistrationPassiveButton != null)
                {
                    if (string.Equals(actionText, "Aktif Yap", StringComparison.CurrentCultureIgnoreCase))
                    {
                        SetRegistrationPassiveButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#10B981");
                        SetRegistrationPassiveButton.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#0B7A54");
                    }
                    else
                    {
                        // Pasif Yap
                        SetRegistrationPassiveButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#EF4444");
                        SetRegistrationPassiveButton.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#7A1A1A");
                    }
                }
            }
            catch
            {
                // ignore brush errors
            }

            if (SetRegistrationPassiveMenuItem != null)
            {
                SetRegistrationPassiveMenuItem.Header = actionText;
            }

            if (EditRegistrationButton != null) EditRegistrationButton.IsEnabled = true;
            if (DeleteRegistrationButton != null) DeleteRegistrationButton.IsEnabled = true;
        }

        private void EditRegistrationButton_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedRegistration();
        }

        private void EditRegistrationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedRegistration();
        }

        private void EditSelectedRegistration()
        {
            if (RegistrationsDataGrid.SelectedItem is not RegistrationListItem selectedRegistration)
            {
                MessageBox.Show("Lütfen düzenlemek için bir operasyon kaydı seçiniz.");
                return;
            }

            using (var db = new AppDbContext())
            {
                var registration = db.Registrations
                    .Include(x => x.Person)
                    .FirstOrDefault(x => x.Id == selectedRegistration.Id);

                if (registration == null)
                {
                    MessageBox.Show("Seçilen operasyon kaydı bulunamadı.");
                    return;
                }

                ShowWizard();
                ResetWizardForm();
                editingRegistrationId = registration.Id;
                skipPersonSelectionStepForEdit = true;
                selectedPersonId = registration.PersonId;

                SelectPersonInGrid(registration.PersonId);

                TourTypeComboBox.SelectedItem = registration.TourType;
                PeriodComboBox.SelectedItem = registration.Period;
                GroupTypeComboBox.SelectedItem = registration.GroupType;
                DaysCountComboBox.SelectedItem = registration.DaysCount;
                VisaRequestComboBox.SelectedItem = registration.VisaRequest;
                TicketRequestComboBox.SelectedItem = registration.TicketRequest;
                // PassportArrived is stored as int? (1 = Evet, 0 = Hayır, -1 = Belirtilmedi/unspecified)
                // Passport arrived now set via PassportArrivedComboBox
                if (registration.PassportArrived.HasValue && PassportArrivedComboBox != null && registration.PassportArrived.Value >= 0)
                {
                    int val = registration.PassportArrived.Value;
                    // try to find matching AppSetting by Id
                    var match = PassportArrivedComboBox.Items.Cast<AppSetting>().FirstOrDefault(x => x.Id == val);
                    if (match != null)
                    {
                        PassportArrivedComboBox.SelectedItem = match;
                    }
                    else
                    {
                        // fallback to matching by text
                        var foundByText = PassportArrivedComboBox.Items.Cast<AppSetting>()
                            .FirstOrDefault(x => (x.Value ?? string.Empty).IndexOf(val == 1 ? "evet" : "hay", StringComparison.CurrentCultureIgnoreCase) >= 0);
                        if (foundByText != null)
                            PassportArrivedComboBox.SelectedItem = foundByText;
                    }
                }
                // Visa status is now a ComboBox
                if (!string.IsNullOrWhiteSpace(registration.VisaStatusChoice) && VisaStatusComboBox != null)
                {
                    VisaStatusComboBox.SelectedItem = registration.VisaStatusChoice;
                    _selectedVisaStatusChoice = registration.VisaStatusChoice;
                }
                // Ticket status now a ComboBox
                if (TicketStatusComboBox != null)
                {
                    TicketStatusComboBox.SelectedItem = registration.TicketStatusChoice;
                    _selectedTicketStatusChoice = registration.TicketStatusChoice;
                }
                CargoRequestComboBox.SelectedItem = registration.CargoRequest;
                RoomTypeComboBox.SelectedItem = registration.RoomType;
                JoinCityComboBox.SelectedItem = registration.DepartureCity;
                TransferComboBox.SelectedItem = string.IsNullOrWhiteSpace(registration.HasTransfer) ? "Yok" : registration.HasTransfer;
                TransferCityComboBox.SelectedItem = registration.TransferCity;
                TransferDistrictTextBox.Text = registration.District;
                PriceTextBox.Text = registration.TotalPrice.ToString("N2");
                CurrencyComboBox.SelectedItem = registration.Currency;
                PaidTextBox.Text = registration.PaidAmount.ToString("N2");
                RemainingDebtTextBox.Text = registration.RemainingDebt.ToString("N2");
                ReferenceTextBox.Text = registration.Reference;

                // Mark that we arrived here from editing a registration in the browser
                _cameFromBrowserEdit = true;
                SwitchToStep(2);
                UpdateNextButtonState();
            }
        }

        private void UpdateRegistrationResultsState()
        {
            if (_registrationsView == null || NoRegistrationResultsPanel == null)
                return;

            bool hasAnyResult = _registrationsView.Cast<object>().Any();
            bool hasSearchText = !string.IsNullOrWhiteSpace(RegistrationSearchTextBox?.Text);
            bool hasPeriodFilter = RegistrationPeriodFilterComboBox?.SelectedItem != null
                && RegistrationPeriodFilterComboBox.SelectedItem.ToString() != "Tüm Dönemler";

            NoRegistrationResultsPanel.Visibility =
                !hasAnyResult && (hasSearchText || hasPeriodFilter)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            // Eğer sonuç yok paneli görünürse, kullanıcıya hangi dönem için sonuç bulunamadığını göster
            try
            {
                if (NoRegistrationResultsPanel.Visibility == Visibility.Visible && NoRegistrationMessageTextBlock != null)
                {
                    var selectedPeriod = RegistrationPeriodFilterComboBox?.SelectedItem?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(selectedPeriod) && selectedPeriod != "Tüm Dönemler")
                    {
                        NoRegistrationMessageTextBlock.Text = $"Bu kayıt daha önce {selectedPeriod} dönemi için yapılmamış";
                    }
                    else
                    {
                        NoRegistrationMessageTextBlock.Text = "Bu kayıt daha Önce Yapılmamış";
                    }
                }
            }
            catch
            {
                // ignore
            }

            // Apply blurring rules requested:
            // Eğer bir dönem seçilmişse ekran buzlu olsun ve başlık (RegistrationBrowserTitleTextBlock) buzsuz kalsın.
            // Ancak arama kutusuna yazılan metin datagridde eşleşiyorsa blur kalksın.
            try
            {
                bool shouldApplyBlur = false;

                // Dönem seçilmişse başlangıçta blur uygula (kullanıcının arama yapmasını bekleyeceğiz)
                if (RegistrationPeriodFilterComboBox?.SelectedItem != null && RegistrationPeriodFilterComboBox.SelectedItem.ToString() != "Tüm Dönemler")
                {
                    shouldApplyBlur = true;
                }

                // Eğer arama metni varsa ve veri gridde sonuç bulunuyorsa blur kalksın
                string search = (RegistrationSearchTextBox?.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(search) && _registrationsView != null)
                {
                    var nSearch = Normalize(search);

                    var matched = _registrationsView.Cast<object>().OfType<RegistrationListItem>()
                        .Any(x => (!string.IsNullOrWhiteSpace(x.PersonFullName) && Normalize(x.PersonFullName).Contains(nSearch))
                                  || (!string.IsNullOrWhiteSpace(x.TourType) && Normalize(x.TourType).Contains(nSearch))
                                  || (!string.IsNullOrWhiteSpace(x.Period) && Normalize(x.Period).Contains(nSearch))
                                  || (!string.IsNullOrWhiteSpace(x.GroupType) && Normalize(x.GroupType).Contains(nSearch))
                                  || (!string.IsNullOrWhiteSpace(x.DaysCount) && Normalize(x.DaysCount).Contains(nSearch))
                                  || (!string.IsNullOrWhiteSpace(x.Reference) && Normalize(x.Reference).Contains(nSearch)));

                    if (matched)
                        shouldApplyBlur = false;
                }

                SetRegistrationBrowserBlur(shouldApplyBlur);

            // Also control the inline no-results panel (just under search box)
            try
            {
                if (InlineNoRegistrationPanel != null)
                {
                    // Show inline suggestion when user typed something AND there are matching rows in the grid
                    // (this allows offering a quick "Yeni Tur Kaydı Ekle" when name similarity exists).
                    // Hide the inline suggestion when a row is selected or when there are no results
                    // because in that case the separate NoRegistrationResultsPanel already explains the absence.
                    bool noRowSelected = RegistrationsDataGrid?.SelectedItem == null;
                    bool showInline = hasSearchText && noRowSelected && hasAnyResult;
                    InlineNoRegistrationPanel.Visibility = showInline ? Visibility.Visible : Visibility.Collapsed;

                    if (showInline && InlineNoRegistrationMessageTextBlock != null)
                    {
                        var selectedPeriod = RegistrationPeriodFilterComboBox?.SelectedItem?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(selectedPeriod) && selectedPeriod != "Tüm Dönemler")
                        {
                            InlineNoRegistrationMessageTextBlock.Text = $"Aradığınız tur kaydı {selectedPeriod} dönemi için AŞAĞIDAKİ listede görünmüyor mu? Yeni kayıt ekleyin.";
                        }
                        else
                        {
                            InlineNoRegistrationMessageTextBlock.Text = "Aradığınız tur kaydı listede görünmüyor mu? Yeni kayıt ekleyin.";
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
            }
            catch
            {
                // ignore
            }
        }

        private void SetRegistrationBrowserBlur(bool blur)
        {
            try
            {
                Effect effect = blur ? (Effect)new BlurEffect { Radius = 8 } : null;

                // Elements to blur: search box, period combobox, warning panel, datagrid container
                if (RegistrationBrowserBlurContainer != null)
                {
                    RegistrationBrowserBlurContainer.Effect = effect;
                    RegistrationBrowserBlurContainer.IsHitTestVisible = !blur;
                }

                // Title remains unblurred
                if (RegistrationBrowserTitleTextBlock != null)
                {
                    RegistrationBrowserTitleTextBlock.Effect = null;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void PersonsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PersonsDataGrid.SelectedItem is Person person)
            {
                HidePersonPopup();
                selectedPersonId = person.Id;

                if (!person.IsActive)
                {
                    NextButton.IsEnabled = false;
                    ToggleActiveButton.Visibility = Visibility.Visible;
                }
                else
                {
                    NextButton.IsEnabled = true;
                    ToggleActiveButton.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                selectedPersonId = 0;
                NextButton.IsEnabled = false;
                ToggleActiveButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowQuickAddButton_Click(object sender, RoutedEventArgs e)
        {
            ShowQuickAddPopup();

            LoadSettingsToWizardForm();

            QuickFirstNameTextBox.Text = SearchTextBox.Text;
            QuickFirstNameTextBox.Focus();
        }

        private void LoadSettingsToWizardForm()
        {
            using (var db = new AppDbContext())
            {
                var activeSettings = db.AppSettings
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.SortOrder)
                    .ToList();

                QuickPassportTypeComboBox.ItemsSource = activeSettings.Where(x => x.Type == "Pasaport Türü").ToList();
                QuickGenderComboBox.ItemsSource = activeSettings.Where(x => x.Type == "Cinsiyet").ToList();
                QuickBloodTypeComboBox.ItemsSource = activeSettings.Where(x => x.Type == "Kan Grubu").ToList();
                QuickAgeRangeComboBox.ItemsSource = activeSettings.Where(x => x.Type == "Yaş Aralığı").ToList();
                // Pasaportu geldi mi? ayarları (Evet/Hayır seçenekleri) genelde AppSettings içinde tutuluyorsa buradan alınabilir
                // Ancak burada doğrudan SettingsView'deki karttan alınacak. Wizard içine yalnızca seçenekleri yüklemek için AppSettings'te "Pasaportu Geldi mi ?" tipi aranır
                var passportOptions = activeSettings.Where(x => x.Type == "Pasaportu Geldi mi ?").OrderBy(x => x.SortOrder).Select(x => x.Value).ToList();
                // Eğer AppSettings içinde yoksa fallback olarak Evet/Hayır ekleyelim
                if (passportOptions == null || passportOptions.Count == 0)
                {
                    passportOptions = new List<string> { "Evet", "Hayır" };
                }
                // No direct ComboBox for passport options in quick add; handled in main wizard load
            }
        }

        private void QuickBirthDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (QuickBirthDatePicker.SelectedDate != null)
            {
                DateTime birthDate = QuickBirthDatePicker.SelectedDate.Value;
                int age = DateTime.Today.Year - birthDate.Year;

                if (birthDate.Date > DateTime.Today.AddYears(-age)) age--;

                string targetRange = "Yetişkin";

                if (age >= 0 && age <= 2) targetRange = "Bebek (0-2)";
                else if (age > 2 && age <= 12) targetRange = "Çocuk (2-12)";
                else if (age > 12 && age < 65) targetRange = "Yetişkin";
                else targetRange = "Yaşlı / Kıdemli";

                QuickAgeRangeComboBox.SelectedValue = targetRange;
            }
        }

        private void SaveQuickPersonButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(QuickFirstNameTextBox.Text))
            {
                MessageBox.Show("Lütfen kişinin 'Adı' alanını boş bırakmayınız.");
                return;
            }

            if (string.IsNullOrWhiteSpace(QuickLastNameTextBox.Text))
            {
                MessageBox.Show("Lütfen kişinin 'Soyadı' alanını boş bırakmayınız.");
                return;
            }

            if (string.IsNullOrWhiteSpace(QuickTcTextBox.Text))
            {
                MessageBox.Show("Lütfen 'TC Kimlik No' alanını boş bırakmayınız.");
                return;
            }

            if (QuickBirthDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Lütfen doğum tarihi seçiniz.");
                return;
            }

            if (QuickGenderComboBox.SelectedValue == null)
            {
                MessageBox.Show("Lütfen cinsiyet seçiniz.");
                return;
            }

            string tcNo = QuickTcTextBox.Text.Trim();

            if (tcNo.Length != 11 || !tcNo.All(char.IsDigit))
            {
                MessageBox.Show("TC Kimlik No hatalı.");
                return;
            }

            using (var db = new AppDbContext())
            {
                if (db.Persons.Any(x => x.TcKimlikNo == tcNo && x.IsActive))
                {
                    MessageBox.Show("Bu TC zaten kayıtlı.");
                    ClearQuickAddForm();
                    SearchTextBox.Text = tcNo;
                    SearchTextBox.Focus();
                    return;
                }
            }

            string passportType = QuickPassportTypeComboBox.SelectedValue?.ToString() ?? "Belirtilmedi";
            string gender = QuickGenderComboBox.SelectedValue.ToString();
            string ageRange = QuickAgeRangeComboBox.SelectedValue?.ToString() ?? "Yetişkin";
            string bloodType = QuickBloodTypeComboBox.SelectedValue?.ToString() ?? "Belirtilmedi";

            if (MessageBox.Show("Kaydı onaylıyor musunuz?", "Onay", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                using (var db = new AppDbContext())
                {
                    var newPerson = new Person
                    {
                        TcKimlikNo = tcNo,
                        FirstName = QuickFirstNameTextBox.Text.Trim(),
                        LastName = QuickLastNameTextBox.Text.Trim(),
                        PhoneNumber = QuickPhoneTextBox.Text.Trim(),
                        Address = QuickAddressTextBox.Text.Trim(),
                        AgeRange = ageRange,
                        BirthDate = QuickBirthDatePicker.SelectedDate.Value,
                        BloodType = bloodType,
                        Gender = gender,
                        PassportNumber = QuickPassportNumberTextBox.Text?.Trim() ?? "Yok",
                        HealthStatus = QuickHealthStatusTextBox.Text?.Trim() ?? "Belirtilmedi",
                        VaccineStatus = QuickVaccineStatusTextBox.Text?.Trim() ?? "Belirtilmedi",
                        PassportType = passportType,
                        IsActive = true
                    };

                    db.Persons.Add(newPerson);
                    db.SaveChanges();

                    selectedPersonId = newPerson.Id;
                    LoadPersons();
                    ClearQuickAddForm();
                    SwitchToStep(2);
                }
            }
        }

        private void ClearQuickAddForm()
        {
            QuickTcTextBox.Clear();
            QuickFirstNameTextBox.Clear();
            QuickLastNameTextBox.Clear();
            QuickPhoneTextBox.Clear();
            QuickBirthDatePicker.SelectedDate = null;
            HidePersonPopup();
        }

        private void StartNewRegistrationButton_Click(object sender, RoutedEventArgs e)
        {
            ShowWizard();
            ResetWizardForm();
            SwitchToStep(1);
        }

        private void ShowRegistrationBrowser(bool preservePeriod = false)
        {
            WizardStepsPanel.Visibility = Visibility.Collapsed;
            RegistrationBrowserGrid.Visibility = Visibility.Visible;
            Step1Grid.Visibility = Visibility.Collapsed;
            Step2Grid.Visibility = Visibility.Collapsed;
            Step3Grid.Visibility = Visibility.Collapsed;
            Step4Grid.Visibility = Visibility.Collapsed;
            Step5Grid.Visibility = Visibility.Collapsed;
            Step6Grid.Visibility = Visibility.Collapsed;
            Step7Grid.Visibility = Visibility.Collapsed;
            Step8Grid.Visibility = Visibility.Collapsed;
            Step9Grid.Visibility = Visibility.Collapsed;
            Step10Grid.Visibility = Visibility.Collapsed;
            Step10Grid.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed;
            ToggleActiveButton.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;
            if (!preservePeriod)
            {
                ResetRegistrationPeriodGuide();
            }
            // If a period is already selected, focus the search box so user can type immediately.
            FocusRegistrationSearchBox();
        }

        private void ShowWizard()
        {
            WizardStepsPanel.Visibility = Visibility.Visible;
            RegistrationBrowserGrid.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Visible;
        }

        private void ResetRegistrationPeriodGuide()
        {
            if (RegistrationPeriodFilterComboBox == null || RegistrationPeriodGuideComboBox == null)
            {
                return;
            }

            _isSyncingRegistrationPeriodGuide = true;
            RegistrationPeriodFilterComboBox.SelectedIndex = -1;
            RegistrationPeriodGuideComboBox.ItemsSource = RegistrationPeriodFilterComboBox.ItemsSource;
            RegistrationPeriodGuideComboBox.SelectedIndex = -1;
            _isSyncingRegistrationPeriodGuide = false;

            UpdateRegistrationPeriodGuideState();

            // Do not auto-focus or open the period guide combobox to avoid unexpected dropdown opening.
        }

        private void UpdateRegistrationPeriodGuideState()
        {
            if (RegistrationPeriodGuideOverlay == null || RegistrationBrowserContentBorder == null || RegistrationBrowserGrid == null)
            {
                return;
            }

            bool shouldShowGuide =
                RegistrationBrowserGrid.Visibility == Visibility.Visible &&
                RegistrationPeriodFilterComboBox?.SelectedItem == null;

            RegistrationPeriodGuideOverlay.Visibility = shouldShowGuide ? Visibility.Visible : Visibility.Collapsed;
            RegistrationBrowserContentBorder.Effect = shouldShowGuide ? _registrationBrowserBlur : null;
            RegistrationBrowserContentBorder.IsHitTestVisible = !shouldShowGuide;
        }

        private void FocusRegistrationSearchBox()
        {
            try
            {
                if (RegistrationBrowserGrid == null || RegistrationSearchTextBox == null)
                    return;

                // If the period guide overlay is visible, do not steal focus
                if (RegistrationPeriodGuideOverlay != null && RegistrationPeriodGuideOverlay.Visibility == Visibility.Visible)
                    return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    RegistrationSearchTextBox.Focus();
                    Keyboard.Focus(RegistrationSearchTextBox);
                    try { RegistrationSearchTextBox.CaretIndex = RegistrationSearchTextBox.Text?.Length ?? 0; } catch { }
                }), DispatcherPriority.Background);
            }
            catch
            {
                // ignore
            }
        }

        private void ResetWizardForm()
        {
            editingRegistrationId = 0;
            skipPersonSelectionStepForEdit = false;
            selectedPersonId = 0;
            PersonsDataGrid.SelectedItem = null;
            InitializePersonsModuleHost();
            SearchTextBox.Clear();
            ShowPassiveCheckBox.IsChecked = false;
            HidePersonPopup();
            TourTypeComboBox.SelectedIndex = -1;
            PeriodComboBox.SelectedIndex = -1;
            GroupTypeComboBox.SelectedIndex = -1;
            DaysCountComboBox.SelectedIndex = -1;
            VisaRequestComboBox.SelectedIndex = -1;
            TicketRequestComboBox.SelectedIndex = -1;
            if (PassportArrivedComboBox != null)
                PassportArrivedComboBox.SelectedIndex = -1;
            if (VisaStatusComboBox != null)
                VisaStatusComboBox.SelectedIndex = -1;
            if (TicketStatusComboBox != null)
                TicketStatusComboBox.SelectedIndex = -1;
            _selectedTicketStatusChoice = string.Empty;
            _selectedVisaStatusChoice = string.Empty;
            _selectedTicketStatusChoice = string.Empty;
            CargoRequestComboBox.SelectedIndex = -1;
            RoomTypeComboBox.SelectedIndex = -1;
            JoinCityComboBox.SelectedIndex = -1;
            TransferComboBox.SelectedIndex = -1;
            TransferCityComboBox.SelectedIndex = -1;
            TransferDistrictTextBox.Clear();
            PriceTextBox.Clear();
            CurrencyComboBox.SelectedIndex = -1;
            PaidTextBox.Clear();
            RemainingDebtTextBox.Clear();
            ReferenceTextBox.Clear();
            TransferPanel.Visibility = Visibility.Collapsed;
            _personsView?.Refresh();
        }

        private void SelectPersonInGrid(int personId)
        {
            var person = PersonsDataGrid.Items
                .OfType<Person>()
                .FirstOrDefault(x => x.Id == personId);

            if (person != null)
            {
                PersonsDataGrid.SelectedItem = person;
                PersonsDataGrid.ScrollIntoView(person);
            }

            _personsWizardView?.SelectPersonForExternalFlow(personId);
        }

        private void SwitchToStep(int step)
        {
            RegistrationBrowserGrid.Visibility = Visibility.Collapsed;
            WizardStepsPanel.Visibility = Visibility.Visible;
            currentStep = step;

            Step1Grid.Visibility = Visibility.Collapsed;
            Step2Grid.Visibility = Visibility.Collapsed;
            Step3Grid.Visibility = Visibility.Collapsed;
            Step4Grid.Visibility = Visibility.Collapsed;
            Step5Grid.Visibility = Visibility.Collapsed;
            Step6Grid.Visibility = Visibility.Collapsed;
            Step7Grid.Visibility = Visibility.Collapsed;
            Step8Grid.Visibility = Visibility.Collapsed;
            Step9Grid.Visibility = Visibility.Collapsed;

            switch (step)
            {
                case 1: Step1Grid.Visibility = Visibility.Visible; break;
                case 2: Step2Grid.Visibility = Visibility.Visible; break;
                case 3: Step3Grid.Visibility = Visibility.Visible; break;
                case 4: Step4Grid.Visibility = Visibility.Visible; break;
                case 5: Step5Grid.Visibility = Visibility.Visible; break;
                case 6: Step6Grid.Visibility = Visibility.Visible; break;
                case 7: Step7Grid.Visibility = Visibility.Visible; break;
                case 8: Step8Grid.Visibility = Visibility.Visible; break;
                case 9: Step9Grid.Visibility = Visibility.Visible; break;
                case 10:
                    Step10Grid.Visibility = Visibility.Visible;
                    break;
            }

            // Ensure preview content is only visible for step 10
            try
            {
                if (PreviewTextBlock != null)
                    PreviewTextBlock.Visibility = (step == 10) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }

            var stepBorders = new[] { Step1Border, Step2Border, Step3Border, Step4Border, Step5Border, Step6Border, Step7Border, Step8Border, Step9Border, Step10Border };
            var stepTexts = new[] { Step1Text, Step2Text, Step3Text, Step4Text, Step5Text, Step6Text, Step7Text, Step8Text, Step9Text, Step10Text };

            for (int i = 0; i < stepBorders.Length; i++)
            {
                bool isActive = i == step - 1;
                // Use the registrations module active color #FFF59E0B so embedded Persons module is unaffected.
                stepBorders[i].Background = (SolidColorBrush)new BrushConverter().ConvertFromString(isActive ? "#FFF59E0B" : "#1F2937");
                stepTexts[i].Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(isActive ? "White" : "#9CA3AF");
                stepTexts[i].FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
            }

            BackButton.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;

            NextButton.Content = step == MaxStep ? "Kaydet" : "İleri";

            // Ensure Step10 visual state is fully controlled to avoid ghosting
            try
            {
                if (step == 10)
                {
                    if (Step10Grid != null)
                    {
                        Step10Grid.Visibility = Visibility.Visible;
                        Step10Grid.Opacity = 1.0;
                        Step10Grid.IsHitTestVisible = true;
                        Panel.SetZIndex(Step10Grid, 50);
                    }

                    if (PreviewTextBlock != null)
                    {
                        PreviewTextBlock.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    if (Step10Grid != null)
                    {
                        Step10Grid.Visibility = Visibility.Collapsed;
                        Step10Grid.Opacity = 0.0;
                        Step10Grid.IsHitTestVisible = false;
                        Panel.SetZIndex(Step10Grid, 0);
                    }

                    if (PreviewTextBlock != null)
                    {
                        PreviewTextBlock.Visibility = Visibility.Collapsed;
                        // Clear text to avoid visual remnants
                        PreviewTextBlock.Text = string.Empty;
                    }
                }
            }
            catch { }

            UpdateNextButtonState();
        }

        private void UpdateNextButtonState()
        {
            if (RegistrationBrowserGrid.Visibility == Visibility.Visible)
            {
                NextButton.IsEnabled = false;
                return;
            }

            switch (currentStep)
            {
                case 1:
                    NextButton.IsEnabled = _personsWizardView?.HasActiveSelectedPerson == true;
                    break;

                case 2:
                    NextButton.IsEnabled =
                        TourTypeComboBox.SelectedItem != null &&
                        PeriodComboBox.SelectedItem != null &&
                        GroupTypeComboBox.SelectedItem != null &&
                        DaysCountComboBox.SelectedItem != null;
                    break;

                default:
                    NextButton.IsEnabled = true;
                    break;
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            // 💾 KAYDETME BLOĞU
            if (NextButton.Content?.ToString() == "💾 Kaydet ve Bitir")
            {
                try
                {
                    bool isEditMode = editingRegistrationId > 0;
                    string visaRequest = VisaRequestComboBox.SelectedItem?.ToString() ?? "Seçilmedi";
                    string ticketRequest = TicketRequestComboBox.SelectedItem?.ToString() ?? "Seçilmedi";
                    string visaStatusChoice = _selectedVisaStatusChoice;
                    string ticketStatusChoice = _selectedTicketStatusChoice;
                    string cargoRequest = CargoRequestComboBox.SelectedItem?.ToString() ?? "Seçilmedi";
                    string roomType = RoomTypeComboBox.SelectedItem?.ToString() ?? "Seçilmedi";
                    string departureCity = JoinCityComboBox.SelectedItem?.ToString() ?? "Seçilmedi";
                    string currency = CurrencyComboBox.SelectedItem?.ToString() ?? "Seçilmedi";
                    string transferStatus = TransferComboBox.SelectedItem?.ToString() ?? "Yok";
                    string transferCity = transferStatus == "Var"
                        ? (TransferCityComboBox.SelectedItem?.ToString() ?? "Belirtilmedi")
                        : string.Empty;
                    string district = string.IsNullOrWhiteSpace(TransferDistrictTextBox?.Text)
                        ? "Belirtilmedi"
                        : TransferDistrictTextBox.Text.Trim();
                    string reference = string.IsNullOrWhiteSpace(ReferenceTextBox?.Text)
                        ? "Belirtilmedi"
                        : ReferenceTextBox.Text.Trim();

                    decimal.TryParse((PriceTextBox?.Text ?? "0").Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out decimal totalPrice);
                    decimal.TryParse((PaidTextBox?.Text ?? "0").Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out decimal paidAmount);
                    decimal.TryParse((RemainingDebtTextBox?.Text ?? "0").Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out decimal remainingDebt);

                    using (var db = new AppDbContext())
                    {
                        string selectedTourType = TourTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
                        string selectedPeriod = PeriodComboBox.SelectedItem?.ToString() ?? string.Empty;
                        string selectedGroupType = GroupTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
                        string selectedDaysCount = DaysCountComboBox.SelectedItem?.ToString() ?? string.Empty;

                        bool duplicateRegistrationExists = db.Registrations.Any(x =>
                            x.PersonId == selectedPersonId &&
                            x.TourType == selectedTourType &&
                            x.Period == selectedPeriod &&
                            x.GroupType == selectedGroupType &&
                            x.DaysCount == selectedDaysCount &&
                            (!isEditMode || x.Id != editingRegistrationId));

                        if (duplicateRegistrationExists)
                        {
                            MessageBox.Show(
                                "Bu kişi aynı Tur Türü, Dönem, Grup Türü ve Grup Süresi için daha önce zaten kaydedilmiş.",
                                "Mükerrer Kayıt",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            NavigationService?.Navigate(new RegistrationsWizardView());
                            return;
                        }

                        var registration = isEditMode
                            ? db.Registrations.FirstOrDefault(x => x.Id == editingRegistrationId)
                            : new Registration
                            {
                                CreatedDate = DateTime.Now,
                                Status = "Aktif",
                                IsActive = true
                            };

                        if (registration == null)
                        {
                            MessageBox.Show("Düzenlenecek kayıt bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        registration.PersonId = selectedPersonId;
                        registration.TourType = selectedTourType;
                        registration.Period = selectedPeriod;
                        registration.GroupType = selectedGroupType;
                        registration.DaysCount = selectedDaysCount;
                        registration.VisaRequest = visaRequest;
                        registration.TicketRequest = ticketRequest;
                        registration.VisaStatusChoice = string.IsNullOrWhiteSpace(visaStatusChoice) ? "Seçilmedi" : visaStatusChoice;
                // Ticket status now from TicketStatusComboBox
                registration.TicketStatusChoice = string.IsNullOrWhiteSpace(ticketStatusChoice)
                    ? (TicketStatusComboBox?.SelectedItem?.ToString() ?? "Seçilmedi")
                    : ticketStatusChoice;
                        // Store PassportArrived using PassportArrivedComboBox.SelectedValue (AppSetting Id) or heuristic mapping
                        // Use -1 as sentinel for unspecified so existing DB NOT NULL constraints are not violated
                        registration.PassportArrived = -1;
                        if (PassportArrivedComboBox?.SelectedItem is AppSetting sel)
                        {
                            registration.PassportArrived = sel.Id;
                        }
                        else
                        {
                            var pText = PassportArrivedComboBox?.SelectedItem?.ToString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(pText))
                            {
                                if (pText.IndexOf("evet", StringComparison.CurrentCultureIgnoreCase) >= 0)
                                    registration.PassportArrived = 1;
                                else if (pText.IndexOf("hay", StringComparison.CurrentCultureIgnoreCase) >= 0)
                                    registration.PassportArrived = 0;
                            }
                        }
                        registration.CargoRequest = cargoRequest;
                        registration.RoomType = roomType;
                        registration.DepartureCity = departureCity;
                        registration.HasTransfer = transferStatus;
                        registration.TransferCity = transferCity;
                        registration.District = district;
                        registration.TotalPrice = totalPrice;
                        registration.Currency = currency;
                        // capture original paid amount for edit mode to produce a payment history entry
                        decimal originalPaid = 0m;
                        if (isEditMode)
                        {
                            originalPaid = registration.PaidAmount;
                        }
                        registration.PaidAmount = paidAmount;
                        registration.RemainingDebt = remainingDebt;
                        registration.Reference = reference;

                        if (!isEditMode)
                        {
                            db.Registrations.Add(registration);
                        }

                        db.SaveChanges();

                        // Eğer yeni kayıt oluşturulduysa ilgili bilgiyi bildirim olarak yayınla
                        try
                        {
                            if (!isEditMode)
                            {
                                try
                                {
                                    var person = db.Persons.FirstOrDefault(p => p.Id == registration.PersonId);
                                    var personName = person != null ? (person.FirstName + " " + person.LastName).Trim() : "(bilinmiyor)";
                                    var tour = registration.TourType ?? "-";
                                    var period = registration.Period ?? "-";
                                    var group = registration.GroupType ?? "-";
                                    var days = registration.DaysCount ?? "-";
                                    var msg = $"{personName} kişisi {tour} + {period} + {group} + {days} gün kaydı oluşturuldu.";
                                    TurizmOperasyonSistemi.Services.NotificationCenter.Publish("registration.created", new { RegistrationId = registration.Id, PersonId = registration.PersonId, PersonName = personName, Tour = tour, Period = period, Group = group, Days = days, Message = msg, Timestamp = DateTime.UtcNow });
                                }
                                catch { }
                            }
                        }
                        catch { }

                        // If paid amount changed (either new registration with initial payment or edited), create a Payment record
                        try
                        {
                            var delta = registration.PaidAmount - originalPaid;
                            if (delta != 0)
                            {
                                db.EnsurePaymentsTableExists();
                                var payment = new TurizmOperasyonSistemi.Models.Payment
                                {
                                    RegistrationId = registration.Id,
                                    CreatedAt = DateTime.Now,
                                    OriginalAmount = delta,
                                    OriginalCurrency = registration.Currency,
                                    AmountInRegistrationCurrency = delta,
                                    Rate = null,
                                    Note = "Kayıt işlemiyle eklenen ödeme",
                                    PaymentMethod = "Kayıt (Wizard)",
                                    UploadedDocumentPath = ""
                                };
                                db.Payments.Add(payment);
                                // update registration aggregates to reflect the payment (already set above, but ensure consistency)
                                registration.PaidAmount = originalPaid + delta;
                                registration.RemainingDebt = registration.TotalPrice - registration.PaidAmount;
                                db.SaveChanges();

                                // publish notification to update UI elsewhere
                                TurizmOperasyonSistemi.Services.NotificationCenter.Publish("payment.updated", registration.Id);
                            }
                        }
                        catch { }

                        // If CargoRequest is not 'Kargo gönderilecek', remove any existing print-queue items
                        // for this registration and clear inspected flag so it won't be included in A4/print pool.
                        try
                        {
                            if (!string.Equals(cargoRequest, "Kargo gönderilecek", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var items = CargoPrintQueue.Instance.Items.ToArray();
                                    foreach (var it in items)
                                    {
                                        try
                                        {
                                            if (it.RegistrationId == registration.Id)
                                                CargoPrintQueue.Instance.Remove(it);
                                        }
                                        catch { }
                                    }
                                }
                                catch { }

                                try
                                {
                                    var store = new CargoInspectedStore();
                                    store.Unmark(registration.Id);
                                }
                                catch { }
                            }
                        }
                        catch { }

                        // Senkronizasyon: Kayıt güncellendiğinde/eklendiğinde
                        // o kişiye ait varsa RoomOccupant kayıtlarını bul ve bağlı
                        // RoomAllocation öğesini kayıt verilerine göre güncelle.
                        try
                        {
                            // Kişiye ait mevcut RoomOccupant kayıtlarını al
                            var occupants = db.RoomOccupants
                                .Include(o => o.RoomAllocation)
                                .Where(o => o.PassengerId == registration.PersonId)
                                .ToList();

                            var processedPreviousAllocIds = new HashSet<int>();

                            foreach (var occ in occupants)
                            {
                                var currentAlloc = occ.RoomAllocation;
                                if (currentAlloc != null)
                                {
                                    processedPreviousAllocIds.Add(currentAlloc.Id);
                                }

                                // Eğer zaten doğru grup/şartlardaysa sadece RoomType güncelle
                                if (currentAlloc != null &&
                                    string.Equals(currentAlloc.TourType ?? string.Empty, registration.TourType ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(currentAlloc.Period ?? string.Empty, registration.Period ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(currentAlloc.GroupType ?? string.Empty, registration.GroupType ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(currentAlloc.DaysCount ?? string.Empty, registration.DaysCount ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                                {
                                    currentAlloc.RoomType = registration.RoomType;
                                    db.RoomAllocations.Update(currentAlloc);
                                    continue;
                                }

                                // Uygun mevcut oda (aynı Tur/Periyot/Grup/Süre) ve kapasite müsait olanı bul
                                var targetAlloc = db.RoomAllocations
                                    .Include(a => a.Occupants)
                                    .FirstOrDefault(a =>
                                        string.Equals(a.TourType ?? string.Empty, registration.TourType ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(a.Period ?? string.Empty, registration.Period ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(a.GroupType ?? string.Empty, registration.GroupType ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(a.DaysCount ?? string.Empty, registration.DaysCount ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                                        (a.Occupants.Count < ResolveRoomCapacity(a.RoomType)));

                                if (targetAlloc != null)
                                {
                                    // Taşıma: RoomOccupant'ın RoomAllocationId değerini değiştir
                                    occ.RoomAllocationId = targetAlloc.Id;
                                    db.RoomOccupants.Update(occ);
                                }
                                else
                                {
                                    // Uygun oda yoksa yeni bir RoomAllocation oluşturup taşı
                                    var newAlloc = new TurizmOperasyonSistemi.Models.RoomAllocation
                                    {
                                        HotelId = 0,
                                        RoomNumber = "AUTO",
                                        RoomType = registration.RoomType ?? string.Empty,
                                        TourType = registration.TourType ?? string.Empty,
                                        Period = registration.Period ?? string.Empty,
                                        GroupType = registration.GroupType ?? string.Empty,
                                        DaysCount = registration.DaysCount ?? string.Empty,
                                        IsConnection = false,
                                        ConnectionGroupId = string.Empty
                                    };

                                    db.RoomAllocations.Add(newAlloc);
                                    db.SaveChanges(); // Id almak için kaydet

                                    occ.RoomAllocationId = newAlloc.Id;
                                    db.RoomOccupants.Update(occ);
                                }
                            }

                            // Boş kalan önceki odaları kapat (sil)
                            foreach (var prevId in processedPreviousAllocIds)
                            {
                                var alloc = db.RoomAllocations
                                    .Include(a => a.Occupants)
                                    .FirstOrDefault(a => a.Id == prevId);

                                if (alloc != null && (alloc.Occupants == null || alloc.Occupants.Count == 0))
                                {
                                    db.RoomAllocations.Remove(alloc);
                                }
                            }

                            db.SaveChanges();
                        }
                        catch
                        {
                            // Hata olsa bile kayıt akışını bozmuyoruz
                        }

                        // Bildirim gönder: Bir kaydın odalama kriterleri güncellendi
                        try
                        {
                            NotificationCenter.Publish("RegistrationSaved", new { PersonId = registration.PersonId, AddToPool = registration.IsActive }, persistToDb: false);
                        }
                        catch
                        {
                            // ignore
                        }

                        LoadRegistrations();
                        ResetWizardForm();
                        ShowRegistrationBrowser();

                        // If there is existing text in the registration search box, clear it and focus
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(RegistrationSearchTextBox?.Text))
                            {
                                RegistrationSearchTextBox.Clear();
                            }
                            FocusRegistrationSearchBox();
                        }
                        catch { }

                        MessageBox.Show(
                            isEditMode
                                ? "Kayıt işlemi başarıyla güncellendi!"
                                : "Kayıt işlemi başarıyla SQLite veritabanına kaydedildi!",
                            "Başarılı",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Veritabanına kaydedilirken bir hata oluştu: {ex.Message}\nDetay: {ex.InnerException?.Message}",
                        "Hata",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                return;
            }

            if (currentStep == 1 && selectedPersonId == 0)
            {
                MessageBox.Show("Lütfen bir kişi seçiniz.");
                return;
            }

            if (currentStep == 2)
            {
                if (TourTypeComboBox.SelectedItem == null ||
                    PeriodComboBox.SelectedItem == null ||
                    GroupTypeComboBox.SelectedItem == null ||
                    DaysCountComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Lütfen tüm tur bilgilerini doldurunuz.");
                    return;
                }
            }

            // ✅ 9. adımdan 10. adıma geçerken önizleme oluştur
            if (currentStep == 9)
            {
                string selectedPersonName = "Seçilmedi";

                if (PersonsDataGrid.SelectedItem is Person selectedPerson)
                {
                    selectedPersonName = $"{selectedPerson.FirstName} {selectedPerson.LastName}".Trim();
                }
                else if (selectedPersonId > 0)
                {
                    using (var db = new AppDbContext())
                    {
                        var person = db.Persons.FirstOrDefault(x => x.Id == selectedPersonId);
                        if (person != null)
                        {
                            selectedPersonName = $"{person.FirstName} {person.LastName}".Trim();
                        }
                    }
                }

                string tourType = TourTypeComboBox?.SelectedItem?.ToString() ?? "Seçilmedi";
                string period = PeriodComboBox?.SelectedItem?.ToString() ?? "Seçilmedi";
                string groupType = GroupTypeComboBox?.SelectedItem?.ToString() ?? "Seçilmedi";
                string daysCount = DaysCountComboBox?.SelectedItem?.ToString() ?? "Seçilmedi";
                string visaRequest = VisaRequestComboBox?.SelectedItem?.ToString() ?? "Seçilmedi";
                string ticketRequest = TicketRequestComboBox?.SelectedItem?.ToString() ?? "Seçilmedi";
                string cargoRequest = CargoRequestComboBox?.SelectedItem?.ToString() ?? "Seçilmedi";
                string roomType = RoomTypeComboBox?.SelectedItem?.ToString() ?? "Seçilmedi";
                string joinCity = JoinCityComboBox?.SelectedItem?.ToString() ?? "Seçilmedi";
                string isTransfer = TransferComboBox?.SelectedItem?.ToString() ?? "Yok";
                string transferCity = isTransfer == "Var"
                    ? (TransferCityComboBox?.SelectedItem?.ToString() ?? "Seçilmedi")
                    : "-";
                string district = TransferDistrictTextBox?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(district))
                {
                    district = "Belirtilmedi";
                }

                string totalPrice = string.IsNullOrWhiteSpace(PriceTextBox?.Text) ? "0" : PriceTextBox.Text.Trim();
                string paidPrice = string.IsNullOrWhiteSpace(PaidTextBox?.Text) ? "0" : PaidTextBox.Text.Trim();
                string remainingPrice = string.IsNullOrWhiteSpace(RemainingDebtTextBox?.Text) ? "0" : RemainingDebtTextBox.Text.Trim();
                string currency = CurrencyComboBox?.SelectedItem?.ToString() ?? "Seçilmedi";
                string reference = string.IsNullOrWhiteSpace(ReferenceTextBox?.Text) ? "Belirtilmedi" : ReferenceTextBox.Text.Trim();

                PreviewTextBlock.Text =
                    $"👤 1. YOLCU BİLGİSİ:\n" +
                    $"👉 {selectedPersonName}\n" +
                    $"--------------------------------------------------\n" +
                    $"📋 2. TUR VE GRUP DETAYLARI:\n" +
                    $"🔹 Tur Türü: {tourType}\n" +
                    $"🔹 Dönem: {period}\n" +
                    $"🔹 Grup Türü: {groupType}\n" +
                    $"🔹 Süre: {daysCount}\n" +
                    $"--------------------------------------------------\n" +
                    $"🎫 3 & 4. TALEPLER:\n" +
                    $"🔹 Vize Talebi: {visaRequest}\n" +
                    $"🔹 Bilet Talebi: {ticketRequest}\n" +
                    $"🔹 Kargo Talebi: {cargoRequest}\n" +
                    $"--------------------------------------------------\n" +
                    $"🛏️ 5. KONAKLAMA:\n" +
                    $"🔹 Oda Türü: {roomType}\n" +
                    $"--------------------------------------------------\n" +
                    $"✈️ 6. ULAŞIM VE TRANSFER:\n" +
                    $"🔹 Katılım Şehri: {joinCity}\n" +
                    $"🔹 İlçe: {district}\n" +
                    $"🔹 Aktarma Durumu: {isTransfer} " +
                    $"{(isTransfer == "Var" ? $"(Şehir: {transferCity})" : "")}\n" +
                    $"--------------------------------------------------\n" +
                    $"💰 7. FİNANSAL ÖZET:\n" +
                    $"🔹 Toplam Fiyat: {totalPrice}\n" +
                    $"🔹 Ödenen: {paidPrice}\n" +
                    $"🔹 Kalan: {remainingPrice}\n" +
                    $"🔹 Para Birimi: {currency}\n" +
                    $"--------------------------------------------------\n" +
                    $"🤝 8. REFERANS:\n" +
                    $"🔹 {reference}\n" +
                    $"--------------------------------------------------\n\n" +
                    $"👉 Lütfen bilgilerin doğruluğunu onaylayın. " +
                    $"'Kaydet' butonuna bastığınızda bu operasyon listeye eklenecektir.";

                SwitchToStep(10);
                NextButton.Content = "💾 Kaydet ve Bitir";

                return;
            }

            // Normal adım geçişleri
            SwitchToStep(currentStep + 1);
        }





        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // If we're leaving the preview step explicitly hide preview content to avoid visual remnants
            if (currentStep == 10)
            {
                HidePreview();
            }

            if (currentStep == 1)
            {
                ResetWizardForm();
                ShowRegistrationBrowser();
            }
            else if (_cameFromBrowserEdit && currentStep == 2)
            {
                // If we arrived here by editing a registration from the browser,
                // going back from the first wizard step should return to the browser and reset the flag.
                _cameFromBrowserEdit = false;
                ResetWizardForm();
                // Return to browser but preserve the selected period so user is not prompted again
                ShowRegistrationBrowser(preservePeriod: true);
            }
            else if (skipPersonSelectionStepForEdit && currentStep == 2)
            {
                ResetWizardForm();
                ShowRegistrationBrowser();
            }
            else if (currentStep > 1)
            {
                SwitchToStep(currentStep - 1);
            }
        }

        private void LoadSettingsToComboBoxes()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    TourTypeComboBox.ItemsSource = db.AppSettings
                        .Where(s => (s.Type == "Tur Türü" || s.Type == "TourType") && s.IsActive)
                        .OrderBy(s => s.SortOrder)
                        .Select(s => s.Value)
                        .ToList();

                    GroupTypeComboBox.ItemsSource = db.AppSettings
                        .Where(s => (s.Type == "Grup Türü" || s.Type == "GroupType") && s.IsActive)
                        .OrderBy(s => s.SortOrder)
                        .Select(s => s.Value)
                        .ToList();

                    DaysCountComboBox.ItemsSource = db.AppSettings
                        .Where(s => (s.Type == "Kaç Günlük Grup" || s.Type == "CountOfDays") && s.IsActive)
                        .OrderBy(s => s.SortOrder)
                        .Select(s => s.Value)
                        .Distinct()
                        .ToList();

                    PeriodComboBox.ItemsSource = db.AppSettings
                        .Where(s => s.Type == "Dönem" && s.IsActive)
                        .OrderBy(s => s.SortOrder)
                        .Select(s => s.Value)
                        .ToList();

                    var registrationPeriods = PeriodComboBox.ItemsSource
                        .Cast<string>()
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct()
                        .ToList();
                    registrationPeriods.Insert(0, "Tüm Dönemler");
                    RegistrationPeriodFilterComboBox.ItemsSource = registrationPeriods;

                    if (RegistrationPeriodFilterComboBox.SelectedItem == null)
                    {
                        RegistrationPeriodFilterComboBox.SelectedIndex = 0;
                    }

                    VisaRequestComboBox.ItemsSource = db.AppSettings
                        .Where(x => (x.Type == "Vize Talebi" || x.Type == "VisaType") && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    TicketRequestComboBox.ItemsSource = db.AppSettings
                        .Where(x => (x.Type == "Bilet Talebi" || x.Type == "TicketType") && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    // attach handler so ticket request changes filter ticket status options
                    TicketRequestComboBox.SelectionChanged -= TicketRequestComboBox_SelectionChanged;
                    TicketRequestComboBox.SelectionChanged += TicketRequestComboBox_SelectionChanged;

                    LoadRegistrationStatusRadioOptions(db);

                    // Load PassportArrived options from AppSettings
                    LoadPassportArrivedOptions(db);

                    CargoRequestComboBox.ItemsSource = db.AppSettings
                        .Where(x => (x.Type == "Kargo Talebi" || x.Type == "CargoType") && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    RoomTypeComboBox.ItemsSource = db.AppSettings
                        .Where(x => (x.Type == "Oda Türü" || x.Type == "RoomType") && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    JoinCityComboBox.ItemsSource = db.AppSettings
                        .Where(x => x.Type == "Katılım Yapılacak Şehir" && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    TransferComboBox.ItemsSource = new List<string> { "Var", "Yok" };

                    TransferCityComboBox.ItemsSource = db.AppSettings
                        .Where(x => x.Type == "Aktarma Şehri" && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    CurrencyComboBox.ItemsSource = db.AppSettings
                        .Where(x => (x.Type == "Para Birimi" || x.Type == "CurrencyType") && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ayarlar verileri yüklenirken hata oluştu: {ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

        }

        private void LoadPassportArrivedOptions(AppDbContext db)
        {
            try
            {
                var settings = db.AppSettings
                    .Where(x => x.Type == "Pasaportu Geldi mi ?" && x.IsActive)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .ToList();

                var options = settings.ToList();

                if (!options.Any())
                {
                    options = new List<AppSetting> {
                        new AppSetting { Id = 1, Value = "Evet" },
                        new AppSetting { Id = 0, Value = "Hayır" }
                    };
                }

                _passportArrivedOptionsAll = options.ToList();
                PassportArrivedComboBox.ItemsSource = _passportArrivedOptionsAll;
                PassportArrivedComboBox.SelectedIndex = -1;
            }
            catch
            {
                // ignore
            }
        }

        private void PassportArrivedRadio_Checked(object sender, RoutedEventArgs e)
        {
            // When passport arrival selection changes, evaluate visa/passport related rules
            EvaluateVisaPassportRules();
        }

        private void PassportArrivedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EvaluateVisaPassportRules();
        }

        private void VisaRequestComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EvaluateVisaPassportRules();
        }

        private string NormalizeForComparison(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return input.Trim().ToLowerInvariant()
                .Replace('ı','i').Replace('ğ','g').Replace('ü','u').Replace('ş','s').Replace('ö','o').Replace('ç','c');
        }
        private void EvaluateVisaPassportRules()
        {
            if (_isApplyingAutoSelection) return;

            try
            {
                _isApplyingAutoSelection = true;

                var visaReq = VisaRequestComboBox?.SelectedItem?.ToString() ?? string.Empty;
                var visaReqNorm = NormalizeForComparison(visaReq);

                // determine passport selection text from ComboBox
                string passportText = string.Empty;
                if (PassportArrivedComboBox?.SelectedItem is AppSetting pas)
                {
                    passportText = pas.Value ?? pas.ToString();
                }
                else if (PassportArrivedComboBox?.SelectedItem != null)
                {
                    passportText = PassportArrivedComboBox.SelectedItem.ToString();
                }
                var passportNorm = NormalizeForComparison(passportText);

                // update which options are visible based on current selections
                ApplyDynamicVisaPassportOptionFiltering(visaReqNorm, passportNorm);

                // If user changed visa request away from 'kendisi', clear any visa status that was auto-selected as 'kendisi'
                if (!(string.IsNullOrWhiteSpace(visaReqNorm) && visaReqNorm.Length == 0))
                {
                    if (!visaReqNorm.Contains("kendisi") && !visaReqNorm.Contains("kendi"))
                    {
                        var curVisa = VisaStatusComboBox?.SelectedItem?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(curVisa) && NormalizeForComparison(curVisa).Contains("kendisi"))
                        {
                            // clear previous kendisi selection so it doesn't persist when switching to other modes
                            VisaStatusComboBox.SelectedIndex = -1;
                            _selectedVisaStatusChoice = string.Empty;
                        }
                    }
                }

                // helper: set visa status combo selection by contains match
                void SetVisaStatusContains(params string[] candidates)
                {
                    if (VisaStatusComboBox == null) return;
                    foreach (var c in candidates)
                    {
                        var found = VisaStatusComboBox.Items.Cast<string>()
                            .FirstOrDefault(v => NormalizeForComparison(v ?? string.Empty).Contains(NormalizeForComparison(c)));

                        if (found != null)
                        {
                            VisaStatusComboBox.SelectedItem = found;
                            return;
                        }
                    }
                }

                // helper: set passport combobox selection by contains match
                void SetPassportContains(params string[] candidates)
                {
                    if (PassportArrivedComboBox == null) return;
                    foreach (var c in candidates)
                    {
                        var found = PassportArrivedComboBox.Items.Cast<AppSetting>()
                            .FirstOrDefault(x => NormalizeForComparison(x.Value ?? string.Empty).Contains(NormalizeForComparison(c)));
                        if (found != null)
                        {
                            PassportArrivedComboBox.SelectedItem = found;
                            return;
                        }
                    }
                }

                // RULE: Vizeyi kendisi alacak
                if (!string.IsNullOrWhiteSpace(visaReqNorm) && (visaReqNorm.Contains("kendisi") || visaReqNorm.Contains("kendi")))
                {
                    SetPassportContains("vermeyecek", "pasaport vermeyecek", "verme");
                    SetVisaStatusContains("kendisi", "vizeyi kendisi", "kendis", "vizeyi kendisi alacak");
                    return;
                }

                // RULE: Vize alacağız
                if (!string.IsNullOrWhiteSpace(visaReqNorm) && visaReqNorm.Contains("alac") && visaReqNorm.Contains("vize"))
                {
                    // Pasaport hayır -> vize durumu: pasaportu bekleniyor
                    if (!string.IsNullOrWhiteSpace(passportNorm) && passportNorm.Contains("hay"))
                    {
                        var anyVisaSelected = VisaStatusComboBox.SelectedItem != null;
                        if (!anyVisaSelected)
                        {
                            SetVisaStatusContains("pasaport beklen", "pasaport bekleniyor", "bekleniyor", "pasaport bekleniyor");
                        }
                        return;
                    }

                    // Pasaport evet -> vize durumu: Vizesi Alınıyor / Vizesi çıktı / Vizesi Çıkmadı
                    if (!string.IsNullOrWhiteSpace(passportNorm) && passportNorm.Contains("evet"))
                    {
                        var anyVisaSelected = VisaStatusComboBox.SelectedItem != null;
                        if (!anyVisaSelected)
                        {
                            SetVisaStatusContains("aliniyor", "alınıyor", "vizesi aliniyor", "vize aliniyor", "aliniyor", "çıkt", "cikti");
                        }
                        return;
                    }
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _isApplyingAutoSelection = false;
            }
        }

        private void ApplyDynamicVisaPassportOptionFiltering(string visaReqNorm, string passportNorm)
        {
            try
            {
                // Default: show all
                var passportList = _passportArrivedOptionsAll ?? new List<AppSetting>();
                var visaList = _visaStatusOptionsAll ?? new List<string>();

                // If visa request indicates 'Vizeyi Kendisi alacak' -> Passport: only 'pasaport vermeyecek', VisaStatus: only 'vizeyi kendisi alacak'
                if (!string.IsNullOrWhiteSpace(visaReqNorm) && (visaReqNorm.Contains("kendisi") || visaReqNorm.Contains("kendi")))
                {
                    passportList = passportList.Where(x => NormalizeForComparison(x.Value ?? string.Empty).Contains("verme") || NormalizeForComparison(x.Value ?? string.Empty).Contains("vermeyecek")).ToList();
                    visaList = visaList.Where(v => NormalizeForComparison(v ?? string.Empty).Contains("kendisi") || NormalizeForComparison(v ?? string.Empty).Contains("kendi")).ToList();
                }
                else if (!string.IsNullOrWhiteSpace(visaReqNorm) && visaReqNorm.Contains("alac") && visaReqNorm.Contains("vize"))
                {
                    // Vize alacağız -> passport options only Evet/Hayır
                    passportList = _passportArrivedOptionsAll.Where(x =>
                        NormalizeForComparison(x.Value ?? string.Empty).Contains("evet") ||
                        NormalizeForComparison(x.Value ?? string.Empty).Contains("hay") ||
                        NormalizeForComparison(x.Value ?? string.Empty).Contains("hayir")).ToList();

                    // If passport arrived == evet -> visa status options: Vizesi Alınıyor, Vizesi çıktı, Vizesi Çıkmadı
                    if (!string.IsNullOrWhiteSpace(passportNorm) && passportNorm.Contains("evet"))
                    {
                        visaList = _visaStatusOptionsAll.Where(v =>
                            NormalizeForComparison(v ?? string.Empty).Contains("alin") ||
                            NormalizeForComparison(v ?? string.Empty).Contains("çıkt") ||
                            NormalizeForComparison(v ?? string.Empty).Contains("cikti") ||
                            NormalizeForComparison(v ?? string.Empty).Contains("çık") ||
                            NormalizeForComparison(v ?? string.Empty).Contains("cikm")).ToList();
                    }
                    else if (!string.IsNullOrWhiteSpace(passportNorm) && passportNorm.Contains("hay"))
                    {
                        visaList = _visaStatusOptionsAll.Where(v => NormalizeForComparison(v ?? string.Empty).Contains("pasaport beklen")).ToList();
                    }
                }

                // Apply to UI: PassportArrivedComboBox and VisaStatusComboBox
                try
                {
                    _isApplyingAutoSelection = true;
                    if (PassportArrivedComboBox != null)
                    {
                        PassportArrivedComboBox.ItemsSource = passportList;
                        if (PassportArrivedComboBox.SelectedItem is AppSetting curP && !passportList.Any(x => x.Id == curP.Id))
                            PassportArrivedComboBox.SelectedIndex = -1;
                    }

                    if (VisaStatusComboBox != null)
                    {
                        VisaStatusComboBox.ItemsSource = visaList;
                        if (VisaStatusComboBox.SelectedItem != null && !visaList.Contains(VisaStatusComboBox.SelectedItem.ToString()))
                            VisaStatusComboBox.SelectedIndex = -1;
                    }
                }
                finally
                {
                    _isApplyingAutoSelection = false;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void LoadRegistrationStatusRadioOptions(AppDbContext db)
        {
            _selectedVisaStatusChoice = string.Empty;
            _selectedTicketStatusChoice = string.Empty;

            var visaStatusOptions = db.AppSettings
                .Where(x => (x.Type == "Vize Durumu" || x.Type == "VisaStatus") && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.Value)
                .ToList();

            if (!visaStatusOptions.Any())
            {
                visaStatusOptions = new List<string> { "Vize Çıktı", "Vize Çıkmadı" };
            }

            // keep full list for filtering logic and populate VisaStatusComboBox
            _visaStatusOptionsAll = visaStatusOptions.ToList();
            VisaStatusComboBox.ItemsSource = _visaStatusOptionsAll;

            var ticketStatusOptions = db.AppSettings
                .Where(x => (x.Type == "Bilet Durumu" || x.Type == "TicketStatus") && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.Value)
                .ToList();

            if (!ticketStatusOptions.Any())
            {
                ticketStatusOptions = new List<string> { "Bileti geldi", "Bileti henüz gelmedi" };
            }

            // populate TicketStatusComboBox and keep full list for filtering
            _ticketStatusOptionsAll = ticketStatusOptions.ToList();
            TicketStatusComboBox.ItemsSource = _ticketStatusOptionsAll;
        }

        private void PopulateRadioButtonPanel(StackPanel panel, IEnumerable<string> options, string groupName, RoutedEventHandler checkedHandler)
        {
            if (panel == null)
                return;

            panel.Children.Clear();

            foreach (var option in options.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var radioButton = new RadioButton
                {
                    Content = option,
                    GroupName = groupName,
                    Margin = new Thickness(0, 0, 18, 0),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6FCFF")),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13
                };

                radioButton.Checked += checkedHandler;
                panel.Children.Add(radioButton);
            }
        }


        private void VisaStatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedVisaStatusChoice = VisaStatusComboBox?.SelectedItem?.ToString() ?? string.Empty;
        }

        private void TicketStatusRadio_Checked(object sender, RoutedEventArgs e)
        {
            _selectedTicketStatusChoice = (sender as RadioButton)?.Content?.ToString() ?? string.Empty;
        }

        private void TicketStatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedTicketStatusChoice = TicketStatusComboBox?.SelectedItem?.ToString() ?? string.Empty;
        }

        private void TicketRequestComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var ticketReq = TicketRequestComboBox?.SelectedItem?.ToString() ?? string.Empty;
                var norm = NormalizeForComparison(ticketReq);
                // Inline filtering: show limited TicketStatus options based on ticket request
                try
                {
                    if (_isApplyingAutoSelection) return;
                    _isApplyingAutoSelection = true;

                    var ticketList = _ticketStatusOptionsAll ?? new List<string>();

                    if (!string.IsNullOrWhiteSpace(norm) && (norm.Contains("kendisi") || norm.Contains("kendi")))
                    {
                        ticketList = ticketList.Where(v => NormalizeForComparison(v ?? string.Empty).Contains("kendisi") || NormalizeForComparison(v ?? string.Empty).Contains("kendi")).ToList();
                    }
                    else if (!string.IsNullOrWhiteSpace(norm) && norm.Contains("alac"))
                    {
                        ticketList = ticketList.Where(v =>
                        {
                            var n = NormalizeForComparison(v ?? string.Empty);
                            return n.Contains("geld") || n.Contains("gelm") || n.Contains("alin") || n.Contains("aliniyor") || n.Contains("cikti");
                        }).ToList();
                    }

                    if (TicketStatusComboBox != null)
                    {
                        TicketStatusComboBox.ItemsSource = ticketList;
                        if (TicketStatusComboBox.SelectedItem != null && (TicketStatusComboBox.SelectedItem.ToString() == null || !ticketList.Contains(TicketStatusComboBox.SelectedItem.ToString())))
                        {
                            TicketStatusComboBox.SelectedIndex = -1;
                        }
                        // If the ticket request implies the person will take the ticket themself, auto-select matching ticket status
                        if (!string.IsNullOrWhiteSpace(norm) && (norm.Contains("kendisi") || norm.Contains("kendi")))
                        {
                            var auto = ticketList.FirstOrDefault(v => NormalizeForComparison(v ?? string.Empty).Contains("kendisi") || NormalizeForComparison(v ?? string.Empty).Contains("kendi"));
                            if (auto != null)
                            {
                                TicketStatusComboBox.SelectedItem = auto;
                                _selectedTicketStatusChoice = auto ?? string.Empty;
                            }
                        }
                    }
                }
                finally
                {
                    _isApplyingAutoSelection = false;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void ClearRadioButtons(StackPanel panel)
        {
            if (panel == null)
                return;

            foreach (var radioButton in panel.Children.OfType<RadioButton>())
            {
                radioButton.IsChecked = false;
            }
        }

        private void SelectRadioButtonInPanel(StackPanel panel, string selectedValue)
        {
            if (panel == null)
                return;

            foreach (var radioButton in panel.Children.OfType<RadioButton>())
            {
                bool isMatch = string.Equals(radioButton.Content?.ToString(), selectedValue, StringComparison.CurrentCultureIgnoreCase);
                radioButton.IsChecked = isMatch;

                // If a radio button is matched, do not attempt to infer ticket combo state here.
                // Ticket status is now stored in TicketStatusComboBox; keep this helper focused on radio panels.
            }
        }

        private void TransferComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TransferComboBox.SelectedItem != null && TransferComboBox.SelectedItem.ToString() == "Var")
            {
                TransferPanel.Visibility = Visibility.Visible;
            }
            else
            {
                TransferPanel.Visibility = Visibility.Collapsed;

                if (TransferCityComboBox != null)
                    TransferCityComboBox.SelectedIndex = -1;
            }
        }

        private void PaymentFields_TextChanged(object sender, TextChangedEventArgs e)
        {
            decimal total = 0;
            decimal paid = 0;

            decimal.TryParse(PriceTextBox.Text, out total);
            decimal.TryParse(PaidTextBox.Text, out paid);

            RemainingDebtTextBox.Text = (total - paid).ToString("N2");
        }

        private void TourDetails_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (currentStep == 2)
            {
                bool isValid =
                    TourTypeComboBox.SelectedItem != null &&
                    PeriodComboBox.SelectedItem != null &&
                    GroupTypeComboBox.SelectedItem != null &&
                    DaysCountComboBox.SelectedItem != null;

                NextButton.IsEnabled = isValid;
            }
        }

        private void ToggleActiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPersonId == 0) return;

            using (var db = new AppDbContext())
            {
                var person = db.Persons.FirstOrDefault(x => x.Id == selectedPersonId);

                if (person != null)
                {
                    person.IsActive = true;
                    db.SaveChanges();

                    LoadPersons();

                    ToggleActiveButton.Visibility = Visibility.Collapsed;
                    NextButton.IsEnabled = true;
                }
            }
        }

        private sealed class RegistrationListItem
        {
            public int Id { get; set; }
            public string PersonFullName { get; set; }
            public string PersonTc { get; set; }
            public string TourType { get; set; }
            public string Period { get; set; }
            public string GroupType { get; set; }
            public string DaysCount { get; set; }
            public string VisaRequest { get; set; }
            public string TicketRequest { get; set; }
            public string VisaStatusChoice { get; set; }
            public string TicketStatusChoice { get; set; }
            public string CargoRequest { get; set; }
            public string PassportArrived { get; set; }
            public string RoomType { get; set; }
            public string DepartureCity { get; set; }
            public string TransferStatus { get; set; }
            public string TransferCity { get; set; }
            public string District { get; set; }
            public decimal TotalPrice { get; set; }
            public decimal PaidAmount { get; set; }
            public decimal RemainingDebt { get; set; }
            public string Currency { get; set; }
            public string Reference { get; set; }
            public DateTime CreatedDate { get; set; }
            public bool IsActive { get; set; }
            public string Status { get; set; }
            public string TotalPriceDisplay => string.IsNullOrWhiteSpace(Currency)
                ? TotalPrice.ToString("N2")
                : $"{TotalPrice:N2} {Currency}";
            public string PaidAmountDisplay => string.IsNullOrWhiteSpace(Currency)
                ? PaidAmount.ToString("N2")
                : $"{PaidAmount:N2} {Currency}";
            public string RemainingDebtDisplay => string.IsNullOrWhiteSpace(Currency)
                ? RemainingDebt.ToString("N2")
                : $"{RemainingDebt:N2} {Currency}";
            public string CreatedDateDisplay => CreatedDate == default
                ? ""
                : CreatedDate.ToString("dd.MM.yyyy");
            public string IsActiveDisplay => IsActive ? "Aktif" : "Pasif";
        }
    }
}
