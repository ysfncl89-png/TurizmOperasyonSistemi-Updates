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

namespace TurizmOperasyonSistemi.Views
{
    public partial class RegistrationsWizardView
    {
        private const int MaxStep = 9;
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


        public RegistrationsWizardView()
        {
            InitializeComponent();
            // Ensure database has PassportArrived column
            try { new AppDbContext().EnsureRegistrationPassportArrivedColumnExists(); } catch { }
            LoadPersons();
            LoadRegistrations();
            LoadSettingsToComboBoxes();
            InitializePersonsModuleHost();
            UpdateRegistrationActionButtons();
            ShowRegistrationBrowser();
            // Attach click handlers to all ComboBox controls on this page so clicking anywhere opens the dropdown
            AttachComboBoxClickHandlers(this);
        }

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
                            bool isTurkey = normalized == "tc" || normalized.Contains("turkiye") || normalized.Contains("turkey") || normalized.StartsWith("tur") || normalized.Contains("turk");

                            if (isTurkey)
                            {
                                personTcOrPassport = x.Person.TcKimlikNo ?? string.Empty;
                            }
                            else
                            {
                                personTcOrPassport = x.Person.PassportNumber ?? string.Empty;
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
                            CargoRequest = x.CargoRequest ?? string.Empty,
                            PassportArrived = x.PassportArrived ?? string.Empty,
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

                db.Registrations.Remove(registration);
                db.SaveChanges();
            }

            LoadRegistrations();
            UpdateRegistrationActionButtons();
        }

        private void SelectRegistration(int registrationId)
        {
            var item = _registrationItems.FirstOrDefault(x => x.Id == registrationId);
            if (item != null)
            {
                RegistrationsDataGrid.SelectedItem = item;
                RegistrationsDataGrid.ScrollIntoView(item);
            }
        }

        private void UpdateRegistrationActionButtons()
        {
            bool hasSelection = RegistrationsDataGrid?.SelectedItem is RegistrationListItem;
            EditRegistrationButton.IsEnabled = hasSelection;
            SetRegistrationPassiveButton.IsEnabled = hasSelection;
            DeleteRegistrationButton.IsEnabled = hasSelection;
            if (DeleteRegistrationMenuItem != null)
            {
                DeleteRegistrationMenuItem.IsEnabled = hasSelection;
            }

            if (!hasSelection)
            {
                SetRegistrationPassiveButton.Content = "Pasif Yap";
                if (SetRegistrationPassiveMenuItem != null)
                {
                    SetRegistrationPassiveMenuItem.Header = "Pasif Yap";
                }

                return;
            }

            var selectedRegistration = (RegistrationListItem)RegistrationsDataGrid.SelectedItem;
            bool canActivate = string.Equals(selectedRegistration.Status, "Pasif", StringComparison.CurrentCultureIgnoreCase);
            string actionText = canActivate ? "Aktif Yap" : "Pasif Yap";

            SetRegistrationPassiveButton.Content = actionText;
            if (SetRegistrationPassiveMenuItem != null)
            {
                SetRegistrationPassiveMenuItem.Header = actionText;
            }
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
                if (!string.IsNullOrWhiteSpace(registration.PassportArrived))
                {
                    if (registration.PassportArrived.Equals("Evet", StringComparison.CurrentCultureIgnoreCase))
                        PassportArrivedYesRadio.IsChecked = true;
                    else if (registration.PassportArrived.Equals("Hayır", StringComparison.CurrentCultureIgnoreCase) || registration.PassportArrived.Equals("Hayir", StringComparison.CurrentCultureIgnoreCase))
                        PassportArrivedNoRadio.IsChecked = true;
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

        private void ShowRegistrationBrowser()
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
            BackButton.Visibility = Visibility.Collapsed;
            ToggleActiveButton.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;
            ResetRegistrationPeriodGuide();
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
            PassportArrivedYesRadio.IsChecked = false;
            PassportArrivedNoRadio.IsChecked = false;
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
                case 9:
                    Step9Grid.Visibility = Visibility.Visible;
                    break;
            }

            var stepBorders = new[] { Step1Border, Step2Border, Step3Border, Step4Border, Step5Border, Step6Border, Step7Border, Step8Border, Step9Border };
            var stepTexts = new[] { Step1Text, Step2Text, Step3Text, Step4Text, Step5Text, Step6Text, Step7Text, Step8Text, Step9Text };

            for (int i = 0; i < stepBorders.Length; i++)
            {
                bool isActive = i == step - 1;
                stepBorders[i].Background = (SolidColorBrush)new BrushConverter().ConvertFromString(isActive ? "#06B6D4" : "#1F2937");
                stepTexts[i].Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(isActive ? "White" : "#9CA3AF");
                stepTexts[i].FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
            }

            BackButton.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;

            NextButton.Content = step == MaxStep ? "Kaydet" : "İleri";

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
                        registration.PassportArrived = PassportArrivedYesRadio.IsChecked == true ? "Evet" : (PassportArrivedNoRadio.IsChecked == true ? "Hayır" : "");
                        registration.CargoRequest = cargoRequest;
                        registration.RoomType = roomType;
                        registration.DepartureCity = departureCity;
                        registration.HasTransfer = transferStatus;
                        registration.TransferCity = transferCity;
                        registration.District = district;
                        registration.TotalPrice = totalPrice;
                        registration.Currency = currency;
                        registration.PaidAmount = paidAmount;
                        registration.RemainingDebt = remainingDebt;
                        registration.Reference = reference;

                        if (!isEditMode)
                        {
                            db.Registrations.Add(registration);
                        }

                        db.SaveChanges();

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
                            NotificationCenter.Publish("RegistrationSaved", registration.PersonId);
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

            // ✅ 8. adımdan 9. adıma geçerken önizleme oluştur
            if (currentStep == 8)
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

                SwitchToStep(9);
                NextButton.Content = "💾 Kaydet ve Bitir";

                return;
            }

            // Normal adım geçişleri
            SwitchToStep(currentStep + 1);
        }





        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep == 1)
            {
                ResetWizardForm();
                ShowRegistrationBrowser();
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
